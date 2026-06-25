using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending LIST operation
    /// </summary>
    /// <remarks>
    /// Creates a new ListOperation
    /// </remarks>
    /// <param name="path">The path to list, or null for the root folder</param>
    /// <param name="context">The execution context</param>
    /// <param name="cancelToken">The cancellation token</param>
    private class ListOperation(string? path, ExecuteContext context, CancellationToken cancelToken)
        : PendingOperation<List<IFileEntry>>(context, true, cancelToken)
    {
        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.List;

        /// <summary>
        /// When true, the operation performs a flat (root) listing via
        /// <see cref="IBackend.ListAsync(CancellationToken)"/> regardless of
        /// <paramref name="path"/>. This is used for backends that do not support
        /// folder operations: the backend manager points the backend at the
        /// sub-folder URL (via <see cref="PendingOperationBase.BackendUrlOverride"/>)
        /// and the operation lists that folder flat. When false and <c>path</c> is
        /// non-null, the folder-enabled backend's folder-scoped list is used.
        /// </summary>
        public bool UseRootList { get; set; }

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to list</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        public override async Task<List<IFileEntry>> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            Context.Statwriter.SendEvent(BackendActionType.List, BackendEventType.Started, null, -1);

            // TODO: Consider returning IAsyncEnumerable instead of List
            List<IFileEntry> r;
            if (!UseRootList && path != null)
            {
                if (backend is not IFolderEnabledBackend folderBackend)
                    throw new NotSupportedException("The backend does not support folder operations");
                r = await folderBackend.ListAsync(path, cancelToken).ToListAsync(cancelToken).ConfigureAwait(false);
            }
            else
            {
                // Flat listing of whatever URL the backend is bound to. For folder-enabled
                // backends this is the root; for non-folder backends the backend manager
                // points the backend at the sub-folder URL so this lists that folder.
                r = await backend.ListAsync(cancelToken).ToListAsync(cancelToken).ConfigureAwait(false);
            }

            // TODO: Investigate better way to solve this so we do not use memory for large lists
            var sb = new StringBuilder();
            sb.AppendLine("[");
            long count = 0;
            foreach (var e in r)
            {
                if (count != 0)
                    sb.AppendLine(",");
                count++;
                sb.Append(System.Text.Json.JsonSerializer.Serialize(e));
            }

            sb.AppendLine();
            sb.Append("]");
            Context.Database.LogRemoteOperation("list", "", sb.ToString());

            Context.Statwriter.SendEvent(BackendActionType.List, BackendEventType.Completed, null, r.Count);

            return r;
        }
    }
}