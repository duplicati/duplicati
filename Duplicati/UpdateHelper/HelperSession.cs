// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.


using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using Duplicati.UpdateHelper.Installers;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Runs one update: handshake with the server, wait for it to exit, install, restart, report
/// </summary>
internal sealed class HelperSession(HelperLog log)
{
    /// <summary>
    /// Exit code when the install succeeded
    /// </summary>
    public const int ExitSuccess = 0;
    /// <summary>
    /// Exit code when the install failed after the server exited
    /// </summary>
    public const int ExitFailed = 1;
    /// <summary>
    /// Exit code when the job was aborted before the server exited; nothing was changed
    /// </summary>
    public const int ExitAborted = 2;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GoTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runs the session
    /// </summary>
    /// <param name="pipeName">The pipe the server listens on</param>
    /// <param name="secret">The shared secret</param>
    /// <returns>The exit code</returns>
    public int Run(string pipeName, string secret)
    {
        var started = DateTime.UtcNow;
        var handshake = Handshake(pipeName, secret);
        if (handshake == null)
            return ExitAborted;

        var (job, installer) = handshake.Value;
        return Perform(job, installer, started) ? ExitSuccess : ExitFailed;
    }

    /// <summary>
    /// Performs the handshake; returns null if the job was aborted
    /// </summary>
    internal (UpdateJob Job, IInstaller Installer)? Handshake(string pipeName, string secret)
    {
        log.Write($"Connecting to {pipeName}");
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.Connect((int)ConnectTimeout.TotalMilliseconds);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        Send(writer, new HelperMessage
        {
            Type = UpdateHelperProtocol.TypeHello,
            ProtocolVersion = UpdateHelperProtocol.ProtocolVersion,
            Secret = secret,
            Pid = Environment.ProcessId,
            Version = Program.Version,
        });

        var msg = Receive(reader, MessageTimeout);
        if (msg == null || msg.Type != UpdateHelperProtocol.TypeJob || msg.Job == null)
        {
            log.Write($"Expected a job, got {msg?.Type ?? "nothing"}");
            return null;
        }

        if (msg.ProtocolVersion != UpdateHelperProtocol.ProtocolVersion)
        {
            Send(writer, Abort($"Protocol version {msg.ProtocolVersion} is not supported by this helper (version {UpdateHelperProtocol.ProtocolVersion})"));
            return null;
        }

        var job = msg.Job;
        if (!string.IsNullOrWhiteSpace(job.LogFile))
            log.Redirect(job.LogFile);

        log.Write($"Job: install {job.TargetVersion} over {job.CurrentVersion} ({job.PackageTypeId}) in {job.InstallDir}, restart kind {job.Restart.Kind}");

        var problem = PackageVerifier.Verify(log, job);
        IInstaller? installer = null;
        if (problem == null)
        {
            installer = InstallerFactory.Create(log, job.PackageTypeId);
            if (installer == null)
                problem = $"Package format is not supported: {job.PackageTypeId}";
        }

        if (problem == null)
        {
            try { problem = installer!.Prepare(job); }
            catch (Exception ex) { problem = $"Preparation failed: {ex.Message}"; }
        }

        if (problem != null)
        {
            log.Write($"Aborting: {problem}");
            Send(writer, Abort(problem));
            return null;
        }

        Send(writer, new HelperMessage { Type = UpdateHelperProtocol.TypeReady, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = Environment.ProcessId });

        msg = Receive(reader, GoTimeout);
        if (msg == null || msg.Type != UpdateHelperProtocol.TypeGo)
        {
            log.Write($"Expected go, got {msg?.Type ?? "nothing"}; aborting without changes");
            return null;
        }

        log.Write("Received go");
        return (job, installer!);
    }

    /// <summary>
    /// Performs the install after the server has agreed
    /// </summary>
    internal bool Perform(UpdateJob job, IInstaller installer, DateTime started)
    {
        var result = new InstallResult
        {
            TargetVersion = job.TargetVersion,
            StartedUtc = started,
            LogFile = log.Path,
        };

        using var listener = new StartedListener(log, job.MarkerFolder);
        Process? spawned = null;
        var stage = "stop";

        try
        {
            Restarter.StopSupervised(log, job.Restart);

            foreach (var pid in job.TerminatePids)
                ProcessControl.Terminate(log, pid);
            foreach (var name in job.TerminateProcessNames)
                ProcessControl.TerminateByName(log, name);

            if (!ProcessControl.WaitForExit(log, job.WaitPids, TimeSpan.FromSeconds(job.WaitForExitTimeoutSeconds)))
                throw new TimeoutException("The running instance did not exit in time; nothing was changed");

            // The old instance is gone from here on; a failure must bring the application back up
            stage = "preinstall";

            if (job.Restart.Kind == UpdateHelperProtocol.RestartWindowsService && !string.IsNullOrWhiteSpace(job.Restart.WindowsServiceName))
                if (!Restarter.WaitForWindowsServiceStopped(log, job.Restart.WindowsServiceName, TimeSpan.FromMinutes(2)))
                    throw new TimeoutException("The Windows service did not stop in time; nothing was changed");

            // The package sat on disk while the old instance exited; make sure it is still the verified file
            var changed = PackageVerifier.Verify(log, job);
            if (changed != null)
                throw new InvalidOperationException($"The package changed after it was verified; nothing was changed: {changed}");

            stage = "install";
            log.Write("Installing");
            installer.Install(job);

            stage = "restart";
            // Only the process the helper starts, or one of its descendants, may report that it started.
            // When a supervisor starts the new instance there is no spawned process and any report is accepted.
            listener.AcceptPid = pid =>
            {
                var root = spawned;
                if (root == null)
                    return true;
                var related = ProcessControl.IsDescendantOf(pid, root.Id);
                if (related == null)
                    log.Write($"Could not resolve the process tree for pid {pid}; accepting the started report");
                return related != false;
            };

            if (installer.RestartHandled)
                log.Write("Restart was handled by the installer");
            else
                spawned = Restarter.Start(log, job.Restart);

            stage = "start";
            if (job.Restart.Kind == UpdateHelperProtocol.RestartNone && !installer.RestartHandled)
            {
                result.Success = true;
                log.Write("Install completed, no restart requested");
                return true;
            }

            log.Write("Waiting for the new instance to start");
            var ok = listener.Wait(
                TimeSpan.FromSeconds(job.WaitForStartTimeoutSeconds),
                () => spawned != null && spawned.HasExited && spawned.ExitCode != 0);

            if (!ok)
            {
                if (spawned != null && spawned.HasExited)
                    throw new InvalidOperationException($"The new instance exited with code {spawned.ExitCode}");
                throw new TimeoutException("The new instance did not report that it started");
            }

            result.Success = true;
            log.Write("Update completed");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Stage = stage;
            result.Error = ex.Message;
            log.Write($"Failed in stage {stage}: {ex}");

            var newInstanceDead = spawned != null && spawned.HasExited;
            var shouldRollback = installer.CanRollback && (stage == "install" || stage == "restart" || (stage == "start" && newInstanceDead));
            if (shouldRollback)
            {
                try
                {
                    installer.Rollback(job);
                    result.RolledBack = true;
                }
                catch (Exception rex)
                {
                    log.Write($"Rollback failed: {rex}");
                }
            }

            // Bring something back up if the old instance is gone and nothing new is running.
            // In the stop stage the old instance did not exit, so it is still running.
            var nothingRunning = stage != "stop" && (spawned == null || newInstanceDead);
            if (nothingRunning && (result.RolledBack || !installer.CanRollback))
            {
                try
                {
                    log.Write("Starting the application after the failure");
                    Restarter.Start(log, job.Restart);
                }
                catch (Exception sex)
                {
                    log.Write($"Failed to start the application: {sex}");
                }
            }
        }
        finally
        {
            result.CleanupPaths = installer.CleanupPaths.ToArray();
            result.FinishedUtc = DateTime.UtcNow;
            WriteResult(job, result);
        }

        return result.Success;
    }

    private void WriteResult(UpdateJob job, InstallResult result)
    {
        if (string.IsNullOrWhiteSpace(job.ResultFile))
            return;

        try
        {
            var dir = Path.GetDirectoryName(job.ResultFile);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(job.ResultFile, JsonSerializer.Serialize(result, HelperJsonContext.Default.InstallResult));
            log.Write($"Result written to {job.ResultFile}");
        }
        catch (Exception ex)
        {
            log.Write($"Failed to write the result file: {ex.Message}");
        }
    }

    private static HelperMessage Abort(string reason)
        => new() { Type = UpdateHelperProtocol.TypeAbort, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = Environment.ProcessId, Reason = reason };

    private void Send(StreamWriter writer, HelperMessage message)
    {
        var line = JsonSerializer.Serialize(message, HelperJsonContext.Default.HelperMessage);
        log.Write($"Sending {message.Type}");
        writer.WriteLine(line);
    }

    private HelperMessage? Receive(StreamReader reader, TimeSpan timeout)
    {
        var task = reader.ReadLineAsync();
        if (!task.Wait(timeout))
        {
            log.Write("Timeout waiting for a message");
            return null;
        }

        var line = task.Result;
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var msg = JsonSerializer.Deserialize(line, HelperJsonContext.Default.HelperMessage);
        log.Write($"Received {msg?.Type}");
        return msg;
    }
}
