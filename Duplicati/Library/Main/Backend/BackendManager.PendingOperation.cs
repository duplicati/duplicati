using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.StreamUtil;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Execution context for the backend manager
    /// </summary>
    /// <param name="ProgressHandler">The progress handler</param>
    /// <param name="Statwriter">The stat writer</param>
    /// <param name="Database">The database collector</param>
    /// <param name="UploadThrottleManager">The upload throttle manager</param>
    /// <param name="DownloadThrottleManager">The download throttle manager</param>
    /// <param name="TaskReader">The task reader</param>
    /// <param name="IsThrottleDisabled">Whether throttling is disabled</param>
    /// <param name="Options">The options</param>
    private sealed record ExecuteContext(
        ProgressHandler ProgressHandler,
        IBackendWriter Statwriter,
        DatabaseCollector Database,
        ThrottleManager UploadThrottleManager,
        ThrottleManager DownloadThrottleManager,
        ITaskReader TaskReader,
        bool IsThrottleDisabled,
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
        /// The logical remote filename, used for all bookkeeping (database updates,
        /// progress events, logging). This is the full relative path as seen by the
        /// caller (e.g. <c>subfolder/file.txt</c>).
        /// </summary>
        public virtual string RemoteFilename => string.Empty;
        /// <summary>
        /// The remote size, if any
        /// </summary>
        public virtual long Size => -1L;

        /// <summary>
        /// The name actually passed to the backend's put/get/delete call. When null
        /// (the default), <see cref="GetEffectiveRemoteName"/> falls back to
        /// <see cref="RemoteFilename"/> (the full relative path) - this is the
        /// behavior for folder-enabled backends. For backends that do not support
        /// folder operations, <see cref="BackendManager.ApplyPathTranslation"/> sets
        /// this to just the filename part (the backend is pointed at the sub-folder
        /// via <see cref="BackendUrlOverride"/>).
        /// </summary>
        public string? EffectiveRemoteName { get; set; }

        /// <summary>
        /// Returns the remote name to pass to the backend's put/get/delete call:
        /// <see cref="EffectiveRemoteName"/> if set, otherwise <see cref="RemoteFilename"/>.
        /// </summary>
        public string GetEffectiveRemoteName()
            => string.IsNullOrEmpty(EffectiveRemoteName) ? RemoteFilename : EffectiveRemoteName!;

        /// <summary>
        /// The backend URL to use for this operation, or null to use the backend
        /// manager's base backend URL. Set by <see cref="BackendManager.ApplyPathTranslation"/>
        /// for non-folder backends to point the backend at the sub-folder containing
        /// the file, so the flat put/get/delete call resolves to the right location.
        /// </summary>
        public string? BackendUrlOverride { get; set; }

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
        public Task<TResult> GetResultAsync() => taskCompleteSignal.Task;

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