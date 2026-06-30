using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending CREATEFOLDER operation. The backend is created bound to
    /// the operation's <see cref="PendingOperationBase.BackendUrlOverride"/> (or the
    /// base backend URL when null), and <see cref="IBackend.CreateFolderAsync"/> is
    /// invoked to create the folder at that URL. A <see cref="FolderAreadyExistedException"/>
    /// is treated as success (the folder already being present is the desired state).
    /// Used by sync to ensure a destination sub-folder exists before uploading a file
    /// into it (the backend manager points the backend at the sub-folder URL, so the
    /// folder gets created at the right location regardless of whether the backend is
    /// folder-enabled).
    /// </summary>
    /// <remarks>
    /// Creates a new CreateFolderOperation.
    /// </remarks>
    /// <param name="context">The execution context</param>
    /// <param name="cancelToken">The cancellation token</param>
    private class CreateFolderOperation(ExecuteContext context, CancellationToken cancelToken)
        : PendingOperation<bool>(context, true, cancelToken)
    {
        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.CreateFolder;

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to create the folder on</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task returning true when the folder exists after the call</returns>
        public override async Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            Context.Statwriter.SendEvent(BackendActionType.CreateFolder, BackendEventType.Started, null, 0);

            try
            {
                await backend.CreateFolderAsync(cancelToken).ConfigureAwait(false);
            }
            catch (FolderAreadyExistedException)
            {
                // The folder already exists, which is the desired outcome of "ensure".
            }

            Context.Statwriter.SendEvent(BackendActionType.CreateFolder, BackendEventType.Completed, null, 0);
            return true;
        }
    }
}
