//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using CoCoL;
using Duplicati.Library.Localization.Short;
using Newtonsoft.Json;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Cryptography;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// This class encapsulates access to the backend and ensures at most one connection is active at a time
    /// </summary>
    internal class BackendHandler : SingleRunner
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackendHandler>();

        public const string VOLUME_HASH = "SHA256";

        /// <summary>
        /// Data storage for an ongoing backend operation
        /// </summary>
        public class FileEntryItem
        {
            /// <summary>
            /// The current operation this entry represents
            /// </summary>
            public readonly BackendActionType Operation;
            /// <summary>
            /// The name of the remote file
            /// </summary>
            public string RemoteFilename;
            /// <summary>
            /// The name of the local file
            /// </summary>
            public string LocalFilename { get { return LocalTempfile; } }
            /// <summary>
            /// A reference to a temporary file that is disposed upon
            /// failure or completion of the item
            /// </summary>
            public TempFile LocalTempfile;
            /// <summary>
            /// True if the item has been encrypted
            /// </summary>
            public bool Encrypted;
            /// <summary>
            /// The expected hash value of the file
            /// </summary>
            public string Hash;
            /// <summary>
            /// The expected size of the file
            /// </summary>
            public long Size;
            /// <summary>
            /// A flag indicating if the file is a extra metadata file
            /// that has no entry in the database
            /// </summary>
            public bool TrackedInDb = true;
            /// <summary>
            /// A flag that indicates that the download is only checked for the hash and the file is not decrypted or returned
            /// </summary>            
            public bool VerifyHashOnly;
            /// <summary>
            /// A flag indicating if the operation is a retry run
            /// </summary>
            public bool IsRetry;

            public FileEntryItem(BackendActionType operation, string remotefilename)
            {
                Operation = operation;
                RemoteFilename = remotefilename;
                Size = -1;
            }

            public FileEntryItem(BackendActionType operation, string remotefilename, long size, string hash)
                : this(operation, remotefilename)
            {
                Size = size;
                Hash = hash;
            }

            public void SetLocalfilename(string name)
            {
                this.LocalTempfile = Library.Utility.TempFile.WrapExistingFile(name);
                this.LocalTempfile.Protected = true;
            }

            public void Encrypt(Options options)
            {
                if (!this.Encrypted && !options.NoEncryption)
                {
                    var tempfile = new Library.Utility.TempFile();
                    using (var enc = DynamicLoader.EncryptionLoader.GetModule(options.EncryptionModule, options.Passphrase, options.RawOptions))
                        enc.Encrypt(this.LocalFilename, tempfile);

                    this.DeleteLocalFile();

                    this.LocalTempfile = tempfile;
                    this.Hash = null;
                    this.Size = 0;
                    this.Encrypted = true;
                }
            }

            public static string CalculateFileHash(string filename)
            {
                using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                using (var hasher = HashAlgorithm.Create(VOLUME_HASH))
                    return Convert.ToBase64String(hasher.ComputeHash(fs));
            }


            public bool UpdateHashAndSize(Options options)
            {
                if (Hash == null || Size < 0)
                {
                    Hash = CalculateFileHash(this.LocalFilename);
                    Size = new System.IO.FileInfo(this.LocalFilename).Length;
                    return true;
                }

                return false;
            }

            public void DeleteLocalFile()
            {
                if (this.LocalTempfile != null)
                {
                    try
                    {
                        this.LocalTempfile.Protected = false;
                        this.LocalTempfile.Dispose();
                    }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DeleteTemporaryFileError", ex, "Failed to dispose temporary file: {0}", this.LocalTempfile); }
                    finally { this.LocalTempfile = null; }
                }
            }
        }


        private readonly DatabaseCommon m_database;
        private IBackend m_backend;
        private readonly Options m_options;
        private readonly string m_backendurl;
        private readonly StatsCollector m_stats;
        private readonly ITaskReader m_taskreader;

        public BackendHandler(Options options, string backendUrl, DatabaseCommon database, StatsCollector stats, ITaskReader taskreader)
            : base()
        {
            m_backendurl = backendUrl;
            m_database = database;
            m_options = options;
            m_backendurl = backendUrl;
            m_stats = stats;
            m_taskreader = taskreader;
            m_backend = DynamicLoader.BackendLoader.GetBackend(backendUrl, options.RawOptions);

            var shortname = m_backendurl;

            // Try not to leak hostnames or other information in the error messages
            try { shortname = new Library.Utility.Uri(shortname).Scheme; }
            catch { }

            if (m_backend == null)
                throw new Duplicati.Library.Interface.UserInformationException(string.Format("Backend not supported: {0}", shortname), "BackendNotSupported");
        }

        protected Task<T> RunRetryOnMain<T>(FileEntryItem fe, Func<Task<T>> method)
        {
            return RunOnMain<T>(() =>
                DoWithRetry<T>(fe, method)
            );
        }

        public Task DeleteFileAsync(string remotename, bool suppressCleanup = false)
        {
            var fe = new FileEntryItem(BackendActionType.Delete, remotename);
            return RunRetryOnMain(fe, () =>
                DoDelete(fe, suppressCleanup)
            );
        }

        public Task CreateFolder(string remotename)
        {
            var fe = new FileEntryItem(BackendActionType.CreateFolder, remotename);
            return RunRetryOnMain(fe, () =>
                DoCreateFolder(fe)
            );
        }


        public Task<IList<Library.Interface.IFileEntry>> ListFilesAsync()
        {
            var fe = new FileEntryItem(BackendActionType.List, null);
            return RunRetryOnMain(fe, () =>
                DoList()
            );
        }

        public Task<Library.Utility.TempFile> GetFileAsync(string remotename, long size, string remotehash)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename, size, remotehash);
            return RunRetryOnMain(fe, () => DoGet(fe));
        }

        public Task<Tuple<Library.Utility.TempFile, long, string>> GetFileWithInfoAsync(string remotename)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename);
            return RunRetryOnMain(fe, async () =>
            {
                var res = await DoGet(fe).ConfigureAwait(false);
                return new Tuple<Library.Utility.TempFile, long, string>(
                    res,
                    fe.Size,
                    fe.Hash
                );
            });
        }

        public Task<Library.Utility.TempFile> GetFileForTestingAsync(string remotename)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename);
            fe.VerifyHashOnly = true;
            return RunRetryOnMain(fe, () => DoGet(fe));
        }

        private void ResetBackend(Exception ex)
        {
            try
            {
                if (m_backend != null)
                    m_backend.Dispose();
            }
            catch (Exception dex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "BackendDisposeError", dex, "Failed to dispose backend instance: {0}", ex.Message);
            }
            m_backend = null;
        }

        private async Task<T> DoWithRetry<T>(FileEntryItem item, Func<Task<T>> method)
        {
            item.IsRetry = false;
            Exception lastException = null;

            if (!await m_taskreader.TransferProgressAsync)
                throw new OperationCanceledException();

            if (m_workerSource.IsCancellationRequested)
                throw new OperationCanceledException();

            for (var i = 0; i < m_options.NumberOfRetries; i++)
            {
                if (m_options.RetryDelay.Ticks != 0 && i != 0)
                    await Task.Delay(m_options.RetryDelay).ConfigureAwait(false);

                if (!await m_taskreader.TransferProgressAsync)
                    throw new OperationCanceledException();

                if (m_workerSource.IsCancellationRequested)
                    throw new OperationCanceledException();

                try
                {
                    if (m_backend == null)
                        m_backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions);
                    if (m_backend == null)
                        throw new Exception("Backend failed to re-load");

                    var r = await method().ConfigureAwait(false);
                    return r;
                }
                catch (Exception ex)
                {
                    item.IsRetry = true;
                    lastException = ex;
                    Logging.Log.WriteRetryMessage(LOGTAG, $"Retry{item.Operation}", ex, "Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, i + 1, m_options.NumberOfRetries, ex.Message);
                    // If the thread is aborted, we exit here
                    if (ex is System.Threading.ThreadAbortException || ex is OperationCanceledException)
                        break;

                    await m_stats.SendEventAsync(item.Operation, i < m_options.NumberOfRetries ? BackendEventType.Retrying : BackendEventType.Failed, item.RemoteFilename, item.Size);

                    bool recovered = false;
                    if (ex is Duplicati.Library.Interface.FolderMissingException && m_options.AutocreateFolders)
                    {
                        try
                        {
                            // If we successfully create the folder, we can re-use the connection
                            m_backend.CreateFolder();
                            recovered = true;
                        }
                        catch (Exception dex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FolderCreateError", dex, "Failed to create folder: {0}", ex.Message);
                        }
                    }

                    if (!recovered)
                        ResetBackend(ex);
                }
                finally
                {
                    if (m_options.NoConnectionReuse)
                        ResetBackend(null);
                }
            }

            throw lastException;
        }

        private async Task<IList<Library.Interface.IFileEntry>> DoList()
        {
            await m_stats.SendEventAsync(BackendActionType.List, BackendEventType.Started, null, -1);

            var r = m_backend.List().ToList();

            var sb = new StringBuilder();
            sb.AppendLine("[");
            long count = 0;
            foreach (var e in r)
            {
                if (count != 0)
                    sb.AppendLine(",");
                count++;
                sb.Append(JsonConvert.SerializeObject(e));
            }

            sb.AppendLine();
            sb.Append("]");
            await m_database.LogRemoteOperationAsync("list", "", sb.ToString());

            await m_stats.SendEventAsync(BackendActionType.List, BackendEventType.Completed, null, r.Count);

            return r;
        }

        private async Task<bool> DoDelete(FileEntryItem item, bool suppressCleanup)
        {
            if (m_options.Dryrun)
            {
                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteRemoteFile", "Would delete remote file: {0}, size: {1}", item.RemoteFilename, Library.Utility.Utility.FormatSizeString(item.Size));
                return true;
            }

            await m_stats.SendEventAsync(BackendActionType.Delete, BackendEventType.Started, item.RemoteFilename, item.Size);

            string result = null;
            try
            {
                m_backend.Delete(item.RemoteFilename);
            }
            catch (Exception ex)
            {
                var isFileMissingException = ex is Library.Interface.FileMissingException || ex is System.IO.FileNotFoundException;
                var wr = ex as System.Net.WebException == null ? null : (ex as System.Net.WebException).Response as System.Net.HttpWebResponse;

                if (isFileMissingException || (wr != null && wr.StatusCode == System.Net.HttpStatusCode.NotFound))
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "DeleteRemoteFileFailed", ex, LC.L("Delete operation failed for {0} with FileNotFound, listing contents", item.RemoteFilename));
                    bool success = false;

                    try
                    {
                        success = !m_backend.List().Select(x => x.Name).Contains(item.RemoteFilename);
                    }
                    catch
                    {
                    }

                    if (success)
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileSuccess", LC.L("Listing indicates file {0} is deleted correctly", item.RemoteFilename));
                        return true;
                    }

                }

                result = ex.ToString();
                throw;
            }
            finally
            {
                await m_database.LogRemoteOperationAsync("delete", item.RemoteFilename, result);
            }

            await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Deleted, -1, null, suppressCleanup, TimeSpan.FromHours(2));
            await m_stats.SendEventAsync(BackendActionType.Delete, BackendEventType.Completed, item.RemoteFilename, item.Size);

            return true;
        }

        private async Task<bool> DoCreateFolder(FileEntryItem item)
        {
            await m_stats.SendEventAsync(BackendActionType.CreateFolder, BackendEventType.Started, null, -1);

            string result = null;
            try
            {
                m_backend.CreateFolder();
            }
            catch (Exception ex)
            {
                result = ex.ToString();
                throw;
            }
            finally
            {
                await m_database.LogRemoteOperationAsync("createfolder", item.RemoteFilename, result);
            }

            await m_stats.SendEventAsync(BackendActionType.CreateFolder, BackendEventType.Completed, null, -1);
            return true;
        }

        private async Task<Library.Utility.TempFile> DoGet(FileEntryItem item)
        {
            Library.Utility.TempFile tmpfile = null;
            await m_stats.SendEventAsync(BackendActionType.Get, BackendEventType.Started, item.RemoteFilename, item.Size);

            try
            {
                var begin = DateTime.Now;

                tmpfile = new Library.Utility.TempFile();
                if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
                {
                    using (var fs = System.IO.File.OpenWrite(tmpfile))
                    using (var ts = new ThrottledStream(fs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                    using (var pgs = new Library.Utility.ProgressReportingStream(ts, pg => HandleProgress(ts, pg)))
                        ((Library.Interface.IStreamingBackend)m_backend).Get(item.RemoteFilename, pgs);
                }
                else
                    m_backend.Get(item.RemoteFilename, tmpfile);

                var duration = DateTime.Now - begin;
                var filehash = FileEntryItem.CalculateFileHash(tmpfile);

                Logging.Log.WriteProfilingMessage(LOGTAG, "DownloadSpeed", "Downloaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(item.Size), duration, Library.Utility.Utility.FormatSizeString((long)(item.Size / duration.TotalSeconds)));

                await m_database.LogRemoteOperationAsync("get", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = new System.IO.FileInfo(tmpfile).Length, Hash = filehash }));
                await m_stats.SendEventAsync(BackendActionType.Get, BackendEventType.Completed, item.RemoteFilename, new System.IO.FileInfo(tmpfile).Length);

                if (!m_options.SkipFileHashChecks)
                {
                    var nl = new System.IO.FileInfo(tmpfile).Length;
                    if (item.Size >= 0)
                    {
                        if (nl != item.Size)
                            throw new Exception(Strings.Controller.DownloadedFileSizeError(item.RemoteFilename, nl, item.Size));
                    }
                    else
                        item.Size = nl;

                    if (!string.IsNullOrEmpty(item.Hash))
                    {
                        if (filehash != item.Hash)
                            throw new Duplicati.Library.Main.BackendManager.HashMismatchException(Strings.Controller.HashMismatchError(tmpfile, item.Hash, filehash));
                    }
                    else
                        item.Hash = filehash;
                }

                // Fast exit
                if (item.VerifyHashOnly)
                    return null;

                // Decrypt before returning
                if (!m_options.NoEncryption)
                {
                    try
                    {
                        using (var tmpfile2 = tmpfile)
                        {
                            tmpfile = new Library.Utility.TempFile();

                            // Auto-guess the encryption module
                            var ext = (System.IO.Path.GetExtension(item.RemoteFilename) ?? "").TrimStart('.');
                            if (!string.Equals(m_options.EncryptionModule, ext, StringComparison.OrdinalIgnoreCase))
                            {
                                // Check if the file is encrypted with something else
                                if (DynamicLoader.EncryptionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                {
                                    using (var encmodule = DynamicLoader.EncryptionLoader.GetModule(ext, m_options.Passphrase, m_options.RawOptions))
                                        if (encmodule != null)
                                        {
                                            Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", using matching encryption module", ext, m_options.EncryptionModule);
                                            encmodule.Decrypt(tmpfile2, tmpfile);
                                        }
                                }
                                // Check if the file is not encrypted
                                else if (DynamicLoader.CompressionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                {
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", guessing that it is not encrypted", ext, m_options.EncryptionModule);
                                }
                                // Fallback, lets see what happens...
                                else
                                {
                                    Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use specified encryption module as no others match", ext, m_options.EncryptionModule);
                                    using (var encmodule = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions))
                                        encmodule.Decrypt(tmpfile2, tmpfile);
                                }
                            }
                            else
                            {
                                using (var encmodule = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions))
                                    encmodule.Decrypt(tmpfile2, tmpfile);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        //If we fail here, make sure that we throw a crypto exception
                        if (ex is System.Security.Cryptography.CryptographicException)
                            throw;
                        else
                            throw new System.Security.Cryptography.CryptographicException(ex.Message, ex);
                    }
                }

                var res = tmpfile;
                tmpfile = null;
                return res;
            }
            finally
            {
                try
                {
                    if (tmpfile != null)
                        tmpfile.Dispose();
                }
                catch
                {
                }
            }
        }

        private string m_lastThrottleUploadValue = null;
        private string m_lastThrottleDownloadValue = null;

        private void HandleProgress(ThrottledStream ts, long pg)
        {
            if (!m_taskreader.TransferProgressAsync.WaitForTask().Result)
                throw new OperationCanceledException();

            // Update the throttle speeds if they have changed
            string tmp;
            m_options.RawOptions.TryGetValue("throttle-upload", out tmp);
            if (tmp != m_lastThrottleUploadValue)
            {
                ts.WriteSpeed = m_options.MaxUploadPrSecond;
                m_lastThrottleUploadValue = tmp;
            }

            m_options.RawOptions.TryGetValue("throttle-download", out tmp);
            if (tmp != m_lastThrottleDownloadValue)
            {
                ts.ReadSpeed = m_options.MaxDownloadPrSecond;
                m_lastThrottleDownloadValue = tmp;
            }

            m_stats.UpdateBackendProgress(pg);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (m_backend != null)
                try { m_backend.Dispose(); }
                catch { }
                finally { m_backend = null; }
        }
    }
}

