using System;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending QuotaInfo operation
    /// </summary>
    /// <remarks>
    /// Creates a new WaitForEmptyOperation
    /// </remarks>
    /// <param name="context">The execution context</param>
    /// <param name="cancelToken">The cancellation token</param>
    private class WaitForEmptyOperation(ExecuteContext context, CancellationToken cancelToken)
        : PendingOperation<bool>(context, true, cancelToken)
    {
        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.WaitForEmpty;

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to get the quota info from</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        public override Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
            => Task.FromResult(true);
    }
}