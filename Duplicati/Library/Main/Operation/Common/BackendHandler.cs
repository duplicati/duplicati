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

namespace Duplicati.Library.Main.Operation.Common
{
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
            /// Reference to the index file entry that is updated if this entry changes
            /// </summary>
            public Tuple<IndexVolumeWriter, FileEntryItem> Indexfile;
            /// <summary>
            /// A flag indicating if the final hash and size of the block volume has been written to the index file
            /// </summary>
            public bool IndexfileUpdated;
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

            public FileEntryItem(BackendActionType operation, string remotefilename, Tuple<IndexVolumeWriter, FileEntryItem> indexfile = null)
            {
                Operation = operation;
                RemoteFilename = remotefilename;
                Indexfile = indexfile;
                Size = -1;
            }

            public FileEntryItem(BackendActionType operation, string remotefilename, long size, string hash, Tuple<IndexVolumeWriter, FileEntryItem> indexfile = null)
                : this(operation, remotefilename, indexfile)
            {
                Size = size;
                Hash = hash;
            }

            public void SetLocalfilename(string name)
            {
                this.LocalTempfile = Library.Utility.TempFile.WrapExistingFile(name);
                this.LocalTempfile.Protected = true;
            }
                
            public async Task Encrypt(Library.Interface.IEncryption encryption, IWriteChannel<LogMessage> logchannel)
            {
                if (encryption != null && !this.Encrypted)
                {
                    var tempfile = new Library.Utility.TempFile();
                    encryption.Encrypt(this.LocalFilename, tempfile);
                    await this.DeleteLocalFile(logchannel);
                    this.LocalTempfile = tempfile;
                    this.Hash = null;
                    this.Size = 0;
                    this.Encrypted = true;
                }
            }

            public static string CalculateFileHash(string filename)
            {
                using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                using (var hasher = System.Security.Cryptography.HashAlgorithm.Create(VOLUME_HASH))
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

            public async Task DeleteLocalFile(IWriteChannel<LogMessage> logchannel)
            {
                if (this.LocalTempfile != null)
                    try { this.LocalTempfile.Dispose(); }
                catch (Exception ex) { await logchannel.WriteAsync(LogMessage.Warning(string.Format("Failed to dispose temporary file: {0}", this.LocalTempfile), ex)); }
                finally { this.LocalTempfile = null; }
            }
        }


        [ChannelName("LogChannel")]
        private IWriteChannel<LogMessage> m_logchannel;

        private DatabaseCommon m_database;
        private IEncryption m_encryption;
        private IBackend m_backend;
        private Options m_options;
        private string m_backendurl;
        private bool m_uploadSuccess;
        private StatsCollector m_stats;

        public BackendHandler(Options options, string backendUrl, DatabaseCommon database, StatsCollector stats)
            : base()
        {
            m_backendurl = backendUrl;
            m_database = database;
            m_options = options;
            m_backendurl = backendUrl;
            m_stats = stats;
            if (!options.NoEncryption)
                m_encryption = DynamicLoader.EncryptionLoader.GetModule(options.EncryptionModule, options.Passphrase, options.RawOptions);
        }
            
        protected Task<T> RunRetryOnMain<T>(FileEntryItem fe, Func<Task<T>> method)
        {
            return RunOnMain<T>(() =>
                DoWithRetry<T>(fe, method)
            );
        }

        public Task PutUnencryptedAsync(string remotename, string localpath)
        {
            var fe = new FileEntryItem(BackendActionType.Put, remotename, null);
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
            
        public Task UploadFileAsync(VolumeWriterBase item, IndexVolumeWriter indexfile = null)
        {
            Tuple<IndexVolumeWriter, FileEntryItem> indexfe = null;
            if (indexfile != null)
                indexfe = new Tuple<IndexVolumeWriter, FileEntryItem>(indexfile, new FileEntryItem(BackendActionType.Put, indexfile.RemoteFilename));

            var fe = new FileEntryItem(BackendActionType.Put, item.RemoteFilename, indexfe);

            return RunRetryOnMain<bool>(fe, async () =>
            {
                if (fe.IsRetry && fe.Indexfile != null && fe.TrackedInDb)
                    await RenameFileAfterErrorAsync(fe);
                else
                    fe.IsRetry = true;
               
                await DoPut(fe);

                m_uploadSuccess = true;
                return true;
            });
        }

        public Task DeleteFileAsync(string remotename, long size)
        {
            var fe = new FileEntryItem(BackendActionType.Delete, remotename, null);
            return RunRetryOnMain(fe, () =>
                DoDelete(fe)
            );
        }

        public Task CreateFolder(string remotename)
        {
            var fe = new FileEntryItem(BackendActionType.CreateFolder, remotename, null);
            return RunRetryOnMain(fe, () =>
                DoCreateFolder(fe)
            );
        }


        public Task<IList<Library.Interface.IFileEntry>> ListFilesAsync()
        {
            var fe = new FileEntryItem(BackendActionType.List, null, null);
            return RunRetryOnMain(fe, () => 
                DoList(fe)
            );
        }

        public Task<Library.Utility.TempFile> GetFileAsync(string remotename, long size, string remotehash)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename, size, remotehash);
            return RunRetryOnMain(fe, () =>
                DoGet(fe)
            );
        }

        public Task<Tuple<Library.Utility.TempFile, long, string>> GetFileWithInfoAsync(string remotename)
        {
            var fe = new FileEntryItem(BackendActionType.Get, remotename, null);
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
            var fe = new FileEntryItem(BackendActionType.Get, remotename, null);
            return RunRetryOnMain(fe, async() =>
            {

            });
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
                await m_logchannel.WriteAsync(LogMessage.Warning(LC.L("Failed to dispose backend instance: {0}", (ex ?? dex).Message), dex));
            }

            m_backend = null;

        }

        private async Task<T> DoWithRetry<T>(FileEntryItem item, Func<Task<T>> method)
        {
            if (m_backend == null)
                m_backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions);
            if (m_backend == null)
                throw new Exception("Backend failed to re-load");

            Exception lastException = null;

            for(var i = 0; i < m_options.NumberOfRetries; i++)
            {
                if (m_options.RetryDelay.Ticks != 0 && i != 0)
                    System.Threading.Thread.Sleep(m_options.RetryDelay);
                
                try
                {
                    return await method();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await m_logchannel.WriteAsync(LogMessage.RetryAttempt(string.Format("Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, i + 1, m_options.NumberOfRetries, ex.Message), ex));

                    // If the thread is aborted, we exit here
                    if (ex is System.Threading.ThreadAbortException)
                        break;

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
                            await m_logchannel.WriteAsync(LogMessage.Warning(string.Format("Failed to create folder: {0}", ex.Message), dex)); 
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
            await m_logchannel.WriteAsync(LogMessage.Information(string.Format("Renaming \"{0}\" to \"{1}\"", oldname, newname)));
            await m_database.RenameRemoteFileAsync(oldname, newname);
            item.RemoteFilename = newname;

            // If there is an index file attached to the block file, 
            // it references the block filename, so we create a new index file
            // which is a copy of the current, but with the new name
            if (item.Indexfile != null)
            {
                if (!item.IndexfileUpdated)
                {
                    item.Indexfile.Item1.FinishVolume(item.Hash, item.Size);
                    item.Indexfile.Item1.Close();
                    item.IndexfileUpdated = true;
                }

                IndexVolumeWriter wr = null;
                try
                {
                    wr = new IndexVolumeWriter(m_options);
                    using(var rd = new IndexVolumeReader(p.CompressionModule, item.Indexfile.Item2.LocalFilename, m_options, m_options.BlockhashSize))
                        wr.CopyFrom(rd, x => x == oldname ? newname : x);
                    item.Indexfile.Item1.Dispose();
                    item.Indexfile = new Tuple<IndexVolumeWriter, FileEntryItem>(wr, item.Indexfile.Item2);
                    item.Indexfile.Item2.LocalTempfile.Dispose();
                    item.Indexfile.Item2.LocalTempfile = wr.TempFile;
                    wr.Close();
                }
                catch
                {
                    if (wr != null)
                        try { wr.Dispose(); }
                        catch { }
                        finally { wr = null; }

                    throw;
                }
            }
        }

        private async Task<bool> DoPut(FileEntryItem item)
        {
            if (m_encryption != null)
                await item.Encrypt(m_encryption, m_logchannel);

            if (item.UpdateHashAndSize(m_options) && item.TrackedInDb)
                await m_database.UpdateRemoteVolume(item.RemoteFilename, RemoteVolumeState.Uploading, item.Size, item.Hash);

            if (item.Indexfile != null && !item.IndexfileUpdated)
            {
                item.Indexfile.Item1.FinishVolume(item.Hash, item.Size);
                item.Indexfile.Item1.Close();
                item.IndexfileUpdated = true;
            }            
                
            await m_database.LogRemoteOperationAsync("put", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = item.Size, Hash = item.Hash }));
            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Started, item.RemoteFilename, item.Size);

            var begin = DateTime.Now;

            if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
            {
                using (var fs = System.IO.File.OpenRead(item.LocalFilename))
                using (var ts = new ThrottledStream(fs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                using (var pgs = new Library.Utility.ProgressReportingStream(ts, item.Size, HandleProgress))
                    ((Library.Interface.IStreamingBackend)m_backend).Put(item.RemoteFilename, pgs);
            }
            else
                m_backend.Put(item.RemoteFilename, item.LocalFilename);

            var duration = DateTime.Now - begin;
            Logging.Log.WriteMessage(string.Format("Uploaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(item.Size), duration, Library.Utility.Utility.FormatSizeString((long)(item.Size / duration.TotalSeconds))), Duplicati.Library.Logging.LogMessageType.Profiling);

            if (item.TrackedInDb)
                await m_database.UpdateRemoteVolume(item.RemoteFilename, RemoteVolumeState.Uploaded, item.Size, item.Hash);

            await m_stats.SendEventAsync(BackendActionType.Put, BackendEventType.Completed, item.RemoteFilename, item.Size);

            if (m_options.ListVerifyUploads)
            {
                var f = m_backend.List().Where(n => n.Name.Equals(item.RemoteFilename, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                if (f == null)
                    throw new Exception(string.Format("List verify failed, file was not found after upload: {0}", f.Name));
                else if (f.Size != item.Size && f.Size >= 0)
                    throw new Exception(string.Format("List verify failed for file: {0}, size was {1} but expected to be {2}", f.Name, f.Size, item.Size));
            }

            await item.DeleteLocalFile(m_logchannel);

            return true;
        }

        private async Task<IList<Library.Interface.IFileEntry>> DoList(FileEntryItem item)
        {
            await m_stats.SendEventAsync(BackendActionType.List, BackendEventType.Started, null, -1);

            var r = m_backend.List();

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

        private async Task<bool> DoDelete(FileEntryItem item)
        {
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
                    await m_logchannel.WriteAsync(LogMessage.Warning(LC.L("Delete operation failed for {0} with FileNotFound, listing contents", item.RemoteFilename), ex));
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
                        await m_logchannel.WriteAsync(LogMessage.Information(LC.L("Listing indicates file {0} is deleted correctly", item.RemoteFilename)));
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

            await m_database.UpdateRemoteVolume(item.RemoteFilename, RemoteVolumeState.Deleted, -1, null);
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
                    using (var pgs = new Library.Utility.ProgressReportingStream(ts, item.Size, HandleProgress))
                        ((Library.Interface.IStreamingBackend)m_backend).Get(item.RemoteFilename, pgs);
                }
                else
                    m_backend.Get(item.RemoteFilename, tmpfile);

                var duration = DateTime.Now - begin;
                Logging.Log.WriteMessage(string.Format("Downloaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(item.Size), duration, Library.Utility.Utility.FormatSizeString((long)(item.Size / duration.TotalSeconds))), Duplicati.Library.Logging.LogMessageType.Profiling);

                await m_database.LogRemoteOperationAsync("get", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = new System.IO.FileInfo(tmpfile).Length, Hash = FileEntryItem.CalculateFileHash(tmpfile) }));
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

                    var nh = FileEntryItem.CalculateFileHash(tmpfile);
                    if (!string.IsNullOrEmpty(item.Hash))
                    {
                        if (nh != item.Hash)
                            throw new Duplicati.Library.Main.BackendManager.HashMismathcException(Strings.Controller.HashMismatchError(tmpfile, item.Hash, nh));
                    }
                    else
                        item.Hash = nh;
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
                            if (!m_encryption.FilenameExtension.Equals(ext, StringComparison.InvariantCultureIgnoreCase))
                            {
                                // Check if the file is encrypted with something else
                                if (DynamicLoader.EncryptionLoader.Keys.Contains(ext, StringComparer.InvariantCultureIgnoreCase))
                                {
                                    await m_logchannel.WriteAsync(LogMessage.Verbose("Filename extension \"{0}\" does not match encryption module \"{1}\", using matching encryption module", ext, m_options.EncryptionModule));
                                    using(var encmodule = DynamicLoader.EncryptionLoader.GetModule(ext, m_options.Passphrase, m_options.RawOptions))
                                        (encmodule ?? m_encryption).Decrypt(tmpfile2, tmpfile);
                                }
                                // Check if the file is not encrypted
                                else if (DynamicLoader.CompressionLoader.Keys.Contains(ext, StringComparer.InvariantCultureIgnoreCase))
                                {
                                        await m_logchannel.WriteAsync(LogMessage.Verbose("Filename extension \"{0}\" does not match encryption module \"{1}\", guessing that it is not encrypted", ext, m_options.EncryptionModule));
                                }
                                // Fallback, lets see what happens...
                                else
                                {
                                    await m_logchannel.WriteAsync(LogMessage.Verbose("Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use specified encryption module as no others match", ext, m_options.EncryptionModule));
                                    m_encryption.Decrypt(tmpfile2, tmpfile);
                                }
                            }
                            else
                            {
                                m_encryption.Decrypt(tmpfile2, tmpfile);
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

        private void HandleProgress(long pg)
        {
            m_stats.UpdateBackendProgress(pg);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (m_backend != null)
                try { m_backend.Dispose(); }
                catch {}
                finally { m_backend = null; }
        }
    }
}

