using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending DELETE operation
    /// </summary>
    private class DeleteOperation : PendingOperation<bool>
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<DeleteOperation>();

        /// <summary>
        /// The remote filename that is to be deleted
        /// </summary>
        public override string RemoteFilename { get; }
        /// <summary>
        /// The size of the remote file, or -1 if unknown
        /// </summary>
        public override long Size { get; }

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.Delete;

        /// <summary>
        /// Creates a new DeleteOperation
        /// </summary>
        /// <param name="remoteName">The remote filename to delete</param>
        /// <param name="size">The size of the remote file, or -1 if unknown</param>
        /// <param name="context">The execution context</param>
        /// <param name="waitForComplete">Whether to wait for the operation to complete</param>
        public DeleteOperation(string remoteName, long size, ExecuteContext context, bool waitForComplete, CancellationToken cancelToken)
            : base(context, waitForComplete, cancelToken)
        {
            RemoteFilename = remoteName;
            Size = size;
        }

        /// <inheritdoc/>
        public override async Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            Context.Statwriter.SendEvent(BackendActionType.Delete, BackendEventType.Started, RemoteFilename, Size);

            string? result = null;
            try
            {
                await backend.DeleteAsync(RemoteFilename, cancelToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Check if the file was not found, and if so, check if it was actually deleted
                var isFileMissingException = ex is Library.Interface.FileMissingException || ex is System.IO.FileNotFoundException;
                var wr = (ex as System.Net.WebException)?.Response as System.Net.HttpWebResponse;
                bool recovered = false;

                if (isFileMissingException || (wr != null && wr.StatusCode == System.Net.HttpStatusCode.NotFound))
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileFailed", LC.L($"Delete operation failed for {RemoteFilename} with FileNotFound, listing contents"));

                    try
                    {
                        recovered = !await backend.ListAsync(cancelToken).Select(x => x.Name).ContainsAsync(RemoteFilename).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    if (recovered)
                        Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileSuccess", LC.L($"Listing indicates file {RemoteFilename} was deleted correctly"));
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "DeleteRemoteFileFailed", ex, LC.L($"Listing confirms file {RemoteFilename} was not deleted"));
                }

                if (!recovered)
                {
                    result = ex.ToString();
                    throw;
                }
            }
            finally
            {
                // Here, we do not know if the file was actually deleted or not
                // We log that the operation was performed, and the result
                Context.Database.LogRemoteOperation("delete", RemoteFilename, result);

                // We also log the new state of the file, so it will be attempted to be re-deleted on later listings
                Context.Database.LogRemoteVolumeUpdated(RemoteFilename, RemoteVolumeState.Deleted, -1, null);
            }

            Context.Statwriter.SendEvent(BackendActionType.Delete, BackendEventType.Completed, RemoteFilename, Size);
            return true;
        }
    }
}