// Copyright (C) 2025, The Duplicati Team
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

using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;

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
        var results = new List<FolderStatusDto>();
        var activeBackupIds = GetActiveBackupIds();
        var backups = _connection.Backups;

        foreach (var backup in backups)
        {
            if (backup.Sources == null)
                continue;

            var status = DetermineBackupStatus(backup, activeBackupIds);
            var lastBackupTime = GetLastBackupTime(backup);

            foreach (var source in backup.Sources)
            {
                if (string.IsNullOrEmpty(source))
                    continue;

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
        var backups = _connection.Backups;

        foreach (var backup in backups)
        {
            if (backup.Sources == null)
                continue;

            foreach (var source in backup.Sources)
            {
                if (string.IsNullOrEmpty(source))
                    continue;

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
    /// Gets the IDs of currently running backups
    /// </summary>
    private HashSet<string> GetActiveBackupIds()
    {
        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check current task
        var currentTask = _queueRunnerService.GetCurrentTask();
        if (currentTask?.BackupID != null &&
            currentTask.Operation == Server.Serialization.DuplicatiOperation.Backup)
        {
            activeIds.Add(currentTask.BackupID);
        }

        // Check queued tasks
        var queuedTasks = _queueRunnerService.GetCurrentTasks();
        foreach (var task in queuedTasks)
        {
            if (task.BackupID != null &&
                task.Operation == Server.Serialization.DuplicatiOperation.Backup)
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
    {
        // Check if backup is currently running
        if (backup.ID != null && activeBackupIds.Contains(backup.ID))
        {
            return FolderBackupStatusValues.InProgress;
        }

        // Check metadata for last backup result
        if (backup.Metadata == null ||
            !backup.Metadata.TryGetValue("LastBackupDate", out var lastDateStr) ||
            string.IsNullOrEmpty(lastDateStr))
        {
            return FolderBackupStatusValues.Never;
        }

        // Check for errors
        if (backup.Metadata.TryGetValue("LastErrorDate", out var errorDate) &&
            !string.IsNullOrEmpty(errorDate))
        {
            // Check if error date is the same as last backup date
            if (DateTime.TryParse(lastDateStr, out var lastDt) &&
                DateTime.TryParse(errorDate, out var errorDt) &&
                Math.Abs((lastDt - errorDt).TotalMinutes) < 1)
            {
                return FolderBackupStatusValues.Failed;
            }
        }

        // Check for warnings
        if (backup.Metadata.TryGetValue("LastWarningDate", out var warningDate) &&
            !string.IsNullOrEmpty(warningDate))
        {
            // Check if warning date is the same as last backup date
            if (DateTime.TryParse(lastDateStr, out var lastDt) &&
                DateTime.TryParse(warningDate, out var warningDt) &&
                Math.Abs((lastDt - warningDt).TotalMinutes) < 1)
            {
                return FolderBackupStatusValues.Warning;
            }
        }

        return FolderBackupStatusValues.BackedUp;
    }

    /// <summary>
    /// Gets the last backup time from backup metadata
    /// </summary>
    private DateTime? GetLastBackupTime(IBackup backup)
    {
        if (backup.Metadata != null &&
            backup.Metadata.TryGetValue("LastBackupDate", out var dateStr) &&
            DateTime.TryParse(dateStr, out var date))
        {
            return date;
        }

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
