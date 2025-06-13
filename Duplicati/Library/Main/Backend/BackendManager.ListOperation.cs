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
    /// <param name="context">The execution context</param>
    /// <param name="cancelToken">The cancellation token</param>
    private class ListOperation(ExecuteContext context, CancellationToken cancelToken)
        : PendingOperation<List<IFileEntry>>(context, true, cancelToken)
    {
        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.List;

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
            var r = await backend.ListAsync(cancelToken).ToListAsync(cancelToken).ConfigureAwait(false);

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