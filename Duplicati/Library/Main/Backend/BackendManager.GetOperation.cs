using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using SharpAESCrypt;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// Represents a pending GET operation
    /// </summary>
    private class GetOperation : PendingOperation<(TempFile File, string Hash, long Size)>
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<GetOperation>();

        /// <summary>
        /// The remote filename that is to be downloaded
        /// </summary>
        public override string RemoteFilename { get; }
        /// <summary>
        /// The size of the remote file, or -1 if unknown
        /// </summary>
        public override long Size { get; }
        /// <summary>
        /// The hash of the remote file, or null if unknown
        /// </summary>
        public required string? Hash { get; set; }

        /// <summary>
        /// The operation type
        /// </summary>
        public override BackendActionType Operation => BackendActionType.Get;

        /// <summary>
        /// Creates a new GetOperation
        /// </summary>
        /// <param name="remotefilename">The remote filename to download</param>
        /// <param name="size">The size of the remote file, or -1 if unknown</param>
        /// <param name="context">The execution context</param>
        /// <param name="cancelToken">The cancellation token</param>
        public GetOperation(string remotefilename, long size, ExecuteContext context, CancellationToken cancelToken)
            : base(context, true, cancelToken)
        {
            RemoteFilename = remotefilename;
            Size = size;
        }

        /// <summary>
        /// Executes the operation
        /// </summary>
        /// <param name="backend">The backend to download from</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>The file, hash and size of the downloaded file</returns>
        public override async Task<(TempFile File, string Hash, long Size)> ExecuteAsync(IBackend backend, CancellationToken cancelToken)
        {
            TempFile? tmpfile = null;
            Context.Statwriter.SendEvent(BackendActionType.Get, BackendEventType.Started, RemoteFilename, Size);
            using var encryption = Context.Options.NoEncryption
                ? null
                : (DynamicLoader.EncryptionLoader.GetModule(Context.Options.EncryptionModule, Context.Options.Passphrase, Context.Options.RawOptions)
                    ?? throw new Exception(Strings.BackendMananger.EncryptionModuleNotFound(Context.Options.EncryptionModule))
            );

            try
            {
                var begin = DateTime.Now;

                // We already know the filename, so we put the decision about if and which decryptor to
                // use prior to download. This allows to set up stacked streams or a pipe doing decryption
                IEncryption? useDecrypter = encryption;
                try
                {
                    // Auto-guess the encryption module
                    var ext = (System.IO.Path.GetExtension(RemoteFilename) ?? "").TrimStart('.');
                    if (!ext.Equals(encryption?.FilenameExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if the file is not encrypted
                        if (DynamicLoader.CompressionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", guessing that it is not encrypted", ext, Context.Options.EncryptionModule);
                            useDecrypter = null;
                        }
                        // Check if the file is encrypted with something else
                        else if (DynamicLoader.EncryptionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", using matching encryption module", ext, Context.Options.EncryptionModule);
                            useDecrypter = DynamicLoader.EncryptionLoader.GetModule(ext, Context.Options.Passphrase, Context.Options.RawOptions);
                            useDecrypter = useDecrypter ?? encryption;
                        }
                        // Fallback, lets see what happens...
                        else
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use specified encryption module as no others match", ext, Context.Options.EncryptionModule);
                        }
                    }
                }
                // If we fail here, make sure that we throw a crypto exception
                catch (System.Security.Cryptography.CryptographicException) { throw; }
                catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }

                (tmpfile, var dataSizeDownloaded, var fileHash) = await DoGetFileAsync(backend, useDecrypter, cancelToken).ConfigureAwait(false);

                var duration = DateTime.Now - begin;
                Logging.Log.WriteProfilingMessage(LOGTAG, "DownloadSpeed", "Downloaded {3}{0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(dataSizeDownloaded),
                    duration, Library.Utility.Utility.FormatSizeString((long)(dataSizeDownloaded / duration.TotalSeconds)),
                    useDecrypter == null ? "" : "and decrypted ");

                Context.Database.LogDbOperation("get", RemoteFilename, System.Text.Json.JsonSerializer.Serialize(new { Size = dataSizeDownloaded, Hash = fileHash }));
                Context.Statwriter.SendEvent(BackendActionType.Get, BackendEventType.Completed, RemoteFilename, dataSizeDownloaded);

                if (!Context.Options.SkipFileHashChecks)
                {
                    if (Size >= 0 && dataSizeDownloaded != Size)
                        throw new Exception(Strings.Controller.DownloadedFileSizeError(RemoteFilename, dataSizeDownloaded, Size));

                    if (!string.IsNullOrEmpty(Hash) && fileHash != Hash)
                        throw new HashMismatchException(Strings.Controller.HashMismatchError(tmpfile, Hash, fileHash));
                }

                return (tmpfile, fileHash, dataSizeDownloaded);
            }
            catch
            {
                tmpfile?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Downloads a file from the backend
        /// </summary>
        /// <param name="backend">The backend to download from</param>
        /// <param name="useDecrypter">The decrypter to use, or null if no decryption is needed</param>
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>The downloaded file, the size of the file, and the hash of the file</returns>
        private async Task<(TempFile tempFile, long downloadSize, string remotehash)> DoGetFileAsync(IBackend backend, IEncryption? useDecrypter, CancellationToken cancelToken)
        {
            TempFile retTarget;
            TempFile? dlTarget = null, decryptTarget = null;
            long retDownloadSize;
            string retHashcode;
            try
            {
                dlTarget = new TempFile();
                if (backend is IStreamingBackend streamingBackend && !Context.Options.DisableStreamingTransfers)
                {
                    // extended to use stacked streams
                    using (var fs = System.IO.File.OpenWrite(dlTarget))
                    using (var act = new StreamUtil.TimeoutObservingStream(fs) { WriteTimeout = backend is ITimeoutExemptBackend ? Timeout.Infinite : Context.Options.ReadWriteTimeout })
                    using (var hasher = HashFactory.CreateHasher(Context.Options.FileHashAlgorithm))
                    using (var hs = new HashCalculatingStream(act, hasher))
                    using (var ss = new ShaderStream(hs, true))
                    using (var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancelToken, act.TimeoutToken))
                    {
                        using (var ts = new ThrottledStream(ss, 0, Context.Options.MaxDownloadPrSecond))
                        using (var pgs = new ProgressReportingStream(ts, pg => Context.HandleProgress(ts, pg, RemoteFilename)))
                        {
                            await streamingBackend.GetAsync(RemoteFilename, pgs, linkedToken.Token).ConfigureAwait(false);
                        }
                        ss.Flush();
                        retDownloadSize = ss.TotalBytesWritten;
                        retHashcode = Convert.ToBase64String(hs.GetFinalHash());
                    }
                }
                else
                {
                    await backend.GetAsync(RemoteFilename, dlTarget, cancelToken).ConfigureAwait(false);
                    retDownloadSize = new System.IO.FileInfo(dlTarget).Length;
                    retHashcode = CalculateFileHash(dlTarget, Context.Options);
                }

                // Decryption is not placed in the stream stack because there seemed to be an effort
                // to throw a CryptographicException on fail. If in main stack, we cannot differentiate
                // in which part of the stack the source of an exception resides.
                if (useDecrypter != null)
                {
                    decryptTarget = new TempFile();
                    try { useDecrypter.Decrypt(dlTarget, decryptTarget); }
                    // If we fail here, make sure that we throw a crypto exception
                    catch (System.Security.Cryptography.CryptographicException) { throw; }
                    catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }

                    retTarget = decryptTarget;
                    decryptTarget = null;
                }
                else
                {
                    retTarget = dlTarget;
                    dlTarget = null;
                }
            }
            finally
            {
                if (dlTarget != null) dlTarget.Dispose();
                if (decryptTarget != null) decryptTarget.Dispose();
            }

            return (retTarget, retDownloadSize, retHashcode);
        }
    }
}