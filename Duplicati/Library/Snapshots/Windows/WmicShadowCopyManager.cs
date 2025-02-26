using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#nullable enable

namespace Duplicati.Library.Snapshots.Windows;

/// <summary>
/// A shadow copy manager using the wmic commandline tool
/// </summary>
[SupportedOSPlatform("windows")]
internal class WmicShadowCopyManager : IDisposable
{
    /// <summary>
    /// The tag used for logging messages
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<SnapshotManager>();

    /// <summary>
    /// A single shadow copy
    /// </summary>
    /// <param name="shadowId">The shadow ID as a string</param>
    /// <param name="parsedId">The shadow ID as a GUID</param>
    /// <param name="originalDrive">The drive that the snapshot is for</param>
    /// <param name="mappedPath">The path that contains the snapshot</param>
    public class WmicShadowCopy(string shadowId, Guid parsedId, string originalDrive, string mappedPath) : IDisposable
    {
        /// <summary>
        /// Gets the shadow ID
        /// </summary>
        public string ShadowID { get; } = shadowId;
        /// <summary>
        /// Gets the shadow ID
        /// </summary>
        public Guid ParsedId { get; } = parsedId;
        /// <summary>
        /// Gets the drive that was originally mapped
        /// </summary>
        public string OriginalDrive { get; } = originalDrive;
        /// <summary>
        /// Gets the path where the snapshot is found
        /// </summary>
        public string MappedPath { get; } = mappedPath;

        /// <summary>
        /// Flag keeping track of the snapshot deletion state
        /// </summary>
        private bool _snapshotDeleted;

        /// <inheritdoc/>
        public void Dispose()
        {
            DeleteShadowCopy();
        }

        /// <summary>
        /// Deletes the shadow copy
        /// </summary>
        private void DeleteShadowCopy()
        {
            if (_snapshotDeleted)
                return;
            if (!string.IsNullOrEmpty(ShadowID))
            {
                _snapshotDeleted = true;
                Logging.Log.WriteVerboseMessage(LOGTAG, "DeleteShadowCopy", $"Deleting Shadow Copy: {ShadowID}");
                DeleteShadow(ShadowID);
            }
        }
    }

    /// <summary>
    /// The list of the currently registered shadow copies
    /// </summary>
    private List<WmicShadowCopy> _shadowCopies = new List<WmicShadowCopy>();

    /// <summary>
    /// Gets the list of the currently registered shadow copies
    /// </summary>
    public IEnumerable<WmicShadowCopy> ShadowCopies => _shadowCopies;

    /// <summary>
    /// Creates a new snapshot for the given drive
    /// </summary>
    /// <param name="drive">The drive to create the snapshot for</param>
    /// <returns>The created snapshot</returns>
    public WmicShadowCopy Add(string drive)
    {
        var shadowId = CreateShadowCopy(drive);
        if (string.IsNullOrEmpty(shadowId))
            throw new InvalidOperationException("Failed to create shadow copy");

        var shadowPath = GetShadowPath(shadowId);            
        if (string.IsNullOrEmpty(shadowPath))
        {
            DeleteShadow(shadowId);
            throw new InvalidOperationException("Failed to get shadow copy path");
        }
        
        var snapshot = new WmicShadowCopy(shadowId, Guid.Parse(shadowId), drive, shadowPath);
        _shadowCopies.Add(snapshot);
        
        return snapshot;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var shadow in ShadowCopies)
        {
            shadow.Dispose();
        }
        _shadowCopies.Clear();
    }

    /// <summary>
    /// Creates a shadow copy
    /// </summary>
    /// <param name="drive">The drive to create the snapshot for</param>
    /// <returns>The shadow id</returns>
    private static string? CreateShadowCopy(string drive)
    {
        string output = ExecuteCommand("wmic", $"shadowcopy call create Volume='{drive}'", 10000);

        // Extract ShadowID using regex
        Match match = Regex.Match(output, @"ShadowID\s*=\s*""({[0-9A-F\-]+})""");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", null, "Failed to create shadow copy for {0}: {1}", drive, output);
        return null;
    }

    /// <summary>
    /// Gets the path where the shadow copy is mounted
    /// </summary>
    /// <param name="shadowId">The shadow copy id</param>
    /// <returns>The path where the copy is mounted</returns>
    private static string? GetShadowPath(string shadowId)
    {
        string output = ExecuteCommand("wmic", "shadowcopy get ID, DeviceObject", 5000);

        // Extract DeviceObject corresponding to the ShadowID
        string pattern = $@"(\\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy\d+)\s+{shadowId}";
        Match match = Regex.Match(output, pattern);

        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", null, "Failed to get shadow copy path for {0}: {1}", shadowId, output);
        return null;
    }

    /// <summary>
    /// Returns the drives that are vss enabled
    /// </summary>
    /// <returns></returns>
    public static HashSet<string> GetVssCapableDrivesViaVssadmin()
    {
        var vssDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var output = ExecuteCommand("vssadmin", "list volumes", 5000);
            
            // Regex to match drive letters in output
            var matches = Regex.Matches(output, @"Volume\s+path:\s*([A-Z]:)\\?\s", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
                vssDrives.Add(match.Groups[1].Value.Substring(0, 1));
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyListFailed", ex, "Failed to list volumes"); 
        }

        return vssDrives;
    }    

    /// <summary>
    /// Delete a shadow copy
    /// </summary>
    /// <param name="shadowId">The shadow id</param>
    private static void DeleteShadow(string shadowId)
        => ExecuteCommand("wmic", $"shadowcopy where ID=\"{shadowId}\" delete", 5000);

    /// <summary>
    /// Executes a command
    /// </summary>
    /// <param name="fileName">The binary to execute</param>
    /// <param name="arguments">The arguments to use</param>
    /// <param name="timeoutMs">The timeout</param>
    /// <returns>The output of the command</returns>
    private static string ExecuteCommand(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = fileName;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                    throw new TimeoutException($"Command '{fileName} {arguments}' timed out after {timeoutMs}ms.");
                }

                var output = outputTask.Result;
                var error = errorTask.Result;

                if (!string.IsNullOrWhiteSpace(error))
                    Logging.Log.WriteWarningMessage(LOGTAG, "ShadowCopyFailed", null, "Failed to execute command: {0} {1}: {2}", fileName, arguments, error); 

                return output;
            }
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", ex, "Failed to execute command: {0} {1}", fileName, arguments); 
            return string.Empty;
        }
    }
}