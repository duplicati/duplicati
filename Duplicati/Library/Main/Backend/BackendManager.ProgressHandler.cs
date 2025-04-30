using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending QuotaInfo operation
    /// </summary>
    private sealed record ProgressHandler(IBackendWriter Stats, ITaskReader TaskReader)
    {
        /// <summary>
        /// A lock guarding for single-threaded access to the list of active transfers
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// The progress of the active transfers
        /// </summary>
        private Dictionary<string, long> _activeTransferProgress = new();

        /// <summary>
        /// Information about an active transfer
        /// </summary>
        /// <param name="Filename">The filename of the transfer</param>
        /// <param name="Started">When the transfer started</param>
        /// <param name="Type">The type of the transfer</param>
        /// <param name="Size">The size of the transfer</param>
        private sealed record TransferInfo(string Filename, DateTime Started, BackendActionType Type, long Size);

        /// <summary>
        /// The information for the active transfers
        /// </summary>
        private Dictionary<string, TransferInfo> _activeTransferInfo = new();

        /// <summary>
        /// The transfer that is currently reported in progress
        /// </summary>
        private TransferInfo? _currentTransfer;

        /// <summary>
        /// Handles the progress of a stream
        /// </summary>
        /// <param name="pg">The progress</param>
        /// <param name="filename">The filename</param>
        public void HandleProgress(long pg, string filename)
        {
            // This pauses and throws on cancellation, but ignores stop
            TaskReader.TransferRendevouz().Await();

            lock (_lock)
            {
                // Record the progress of the transfer
                if (_activeTransferProgress.ContainsKey(filename))
                    _activeTransferProgress[filename] = pg;

                // Only update the progress if the filename matches the current transfer
                if (filename != _currentTransfer?.Filename)
                    return;
            }

            Stats.BackendProgressUpdater.UpdateProgress(pg);
        }

        /// <summary>
        /// Begins the transfer of a file
        /// </summary>
        /// <param name="filename">The filename</param>
        public void BeginTransfer(BackendActionType type, long size, string filename)
        {
            TransferInfo? info = null;
            lock (_lock)
            {
                // Ignore the transfer if it is already in progress
                if (_activeTransferProgress.ContainsKey(filename))
                    return;

                _activeTransferProgress.Add(filename, 0);
                info = new TransferInfo(filename, DateTime.UtcNow, type, size);
                _activeTransferInfo.Add(filename, info);

                // Don't process the event if it is not the current transfer
                if (_currentTransfer != null)
                    return;

                // This is now the current transfer
                _currentTransfer = info;
            }

            Stats.BackendProgressUpdater.StartAction(info.Type, info.Filename, info.Size, info.Started);
        }

        /// <summary>
        /// Ends the transfer of a file
        /// </summary>
        /// <param name="filename">The filename</param>
        public void EndTransfer(string filename)
        {
            TransferInfo? info = null;
            long progress = 0;

            lock (_lock)
            {
                _activeTransferProgress.Remove(filename);
                _activeTransferInfo.Remove(filename);

                // If the current transfer is not the one that ended, ignore it
                if (_currentTransfer?.Filename != filename)
                    return;

                // Find the last started transfer, and report that as the current transfer
                // We choose the last one, as it will most likely be in-progress for the longest time
                info = _currentTransfer = _activeTransferInfo.Values.OrderBy(x => x.Started).LastOrDefault();
                if (_currentTransfer != null)
                    _activeTransferProgress.TryGetValue(_currentTransfer.Filename, out progress);
            }

            if (info != null)
            {
                Stats.BackendProgressUpdater.StartAction(info.Type, info.Filename, info.Size, info.Started);
                Stats.BackendProgressUpdater.UpdateProgress(progress);
            }
        }
    }
}