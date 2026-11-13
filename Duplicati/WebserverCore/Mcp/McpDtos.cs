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

using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Mcp;

/// <summary>
/// A task in the server's queue
/// </summary>
/// <param name="TaskId">The ID of the task</param>
/// <param name="BackupId">The ID of the backup the task works on</param>
/// <param name="BackupName">The name of the backup the task works on</param>
/// <param name="Operation">The operation the task performs</param>
/// <param name="Status">Either Running or Waiting</param>
public sealed record McpQueueItem(long TaskId, string? BackupId, string? BackupName, string Operation, string Status);

/// <summary>
/// The state of the server
/// </summary>
/// <param name="Version">The server version</param>
/// <param name="Paused">True if the server is paused; a paused server does not start queued tasks</param>
/// <param name="Queue">The running task followed by the waiting tasks</param>
public sealed record McpServerStatus(string Version, bool Paused, IReadOnlyList<McpQueueItem> Queue);

/// <summary>
/// A configured backup, without credentials or settings
/// </summary>
/// <param name="Id">The ID of the backup</param>
/// <param name="Name">The name of the backup</param>
/// <param name="Description">The description of the backup</param>
/// <param name="OperationType">Backup or Sync</param>
/// <param name="Sources">The source paths</param>
/// <param name="DestinationType">The storage type of the destination, such as s3 or file</param>
/// <param name="LastBackupStarted">When the last successful backup started</param>
/// <param name="LastBackupFinished">When the last successful backup finished</param>
/// <param name="LastErrorDate">When the last failed run happened</param>
/// <param name="LastErrorMessage">The message of the last failed run</param>
/// <param name="VersionCount">The number of versions in the backup</param>
/// <param name="ActiveTaskId">The ID of the queued or running task for this backup, if any</param>
public sealed record McpBackupInfo(
    string Id,
    string Name,
    string? Description,
    string OperationType,
    IReadOnlyList<string> Sources,
    string? DestinationType,
    string? LastBackupStarted,
    string? LastBackupFinished,
    string? LastErrorDate,
    string? LastErrorMessage,
    long? VersionCount,
    long? ActiveTaskId);

/// <summary>
/// The list of configured backups
/// </summary>
/// <param name="Backups">The backups</param>
public sealed record McpBackupList(IReadOnlyList<McpBackupInfo> Backups);

/// <summary>
/// The progress of a running task
/// </summary>
/// <param name="Phase">The phase the operation is in</param>
/// <param name="OverallProgress">The progress from 0 to 1</param>
/// <param name="ProcessedFileCount">The number of files processed</param>
/// <param name="TotalFileCount">The number of files to process</param>
/// <param name="ProcessedFileSize">The number of bytes processed</param>
/// <param name="TotalFileSize">The number of bytes to process</param>
/// <param name="StillCounting">True if the totals are still growing</param>
public sealed record McpProgress(string Phase, float OverallProgress, long ProcessedFileCount, long TotalFileCount, long ProcessedFileSize, long TotalFileSize, bool StillCounting);

/// <summary>
/// The version to restore from to get the state a backup captured
/// </summary>
/// <param name="Time">The time of the version, to pass as the time of a restore</param>
/// <param name="Version">The version number at the time of the call, where 0 is the newest; <c>null</c> if the versions could not be read because the backup is in use</param>
public sealed record McpRestorePoint(string Time, long? Version);

/// <summary>
/// The state of a task
/// </summary>
/// <param name="TaskId">The ID of the task</param>
/// <param name="Status">Waiting, Running, Completed or Failed</param>
/// <param name="Finished">True if the task has ended; false means call wait_for_task again</param>
/// <param name="BackupId">The ID of the backup the task works on</param>
/// <param name="Operation">The operation the task performs, when known</param>
/// <param name="AlreadyQueued">True if the request found a task for the backup already queued or running and returned that one</param>
/// <param name="ServerPaused">True if the server is paused, in which case a waiting task does not start</param>
/// <param name="ErrorMessage">The error, if the task failed</param>
/// <param name="Progress">The progress, if the task is running</param>
/// <param name="Result">The result numbers and messages, if the task finished with results</param>
/// <param name="RestorePoint">For a finished backup, the version that holds what it captured</param>
public sealed record McpTaskInfo(
    long TaskId,
    string Status,
    bool Finished,
    string? BackupId,
    string? Operation,
    bool AlreadyQueued,
    bool ServerPaused,
    string? ErrorMessage,
    McpProgress? Progress,
    TaskResultSummary? Result,
    McpRestorePoint? RestorePoint);

/// <summary>
/// A version of a backup
/// </summary>
/// <param name="Version">The version number; 0 is the newest</param>
/// <param name="Time">The time of the version</param>
/// <param name="IsFullBackup">False if the backup was interrupted and the version is partial</param>
/// <param name="FileCount">The number of files in the version</param>
/// <param name="FileSizes">The size of the files in the version</param>
public sealed record McpVersion(long Version, string Time, bool IsFullBackup, long? FileCount, long? FileSizes);

/// <summary>
/// The versions of a backup
/// </summary>
/// <param name="Versions">The versions, newest first</param>
public sealed record McpVersionList(IReadOnlyList<McpVersion> Versions);

/// <summary>
/// A file or folder in a backup
/// </summary>
/// <param name="Path">The full path</param>
/// <param name="IsDirectory">True for a folder</param>
/// <param name="Size">The size in bytes</param>
/// <param name="LastModified">The last modification time</param>
/// <param name="Version">The version the entry was found in, for searches</param>
public sealed record McpFileEntry(string Path, bool IsDirectory, long Size, DateTime LastModified, long? Version);

/// <summary>
/// One page of files and folders
/// </summary>
/// <param name="Entries">The entries on this page</param>
/// <param name="Page">The page number, starting at 0</param>
/// <param name="PageSize">The page size</param>
/// <param name="TotalCount">The number of entries on all pages, which is only counted for the first page</param>
public sealed record McpFileList(IReadOnlyList<McpFileEntry> Entries, int Page, int PageSize, long? TotalCount);
