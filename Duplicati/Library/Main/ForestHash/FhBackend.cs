using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Utility;
using Duplicati.Library.Main.ForestHash.Database;
using Duplicati.Library.Main.ForestHash.Volumes;
using Newtonsoft.Json;

namespace Duplicati.Library.Main.ForestHash
{
    public class FhBackend : IDisposable
    {
    	public const string VOLUME_HASH = "SHA256";
    
        private enum OperationType
        {
            Get,
            Put,
            List,
            Delete,
            CreateFolder,
            Terminate
        }

        public interface IDownloadWaitHandle
        {
            TempFile Wait();
        }

        private class FileEntryItem : IDownloadWaitHandle
        {
            public OperationType Operation;
            public string RemoteFilename;
            public string LocalFilename;
            public bool Encrypted;
            public object Result;
            public string Hash;
            public long Size;
            public ShadowVolumeWriter Shadow;

            private System.Threading.ManualResetEvent DoneEvent;

            public FileEntryItem(OperationType operation, string remotefilename, ShadowVolumeWriter shadow = null)
            {
                Operation = operation;
                RemoteFilename = remotefilename;
                Shadow = shadow;

                DoneEvent = new System.Threading.ManualResetEvent(false);
            }

            public FileEntryItem(OperationType operation, string remotefilename, long size, string hash, ShadowVolumeWriter shadow = null)
                : this(operation, remotefilename, shadow)
            {
                Size = size;
                Hash = hash;
            }

            public void SignalComplete()
            {
                DoneEvent.Set();
            }

            public void WaitForComplete()
            {
                DoneEvent.WaitOne();
            }

            TempFile IDownloadWaitHandle.Wait()
            {
                this.WaitForComplete();
                return (TempFile)this.Result;
            }

            public void Encrypt(Library.Interface.IEncryption encryption)
            {
                if (encryption != null && !this.Encrypted)
                {
                    var sourcefile = this.LocalFilename + "." + encryption.FilenameExtension;
                    encryption.Encrypt(this.LocalFilename, sourcefile);
                    this.LocalFilename = sourcefile;
                    this.Hash = null;
                    this.Size = 0;
                    this.Encrypted = true;
                }
            }

            public static string CalculateFileHash(string filename)
            {
                using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                    return Convert.ToBase64String(System.Security.Cryptography.HashAlgorithm.Create(VOLUME_HASH).ComputeHash(fs));
            }

            public bool UpdateHashAndSize(FhOptions options)
            {
                if (Hash == null || Size < 0)
                {
                    Hash = CalculateFileHash(this.LocalFilename);
                    Size = new System.IO.FileInfo(this.LocalFilename).Length;
                    return true;
                }

                return false;
            }
        }

        private BlockingQueue<FileEntryItem> m_queue;
        private System.Threading.Thread m_thread;
        private FhOptions m_options;
        private LocalDatabase m_database;
        private volatile Exception m_lastException;
        private Library.Interface.IEncryption m_encryption;
        private Library.Interface.IBackend m_backend;
        private string m_backendurl;
        private CommunicationStatistics m_stats;

        public FhBackend(string backendurl, FhOptions options, LocalDatabase database, CommunicationStatistics stats)
        {
            m_options = options;
            m_database = database;
            m_backendurl = backendurl;
            m_stats = stats;

            m_backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions);

            if (!m_options.NoEncryption)
                m_encryption = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions);

            m_queue = new BlockingQueue<FileEntryItem>(options.AsynchronousUpload ? (options.AsynchronousUploadLimit == 0 ? int.MaxValue : options.AsynchronousUploadLimit) : 1);
            m_thread = new System.Threading.Thread(this.ThreadRun);
            m_thread.Name = "Async Uploader";
            m_thread.IsBackground = true;
            m_thread.Start();
        }

        private void ThreadRun()
        {
            while (!m_queue.Completed)
            {
                var item = m_queue.Dequeue();
                if (item != null)
                {
                    int retries = 0;
                    Exception lastException = null;

                    do
                    {
                        try
                        {
                            if (m_options.NoConnectionReuse && m_backend != null)
                            {
                                m_backend.Dispose();
                                m_backend = null;
                            }

                            if (m_backend == null)
                                m_backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions);

                            switch (item.Operation)
                            {
                                case OperationType.Put:
                                    DoPut(item);
                                    break;
                                case OperationType.Get:
                                    DoGet(item);
                                    break;
                                case OperationType.List:
                                    DoList(item);
                                    break;
                                case OperationType.Delete:
                                    DoDelete(item);
                                    break;
                                case OperationType.CreateFolder:
                                    DoCreateFolder(item);
                                    break;
                                case OperationType.Terminate:
                                    m_queue.SetCompleted();
                                    break;
                            }

                            lastException = null;
                            retries = m_options.NumberOfRetries;
                        }
                        catch (Exception ex)
                        {
                            retries++;
                            lastException = ex;
                            m_stats.LogRetryAttempt(string.Format("Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, retries, m_options.NumberOfRetries, ex.Message), ex);
                            m_database.LogMessage("warning", string.Format("Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, retries, m_options.NumberOfRetries, ex.Message), ex);

                            try { m_backend.Dispose(); }
                            catch(Exception dex) { m_database.LogMessage("warning", string.Format("Failed to expose backend instance: {0}", ex.Message), dex); }

                            m_backend = null;

                            if (retries < m_options.NumberOfRetries && m_options.RetryDelay.Ticks != 0)
                                System.Threading.Thread.Sleep(m_options.RetryDelay);
                        }

                    } while (retries < m_options.NumberOfRetries);

                    if (lastException != null)
                    {
                        m_lastException = lastException;
                        m_queue.SetCompleted();
                    }

                    item.SignalComplete();
                }
            }

            //Make sure everything in the queue is signalled
            FileEntryItem i;
            while ((i = m_queue.Dequeue()) != null)
                i.SignalComplete();
        }

        private void DoPut(FileEntryItem item)
        {
            item.Encrypt(m_encryption);
            if (item.UpdateHashAndSize(m_options))
                m_database.UpdateRemoteVolume(item.RemoteFilename, RemoteVolumeState.Uploading, item.Size, item.Hash);

            if (item.Shadow != null)
            {
                item.Shadow.FinishVolume(item.Hash, item.Size);
                item.Shadow.Close();
                item.Shadow = null;
            }            

            m_stats.AddNumberOfRemoteCalls(1);
            m_database.LogRemoteOperation("put", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = item.Size, Hash = item.Hash }));

            if (m_backend is Library.Interface.IStreamingBackend)
            {
                using (var fs = System.IO.File.OpenRead(item.LocalFilename))
                using (var ts = new ThrottledStream(fs, m_options.MaxDownloadPrSecond, m_options.MaxUploadPrSecond))
                using (var pgs = new Utility.ProgressReportingStream(ts, item.Size))
                    ((Library.Interface.IStreamingBackend)m_backend).Put(item.RemoteFilename, pgs);
            }
            else
                m_backend.Put(item.RemoteFilename, item.LocalFilename);

            m_stats.AddBytesUploaded(item.Size);
        }

        private void DoGet(FileEntryItem item)
        {
            Utility.TempFile tmpfile = null;
            try
            {
                m_stats.AddNumberOfRemoteCalls(1);
                tmpfile = new Utility.TempFile();
                if (m_backend is Library.Interface.IStreamingBackend)
                {
                    using (var fs = System.IO.File.OpenWrite(tmpfile))
                    using (var ts = new ThrottledStream(fs, m_options.MaxDownloadPrSecond, m_options.MaxUploadPrSecond))
                    using (var pgs = new Utility.ProgressReportingStream(ts, item.Size))
                        ((Library.Interface.IStreamingBackend)m_backend).Get(item.RemoteFilename, pgs);
                }
                else
                    m_backend.Get(item.RemoteFilename, tmpfile);
                
                m_stats.AddBytesDownloaded(new System.IO.FileInfo(tmpfile).Length);
                m_database.LogRemoteOperation("get", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = new System.IO.FileInfo(tmpfile).Length, Hash = FileEntryItem.CalculateFileHash(tmpfile) }));

                if (!m_options.SkipFileHashChecks)
                {
                    if (item.Size >= 0)
                    {
                        var nl = new System.IO.FileInfo(tmpfile).Length;
                        if (nl != item.Size)
                            throw new Exception(string.Format(Strings.BackendWrapper.DownloadedFileSizeError, item.RemoteFilename, nl, item.Size));
                    }

                    if (!string.IsNullOrEmpty(item.Hash))
                    {
                        var nh = FileEntryItem.CalculateFileHash(tmpfile);
                        if (nh != item.Hash)
                            throw new BackendWrapper.HashMismathcException(string.Format(Strings.BackendWrapper.HashMismatchError, tmpfile, item.Hash, nh));
                    }
                }

                // Decrypt before returning
                if (!m_options.NoEncryption)
                {
                    Utility.TempFile tmpfile2 = tmpfile;

                    try
                    {
                        tmpfile = new Utility.TempFile();
                        m_encryption.Decrypt(tmpfile2, tmpfile);
                    }
                    catch (Exception ex)
                    {
                        //If we fail here, make sure that we throw a crypto exception
                        if (ex is System.Security.Cryptography.CryptographicException)
                            throw;
                        else
                            throw new System.Security.Cryptography.CryptographicException(ex.Message, ex);
                    }
                    finally
                    {
                        if (tmpfile2 != null)
                            tmpfile2.Dispose();
                    }
                }

                item.Result = tmpfile;
                tmpfile = null;
            }
            catch
            {
                if (tmpfile != null)
                    tmpfile.Dispose();

                throw;
            }
        }

        private void DoList(FileEntryItem item)
        {
            var r = m_backend.List();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[");
            bool first = true;
            foreach (var e in r)
            {
                if (first)
                    first = false;
                else
                    sb.AppendLine(",");
                sb.Append(JsonConvert.SerializeObject(e));
            }

            sb.AppendLine();
            sb.Append("]");
            m_database.LogRemoteOperation("list", "", sb.ToString());
            item.Result = r;
        }

        private void DoDelete (FileEntryItem item)
        {
            m_stats.AddNumberOfRemoteCalls(1);

            string result = null;
            try
            {
                m_backend.Delete(item.RemoteFilename);
            } 
            catch (Exception ex)
            {
                result = ex.ToString();
                throw;
            }
            finally
            {
                m_database.LogRemoteOperation("delete", item.RemoteFilename, result);
            }
            
            m_database.UpdateRemoteVolume(item.RemoteFilename, RemoteVolumeState.Deleted, -1, null);
        }
        
        private void DoCreateFolder (FileEntryItem item)
        {
            string result = null;
            try
            {
                (m_backend as Library.Interface.IBackend_v2).CreateFolder();
            } 
            catch (Exception ex)
            {
                result = ex.ToString();
                throw;
            }
            finally
            {
                m_database.LogRemoteOperation("createfolder", item.RemoteFilename, result);
            }
        }

        public void Put(VolumeWriterBase item, ShadowVolumeWriter shadow = null)
        {
            if (m_lastException != null)
                throw m_lastException;
            
            item.Close();
            m_database.UpdateRemoteVolume(item.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
            var req = new FileEntryItem(OperationType.Put, item.RemoteFilename, shadow);
            req.LocalFilename = item.LocalFilename;

            if (m_queue.Enqueue(req) && !m_options.AsynchronousUpload)
                req.WaitForComplete();

            if (m_lastException != null)
                throw m_lastException;

            if (shadow != null)
            {
                m_database.UpdateRemoteVolume(shadow.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                var req2 = new FileEntryItem(OperationType.Put, shadow.RemoteFilename);
                req2.LocalFilename = shadow.LocalFilename;

                if (m_queue.Enqueue(req2) && !m_options.AsynchronousUpload)
                    req2.WaitForComplete();

                if (m_lastException != null)
                    throw m_lastException;
            }
        }

        public Library.Utility.TempFile Get(string remotename, long size, string hash)
        {
            if (m_lastException != null)
                throw m_lastException;

            var req = new FileEntryItem(OperationType.Get, remotename, size, hash);
            if (m_queue.Enqueue(req))
                req.WaitForComplete();

            if (m_lastException != null)
                throw m_lastException;

            return (Library.Utility.TempFile)req.Result;
        }

        public IDownloadWaitHandle GetAsync(string remotename, long size, string hash)
        {
            if (m_lastException != null)
                throw m_lastException;

            var req = new FileEntryItem(OperationType.Get, remotename, size, hash);
            if (m_queue.Enqueue(req))
                return req;

            if (m_lastException != null)
                throw m_lastException;
            else
                throw new InvalidOperationException("GetAsync called after backend is shut down");
        }

        public IList<Library.Interface.IFileEntry> List()
        {
            if (m_lastException != null)
                throw m_lastException;

            var req = new FileEntryItem(OperationType.List, null);
            if (m_queue.Enqueue(req))
                req.WaitForComplete();

            if (m_lastException != null)
                throw m_lastException;

            return (IList<Library.Interface.IFileEntry>)req.Result;
        }

        public void WaitForComplete()
        {
            if (m_lastException != null)
                throw m_lastException;

            var item = new FileEntryItem(OperationType.Terminate, null);
            if (m_queue.Enqueue(item))
                item.WaitForComplete();

            if (m_lastException != null)
                throw m_lastException;
        }

        public void CreateFolder(string remotename)
        {
            var item = new FileEntryItem(OperationType.CreateFolder, remotename);
            if (m_queue.Enqueue(item))
                item.WaitForComplete();
            
            if (m_lastException != null)
                throw m_lastException;
        }

        public void Delete(string remotename, bool synchronous = false)
        {
            m_database.UpdateRemoteVolume(remotename, RemoteVolumeState.Deleting, -1, null);
            var item = new FileEntryItem(OperationType.Delete, remotename);
            if (m_queue.Enqueue(item) && synchronous)
                item.WaitForComplete();

            if (m_lastException != null)
                throw m_lastException;
        }

        public void Dispose()
        {
            if (m_queue != null && !m_queue.Completed)
                m_queue.SetCompleted();

            if (m_backend != null)
            {
                m_backend.Dispose();
                m_backend = null;
            }

            if (m_thread != null)
            {
                if (!m_thread.Join(TimeSpan.FromSeconds(10)))
                {
                    m_thread.Abort();
                    m_thread.Join(TimeSpan.FromSeconds(10));
                }

                m_thread = null;
            }
        }
    }
}
