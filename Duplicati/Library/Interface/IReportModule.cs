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
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// A snapshot of a single backend event, passed to <see cref="IReportModule.OnBackendEventAsync"/>.
    /// This mirrors the data flowing through <c>Duplicati.Library.Main.IMessageSink.BackendEvent</c>
    /// but is declared here so the interface does not depend on <c>Duplicati.Library.Main</c>.
    /// </summary>
    /// <param name="Action">The backend action name (e.g. <c>Put</c>, <c>Get</c>, <c>Delete</c>).</param>
    /// <param name="Type">The event type name (e.g. <c>Started</c>, <c>Completed</c>, <c>Failed</c>).</param>
    /// <param name="Path">The target path of the backend action.</param>
    /// <param name="Size">The size of the element being transferred, or <c>0</c> when not applicable.</param>
    public record ReportBackendEvent(string Action, string Type, string Path, long Size);

    /// <summary>
    /// A snapshot of a single log entry, passed to <see cref="IReportModule.OnLogEntryAsync"/>.
    /// This mirrors the data in <c>Duplicati.Library.Logging.LogEntry</c> but is declared here so
    /// the interface does not depend on <c>Duplicati.Library.Logging</c>.
    /// </summary>
    /// <param name="Message">The formatted log message.</param>
    /// <param name="Level">The log level name (e.g. <c>Information</c>, <c>Warning</c>, <c>Error</c>).</param>
    /// <param name="Tag">The log tag associated with the entry.</param>
    /// <param name="Id">The log message id, if any.</param>
    /// <param name="Timestamp">The time the entry was created, in UTC.</param>
    /// <param name="Exception">A string representation of an associated exception, or <c>null</c>.</param>
    public record ReportLogEntry(string Message, string Level, string Tag, string Id, DateTime Timestamp, string Exception);

    /// <summary>
    /// A snapshot of the operation progress, passed to <see cref="IReportModule.OnProgressTickAsync"/>
    /// at a regular cadence while an operation is running. The cadence is controlled by the engine,
    /// not by the module; modules decide how often to act on these snapshots.
    /// </summary>
    /// <param name="Phase">The current operation phase name.</param>
    /// <param name="Progress">The overall progress, in the range <c>[0, 1]</c>.</param>
    /// <param name="FilesProcessed">The number of files processed so far.</param>
    /// <param name="FileSizeProcessed">The number of bytes processed so far.</param>
    /// <param name="FileCount">The total number of files, or <c>0</c> if still counting.</param>
    /// <param name="FileSize">The total number of bytes, or <c>0</c> if still counting.</param>
    /// <param name="CountingFiles"><c>true</c> if the file count and size are not yet final.</param>
    /// <param name="CurrentFilename">The file currently being processed, or <c>null</c>.</param>
    /// <param name="CurrentFileSize">The size of the file currently being processed.</param>
    /// <param name="CurrentFileOffset">The byte offset reached in the file currently being processed.</param>
    /// <param name="ActiveTransfers">The active backend transfers, or an empty array when none.</param>
    public record ReportProgressSnapshot(
        string Phase,
        float Progress,
        long FilesProcessed,
        long FileSizeProcessed,
        long FileCount,
        long FileSize,
        bool CountingFiles,
        string CurrentFilename,
        long CurrentFileSize,
        long CurrentFileOffset,
        ReportBackendEvent[] ActiveTransfers);

    /// <summary>
    /// Interface for implementing reporting modules that observe an operation's
    /// lifecycle and progress.
    ///
    /// Modules implementing this interface are notified when an operation starts and
    /// completes (<see cref="OnOperationStartedAsync"/> / <see cref="OnOperationCompletedAsync"/>),
    /// receive every backend event and log entry that flows through the operation's
    /// message sink (<see cref="OnBackendEventAsync"/> / <see cref="OnLogEntryAsync"/>),
    /// and are polled with periodic progress snapshots while the operation is running
    /// (<see cref="OnProgressTickAsync"/>).
    ///
    /// All callbacks are asynchronous and isolated from one another: an exception
    /// thrown by one callback is logged and does not abort subsequent callbacks or
    /// the operation itself.
    /// </summary>
    public interface IReportModule : IGenericModule
    {
        /// <summary>
        /// Gets a value indicating whether the module is configured to do work for the
        /// current operation.
        ///
        /// This is checked once after <see cref="IGenericModule.Configure"/> has been
        /// called. When <see cref="IsActive"/> is <c>false</c>, the engine skips the
        /// module entirely: no adapter is appended to the message sink, no progress
        /// ticker is started, and none of the event callbacks are invoked. This keeps
        /// the hot path free of per-event interception overhead for modules that have
        /// nothing to report.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Called when an operation has started, before any work is performed.
        /// </summary>
        /// <param name="operationName">The name of the operation (e.g. <c>Backup</c>, <c>Restore</c>).</param>
        /// <param name="result">The result object that will be populated as the operation runs.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnOperationStartedAsync(string operationName, IBasicResults result, CancellationToken cancellationToken);

        /// <summary>
        /// Called when an operation has completed, whether successfully or not.
        /// </summary>
        /// <param name="result">The populated result object.</param>
        /// <param name="exception">The exception that stopped the operation, or <c>null</c> on success.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnOperationCompletedAsync(IBasicResults result, Exception exception, CancellationToken cancellationToken);

        /// <summary>
        /// Called for each backend event reported by the operation's message sink.
        /// </summary>
        /// <param name="evt">A snapshot of the backend event.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnBackendEventAsync(ReportBackendEvent evt, CancellationToken cancellationToken);

        /// <summary>
        /// Called for each log entry written during the operation.
        /// </summary>
        /// <param name="entry">A snapshot of the log entry.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnLogEntryAsync(ReportLogEntry entry, CancellationToken cancellationToken);

        /// <summary>
        /// Called at a regular cadence while the operation is running, with a snapshot
        /// of the current operation and backend progress. The cadence is controlled by
        /// the engine; modules decide how often to act on these snapshots (for example,
        /// posting an update every 30 seconds).
        /// </summary>
        /// <param name="snapshot">A snapshot of the current progress.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task OnProgressTickAsync(ReportProgressSnapshot snapshot, CancellationToken cancellationToken);
    }
}
