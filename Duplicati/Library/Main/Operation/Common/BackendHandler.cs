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

namespace Duplicati.Library.Main.Operation.Common
{    
    /// <summary>
    /// This class encapsulates access to the backend and ensures at most one connection is active at a time
    /// </summary>
    internal class BackendHandler : SingleRunner
    {
        public const string VOLUME_HASH = "SHA256";

        /// <summary>
        /// Data storage for an ongoing backend operation
        /// </summary>
        protected class FileEntryItem
        {
            /// <summary>
            /// The current operation this entry represents
            /// </summary>
            public BackendActionType Operation;
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
                
            public async Task Encrypt(Options options, LogWrapper log)
            {
                if (!this.Encrypted && !options.NoEncryption)
                {
                    var tempfile = new Library.Utility.TempFile();
                    using (var enc = DynamicLoader.EncryptionLoader.GetModule(options.EncryptionModule, options.Passphrase, options.RawOptions))
                        enc.Encrypt(this.LocalFilename, tempfile);

                    await this.DeleteLocalFile(log);

                    this.LocalTempfile = tempfile;
                    this.Hash = null;
                    this.Size = 0;
                    this.Encrypted = true;
                }
            }

            public static string CalculateFileHash(string filename)
            {
                using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                using (var hasher = Duplicati.Library.Utility.HashAlgorithmHelper.Create(VOLUME_HASH))
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

            public async Task DeleteLocalFile(LogWrapper log)
            {
                if (this.LocalTempfile != null)
                    try { this.LocalTempfile.Dispose(); }
                catch (Exception ex) { await log.WriteWarningAsync(string.Format("Failed to dispose temporary file: {0}", this.LocalTempfile), ex); }
                finally { this.LocalTempfile = null; }
            }
        }


        private LogWrapper m_log;

        private IBackendHandlerDatabase m_database;
        private IBackend m_backend;
        private Options m_options;
        private string m_backendurl;
        private bool m_uploadSuccess;
        private StatsCollector m_stats;
        private ITaskReader m_taskreader;

        public BackendHandler(Options options, string backendUrl, IBackendHandlerDatabase database, StatsCollector stats, ITaskReader taskreader)
            : base()
        {
            m_backendurl = backendUrl;
            m_database = database;
            m_options = options;
            m_backendurl = backendUrl;
            m_stats = stats;
            m_taskreader = taskreader;
            m_log = new LogWrapper();
            m_backend = DynamicLoader.BackendLoader.GetBackend(backendUrl, options.RawOptions);
			
            var shortname = m_backendurl;

			// Try not to leak hostnames or other information in the error messages
			try { shortname = new Library.Utility.Uri(shortname).Scheme; }
			catch { }

			if (m_backend == null)
			    throw new Duplicati.Library.Interface.UserInformationException(string.Format("Backend not supported: {0}", shortname));
		}
            
        protected Task<T> RunRetryOnMain<T>(FileEntryItem fe, Func<Task<T>> method)
        {
            return RunOnMain<T>(() =>
                DoWithRetry<T>(fe, method)
            );
        }

        public IQuotaInfo QuotaInfo { get { return (m_backend as IQuotaEnabledBackend)?.Quota; } }

        public Task PutUnencryptedAsync(string remotename, string localpath)
        {
            var fe = new FileEntryItem(BackendActionType.Put, remotename);
            fe.SetLocalfilename(localpath);
            fe.Encrypted = true; //Prevent encryption
            fe.TrackedInDb = false; //Prevent Db updates

            return RunRetryOnMain<bool>(fe, async () =>
            {
                await DoPut(fe);
                m_uploadSuccess = true;
                return true;
            });

        }
            
        public async Task UploadFileAsync(VolumeWriterBase item, Func<string, Task<IndexVolumeWriter>> createIndexFile = null)
        {
            var fe = new FileEntryItem(BackendActionType.Put, item.RemoteFilename);
            fe.SetLocalfilename(item.LocalFilename);
            item.Close();

            var tcs = new TaskCompletionSource<bool>();

            var backgroundhashAndEncrypt = Task.Run(async () =>
            {
                await fe.Encrypt(m_options, m_log).ConfigureAwait(false);
                return fe.UpdateHashAndSize(m_options);
            });

            await RunOnMain(async () =>
            {
                try
                {
                    await DoWithRetry(fe, async () => {
                        if (fe.IsRetry)
                            await RenameFileAfterErrorAsync(fe);

                        // Make sure the encryption and hashing has completed
                        await backgroundhashAndEncrypt;

                        return await DoPut(fe);
                    });

                    if (createIndexFile != null)
                    {
                        var ix = await createIndexFile(fe.RemoteFilename);
                        var indexFile = new FileEntryItem(BackendActionType.Put, ix.RemoteFilename);
                        indexFile.SetLocalfilename(ix.LocalFilename);

                        await m_database.UpdateRemoteVolumeAsync(indexFile.RemoteFilename, RemoteVolumeState.Uploading, -1, null);

                        await DoWithRetry(indexFile, async () => {
                            if (indexFile.IsRetry)
                                await RenameFileAfterErrorAsync(indexFile);

                            return await DoPut(indexFile);
                        });
                    }

                    tcs.TrySetResult(true);
                }
                catch(Exception ex)
                {
                    if (ex is System.Threading.ThreadAbortException)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetException(ex);
                }
            });

            await tcs.Task;
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
                DoList(fe)
            );
        }

        public Task<Library.Utility.TempFile> GetFileAsync(string remotename, long size, string remotehash)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename, size, remotehash);
            return RunRetryOnMain(fe, () => DoGet(fe) );
        }

        public Task<Tuple<Library.Utility.TempFile, long, string>> GetFileWithInfoAsync(string remotename)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename);
            return RunRetryOnMain(fe, async () => {
                var res = await DoGet(fe);
                return new Tuple<Library.Utility.TempFile, long, string>(
                    res,
                    fe.Size,
                    fe.Hash
                );
            });
        }

        public Task<Library.Utility.TempFile> GetFileForTestingAsync(string remotename, long size, string remotehash)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename);
            fe.VerifyHashOnly = true;
            return RunRetryOnMain(fe, () => DoGet(fe));
        }

        private async Task ResetBackendAsync(Exception ex)
        {
            try
            {
                if (m_backend != null)
                    m_backend.Dispose();
            }
            catch (Exception dex)
            {
                await m_log.WriteWarningAsync(LC.L("Failed to dispose backend instance: {0}", (ex ?? dex).Message), dex);
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
            
            for(var i = 0; i < m_options.NumberOfRetries; i++)
            {
                if (m_options.RetryDelay.Ticks != 0 && i != 0)
                    await Task.Delay(m_options.RetryDelay);

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
                    
                    var r = await method();
                    return r;
                }
                catch (Exception ex)
                {
                    item.IsRetry = true;
                    lastException = ex;
                    await m_log.WriteRetryAttemptAsync(string.Format("Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, i + 1, m_options.NumberOfRetries, ex.Message), ex);

                    // If the thread is aborted, we exit here
                    if (ex is System.Threading.ThreadAbortException || ex is OperationCanceledException)
                        break;

                    if (ex is System.Net.WebException && ((System.Net.WebException)ex).Status == System.Net.WebExceptionStatus.NameResolutionFailure)
                    {
                        try
                        {
                            var names = m_backend.DNSName ?? new string[0];
                            foreach (var name in names)
                                if (!string.IsNullOrWhiteSpace(name))
                                    try
                                    {
                                        System.Net.Dns.GetHostEntry(name);
                                    }
                                    catch (Exception dnsex)
                                    {
                                        await m_log.WriteVerboseAsync("Failed to refresh DNS record for {0}: {1}", name, dnsex);
                                    }
                        }
                        catch (Exception bkdnsex)
                        {
                            await m_log.WriteWarningAsync("Failed to get DNS names from the backend", bkdnsex);
                        }
                    }

                    await m_stats.SendEventAsync(item.Operation, i < m_options.NumberOfRetries ? BackendEventType.Retrying : BackendEventType.Failed, item.RemoteFilename, item.Size);

                    bool recovered = false;
                    if (!m_uploadSuccess && ex is Duplicati.Library.Interface.FolderMissingException && m_options.AutocreateFolders)
                    {
                        try
                        { 
                            // If we successfully create the folder, we can re-use the connection
                            m_backend.CreateFolder(); 
                            recovered = true;
                        }
                        catch (Exception dex)
                        { 
                            await m_log.WriteWarningAsync(string.Format("Failed to create folder: {0}", ex.Message), dex); 
                        }
                    }
                        
                    if (!recovered)
                        await ResetBackendAsync(ex);
                }
                finally
                {
                    if (m_options.NoConnectionReuse)
                        await ResetBackendAsync(null);
                }
            }

            throw lastException;
        }

        private async Task RenameFileAfterErrorAsync(FileEntryItem item)
        {
            var p = VolumeBase.ParseFilename(item.RemoteFilename);
            var guid = VolumeWriterBase.GenerateGuid(m_options);
            var time = p.Time.Ticks == 0 ? p.Time : p.Time.AddSeconds(1);
            var newname = VolumeBase.GenerateFilename(p.FileType, p.Prefix, guid, time, p.CompressionModule, p.EncryptionModule);
            var oldname = item.RemoteFilename;

            await m_stats.SendEventAsync(item.Operation, BackendEventType.Rename, oldname, item.Size);
            await m_stats.SendEventAsync(item.Operation, BackendEventType.Rename, newname, item.Size);
            await m_log.WriteInformationAsync(string.Format("Renaming \"{0}\" to \"{1}\"", oldname, newname));
            await m_database.RenameRemoteFileAsync(oldname, newname);
            item.RemoteFilename = newname;
        }

        private async Task<bool> DoPut(FileEntryItem item, bool updatedHash = false)
        {
            // If this is not already encrypted, do it now
            await item.Encrypt(m_options, m_log);

            updatedHash |= item.UpdateHashAndSize(m_options);

            if (updatedHash && item.TrackedInDb)
                await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Uploading, item.Size, item.Hash);

            if (m_options.Dryrun)
            {
                await m_log.WriteDryRunAsync("Would upload volume: {0}, size: {1}", item.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(item.LocalFilename).Length));
                await item.DeleteLocalFile(m_log);
                return true;
            }
            
            await m_database.LogRemoteOperationAsync("put", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = item.Size, Hash = item.Hash }));
            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Started, item.RemoteFilename, item.Size);

            var begin = DateTime.Now;

            if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
            {
                using (var fs = System.IO.File.OpenRead(item.LocalFilename))
                using (var ts = new ThrottledStream(fs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                using (var pgs = new Library.Utility.ProgressReportingStream(ts, item.Size, pg => HandleProgress(ts, pg)))
                    ((Library.Interface.IStreamingBackend)m_backend).Put(item.RemoteFilename, pgs);
            }
            else
                m_backend.Put(item.RemoteFilename, item.LocalFilename);

            var duration = DateTime.Now - begin;
            await m_log.WriteProfilingAsync(string.Format("Uploaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(item.Size), duration, Library.Utility.Utility.FormatSizeString((long)(item.Size / duration.TotalSeconds))), null);

            if (item.TrackedInDb)
                await m_database.UpdateRemoteVolumeAsync(item.RemoteFilename, RemoteVolumeState.Uploaded, item.Size, item.Hash);

            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Completed, item.RemoteFilename, item.Size);

            if (m_options.ListVerifyUploads)
            {
                var f = m_backend.List().Where(n => n.Name.Equals(item.RemoteFilename, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                if (f == null)
                    throw new Exception(string.Format("List verify failed, file was not found after upload: {0}", item.RemoteFilename));
                else if (f.Size != item.Size && f.Size >= 0)
                    throw new Exception(string.Format("List verify failed for file: {0}, size was {1} but expected to be {2}", f.Name, f.Size, item.Size));
            }
                
            await item.DeleteLocalFile(m_log);
            await m_database.CommitTransactionAsync("CommitAfterUpload");

            return true;
        }

        private async Task<IList<Library.Interface.IFileEntry>> DoList(FileEntryItem item)
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
                await m_log.WriteDryRunAsync("Would upload delete remote volume: {0}, size: {1}", item.RemoteFilename, Library.Utility.Utility.FormatSizeString(item.Size));
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
                    await m_log.WriteWarningAsync(LC.L("Delete operation failed for {0} with FileNotFound, listing contents", item.RemoteFilename), ex);
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
                        await m_log.WriteInformationAsync(LC.L("Listing indicates file {0} is deleted correctly", item.RemoteFilename));
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

            // Grab size from the database if we do not know it,
            // as this makes the output prettier
            // Don't care if we fail to read it for whatever reason
            var size = item.Size;
            if (size < 0)
                try { size = (await m_database.GetVolumeInfoAsync(item.RemoteFilename)).Size; }
                catch { }

            await m_stats.SendEventAsync(BackendActionType.Get, BackendEventType.Started, item.RemoteFilename, size);

            try
            {
                var begin = DateTime.Now;

                tmpfile = new Library.Utility.TempFile();
                if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
                {
                    using (var fs = System.IO.File.OpenWrite(tmpfile))
                    using (var ts = new ThrottledStream(fs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                    using (var pgs = new Library.Utility.ProgressReportingStream(ts, item.Size, pg => HandleProgress(ts, pg)))
                        ((Library.Interface.IStreamingBackend)m_backend).Get(item.RemoteFilename, pgs);
                }
                else
                    m_backend.Get(item.RemoteFilename, tmpfile);

                var duration = DateTime.Now - begin;
                var filehash = FileEntryItem.CalculateFileHash(tmpfile);
                var filesize = new FileInfo(tmpfile).Length;

                await m_log.WriteProfilingAsync(string.Format("Downloaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(filesize), duration, Library.Utility.Utility.FormatSizeString((long)(filesize / duration.TotalSeconds))), null);
                await m_database.LogRemoteOperationAsync("get", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = filesize, Hash = filehash }));
                await m_stats.SendEventAsync(BackendActionType.Get, BackendEventType.Completed, item.RemoteFilename, filesize);

                if (!m_options.SkipFileHashChecks)
                {
                    if (item.Size >= 0)
                    {
                        if (filesize != item.Size)
                            throw new Exception(Strings.Controller.DownloadedFileSizeError(item.RemoteFilename, filesize, item.Size));
                    }
                    else
                        item.Size = filesize;

                    if (!string.IsNullOrEmpty(item.Hash))
                    {
                        if (filehash != item.Hash)
                            throw new Duplicati.Library.Main.Operation.Common.HashMismatchException(Strings.Controller.HashMismatchError(tmpfile, item.Hash, filehash));
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
                        using(var tmpfile2 = tmpfile)
                        { 
                            tmpfile = new Library.Utility.TempFile();

                            // Auto-guess the encryption module
                            var ext = (System.IO.Path.GetExtension(item.RemoteFilename) ?? "").TrimStart('.');
                            if (!string.Equals(m_options.EncryptionModule, ext, StringComparison.OrdinalIgnoreCase))
                            {
                                // Check if the file is encrypted with something else
                                if (DynamicLoader.EncryptionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                {
                                    using(var encmodule = DynamicLoader.EncryptionLoader.GetModule(ext, m_options.Passphrase, m_options.RawOptions))
                                        if (encmodule != null)
                                        {
                                            await m_log.WriteVerboseAsync("Filename extension \"{0}\" does not match encryption module \"{1}\", using matching encryption module", ext, m_options.EncryptionModule);
                                            encmodule.Decrypt(tmpfile2, tmpfile);
                                        }
                                }
                                // Check if the file is not encrypted
                                else if (DynamicLoader.CompressionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                {
                                    await m_log.WriteVerboseAsync("Filename extension \"{0}\" does not match encryption module \"{1}\", guessing that it is not encrypted", ext, m_options.EncryptionModule);
                                }
                                // Fallback, lets see what happens...
                                else
                                {
                                    await m_log.WriteVerboseAsync("Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use specified encryption module as no others match", ext, m_options.EncryptionModule);
                                    using(var encmodule = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions))
                                        encmodule.Decrypt(tmpfile2, tmpfile);
                                }
                            }
                            else
                            {
                                using(var encmodule = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions))
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
                catch {}
                finally { m_backend = null; }

            if (m_log != null)
                m_log.Dispose();
        }
    }
}

