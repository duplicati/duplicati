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

namespace Duplicati.Server.Serialization.Interface
{
    /// <summary>
    /// Representation of the current active transfer.
    /// </summary>
    /// <param name="BackendAction">The action being performed by the backend</param>
    /// <param name="BackendPath">The path of the file being transferred</param>
    /// <param name="BackendFileSize">The total size of the file being transferred</param>
    /// <param name="BackendFileProgress">The current progress of the file transfer</param>
    /// <param name="BackendSpeed">The current speed of the file transfer</param>
    /// <param name="BackendIsBlocking">Indicates if the backend operation is blocking</param>
    public sealed record ActiveTransfer(
        string BackendAction,
        string BackendPath,
        long BackendFileSize,
        long BackendFileProgress,
        long BackendSpeed,
        bool BackendIsBlocking
    );

    /// <summary>
    /// Representation of a file being processed.
    /// </summary>
    /// <param name="Path">The path of the file</param>
    /// <param name="Size">The size of the file</param>
    /// <param name="Offset">How much of the file has been processed</param>
    public sealed record ActiveFile(
        string Path,
        long Size,
        long Offset
    );

    public interface IProgressEventData
    {
        string? BackupID { get; }
        long TaskID { get; }

        string BackendAction { get; }
        string? BackendPath { get; }
        long BackendFileSize { get; }
        long BackendFileProgress { get; }
        long BackendSpeed { get; }
        bool BackendIsBlocking { get; }

        string? CurrentFilename { get; }
        long CurrentFilesize { get; }
        long CurrentFileoffset { get; }
        bool CurrentFilecomplete { get; }

        string Phase { get; }
        float OverallProgress { get; }
        long ProcessedFileCount { get; }
        long ProcessedFileSize { get; }
        long TotalFileCount { get; }
        long TotalFileSize { get; }
        bool StillCounting { get; }

        ActiveTransfer[]? ActiveTransfers { get; }

        /// <summary>
        /// The files being processed in parallel, largest first.
        /// Empty for operations that process one file at a time, which report it in <see cref="CurrentFilename"/>.
        /// </summary>
        ActiveFile[]? ActiveFiles => null;

        /// <summary>
        /// The bytes read from source files. Unlike <see cref="ProcessedFileSize"/>, files skipped
        /// because they had not changed are not counted. Null for operations that do not read source files.
        /// </summary>
        long? SourceBytesRead => null;

        /// <summary>
        /// The bytes uploaded by the operation, counting completed transfers and the progress of active ones.
        /// Unlike <see cref="ActiveTransfers"/>, transfers that complete between two progress reads are included.
        /// </summary>
        long? BackendBytesUploaded => null;

        /// <summary>
        /// The bytes downloaded by the operation, counted like <see cref="BackendBytesUploaded"/>.
        /// </summary>
        long? BackendBytesDownloaded => null;

        /// <summary>
        /// The 1-based index of the current remote sync destination being processed.
        /// 0 when not in a remote sync phase.
        /// </summary>
        int RemoteSyncDestinationIndex { get; }

        /// <summary>
        /// The total number of remote sync destinations to process.
        /// 0 when not in a remote sync phase.
        /// </summary>
        int RemoteSyncDestinationCount { get; }

        /// <summary>
        /// The EWMA-smoothed estimated time remaining for the current operation,
        /// or <c>null</c> when no estimate is available.
        /// </summary>
        TimeSpan? EstimatedTimeToCompletion { get; }
    }
}
