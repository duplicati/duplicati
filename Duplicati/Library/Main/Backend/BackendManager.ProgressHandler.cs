using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Handles the progress of file transfers for the backend
    /// </summary>
    private sealed record ProgressHandler(IBackendWriter Stats, ITaskReader TaskReader)
    {
        /// <summary>
        /// Handles the progress of a stream
        /// </summary>
        /// <param name="pg">The progress</param>
        /// <param name="filename">The filename</param>
        public void HandleProgress(long pg, string filename)
        {
            // This pauses and throws on cancellation, but ignores stop
            TaskReader.TransferRendevouz().Await();
            Stats.BackendProgressUpdater.UpdateProgress(filename, pg);
        }

        /// <summary>
        /// Begins the transfer of a file
        /// </summary>
        /// <param name="filename">The filename</param>
        public void BeginTransfer(BackendActionType type, long size, string filename)
            => Stats.BackendProgressUpdater.StartAction(type, filename, size);

        /// <summary>
        /// Ends the transfer of a file
        /// </summary>
        /// <param name="type">The type of action</param>
        /// <param name="filename">The filename</param>
        public void EndTransfer(BackendActionType type, string filename)
            => Stats.BackendProgressUpdater.EndAction(type, filename);

        /// <summary>
        /// Sets whether the backend is blocking
        /// </summary>
        /// <param name="blocking">True if blocking, false otherwise</param>
        public void SetIsBlocking(bool blocking)
            => Stats.BackendProgressUpdater.SetBlocking(blocking);

    }
}