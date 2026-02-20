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

namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the backup status of a folder.
/// Used by the Windows Shell Extension to show overlay icons.
/// </summary>
public sealed record FolderStatusDto
{
    /// <summary>
    /// The full path to the folder
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The backup status: "backedup", "warning", "failed", "inprogress", "never", or "notinbackup"
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The name of the backup that includes this folder
    /// </summary>
    public string? BackupName { get; init; }

    /// <summary>
    /// The timestamp of the last successful backup
    /// </summary>
    public DateTime? LastBackupTime { get; init; }

    /// <summary>
    /// The ID of the backup that includes this folder
    /// </summary>
    public string? BackupId { get; init; }
}

/// <summary>
/// Represents the backup status values
/// </summary>
public static class FolderBackupStatusValues
{
    /// <summary>
    /// Folder is not included in any backup
    /// </summary>
    public const string NotInBackup = "notinbackup";

    /// <summary>
    /// Folder is included and backup completed successfully
    /// </summary>
    public const string BackedUp = "backedup";

    /// <summary>
    /// Folder is included but backup had warnings
    /// </summary>
    public const string Warning = "warning";

    /// <summary>
    /// Folder is included but backup failed
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// Folder is included and backup is currently running
    /// </summary>
    public const string InProgress = "inprogress";

    /// <summary>
    /// Folder is included but backup has never run
    /// </summary>
    public const string Never = "never";
}
