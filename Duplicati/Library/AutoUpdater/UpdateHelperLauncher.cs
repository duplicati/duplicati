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


#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// What the launcher needs to hand a package to the helper
/// </summary>
/// <param name="PackagePath">The downloaded package</param>
/// <param name="PackageLength">The package length from the signed manifest</param>
/// <param name="PackageSha256">The package SHA256 from the signed manifest, base64</param>
/// <param name="PackageTypeId">The package type id of the installation</param>
/// <param name="TargetVersion">The version being installed</param>
/// <param name="CurrentVersion">The running version</param>
/// <param name="Host">The host environment</param>
/// <param name="InstallDir">The folder the running installation lives in</param>
/// <param name="HelperSourcePath">The helper executable to copy and run</param>
/// <param name="WaitPids">The process ids the helper waits for before installing</param>
/// <param name="TerminatePids">The process ids the helper terminates before waiting</param>
/// <param name="UpdatesFolder">The updates folder</param>
/// <param name="MsiProperties">Properties for msiexec</param>
/// <param name="PortableMode">True if the data folder lives inside the install folder</param>
/// <param name="FinalCheck">Called right before the helper is told to go; returns a problem description to abort, or null</param>
public sealed record UpdateLaunchRequest(
    string PackagePath,
    long PackageLength,
    string PackageSha256,
    string PackageTypeId,
    string SignedManifest,
    string TargetVersion,
    string CurrentVersion,
    HostEnvironment Host,
    string InstallDir,
    string HelperSourcePath,
    int[] WaitPids,
    int[] TerminatePids,
    string UpdatesFolder,
    Dictionary<string, string> MsiProperties,
    bool PortableMode,
    Func<string?> FinalCheck);

/// <summary>
/// The outcome of a successful launch; the helper has been told to go
/// </summary>
/// <param name="HelperPid">The helper process id</param>
/// <param name="Record">The record written for the new instance</param>
public sealed record UpdateLaunchResult(int HelperPid, PendingInstallRecord Record);

/// <summary>
/// Copies the helper to the updates folder, starts it and performs the handshake
/// </summary>
public static class UpdateHelperLauncher
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// The process name of the tray icon, terminated before an MSI upgrade in service mode
    /// </summary>
    private const string TrayIconProcessName = "Duplicati.GUI.TrayIcon";

    /// <summary>
    /// Launches the helper and performs the handshake up to and including <c>go</c>
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="log">A log callback</param>
    /// <param name="cancellationToken">A cancellation token</param>
    /// <returns>The launch result</returns>
    /// <exception cref="UpdateInstallException">Thrown if the helper aborted or could not be started; nothing has changed</exception>
    public static async Task<UpdateLaunchResult> LaunchAsync(UpdateLaunchRequest request, Action<string> log, CancellationToken cancellationToken)
    {
        var helperSource = request.HelperSourcePath;
        if (!File.Exists(helperSource))
            throw new UpdateInstallException($"The update helper was not found at {helperSource}");

        // Short, so the pipe name fits the macOS socket path limit
        var id = Guid.NewGuid().ToString("N").Substring(0, 8);
        var helperDir = Path.Combine(request.UpdatesFolder, $"helper-{id}");
        Directory.CreateDirectory(helperDir);
        var helperExe = Path.Combine(helperDir, Path.GetFileName(helperSource));

        try
        {
            File.Copy(helperSource, helperExe, true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(helperExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

            var pipeName = UpdateHelperProtocol.ServerPipePrefix + id;
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);
            var connectTask = pipe.WaitForConnectionAsync(connectCts.Token);

            var psi = CreateStartInfo(request.Host, helperExe, helperDir, pipeName, id);
            log($"Starting update helper: {psi.FileName} {string.Join(" ", psi.ArgumentList)}");

            using var process = Process.Start(psi) ?? throw new UpdateInstallException("Failed to start the update helper");
            try
            {
                await process.StandardInput.WriteLineAsync(secret).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                process.StandardInput.Close();

                try
                {
                    await connectTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new UpdateInstallException("The update helper did not connect in time");
                }

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

                var hello = await ReceiveAsync(reader, MessageTimeout, cancellationToken).ConfigureAwait(false);
                if (hello == null || hello.Type != UpdateHelperProtocol.TypeHello)
                    throw new UpdateInstallException($"Expected hello from the update helper, got {hello?.Type ?? "nothing"}");
                if (hello.ProtocolVersion != UpdateHelperProtocol.ProtocolVersion)
                    throw new UpdateInstallException($"The update helper speaks protocol version {hello.ProtocolVersion}, expected {UpdateHelperProtocol.ProtocolVersion}");
                if (!SecretsMatch(hello.Secret, secret))
                    throw new UpdateInstallException("The update helper presented a wrong secret");

                var helperPid = hello.Pid > 0 ? hello.Pid : process.Id;
                log($"Update helper {hello.Version} connected, pid {helperPid}");

                var job = CreateJob(request, helperDir);
                await SendAsync(writer, new HelperMessage { Type = UpdateHelperProtocol.TypeJob, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = Environment.ProcessId, Version = request.CurrentVersion, Job = job }).ConfigureAwait(false);

                var reply = await ReceiveAsync(reader, MessageTimeout, cancellationToken).ConfigureAwait(false);
                if (reply == null)
                    throw new UpdateInstallException("The update helper disconnected before confirming the job");
                if (reply.Type == UpdateHelperProtocol.TypeAbort)
                    throw new UpdateInstallException(reply.Reason ?? "The update helper aborted without a reason");
                if (reply.Type != UpdateHelperProtocol.TypeReady)
                    throw new UpdateInstallException($"Expected ready from the update helper, got {reply.Type}");

                var problem = request.FinalCheck();
                if (problem != null)
                    throw new UpdateInstallException(problem);

                var record = new PendingInstallRecord
                {
                    TargetVersion = request.TargetVersion,
                    PreviousVersion = request.CurrentVersion,
                    HelperPid = helperPid,
                    HelperListenPipe = UpdateHelperProtocol.HelperListenPipePrefix + helperPid,
                    HelperDirectory = helperDir,
                    PackagePath = request.PackagePath,
                    InstallDir = request.InstallDir,
                    AppImagePath = request.Host.AppImagePath,
                    ResultFile = job.ResultFile,
                    MarkerFolder = job.MarkerFolder,
                    StartedUtc = DateTime.UtcNow,
                    RestartKind = job.Restart.Kind,
                };
                PendingInstallStore.Write(request.UpdatesFolder, record);

                await SendAsync(writer, new HelperMessage { Type = UpdateHelperProtocol.TypeGo, ProtocolVersion = UpdateHelperProtocol.ProtocolVersion, Pid = Environment.ProcessId }).ConfigureAwait(false);
                log("Update helper told to go");

                return new UpdateLaunchResult(helperPid, record);
            }
            catch
            {
                // Closing the pipe without go makes the helper exit without changes
                try { pipe.Dispose(); } catch { }
                try { if (!process.HasExited) process.WaitForExit(5000); } catch { }
                try { if (!process.HasExited) process.Kill(); } catch { }
                throw;
            }
        }
        catch
        {
            try { Directory.Delete(helperDir, true); } catch { }
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(HostEnvironment host, string helperExe, string helperDir, string pipeName, string id)
    {
        ProcessStartInfo psi;
        if (host.Supervisor == SupervisorKind.Systemd && !string.IsNullOrWhiteSpace(host.SystemdRunPath))
        {
            // Run the helper outside the service cgroup, or stopping the unit kills it too
            psi = new ProcessStartInfo(host.SystemdRunPath);
            psi.ArgumentList.Add("--scope");
            psi.ArgumentList.Add("--quiet");
            psi.ArgumentList.Add($"--unit=duplicati-update-{id}");
            if (host.SystemdUserUnit)
                psi.ArgumentList.Add("--user");
            psi.ArgumentList.Add(helperExe);
        }
        else
        {
            psi = new ProcessStartInfo(helperExe);
        }

        psi.ArgumentList.Add("install");
        psi.ArgumentList.Add("--pipe");
        psi.ArgumentList.Add(pipeName);
        psi.UseShellExecute = false;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = false;
        psi.RedirectStandardError = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = helperDir;
        return psi;
    }

    private static UpdateJob CreateJob(UpdateLaunchRequest request, string helperDir)
    {
        var host = request.Host;
        var format = UpdateInstallSupport.GetFormat(request.PackageTypeId);
        var terminateNames = host.Supervisor == SupervisorKind.WindowsService && format == "msi"
            ? new[] { TrayIconProcessName }
            : [];

        return new UpdateJob
        {
            PackagePath = request.PackagePath,
            PackageLength = request.PackageLength,
            PackageSha256 = request.PackageSha256,
            PackageTypeId = request.PackageTypeId,
            SignedManifest = request.SignedManifest,
            InstallDir = request.InstallDir,
            TargetVersion = request.TargetVersion,
            CurrentVersion = request.CurrentVersion,
            WaitPids = request.WaitPids,
            TerminatePids = request.TerminatePids,
            TerminateProcessNames = terminateNames,
            Restart = host.CreateRestartInstruction(),
            AppImagePath = host.AppImagePath,
            MsiProperties = request.MsiProperties,
            ResultFile = Path.Combine(helperDir, UpdateHelperProtocol.ResultFilename),
            LogFile = Path.Combine(helperDir, UpdateHelperProtocol.HelperLogFilename),
            MarkerFolder = request.UpdatesFolder,
            PortableMode = request.PortableMode,
            InteractiveSession = host.InteractiveSession,
        };
    }

    private static bool SecretsMatch(string? presented, string expected)
    {
        if (string.IsNullOrEmpty(presented))
            return false;
        var a = Encoding.UTF8.GetBytes(presented);
        var b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static Task SendAsync(StreamWriter writer, HelperMessage message)
        => writer.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions));

    private static async Task<HelperMessage?> ReceiveAsync(StreamReader reader, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        string? line;
        try
        {
            line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateInstallException("Timeout waiting for the update helper");
        }

        if (string.IsNullOrWhiteSpace(line))
            return null;
        return JsonSerializer.Deserialize<HelperMessage>(line, JsonOptions);
    }
}
