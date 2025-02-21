#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

partial class BackendManager
{
    /// <summary>
    /// Wrapper class for making a backend disposable and reclaimable
    /// </summary>
    private sealed class ReclaimableBackend : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<ReclaimableBackend>();

        /// <summary>
        /// The backend being wrapped
        /// </summary>
        public IBackend Backend { get; }
        /// <summary>
        /// The pool where the backend should be returned to
        /// </summary>
        private readonly ConcurrentQueue<IBackend> pool;
        /// <summary>
        /// Whether the backend should be reused
        /// </summary>
        private bool reuse;
        /// <summary>
        /// Whether the backend wrapper has been disposed
        /// </summary>
        private bool disposed;

        /// <summary>
        /// Creates a new instance of the <see cref="ReclaimableBackend"/> class
        /// </summary>
        /// <param name="backend">The backend to wrap</param>
        /// <param name="pool">The pool where the backend should be returned to</param>
        /// <param name="reuse">Whether the backend should be reused or disposed</param>
        public ReclaimableBackend(IBackend backend, ConcurrentQueue<IBackend> pool, bool reuse)
        {
            Backend = backend;
            this.pool = pool;
            this.reuse = reuse;
        }

        /// <summary>
        /// Prevents the backend from being reclaimed
        /// </summary>
        public void PreventReuse()
        {
            reuse = false;
        }

        /// <summary>
        /// Disposes the backend wrapper
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (reuse)
                pool.Enqueue(Backend);
            else
                try { Backend.Dispose(); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "BackendDisposeError", ex, "Failed to dispose backend instance: {0}", ex.Message); }
        }
    }

    /// <summary>
    /// The handler for processing backend operations
    /// </summary>
    private class Handler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<Handler>();

        /// <summary>
        /// The list of active downloads
        /// </summary>
        private readonly List<Task> activeDownloads = [];
        /// <summary>
        /// The list of active uploads
        /// </summary>
        private readonly List<Task> activeUploads = [];
        /// <summary>
        /// The pool of backends currently created
        /// </summary>
        private readonly ConcurrentQueue<IBackend> backendPool = new();
        /// <summary>
        /// The URL of the backend
        /// </summary>
        private readonly string backendUrl;
        /// <summary>
        /// The context for the handler
        /// </summary>
        private readonly ExecuteContext context;
        /// <summary>
        /// The maximum number of parallel downloads
        /// </summary>
        private readonly int maxParallelDownloads;
        /// <summary>
        /// The maximum number of parallel uploads
        /// </summary>
        private readonly int maxParallelUploads;
        /// <summary>
        /// The maximum number of retries
        /// </summary>
        private readonly int maxRetries;
        /// <summary>
        /// The delay between retries
        /// </summary>
        private readonly TimeSpan retryDelay;
        /// <summary>
        /// Whether to retry with exponential backoff
        /// </summary>
        private readonly bool retryWithExponentialBackoff;
        /// <summary>
        /// Whether to allow backend reuse
        /// </summary>
        private readonly bool allowBackendReuse;
        /// <summary>
        /// Whether any files have been uploaded
        /// </summary>
        private bool anyUploaded;
        /// <summary>
        /// Whether any files have been downloaded
        /// </summary>
        private bool anyDownloaded;

        /// <summary>
        /// Creates and runs with an instance of the <see cref="Handler"/> class
        /// </summary>
        /// <param name="requestChannel">The channel for pending operations</param>
        /// <param name="backendUrl">The URL of the backend</param>
        /// <param name="context">The execution context</param>
        /// <returns>An awaitable task</returns>
        public static Task RunHandlerAsync(IReadChannel<PendingOperationBase> requestChannel, string backendUrl, ExecuteContext context)
            => AutomationExtensions.RunTask(new { requestChannel },
                self => new Handler(backendUrl, context).Run(self.requestChannel)
            );

        /// <summary>
        /// Creates a new instance of the <see cref="Handler"/> class
        /// </summary>
        /// <param name="backendUrl">The URL of the backend</param>
        /// <param name="context">The execution context</param>
        private Handler(string backendUrl, ExecuteContext context)
        {
            this.backendUrl = backendUrl;
            this.context = context;

            // TODO Currently, only the restore process uses parallel downloads. If others need it as well, maybe use another option.
            maxParallelDownloads = Math.Max(1, context.Options.RestoreVolumeDownloaders);
            maxParallelUploads = Math.Max(1, context.Options.AsynchronousConcurrentUploadLimit);
            maxRetries = context.Options.NumberOfRetries;
            retryDelay = context.Options.RetryDelay;
            retryWithExponentialBackoff = context.Options.RetryWithExponentialBackoff;
            allowBackendReuse = !context.Options.NoConnectionReuse;

        }

        /// <summary>
        /// Creates a new backend instance or reuses an existing one
        /// </summary>
        /// <returns>The backend instance</returns>
        private ReclaimableBackend CreateBackend()
        {
            backendPool.TryDequeue(out var backend);
            if (backend == null)
                backend = DynamicLoader.BackendLoader.GetBackend(backendUrl, context.Options.RawOptions);

            return new ReclaimableBackend(
                backend,
                backendPool,
                allowBackendReuse
            );
        }

        /// <summary>
        /// Reclaims completed downloads
        /// </summary>
        /// <returns>An awaitable task</returns>
        private async Task ReclaimCompletedDownloads()
        {
            for (int i = activeDownloads.Count - 1; i >= 0; i--)
            {
                if (activeDownloads[i].IsCompleted)
                {
                    // Make sure the task is awaited so we capture any exceptions
                    await activeDownloads[i].ConfigureAwait(false);
                    activeDownloads.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Reclaims completed uploads
        /// </summary>
        /// <returns>An awaitable task</returns>
        private async Task ReclaimCompletedUploads()
        {
            for (int i = activeUploads.Count - 1; i >= 0; i--)
            {
                if (activeUploads[i].IsCompleted)
                {
                    // Make sure the task is awaited so we capture any exceptions
                    await activeUploads[i].ConfigureAwait(false);
                    activeUploads.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Ensures that there are at most N - 1 active downloads
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        private async Task EnsureAtMostNActiveDownloads(int n)
        {
            while (activeDownloads.Count >= n)
            {
                await Task.WhenAny(activeDownloads).ConfigureAwait(false);
                await ReclaimCompletedDownloads().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Ensures that there are at most N - 1 active uploads
        /// </summary>
        /// <param name="n">The maximum number of active uploads</param>
        /// <returns>An awaitable task</returns>
        private async Task EnsureAtMostNActiveUploads(int n)
        {
            while (activeUploads.Count >= n)
            {
                await Task.WhenAny(activeUploads).ConfigureAwait(false);
                await ReclaimCompletedUploads().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs the handler
        /// </summary>
        /// <param name="requestChannel">The channel for pending operations</param>
        /// <returns>An awaitable task</returns>
        private async Task Run(IReadChannel<PendingOperationBase> requestChannel)
        {
            using var tcs = new CancellationTokenSource();
            try
            {
                while (true)
                {
                    // Get next operation
                    var op = await requestChannel.ReadAsync().ConfigureAwait(false);

                    try
                    {
                        // Clean up completed uploads, if any
                        await ReclaimCompletedUploads().ConfigureAwait(false);

                        // Allow PUT operations to be queued, if requested
                        if (op is PutOperation putOp && !putOp.WaitForComplete)
                        {
                            // Wait for any active downloads to complete before starting an upload
                            await EnsureAtMostNActiveDownloads(1).ConfigureAwait(false);
                            await EnsureAtMostNActiveUploads(maxParallelUploads).ConfigureAwait(false);

                            // Operation is accepted into queue, so we can signal completion
                            putOp.SetComplete(true);
                            activeUploads.Add(ExecuteWithRetry(putOp, tcs.Token));
                        }
                        else if (op is GetOperation getOp)
                        {
                            // Wait for any active uploads to complete before starting a download
                            await EnsureAtMostNActiveUploads(1).ConfigureAwait(false);
                            await EnsureAtMostNActiveDownloads(maxParallelDownloads).ConfigureAwait(false);

                            // Operation is accepted into queue, so we can signal completion
                            activeDownloads.Add(ExecuteWithRetry(getOp, tcs.Token));
                        }
                        else
                        {
                            // Wait for all of the active uploads and downloads to complete
                            await EnsureAtMostNActiveUploads(1).ConfigureAwait(false);
                            await EnsureAtMostNActiveDownloads(1).ConfigureAwait(false);

                            // Execute the operation
                            await ExecuteWithRetry(op, tcs.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerHandlerFailure", ex, "Error in handler: {0}", ex.Message);
                        // If we fail, the task may "hang", so we ensure it is completed here
                        op.SetFailed(ex);
                        throw;
                    }
                }
            }
            finally
            {
                // Terminate any active uploads and downloads. Exceptions thrown by the downloads should be captured by the callers.
                tcs.Cancel();

                if (activeUploads.Count > 0)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerDisposeWhileActive", null, "Terminating {0} active uploads", activeUploads.Count);

                    // Wait for all active uploads to complete
                    await Task.WhenAny(Task.Delay(1000), Task.WhenAll(activeUploads)).ConfigureAwait(false);
                    for (int i = activeUploads.Count - 1; i >= 0; i--)
                    {
                        var t = activeUploads[i];
                        activeUploads.RemoveAt(i);
                        if (t.IsCompleted && !t.IsCanceled && t.Exception != null)
                            Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerDisposeError", t.Exception, "Error in active upload: {0}", t.Exception.Message);
                    }

                    if (activeUploads.Count > 0)
                        Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerDisposeError", null, "Terminating, but {0} active uploads are still active", activeUploads.Count);
                }

                // Dispose of any remaining backends
                while (backendPool.TryDequeue(out var backend))
                    try { backend.Dispose(); }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "BackendManagerDisposeError", ex, "Failed to dispose backend instance: {0}", ex.Message); }
            }
        }

        /// <summary>
        /// Tries to create a folder, handling errors
        /// </summary>
        /// <returns><c>true</c> if the folder was created, <c>false</c> otherwise</returns>
        private async Task<bool> TryCreateFolder()
        {
            using var backend = CreateBackend();
            try
            {
                // If we successfully create the folder, we can re-use the connection
                await backend.Backend.CreateFolderAsync(context.TaskReader.TransferToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                // Failure should not reuse the backend
                backend.PreventReuse();
                Logging.Log.WriteWarningMessage(LOGTAG, "FolderCreateError", ex, "Failed to create folder: {0}", ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Executes an operation with retries and error handling
        /// </summary>
        /// <param name="op">The operation to execute</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        private async Task ExecuteWithRetry(PendingOperationBase op, CancellationToken cancellationToken)
        {
            // Once in this method, we MUST set the op result,
            // or the program will hang waiting for the operation to complete

            int retries = 0;
            Exception? lastException = null;

            do
            {
                try
                {
                    // Happy case is execute and return
                    await Execute(op, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    retries++;
                    lastException = ex;
                    Logging.Log.WriteRetryMessage(LOGTAG, $"Retry{op.Operation}", ex, "Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", op.Operation, op.RemoteFilename, retries, maxRetries, ex.Message);

                    // If we are cancelled, stop retrying
                    if (op.CancelToken.IsCancellationRequested || context.TaskReader.ProgressToken.IsCancellationRequested || context.TaskReader.TransferToken.IsCancellationRequested)
                    {
                        op.SetCancelled();
                        return;
                    }

                    // Refresh DNS name if we fail to connect in order to prevent issues with incorrect DNS entries
                    var dnsFailure = Library.Utility.Utility.FlattenException(ex).Any(x => x is System.Net.WebException wex && wex.Status == System.Net.WebExceptionStatus.NameResolutionFailure);
                    if (dnsFailure)
                    {
                        try
                        {
                            using (var backend = CreateBackend())
                                foreach (var name in await backend.Backend.GetDNSNamesAsync(context.TaskReader.TransferToken).ConfigureAwait(false) ?? [])
                                    if (!string.IsNullOrWhiteSpace(name))
                                        System.Net.Dns.GetHostEntry(name);
                        }
                        catch
                        {
                        }
                    }

                    context.Statwriter.SendEvent(op.Operation, retries < maxRetries ? BackendEventType.Retrying : BackendEventType.Failed, op.RemoteFilename, op.Size);

                    // Check if we can recover from the error
                    var recovered = false;

                    // Check if this was a folder missing exception and we are allowed to autocreate folders
                    if (!(anyDownloaded || anyUploaded) && context.Options.AutocreateFolders && Library.Utility.Utility.FlattenException(ex).Any(x => x is FolderMissingException))
                    {
                        if (await TryCreateFolder().ConfigureAwait(false))
                            recovered = true;
                    }

                    // We did not recover, so wait or give up
                    if (!recovered && retries < maxRetries && retryDelay.Ticks != 0)
                    {
                        var delay = Library.Utility.Utility.GetRetryDelay(retryDelay, retries, retryWithExponentialBackoff);
                        await Task.Delay(delay, context.TaskReader.ProgressToken).ConfigureAwait(false);
                    }
                }

            } while (retries < maxRetries);

            // If we have a last exception, we failed
            if (lastException != null)
            {
                op.SetFailed(lastException);
                (op as IDisposable)?.Dispose();

                // Stop processing tasks if the operation failed and is not being waited for
                // Delete operations can be retried later, so we don't stop processing
                if (!op.WaitForComplete && op is not DeleteOperation)
                    throw lastException;
            }
        }

        /// <summary>
        /// Fan-out for executing operations.
        /// This method requires manual updates when new operation types are added,
        /// but avoids a reflection-based dispatch.
        /// </summary>
        /// <param name="op">The operation to execute</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        private async Task Execute(PendingOperationBase op, CancellationToken cancellationToken)
        {
            await context.TaskReader.ProgressRendevouz().ConfigureAwait(false);
            using (new Logging.Timer(LOGTAG, $"RemoteOperation{op.Operation}", $"RemoteOperation{op.Operation}"))
                switch (op)
                {
                    case PutOperation putOp:
                        await Execute(putOp, cancellationToken).ConfigureAwait(false);
                        anyUploaded = true;
                        return;
                    case GetOperation getOp:
                        await Execute(getOp, cancellationToken).ConfigureAwait(false);
                        anyDownloaded = true;
                        return;
                    case DeleteOperation deleteOp:
                        await Execute(deleteOp, cancellationToken).ConfigureAwait(false);
                        return;
                    case ListOperation listOp:
                        await Execute(listOp, cancellationToken).ConfigureAwait(false);
                        return;
                    case QuotaInfoOperation quotaOp:
                        await Execute(quotaOp, cancellationToken).ConfigureAwait(false);
                        return;
                    case WaitForEmptyOperation waitOp:
                        waitOp.SetComplete(true);
                        return;
                    default:
                        throw new NotImplementedException($"Operation type {op.GetType()} is not supported");
                }
        }

        /// <summary>
        /// Executes a specific operation
        /// </summary>
        /// <typeparam name="TResult">The return value type of the operation</typeparam>
        /// <param name="op">The operation to execute</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        private async Task Execute<TResult>(PendingOperation<TResult> op, CancellationToken cancellationToken)
        {
            using var backend = CreateBackend();
            using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, op.CancelToken, context.TaskReader.TransferToken);

            try
            {
                // Start processing the operation
                var task = op.ExecuteAsync(backend.Backend, token.Token);

                if (typeof(TResult) == typeof(bool) && !op.WaitForComplete)
                {
                    // Operation is accepted into queue, so we can signal completion
                    op.SetComplete((TResult)(object)true);
                    await task.ConfigureAwait(false);
                }
                else
                {
                    if (!op.WaitForComplete)
                        throw new NotImplementedException($"WaitForComplete is required for operations returning a value: {op.GetType().FullName}");

                    // Wait for the operation to complete
                    op.SetComplete(await task.ConfigureAwait(false));
                }
            }
            catch
            {
                // If the operation fails, we prevent reuse of the backend
                backend.PreventReuse();
                throw;
            }
        }
    }
}
