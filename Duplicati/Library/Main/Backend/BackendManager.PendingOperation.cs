using System;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Execution context for the backend manager
    /// </summary>
    /// <param name="HandleProgress">A progress handler</param>
    /// <param name="Statwriter">The stat writer</param>
    /// <param name="Database">The database collector</param>
    /// <param name="TaskReader">The task reader</param>
    /// <param name="Options">The options</param>
    private sealed record ExecuteContext(
        Action<ThrottledStream, long, string> HandleProgress,
        IBackendWriter Statwriter,
        DatabaseCollector Database,
        ITaskReader TaskReader,
        Options Options);

    /// <summary>
    /// A base non-generic pending operation
    /// </summary>
    private abstract class PendingOperationBase
    {
        /// <summary>
        /// The execution context
        /// </summary>
        public ExecuteContext Context { get; }
        /// <summary>
        /// Whether to wait for the operation to complete.
        /// If false, the operation is queued and the task is returned after the operation is queued.
        /// </summary>
        public bool WaitForComplete { get; }
        /// <summary>
        /// The cancellation token
        /// </summary>
        public CancellationToken CancelToken { get; set; }
        /// <summary>
        /// The type of operation the pending operation represents
        /// </summary>
        public abstract BackendActionType Operation { get; }
        /// <summary>
        /// The remote filename, if any
        /// </summary>
        public virtual string RemoteFilename => string.Empty;
        /// <summary>
        /// The remote size, if any
        /// </summary>
        public virtual long Size => -1L;

        /// <summary>
        /// Sets the operation as cancelled
        /// </summary>
        public abstract void SetCancelled();
        /// <summary>
        /// Sets the operation as failed
        /// </summary>
        /// <param name="ex">The exception</param>
        public abstract void SetFailed(Exception ex);

        /// <summary>
        /// Creates a new pending operation
        /// </summary>
        /// <param name="context">The execution context</param>
        /// <param name="waitForComplete">Whether to wait for the operation to complete</param>
        /// <param name="cancelToken">The cancellation token</param>
        public PendingOperationBase(ExecuteContext context, bool waitForComplete, CancellationToken cancelToken)
        {
            Context = context;
            WaitForComplete = waitForComplete;
            CancelToken = cancelToken;
        }
    }

    /// <summary>
    /// The basic queued backend operation
    /// </summary>
    /// <remarks>
    /// Creates a new pending operation
    /// </remarks>
    /// <param name="context">The execution context</param>
    /// <param name="waitForComplete">Whether to wait for the operation to complete</param>
    /// <param name="cancelToken">The cancellation token</param>
    private abstract class PendingOperation<TResult>(ExecuteContext context, bool waitForComplete, CancellationToken cancelToken) : PendingOperationBase(context, waitForComplete, cancelToken)
    {
        /// <summary>
        /// A signal for the task completion
        /// </summary>
        private readonly TaskCompletionSource<TResult> taskCompleteSignal = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>
        /// The task that is completed when the operation is done
        /// </summary>
        public Task<TResult> GetResult() => taskCompleteSignal.Task;

        /// <summary>
        /// Sets the operation as complete
        /// </summary>
        public void SetComplete(TResult result)
        {
            taskCompleteSignal.TrySetResult(result);
        }

        /// <summary>
        /// Sets the operation as failed
        /// </summary>
        /// <param name="ex">The exception</param>
        public override void SetFailed(Exception ex)
        {
            taskCompleteSignal.TrySetException(ex);
        }

        /// <summary>
        /// Sets the operation as cancelled
        /// </summary>
        public override void SetCancelled()
        {
            taskCompleteSignal.TrySetCanceled();
        }

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to execute the operation on</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        public abstract Task<TResult> ExecuteAsync(IBackend backend, CancellationToken cancelToken);
    }

}