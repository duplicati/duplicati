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
        /// Flag indicating whether the file should be decrypted
        /// </summary>
        public bool Decrypt { get; set; }
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

            try
            {
                // Start and time the donwload
                var begin = DateTime.Now;

                (tmpfile, var dataSizeDownloaded, var fileHash) = await DoGetFileAsync(backend, cancelToken).ConfigureAwait(false);

                var duration = DateTime.Now - begin;
                Logging.Log.WriteProfilingMessage(LOGTAG, "DownloadSpeed", "Downloaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(dataSizeDownloaded),
                    duration, Library.Utility.Utility.FormatSizeString((long)(dataSizeDownloaded / duration.TotalSeconds)));

                Context.Database.LogRemoteOperation("get", RemoteFilename, System.Text.Json.JsonSerializer.Serialize(new { Size = dataSizeDownloaded, Hash = fileHash }));
                Context.Statwriter.SendEvent(BackendActionType.Get, BackendEventType.Completed, RemoteFilename, dataSizeDownloaded);

                if (!Context.Options.SkipFileHashChecks)
                {
                    if (Size >= 0 && dataSizeDownloaded != Size)
                        throw new Exception(Strings.Controller.DownloadedFileSizeError(RemoteFilename, dataSizeDownloaded, Size));

                    if (!string.IsNullOrEmpty(Hash) && fileHash != Hash)
                        throw new HashMismatchException(Strings.Controller.HashMismatchError(tmpfile, Hash, fileHash));
                }

                // Perform decryption after hash validation, if needed
                if (Decrypt)
                    tmpfile = DecryptFile(tmpfile, RemoteFilename, Context.Options);

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
        /// <param name="cancelToken">The cancellation token</param>
        /// <returns>The downloaded file, the size of the file, and the hash of the file</returns>
        private async Task<(TempFile tempFile, long downloadSize, string remotehash)> DoGetFileAsync(IBackend backend, CancellationToken cancelToken)
        {
            TempFile? dlTarget = null;
            try
            {
                long retDownloadSize;
                string retHashcode;
                dlTarget = new TempFile();
                if (backend is IStreamingBackend streamingBackend && !Context.Options.DisableStreamingTransfers)
                {
                    // extended to use stacked streams
                    using (var fs = System.IO.File.OpenWrite(dlTarget))
                    using (var hasher = HashFactory.CreateHasher(Context.Options.FileHashAlgorithm))
                    using (var hs = new HashCalculatingStream(fs, hasher))
                    using (var ss = new ShaderStream(hs, true))
                    {
                        using (var ts = new ThrottledStream(ss, 0, Context.Options.MaxDownloadPrSecond))
                        using (var pgs = new ProgressReportingStream(ts, pg => Context.HandleProgress(ts, pg, RemoteFilename)))
                        {
                            await streamingBackend.GetAsync(RemoteFilename, pgs, cancelToken).ConfigureAwait(false);
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

                var retTarget = dlTarget;
                dlTarget = null;
                return (retTarget, retDownloadSize, retHashcode);
            }
            finally
            {
                // Remove temp files on failure
                dlTarget?.Dispose();
            }
        }

        /// <summary>
        /// Detects the encryption module to use, based on the filename.
        /// This makes it possible to restore from a folder with mixed encryption modules.
        /// </summary>
        /// <param name="encryption">The default encryption module</param>
        /// <returns>The encryption module to use</returns>
        private static IEncryption? DetectEncryptionModule(string filename, Options options, IEncryption? encryption)
        {
            try
            {
                // Auto-guess the encryption module
                var ext = (System.IO.Path.GetExtension(filename) ?? "").TrimStart('.');
                if (!ext.Equals(encryption?.FilenameExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if the file is not encrypted
                    if (DynamicLoader.CompressionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        if (encryption != null)
                            Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", guessing that it is not encrypted", ext, options.EncryptionModule);
                        return null;
                    }
                    // Check if the file is encrypted with something else
                    else if (DynamicLoader.EncryptionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use matching encryption module", ext, options.EncryptionModule);

                        try
                        {
                            return DynamicLoader.EncryptionLoader.GetModule(ext, options.Passphrase, options.RawOptions)
                                ?? encryption;
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "AutomaticDecryptionDetection", ex, "Failed to load encryption module \"{0}\", using specified encryption module \"{1}\"", ext, options.EncryptionModule);
                        }
                    }
                    // Fallback, lets see what happens...
                    else
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use specified encryption module as no others match", ext, options.EncryptionModule);
                    }
                }

                return encryption;
            }
            // If we fail here, make sure that we throw a crypto exception
            catch (System.Security.Cryptography.CryptographicException) { throw; }
            catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }
        }

        /// <summary>
        /// Performs decryption of a file. This could be more efficient if we decrypt while downloading,
        /// but that would prevent us from verifying the hash of the encrypted file, before decrypting it.
        /// Decrypting afterwards also ensures we can control the thrown exceptions.
        /// </summary>
        /// <param name="tempFile">The encrypted file</param>
        /// <param name="decrypter">Then encryption module to use, or <c>null</c> for no encryption</param>
        /// <returns>The decrypted file</returns>
        private static TempFile DecryptFile(TempFile tempFile, IEncryption? decrypter)
        {
            // Support no encryption
            if (decrypter == null)
                return tempFile;

            TempFile? decryptTarget = null;

            // Always dispose the source file
            using (tempFile)
            using (new Logging.Timer(LOGTAG, "DecryptFile", "Decrypting " + tempFile))
            {
                try
                {
                    decryptTarget = new TempFile();
                    try { decrypter.Decrypt(tempFile, decryptTarget); }
                    // If we fail here, make sure that we throw a crypto exception
                    catch (System.Security.Cryptography.CryptographicException) { throw; }
                    catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }

                    var result = decryptTarget;
                    decryptTarget = null;
                    return result;
                }
                finally
                {
                    // Remove temp files on failure
                    decryptTarget?.Dispose();
                }
            }
        }

        /// <summary>
        /// Decrypts a file using the specified options
        /// </summary>
        /// <param name="tmpfile">The file to decrypt</param>
        /// <param name="filename">The name of the file. Used for detecting encryption algorithm if not specified in options or if it differs from the options</param>
        /// <param name="options">The Duplicati options</param>
        /// <returns>The decrypted file</returns>
        public static TempFile DecryptFile(TempFile tmpfile, string filename, Options options)
        {
            using var encryption = options.NoEncryption
                                    ? null
                                    : (DynamicLoader.EncryptionLoader.GetModule(options.EncryptionModule, options.Passphrase, options.RawOptions)
                                        ?? throw new Exception(Strings.BackendMananger.EncryptionModuleNotFound(options.EncryptionModule))
                                );
            return DecryptFile(tmpfile, DetectEncryptionModule(filename, options, encryption));
        }
    }
}