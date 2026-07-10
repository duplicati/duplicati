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

using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Bridges an <see cref="IReportModule"/> to the operation's <see cref="IMessageSink"/>.
    ///
    /// The adapter is appended to the controller's message sink so it receives backend
    /// events (<see cref="BackendEvent"/>) and log entries (<see cref="WriteMessage"/>),
    /// and is handed the live <see cref="IBackendProgress"/> and
    /// <see cref="IOperationProgress"/> objects so a progress ticker can build snapshots.
    /// Each callback is forwarded to the module and isolated from failures: exceptions
    /// thrown by the module are logged and do not propagate to the engine.
    /// </summary>
    internal sealed class ReportModuleAdapter : IMessageSink
    {
        private static readonly string LOGTAG = Log.LogTagFromType<ReportModuleAdapter>();

        private readonly IReportModule m_module;
        private readonly CancellationToken m_cancellationToken;
        private IBackendProgress m_backendProgress;
        private IOperationProgress m_operationProgress;

        /// <summary>
        /// The module being bridged.
        /// </summary>
        public IReportModule Module => m_module;

        /// <summary>
        /// Constructs a new adapter for the given module.
        /// </summary>
        /// <param name="module">The report module to forward events to.</param>
        /// <param name="cancellationToken">A token used to cancel forwarded callbacks.</param>
        public ReportModuleAdapter(IReportModule module, CancellationToken cancellationToken)
        {
            m_module = module;
            m_cancellationToken = cancellationToken;
        }

        /// <inheritdoc />
        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
        {
            var evt = new ReportBackendEvent(action.ToString(), type.ToString(), path, size);
            Forward(() => m_module.OnBackendEventAsync(evt, m_cancellationToken), "OnBackendEventAsync");
        }

        /// <inheritdoc />
        public void SetBackendProgress(IBackendProgress progress)
            => m_backendProgress = progress;

        /// <inheritdoc />
        public void SetOperationProgress(IOperationProgress progress)
            => m_operationProgress = progress;

        /// <inheritdoc />
        public void WriteMessage(LogEntry entry)
        {
            if (entry == null)
                return;

            var snapshot = new ReportLogEntry(
                entry.AsString(true),
                entry.Level.ToString(),
                entry.FilterTag,
                entry.Id,
                entry.When,
                entry.Exception?.ToString());

            Forward(() => m_module.OnLogEntryAsync(snapshot, m_cancellationToken), "OnLogEntryAsync");
        }

        /// <summary>
        /// Builds a progress snapshot from the current backend and operation progress
        /// objects and invokes <see cref="IReportModule.OnProgressTickAsync"/> on the module.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task TickAsync()
        {
            var snapshot = BuildSnapshot();
            return ForwardAsync(() => m_module.OnProgressTickAsync(snapshot, m_cancellationToken), "OnProgressTickAsync");
        }

        /// <summary>
        /// Builds a snapshot of the current progress, mapping the live progress objects
        /// (which live in <c>Duplicati.Library.Main</c>) to the interface-level
        /// <see cref="ReportProgressSnapshot"/>.
        /// </summary>
        private ReportProgressSnapshot BuildSnapshot()
        {
            OperationPhase phase = default;
            float progress = 0;
            long filesProcessed = 0;
            long fileSizeProcessed = 0;
            long fileCount = 0;
            long fileSize = 0;
            bool countingFiles = false;
            string currentFilename = null;
            long currentFileSize = 0;
            long currentFileOffset = 0;

            if (m_operationProgress != null)
            {
                m_operationProgress.UpdateOverall(out phase, out progress, out filesProcessed,
                    out fileSizeProcessed, out fileCount, out fileSize, out countingFiles);
                m_operationProgress.UpdateFile(out currentFilename, out currentFileSize, out currentFileOffset, out _);
            }

            ReportBackendEvent[] activeTransfers = System.Array.Empty<ReportBackendEvent>();
            if (m_backendProgress != null)
            {
                var transfers = m_backendProgress.GetActiveTransfers();
                if (transfers != null && transfers.Length > 0)
                {
                    activeTransfers = new ReportBackendEvent[transfers.Length];
                    for (int i = 0; i < transfers.Length; i++)
                    {
                        var t = transfers[i];
                        activeTransfers[i] = new ReportBackendEvent(
                            t.Action.ToString(), "Progress", t.Path, t.Size);
                    }
                }
            }

            return new ReportProgressSnapshot(
                phase.ToString(),
                progress,
                filesProcessed,
                fileSizeProcessed,
                fileCount,
                fileSize,
                countingFiles,
                currentFilename,
                currentFileSize,
                currentFileOffset,
                activeTransfers);
        }

        private void Forward(System.Func<Task> action, string label)
            => ForwardAsync(action, label).FireAndForget();

        private async Task ForwardAsync(System.Func<Task> action, string label)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, $"ReportModule{label}", ex,
                    "Report module {0} callback {1} failed: {2}", m_module.Key, label, ex.Message);
            }
        }
    }
}
