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
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// The numbers of a finished backup
/// </summary>
/// <param name="ExaminedFiles">The number of files examined</param>
/// <param name="SizeOfExaminedFiles">The size of the examined files in bytes</param>
/// <param name="AddedFiles">The number of files added</param>
/// <param name="ModifiedFiles">The number of files modified</param>
/// <param name="DeletedFiles">The number of files deleted</param>
/// <param name="FilesWithError">The number of files that could not be read</param>
/// <param name="PartialBackup">True if the backup was stopped before it had processed all files</param>
/// <param name="VersionTime">The time of the version that holds what the backup captured, in UTC, or <c>null</c> if the backup was stopped before it finished or the time is not known. It is left out when the summary is written as JSON, as the reader gets it only for a backup that succeeded and a version that still exists.</param>
public sealed record BackupResultSummary(
    long ExaminedFiles,
    long SizeOfExaminedFiles,
    long AddedFiles,
    long ModifiedFiles,
    long DeletedFiles,
    long FilesWithError,
    bool PartialBackup,
    [property: System.Text.Json.Serialization.JsonIgnore] DateTime? VersionTime);

/// <summary>
/// The numbers of a finished restore
/// </summary>
/// <param name="RestoredFiles">The number of files restored</param>
/// <param name="SizeOfRestoredFiles">The size of the restored files in bytes</param>
/// <param name="RestoredFolders">The number of folders restored</param>
/// <param name="RestoredSymlinks">The number of symlinks restored</param>
/// <param name="UnmodifiedFiles">The number of files that already had the content to restore</param>
/// <param name="RestorePath">The folder restored to, or <c>null</c> when files were restored to their original location</param>
public sealed record RestoreResultSummary(
    long RestoredFiles,
    long SizeOfRestoredFiles,
    long RestoredFolders,
    long RestoredSymlinks,
    long UnmodifiedFiles,
    string? RestorePath);

/// <summary>
/// A compact description of the results of a finished task.
/// It holds numbers and a capped number of messages, so it is cheap to keep in the task result cache,
/// unlike the results object, which can hold every warning the operation produced.
/// </summary>
/// <param name="ParsedResult">The overall outcome of the operation</param>
/// <param name="BeginTime">The time the operation started, in UTC</param>
/// <param name="EndTime">The time the operation ended, in UTC</param>
/// <param name="Interrupted">True if the operation was interrupted</param>
/// <param name="WarningCount">The total number of warnings</param>
/// <param name="Warnings">The first warnings, at most <see cref="MaxMessages"/></param>
/// <param name="ErrorCount">The total number of errors</param>
/// <param name="Errors">The first errors, at most <see cref="MaxMessages"/></param>
/// <param name="Backup">The backup numbers, or <c>null</c> if the task was not a backup</param>
/// <param name="Restore">The restore numbers, or <c>null</c> if the task was not a restore</param>
public sealed record TaskResultSummary(
    ParsedResultType ParsedResult,
    DateTime BeginTime,
    DateTime EndTime,
    bool Interrupted,
    int WarningCount,
    IReadOnlyList<string> Warnings,
    int ErrorCount,
    IReadOnlyList<string> Errors,
    BackupResultSummary? Backup,
    RestoreResultSummary? Restore)
{
    /// <summary>
    /// The maximum number of warnings, and of errors, kept in a summary
    /// </summary>
    public const int MaxMessages = 5;

    /// <summary>
    /// The maximum length of a message kept in a summary
    /// </summary>
    public const int MaxMessageLength = 500;

    /// <summary>
    /// Creates a summary from the results of an operation
    /// </summary>
    /// <param name="result">The results, or <c>null</c> if the operation produced none</param>
    /// <returns>The summary, or <c>null</c> if there are no results</returns>
    public static TaskResultSummary? FromResults(IBasicResults? result)
    {
        if (result == null)
            return null;

        var warnings = result.Warnings?.ToList() ?? [];
        var errors = result.Errors?.ToList() ?? [];

        return new TaskResultSummary(
            result.ParsedResult,
            result.BeginTime.ToUniversalTime(),
            result.EndTime.ToUniversalTime(),
            result.Interrupted,
            warnings.Count,
            Cap(warnings),
            errors.Count,
            Cap(errors),
            result is IBackupResults b
                ? new BackupResultSummary(b.ExaminedFiles, b.SizeOfExaminedFiles, b.AddedFiles, b.ModifiedFiles, b.DeletedFiles, b.FilesWithError, b.PartialBackup, GetVersionTime(b))
                : null,
            result is IRestoreResults r
                ? new RestoreResultSummary(r.RestoredFiles, r.SizeOfRestoredFiles, r.RestoredFolders, r.RestoredSymlinks, r.UnmodifiedFiles, string.IsNullOrWhiteSpace(r.RestorePath) ? null : r.RestorePath)
                : null
        );
    }

    /// <summary>
    /// Gets the time of the version a backup has left as the newest
    /// </summary>
    /// <param name="result">The results of the backup</param>
    /// <returns>The time in UTC, or <c>null</c> if the results do not hold it</returns>
    private static DateTime? GetVersionTime(IBackupResults result)
    {
        // The newest version is recorded as the last step of a backup. A backup that is stopped, or only
        // simulated, skips that step, which leaves the version that was the newest before the backup started.
        if (result.Interrupted || result.PartialBackup || result.Dryrun)
            return null;

        if (result.BackendStatistics is not IParsedBackendStatistics stats)
            return null;

        // A backup without versions has the zero time here, which is no longer zero after a time zone conversion
        var time = stats.LastBackupDate.ToUniversalTime();
        return time > DateTime.UnixEpoch ? time : null;
    }

    /// <summary>
    /// Limits the number of messages and the length of each
    /// </summary>
    /// <param name="messages">The messages to limit</param>
    /// <returns>The limited messages</returns>
    private static List<string> Cap(List<string> messages)
        => messages
            .Take(MaxMessages)
            .Select(x => x.Length <= MaxMessageLength ? x : x.Substring(0, MaxMessageLength) + "…")
            .ToList();
}
