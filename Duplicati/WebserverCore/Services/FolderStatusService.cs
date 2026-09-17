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

using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Service for querying folder backup status.
/// Provides information to the Windows Shell Extension for showing overlay icons.
/// </summary>
public class FolderStatusService : IFolderStatusService
{
    private readonly Connection _connection;
    private readonly IQueueRunnerService _queueRunnerService;

    /// <summary>
    /// Creates a new instance of the folder status service
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="queueRunnerService">Queue runner service for checking active backups</param>
    public FolderStatusService(Connection connection, IQueueRunnerService queueRunnerService)
    {
        _connection = connection;
        _queueRunnerService = queueRunnerService;
    }

    /// <summary>
    /// Gets the backup status for all source folders across all backups
    /// </summary>
    public IEnumerable<FolderStatusDto> GetAllFolderStatuses()
    {
        if (!_connection.ApplicationSettings.EnableFolderStatusService)
            throw new ServiceUnavailableException("Folder status service is disabled");

        var results = new List<FolderStatusDto>();

        var activeBackupIds = GetActiveBackupIds();

        foreach (var (backup, sources) in GetBackupsWithSources())
        {
            var status = DetermineBackupStatus(backup, activeBackupIds);
            var lastBackupTime = GetLastBackupTime(backup);

            foreach (var source in sources)
            {
                results.Add(new FolderStatusDto
                {
                    Path = source,
                    Status = status,
                    BackupName = backup.Name,
                    LastBackupTime = lastBackupTime,
                    BackupId = backup.ID
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Gets the backup status for a specific folder path
    /// </summary>
    public FolderStatusDto GetFolderStatus(string path)
    {
        if (!_connection.ApplicationSettings.EnableFolderStatusService)
            throw new ServiceUnavailableException("Folder status service is disabled");

        if (string.IsNullOrEmpty(path))
        {
            return new FolderStatusDto
            {
                Path = path,
                Status = FolderBackupStatusValues.NotInBackup
            };
        }

        // Normalize the path for comparison
        var normalizedPath = NormalizePath(path);
        var activeBackupIds = GetActiveBackupIds();

        foreach (var (backup, sources) in GetBackupsWithSources())
        {
            foreach (var source in sources)
            {
                var normalizedSource = NormalizePath(source);

                // Check if the path matches or is a subdirectory
                if (string.Equals(normalizedPath, normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var status = DetermineBackupStatus(backup, activeBackupIds);
                    var lastBackupTime = GetLastBackupTime(backup);

                    return new FolderStatusDto
                    {
                        Path = path,
                        Status = status,
                        BackupName = backup.Name,
                        LastBackupTime = lastBackupTime,
                        BackupId = backup.ID
                    };
                }
            }
        }

        return new FolderStatusDto
        {
            Path = path,
            Status = FolderBackupStatusValues.NotInBackup
        };
    }

    /// <summary>
    /// Loads each backup together with its folder sources.
    /// The backup list only carries the base row and metadata, so the full
    /// backup is loaded to get the sources. The full backup carries the target
    /// url and settings, so it is masked right away; only the name, id, metadata
    /// and folder sources are used. Special sources (such as source providers)
    /// are skipped, and placeholders like %MY_DOCUMENTS% are expanded to the
    /// paths Explorer will ask about.
    /// </summary>
    private IEnumerable<(IBackup Backup, string[] Sources)> GetBackupsWithSources()
    {
        foreach (var summary in _connection.Backups)
        {
            if (string.IsNullOrEmpty(summary.ID))
                continue;

            var backup = _connection.GetBackup(summary.ID);
            if (backup == null)
                continue;

            backup.MaskSensitiveInformation();
            if (backup.Sources == null)
                continue;

            var sources = backup.Sources
                .Where(x => !string.IsNullOrWhiteSpace(x) && !SourceMasking.IsSpecialSource(x))
                .Select(SpecialFolders.ExpandEnvironmentVariables)
                .ToArray();

            if (sources.Length > 0)
                yield return (backup, sources);
        }
    }

    /// <summary>
    /// Gets the IDs of currently running backups
    /// </summary>
    private HashSet<string> GetActiveBackupIds()
    {
        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check current task
        var currentTask = _queueRunnerService.GetCurrentTask();
        if (currentTask?.BackupID != null &&
            currentTask.Operation == Server.Serialization.DuplicatiOperation.BackupOrSync)
        {
            activeIds.Add(currentTask.BackupID);
        }

        // Check queued tasks
        var queuedTasks = _queueRunnerService.GetCurrentTasks();
        foreach (var task in queuedTasks)
        {
            if (task.BackupID != null &&
                task.Operation == Server.Serialization.DuplicatiOperation.BackupOrSync)
            {
                activeIds.Add(task.BackupID);
            }
        }

        return activeIds;
    }

    /// <summary>
    /// Determines the backup status for a backup configuration
    /// </summary>
    private string DetermineBackupStatus(IBackup backup, HashSet<string> activeBackupIds)
        => DetermineStatus(backup.Metadata, backup.ID != null && activeBackupIds.Contains(backup.ID));

    /// <summary>
    /// Determines the folder status from the metadata the runner records for a backup.
    /// The dates are written with <see cref="Library.Utility.Utility.SerializeDateTime"/>,
    /// so they must be read with the matching deserializer. A completed run records
    /// LastBackupFinished and a failed run records LastErrorDate, so the newer of the
    /// two tells whether the latest attempt succeeded. LastBackupDate is the time of
    /// the newest version on the destination, which does not move when a run finds no
    /// changes, so it is only used for metadata written before LastBackupFinished
    /// existed. Warnings are not recorded in the metadata, so the warning status is
    /// currently never produced here.
    /// </summary>
    /// <param name="metadata">The backup metadata, or null if none</param>
    /// <param name="isActive">True if the backup is running or queued</param>
    /// <returns>One of the <see cref="FolderBackupStatusValues"/></returns>
    public static string DetermineStatus(IDictionary<string, string>? metadata, bool isActive)
    {
        if (isActive)
            return FolderBackupStatusValues.InProgress;

        var lastBackup = GetLastBackupTime(metadata);
        var lastError = ReadDate(metadata, "LastErrorDate");

        if (lastError != null && (lastBackup == null || lastError >= lastBackup))
            return FolderBackupStatusValues.Failed;

        return lastBackup == null
            ? FolderBackupStatusValues.Never
            : FolderBackupStatusValues.BackedUp;
    }

    /// <summary>
    /// Gets the last backup time from backup metadata
    /// </summary>
    private static DateTime? GetLastBackupTime(IBackup backup)
        => GetLastBackupTime(backup.Metadata);

    /// <summary>
    /// Gets the time the last backup run completed, in UTC.
    /// Falls back to the newest version time for metadata from older versions.
    /// </summary>
    /// <param name="metadata">The backup metadata, or null if none</param>
    /// <returns>The last backup time, or null if the backup never completed</returns>
    public static DateTime? GetLastBackupTime(IDictionary<string, string>? metadata)
        => ReadDate(metadata, "LastBackupFinished") ?? ReadDate(metadata, "LastBackupDate");

    /// <summary>
    /// Reads a serialized date from the metadata
    /// </summary>
    private static DateTime? ReadDate(IDictionary<string, string>? metadata, string key)
    {
        if (metadata != null
            && metadata.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && Library.Utility.Utility.TryDeserializeDateTime(value, out var date))
            return date.ToUniversalTime();

        return null;
    }

    /// <summary>
    /// Normalizes a file path for comparison
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        try
        {
            // Get full path and remove trailing separator
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
