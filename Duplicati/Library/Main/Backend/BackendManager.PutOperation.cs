using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using Newtonsoft.Json;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending PUT operation
    /// </summary>
    private class PutOperation : PendingOperation<bool>
    {
        /// <summary>
        /// Different states of the operation
        /// </summary>
        private enum OperationState
        {
            /// <summary>
            /// The operation is not started
            /// </summary>
            None,
            /// <summary>
            /// The upload has started
            /// </summary>
            Uploading,
            /// <summary>
            /// The upload has completed
            /// </summary>
            Uploaded,
            /// <summary>
            /// The operation has completed
            /// </summary>
            Completed
        }

        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<PutOperation>();

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.Put;
        /// <summary>
        /// The remote filename backing field
        /// </summary>
        private string remoteFilename;
        /// <summary>
        /// The remote filename
        /// </summary>
        public override string RemoteFilename => remoteFilename;
        /// <summary>
        /// The local temporary file
        /// </summary>
        public required TempFile? LocalTempfile { get; set; }
        /// <summary>
        /// Flag indicating if the file is not to be encrypted
        /// </summary>
        public required bool Unencrypted { get; set; }
        /// <summary>
        /// The local filename as a string
        /// </summary>
        public string LocalFilename => LocalTempfile;
        /// <summary>
        /// Flag to indicate if the file is not tracked in the database
        /// </summary>
        public required bool TrackedInDb { get; init; }
        /// <summary>
        /// The encryption and hashing task, the return value indicates if the hash and size was updated
        /// </summary>
        private Task<(string Hash, long Size)>? encryptionAndHashingTask;
        /// <summary>
        /// The state of the operation
        /// </summary>
        private OperationState operationState = OperationState.None;
        /// <summary>
        /// The pending index operation, if any
        /// </summary>
        private (IndexVolumeWriter Writer, PutOperation Operation)? indexOperation;
        /// <summary>
        /// The original index file, if any
        /// </summary>
        public required IndexVolumeWriter? OriginalIndexFile { get; init; }
        /// <summary>
        /// A callback that is invoked when the index volume is finished
        /// </summary>
        public required Action? IndexVolumeFinishedCallback { get; init; }

        /// <summary>
        /// Creates a new put operation 
        /// </summary>
        /// <param name="context">The execution context</param>
        /// <param name="waitForComplete">True if the operation should wait for completion</param>
        /// <param name="cancelToken">The cancellation token</param>
        public PutOperation(string remotefilename, ExecuteContext context, bool waitForComplete, CancellationToken cancelToken)
            : base(context, waitForComplete, cancelToken)
        {
            remoteFilename = remotefilename;
        }

        /// <summary>
        /// Starts the encryption of the file
        /// </summary>
        /// <param name="options">The options to use</param>
        public void StartEncryptionAndHashing()
        {
            if (encryptionAndHashingTask != null)
                throw new Exception("Encryption already started");

            // Run detached to allow encrypting while waiting in upload queue
            encryptionAndHashingTask = Task.Run(() =>
            {
                if (!Context.Options.NoEncryption && !Unencrypted)
                {
                    using var enc = DynamicLoader.EncryptionLoader.GetModule(Context.Options.EncryptionModule, Context.Options.Passphrase, Context.Options.RawOptions);
                    if (enc == null)
                        throw new Exception(Strings.BackendMananger.EncryptionModuleNotFound(Context.Options.EncryptionModule));

                    var tempfile = new TempFile();
                    enc.Encrypt(LocalFilename, tempfile);
                    DeleteLocalFile();
                    LocalTempfile = tempfile;
                }

                if (!TrackedInDb)
                    return (string.Empty, -1L);

                return CalculateHashAndSize();

            });
        }

        /// <summary>
        /// Deletes the local temporary file
        /// </summary>
        private void DeleteLocalFile()
        {
            try { LocalTempfile?.Dispose(); }
            catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DeleteTemporaryFileError", ex, $"Failed to dispose temporary file: {LocalTempfile}"); }
            finally { LocalTempfile = null; }
        }

        /// <summary>
        /// Calculates the hash and size of the file
        /// </summary>
        /// <returns>The hash and size of the file</returns>
        private (string Hash, long Size) CalculateHashAndSize()
        {
            var hash = CalculateFileHash(LocalFilename, Context.Options);
            var size = new System.IO.FileInfo(LocalFilename).Length;

            return (hash, size);

        }

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to use</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        public override async Task<bool> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            // This operation is slightly more complex than the others, as it involves two files.
            // To keep the rest of the code simpler, the upload treats the pair as a single operation
            // for the caller, but internally it is two separate operations.

            // If there is a retry, this function needs to handle a retry in various states
            // And, if the block file has been renamed, the index file needs to be rewritten

            if (operationState == OperationState.Completed)
                throw new Exception("Operation already completed");

            // If this is a retry after the block has uploaded, pass the operating to the index file upload
            if (operationState == OperationState.Uploaded && indexOperation != null)
            {
                await indexOperation.Value.Operation.ExecuteAsync(backend, cancelToken).ConfigureAwait(false);
                indexOperation.Value.Writer.Dispose();
                operationState = OperationState.Completed;
                return true;
            }

            // Check if the operation is in a valid state
            if (operationState == OperationState.Uploaded)
                throw new Exception("Operation already uploaded");

            if (this.encryptionAndHashingTask == null)
                throw new Exception("Encryption not started");

            // On retry attempts, we get the same value, without recalculating
            (var hash, var size) = await encryptionAndHashingTask.ConfigureAwait(false);

            // On first upload attempt, calculate the hash and size
            if (operationState == OperationState.None && TrackedInDb)
                Context.Database.LogRemoteVolumeUpdated(RemoteFilename, RemoteVolumeState.Uploading, size, hash);

            // First attempt here, finish the index file now that all information is known
            if (OriginalIndexFile != null && indexOperation == null)
            {
                Context.Database.LogRemoteVolumeUpdated(OriginalIndexFile.RemoteFilename, RemoteVolumeState.Uploading, -1, null);

                // Prepare an upload operation for the index file
                var req2 = new PutOperation(OriginalIndexFile.RemoteFilename, Context, false, cancelToken)
                {
                    LocalTempfile = OriginalIndexFile.TempFile,
                    TrackedInDb = TrackedInDb,
                    Unencrypted = Unencrypted,
                    IndexVolumeFinishedCallback = null,
                    OriginalIndexFile = null
                };

                OriginalIndexFile.FinishVolume(hash, size);
                IndexVolumeFinishedCallback?.Invoke();
                OriginalIndexFile.Close();

                indexOperation = (OriginalIndexFile, req2);
            }

            // If we have previously attempted to upload, we need to rename the file
            if (operationState == OperationState.Uploading)
                RenameFileAfterError(size);

            // Flag for next attempt, if any
            operationState = OperationState.Uploading;

            await PerformUpload(backend, hash, size, cancelToken).ConfigureAwait(false);

            operationState = OperationState.Uploaded;

            // Upload completed, prepare the index file if any
            if (indexOperation != null)
            {
                // TODO: It would be better if we encrypt the index file while uploading the block file
                // since most operations work correctly. But if the upload of the block file fails
                // we need to deal with decryption, or keep the unencrypted file around
                indexOperation.Value.Operation.StartEncryptionAndHashing();
                await indexOperation.Value.Operation.ExecuteAsync(backend, cancelToken).ConfigureAwait(false);
                indexOperation.Value.Writer.Dispose();
            }

            operationState = OperationState.Completed;
            return true;
        }

        /// <summary>
        /// Performs the actual upload of a file
        /// </summary>
        /// <param name="backend">The backend to upload to</param>
        /// <param name="Hash">The hash of the file</param>
        /// <param name="Size">The size of the file</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        private async Task PerformUpload(IBackend backend, string Hash, long Size, CancellationToken cancelToken)
        {
            Context.Database.LogRemoteOperation("put", RemoteFilename, JsonConvert.SerializeObject(new { Size = Size, Hash = Hash }));
            Context.Statwriter.SendEvent(BackendActionType.Put, BackendEventType.Started, RemoteFilename, Size);

            var begin = DateTime.Now;

            if (backend is IStreamingBackend streamingBackend && !Context.Options.DisableStreamingTransfers)
            {
                using var fs = System.IO.File.OpenRead(LocalFilename);
                using var ts = new ThrottledStream(fs, Context.Options.MaxUploadPrSecond, 0);
                using var pgs = new ProgressReportingStream(ts, pg => Context.HandleProgress(ts, pg, RemoteFilename));
                await streamingBackend.PutAsync(RemoteFilename, pgs, cancelToken).ConfigureAwait(false);
            }
            else
                await backend.PutAsync(RemoteFilename, LocalFilename, cancelToken).ConfigureAwait(false);

            var duration = DateTime.Now - begin;
            Logging.Log.WriteProfilingMessage(LOGTAG, "UploadSpeed", "Uploaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(Size), duration, Library.Utility.Utility.FormatSizeString((long)(Size / duration.TotalSeconds)));

            if (TrackedInDb)
                Context.Database.LogRemoteVolumeUpdated(RemoteFilename, RemoteVolumeState.Uploaded, Size, Hash);

            Context.Statwriter.SendEvent(BackendActionType.Put, BackendEventType.Completed, RemoteFilename, Size);

            if (Context.Options.ListVerifyUploads)
            {
                var f = await backend.ListAsync(cancelToken).FirstOrDefaultAsync(n => n.Name.Equals(RemoteFilename, StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false);
                if (f == null)
                    throw new Exception(string.Format($"List verify failed, file was not found after upload: {RemoteFilename}"));
                else if (f.Size != Size && f.Size >= 0)
                    throw new Exception(string.Format($"List verify failed for file: {f.Name}, size was {f.Size} but expected to be {Size}"));
            }

            DeleteLocalFile();
        }

        /// <summary>
        /// Renames the remote target file after an error, and updates the index file, if any
        /// </summary>
        /// <param name="Size">The size of the file</param>
        private void RenameFileAfterError(long Size)
        {
            var p = VolumeBase.ParseFilename(RemoteFilename);
            var guid = VolumeWriterBase.GenerateGuid();
            var time = p.Time.Ticks == 0 ? p.Time : p.Time.AddSeconds(1);
            var newname = VolumeBase.GenerateFilename(p.FileType, p.Prefix, guid, time, p.CompressionModule, p.EncryptionModule);
            var oldname = RemoteFilename;

            Context.Statwriter.SendEvent(BackendActionType.Put, BackendEventType.Rename, oldname, Size);
            Context.Statwriter.SendEvent(BackendActionType.Put, BackendEventType.Rename, newname, Size);
            Logging.Log.WriteInformationMessage(LOGTAG, "RenameRemoteTargetFile", "Renaming \"{0}\" to \"{1}\"", oldname, newname);
            Context.Database.LogRemoteVolumeRenamed(oldname, newname);
            remoteFilename = newname;

            // If there is an index file attached to the block file, 
            // it references the block filename, so we create a new index file
            // which is a copy of the current, but with the new name
            if (indexOperation != null)
            {
                IndexVolumeWriter? wr = null;
                try
                {
                    var hashsize = HashFactory.HashSizeBytes(Context.Options.BlockHashAlgorithm);
                    wr = new IndexVolumeWriter(Context.Options);
                    using (var rd = new IndexVolumeReader(p.CompressionModule, indexOperation.Value.Operation.LocalFilename, Context.Options, hashsize))
                        wr.CopyFrom(rd, x => x == oldname ? newname : x);
                    indexOperation.Value.Writer.Dispose();
                    indexOperation = (wr, indexOperation.Value.Operation);
                    indexOperation.Value.Operation.LocalTempfile?.Dispose();
                    indexOperation.Value.Operation.LocalTempfile = wr.TempFile;
                    wr.Close();
                }
                catch
                {
                    wr?.Dispose();
                    throw;
                }
            }
        }
    }
}