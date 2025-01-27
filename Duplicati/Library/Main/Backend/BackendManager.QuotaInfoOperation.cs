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
    /// Creates a new QuotaInfoOperation
    /// </remarks>
    /// <param name="context">The execution context</param>
    /// <param name="cancelToken">The cancellation token</param>
    private class QuotaInfoOperation(ExecuteContext context, CancellationToken cancelToken)
        : PendingOperation<IQuotaInfo?>(context, true, cancelToken)
    {
        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.QuotaInfo;

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to get the quota info from</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        public override async Task<IQuotaInfo?> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            IQuotaInfo? quotaInfo = null;
            if (backend is IQuotaEnabledBackend quotaEnabledBackend && !Context.Options.QuotaDisable)
            {
                Context.Statwriter.SendEvent(BackendActionType.QuotaInfo, BackendEventType.Started, null, 0);
                quotaInfo = await quotaEnabledBackend.GetQuotaInfoAsync(cancelToken).ConfigureAwait(false);
            }

            return quotaInfo;
        }

    }
}