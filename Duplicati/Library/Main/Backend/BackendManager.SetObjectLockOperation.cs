using System;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending object lock operation
    /// </summary>
    private class SetObjectLockOperation : PendingOperation<bool>
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<SetObjectLockOperation>();

        /// <summary>
        /// The remote filename that should be locked
        /// </summary>
        public override string RemoteFilename { get; }

        /// <summary>
        /// The desired lock expiration time in UTC
        /// </summary>
        public DateTime LockUntilUtc { get; }

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.SetObjectLock;

        /// <summary>
        /// Creates a new SetObjectLockOperation
        /// </summary>
        /// <param name="remoteName">The remote file name to lock</param>
        /// <param name="lockUntilUtc">The UTC timestamp to lock the file until</param>
        /// <param name="context">The execution context</param>
        /// <param name="cancelToken">The cancellation token</param>
        public SetObjectLockOperation(string remoteName, DateTime lockUntilUtc, ExecuteContext context, CancellationToken cancelToken)
            : base(context, true, cancelToken)
        {
            RemoteFilename = remoteName;
            LockUntilUtc = lockUntilUtc;
        }

        /// <inheritdoc />
        public override async Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            if (backend is not ILockingBackend lockingBackend)
                throw new NotSupportedException("Backend does not support object locking operations");

            Context.Statwriter.SendEvent(BackendActionType.SetObjectLock, BackendEventType.Started, RemoteFilename, 0);

            try
            {
                await lockingBackend.SetObjectLockUntilAsync(RemoteFilename, LockUntilUtc, cancelToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "SetObjectLockFailed", ex, "Failed to apply object lock for {0}: {1}", RemoteFilename, ex.Message);
                throw;
            }
            finally
            {
                Context.Statwriter.SendEvent(BackendActionType.SetObjectLock, BackendEventType.Completed, RemoteFilename, 0);
            }

            return true;
        }
    }
}
