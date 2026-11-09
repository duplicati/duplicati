using System;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending recursive folder delete. Only queued for backends that
    /// implement both <see cref="IFolderDeleteEnabledBackend"/> and
    /// <see cref="IFolderEnabledBackend"/>, so the path is a relative path against the
    /// base backend URL.
    /// </summary>
    /// <param name="path">The relative path of the folder to delete</param>
    /// <param name="context">The execution context</param>
    /// <param name="cancelToken">The cancellation token</param>
    private class DeleteFolderOperation(string path, ExecuteContext context, CancellationToken cancelToken)
        : PendingOperation<bool>(context, true, cancelToken)
    {
        /// <summary>
        /// The relative path of the folder to delete
        /// </summary>
        public override string RemoteFilename { get; } = path;

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.DeleteFolder;

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to delete the folder on</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task returning true when the folder is gone</returns>
        public override async Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            if (backend is not IFolderDeleteEnabledBackend folderBackend)
                throw new NotSupportedException($"The backend {backend.DisplayName} cannot delete folders");

            Context.Statwriter.SendEvent(BackendActionType.DeleteFolder, BackendEventType.Started, RemoteFilename, 0);
            await folderBackend.DeleteFolderRecursiveAsync(RemoteFilename, cancelToken).ConfigureAwait(false);
            Context.Statwriter.SendEvent(BackendActionType.DeleteFolder, BackendEventType.Completed, RemoteFilename, 0);
            return true;
        }
    }
}
