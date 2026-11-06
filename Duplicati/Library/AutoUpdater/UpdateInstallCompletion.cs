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
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.AutoUpdater;

/// <summary>
/// Completes a pending install from the new instance: tells the helper we are up, waits for it, cleans up
/// </summary>
public static class UpdateInstallCompletion
{
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HelperExitTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Completes the pending install
    /// </summary>
    /// <param name="updatesFolder">The updates folder</param>
    /// <param name="record">The pending record</param>
    /// <param name="currentVersion">The version now running</param>
    /// <param name="log">A log callback</param>
    /// <param name="cancellationToken">A cancellation token</param>
    /// <returns>The result reported by the helper, or a synthesized failure</returns>
    public static async Task<InstallResult> CompleteAsync(string updatesFolder, PendingInstallRecord record, string currentVersion, Action<string> log, CancellationToken cancellationToken)
    {
        var started = new HelperMessage
        {
            Type = UpdateHelperProtocol.TypeStarted,
            ProtocolVersion = UpdateHelperProtocol.ProtocolVersion,
            Pid = Environment.ProcessId,
            Version = currentVersion,
        };
        var line = JsonSerializer.Serialize(started);

        var helperRunning = IsRunning(record.HelperPid);
        if (helperRunning)
        {
            if (!await TrySignalPipeAsync(record.HelperListenPipe, line, log, cancellationToken).ConfigureAwait(false))
                WriteMarker(record, line, log);

            log($"Waiting for the update helper (pid {record.HelperPid}) to exit");
            var deadline = DateTime.UtcNow + HelperExitTimeout;
            while (IsRunning(record.HelperPid) && DateTime.UtcNow < deadline)
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            if (IsRunning(record.HelperPid))
                log("The update helper did not exit in time; cleaning up anyway");
        }
        else
        {
            log($"The update helper (pid {record.HelperPid}) is no longer running");
        }

        var result = PendingInstallStore.ReadResult(record.ResultFile) ?? new InstallResult
        {
            Success = false,
            Stage = "unknown",
            Error = helperRunning
                ? "The update helper exited without writing a result"
                : "The update helper was not running when the server started; the previous install attempt may have failed",
            TargetVersion = record.TargetVersion,
            StartedUtc = record.StartedUtc,
            FinishedUtc = DateTime.UtcNow,
            LogFile = Path.Combine(record.HelperDirectory, UpdateHelperProtocol.HelperLogFilename),
        };

        // If the helper reported success, but the running version is not the target, something restarted the old version
        if (result.Success && !string.Equals(currentVersion, record.TargetVersion, StringComparison.OrdinalIgnoreCase)
            && UpdaterManager.TryParseVersion(currentVersion) < UpdaterManager.TryParseVersion(record.TargetVersion))
        {
            result.Success = false;
            result.Stage = "verify";
            result.Error = $"The update helper reported success, but version {currentVersion} is running instead of {record.TargetVersion}";
        }

        Cleanup(updatesFolder, record, result, log);
        return result;
    }

    private static async Task<bool> TrySignalPipeAsync(string pipeName, string line, Action<string> log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            return false;

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(PipeTimeout);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            log($"Signalled the update helper on {pipeName}");
            return true;
        }
        catch (Exception ex)
        {
            log($"Could not signal the update helper on {pipeName}: {ex.Message}");
            return false;
        }
    }

    private static void WriteMarker(PendingInstallRecord record, string line, Action<string> log)
    {
        try
        {
            var folder = string.IsNullOrWhiteSpace(record.MarkerFolder) ? Path.GetDirectoryName(record.ResultFile) ?? "" : record.MarkerFolder;
            var marker = Path.Combine(folder, UpdateHelperProtocol.StartedMarkerPrefix + record.HelperPid + ".json");
            // Write to a temporary name and rename, so the helper never reads a partial marker
            var temp = marker + ".tmp-" + Environment.ProcessId;
            File.WriteAllText(temp, line);
            File.Move(temp, marker, true);
            log($"Wrote the started marker {marker}");
        }
        catch (Exception ex)
        {
            log($"Could not write the started marker: {ex.Message}");
        }
    }

    private static void Cleanup(string updatesFolder, PendingInstallRecord record, InstallResult result, Action<string> log)
    {
        // The result file is written by the helper; only delete leftovers where the installers put them
        foreach (var path in result.CleanupPaths)
        {
            if (IsAllowedCleanupPath(path, record, updatesFolder))
                TryDelete(path, log);
            else
                log($"Ignoring cleanup path outside the installation: {path}");
        }

        if (!string.IsNullOrWhiteSpace(record.PackagePath))
        {
            if (IsInsideFolder(record.PackagePath, updatesFolder))
                TryDelete(record.PackagePath, log);
            else
                log($"Ignoring package path outside the updates folder: {record.PackagePath}");
        }

        // Keep the helper folder when the install failed, so the log can be inspected
        if (result.Success && IsInsideFolder(record.HelperDirectory, updatesFolder))
            TryDelete(record.HelperDirectory, log, retries: 10);

        try
        {
            var marker = Path.Combine(record.MarkerFolder, UpdateHelperProtocol.StartedMarkerPrefix + record.HelperPid + ".json");
            if (File.Exists(marker))
                File.Delete(marker);
        }
        catch { }

        try { PendingInstallStore.Delete(updatesFolder); }
        catch (Exception ex) { log($"Could not delete the pending install record: {ex.Message}"); }
    }

    /// <summary>
    /// Checks that a cleanup path is one the installers produce: inside the updates folder, inside the
    /// install folder, or a sibling of the install folder or the AppImage named like a leftover
    /// (<c>name.old-*</c> or <c>name.failed-*</c>)
    /// </summary>
    /// <param name="path">The path the helper asked to delete</param>
    /// <param name="record">The pending record</param>
    /// <param name="updatesFolder">The updates folder</param>
    /// <returns>True if the path may be deleted</returns>
    internal static bool IsAllowedCleanupPath(string path, PendingInstallRecord record, string updatesFolder)
    {
        try
        {
            if (IsInsideFolder(path, updatesFolder))
                return true;
            if (!string.IsNullOrWhiteSpace(record.InstallDir))
            {
                if (IsInsideFolder(path, record.InstallDir))
                    return true;

                // Leftovers sit next to the install folder, or next to the application bundle
                // that contains it (Name.app/Contents/MacOS on macOS)
                var candidate = (string?)Normalize(record.InstallDir);
                for (var i = 0; i < 3 && !string.IsNullOrEmpty(candidate); i++)
                {
                    if (IsLeftoverOf(path, candidate))
                        return true;
                    candidate = Path.GetDirectoryName(candidate);
                }
            }
            if (!string.IsNullOrWhiteSpace(record.AppImagePath) && IsLeftoverOf(path, record.AppImagePath))
                return true;
        }
        catch (Exception)
        {
            // Malformed paths are not deleted
        }

        return false;
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsInsideFolder(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
            return false;

        var full = Normalize(path);
        var root = Normalize(folder);
        return root.Length > 0 && full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool IsLeftoverOf(string path, string original)
    {
        var full = Normalize(path);
        var orig = Normalize(original);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetDirectoryName(orig), PathComparison))
            return false;

        var name = Path.GetFileName(full);
        var origName = Path.GetFileName(orig);
        return origName.Length > 0
            && (name.StartsWith(origName + ".old-", PathComparison) || name.StartsWith(origName + ".failed-", PathComparison));
    }

    private static void TryDelete(string path, Action<string> log, int retries = 1)
    {
        for (var i = 0; i < retries; i++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch (Exception ex)
            {
                if (i == retries - 1)
                {
                    log($"Could not delete {path}: {ex.Message}");
                    return;
                }
                Thread.Sleep(1000);
            }
        }
    }

    private static bool IsRunning(int pid)
    {
        if (pid <= 0)
            return false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
