using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Utility;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Newtonsoft.Json;

namespace Duplicati.Library.Main
{
    internal class BackendManager : IDisposable
    {
    	public const string VOLUME_HASH = "SHA256";
    
        /// <summary>
        /// Class to represent hash failures
        /// </summary>
        [Serializable]
        public class HashMismathcException : Exception
        {
            /// <summary>
            /// Default constructor, sets a generic string as the message
            /// </summary>
            public HashMismathcException() : base() { }

            /// <summary>
            /// Constructor with non-default message
            /// </summary>
            /// <param name="message">The exception message</param>
            public HashMismathcException(string message) : base(message) { }

            /// <summary>
            /// Constructor with non-default message and inner exception details
            /// </summary>
            /// <param name="message">The exception message</param>
            /// <param name="innerException">The exception that caused this exception</param>
            public HashMismathcException(string message, Exception innerException) : base(message, innerException) { }
        }
    
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
            TempFile Wait(out string hash, out long size);
        }

        private class FileEntryItem : IDownloadWaitHandle
        {
            /// <summary>
            /// The current operation this entry represents
            /// </summary>
            public OperationType Operation;
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
            /// The result object
            /// </summary>
            public object Result;
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
            /// An exception that this item has caused
            /// </summary>
            public Exception Exception;
            /// <summary>
            /// True if an exception ultimately kills the handler,
            /// false if the item is returned with an exception
            /// </summary>
            public bool ExceptionKillsHandler;
            /// <summary>
            /// A flag indicating if the file is a extra metadata file
            /// that has no entry in the database
            /// </summary>
            public bool NotTrackedInDb;

            /// <summary>
            /// The event that is signaled once the operation is complete or has failed
            /// </summary>
            private System.Threading.ManualResetEvent DoneEvent;

            public FileEntryItem(OperationType operation, string remotefilename, Tuple<IndexVolumeWriter, FileEntryItem> indexfile = null)
            {
                Operation = operation;
                RemoteFilename = remotefilename;
                Indexfile = indexfile;
                ExceptionKillsHandler = operation != OperationType.Get;
                Size = -1;

                DoneEvent = new System.Threading.ManualResetEvent(false);
            }

            public FileEntryItem(OperationType operation, string remotefilename, long size, string hash, Tuple<IndexVolumeWriter, FileEntryItem> indexfile = null)
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
                if (Exception != null)
                	throw Exception;
                	
                return (TempFile)this.Result;
            }

            TempFile IDownloadWaitHandle.Wait(out string hash, out long size)
            {
                this.WaitForComplete();
                
                if (Exception != null)
                	throw Exception;
                	
                hash = this.Hash;
                size = this.Size;
                
                return (TempFile)this.Result;
            }

            public void Encrypt(Library.Interface.IEncryption encryption, IBackendWriter stat)
            {
                if (encryption != null && !this.Encrypted)
                {
                    var tempfile = new Library.Utility.TempFile();
                    encryption.Encrypt(this.LocalFilename, tempfile);
                    this.DeleteLocalFile(stat);
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
            
            public void DeleteLocalFile(IBackendWriter stat)
            {
                if (this.LocalTempfile != null)
                    try { this.LocalTempfile.Dispose(); }
                    catch (Exception ex) { stat.AddWarning(string.Format("Failed to dispose temporary file: {0}", this.LocalTempfile), ex); }
                    finally { this.LocalTempfile = null; }
            }
            
            public BackendActionType BackendActionType
            { 
                get
                {
                    switch (this.Operation)
                    {
                        case OperationType.Get:
                            return BackendActionType.Get;
                        case OperationType.Put:
                            return BackendActionType.Put;
                        case OperationType.Delete:
                            return BackendActionType.Delete;
                        case OperationType.List:
                            return BackendActionType.List;
                        case OperationType.CreateFolder:
                            return BackendActionType.CreateFolder;
                        default:
                            throw new Exception(string.Format("Unexpected operation type: {0}", this.Operation));
                    }
                }
            }
            
        }
        
        private class DatabaseCollector
        {
	        private object m_dbqueuelock = new object();
	        private LocalDatabase m_database;
	        private System.Threading.Thread m_callerThread;
	        private List<IDbEntry> m_dbqueue;
	        private IBackendWriter m_stats;
	        
	        private interface IDbEntry { }
	        
	        private class DbOperation : IDbEntry
	        {
	        	public string Action;
	        	public string File;
	        	public string Result;
	        }
	        
	        private class DbUpdate : IDbEntry
	        {
	        	public string Remotename;
	        	public RemoteVolumeState State;
	        	public long Size;
	        	public string Hash;
	        }
	        
            private class DbRename : IDbEntry
            {
                public string Oldname;
                public string Newname;
            }
            
	        public DatabaseCollector(LocalDatabase database, IBackendWriter stats)
	        {
	        	m_database = database;
	        	m_stats = stats;
	        	m_dbqueue = new List<IDbEntry>();	
	        	if (m_database != null)
	        		m_callerThread = System.Threading.Thread.CurrentThread;
	        }

	
	        public void LogDbOperation(string action, string file, string result)
	        {
	        	lock(m_dbqueuelock)
		    		m_dbqueue.Add(new DbOperation() { Action = action, File = file, Result = result });
	        }
	        
	        public void LogDbUpdate(string remotename, RemoteVolumeState state, long size, string hash)
	        {
	        	lock(m_dbqueuelock)
		    		m_dbqueue.Add(new DbUpdate() { Remotename = remotename, State = state, Size = size, Hash = hash });
	        }

            public void LogDbRename(string oldname, string newname)
            {
                lock(m_dbqueuelock)
                    m_dbqueue.Add(new DbRename() { Oldname = oldname, Newname = newname });
            }
            
	        public bool FlushDbMessages(bool checkThread = false)
	        {
	        	if (m_database != null && (checkThread == false || m_callerThread == System.Threading.Thread.CurrentThread))
	        		return FlushDbMessages(m_database, null);
	        	
	        	return false;
	        }
	        
	        public bool FlushDbMessages(LocalDatabase db, System.Data.IDbTransaction transaction)
	        {
	        	List<IDbEntry> entries;
	        	lock(m_dbqueuelock)
	        		if (m_dbqueue.Count == 0)
	        			return false;
	        		else
	        		{
	        			entries = m_dbqueue;
	        			m_dbqueue = new List<IDbEntry>();
	        		}
	        	
	        	//As we replace the list, we can now freely access the elements without locking
	        	foreach(var e in entries)
	        		if (e is DbOperation)
	        			db.LogRemoteOperation(((DbOperation)e).Action, ((DbOperation)e).File, ((DbOperation)e).Result, transaction);
	        		else if (e is DbUpdate)
	        			db.UpdateRemoteVolume(((DbUpdate)e).Remotename, ((DbUpdate)e).State, ((DbUpdate)e).Size, ((DbUpdate)e).Hash, transaction);
                    else if (e is DbRename)
                        db.RenameRemoteFile(((DbRename)e).Oldname, ((DbRename)e).Newname, transaction);
	        		else if (e != null)
	        			m_stats.AddError(string.Format("Queue had element of type: {0}, {1}", e.GetType(), e.ToString()), null);
	        			
	        	return true;
	        }
        }

        private BlockingQueue<FileEntryItem> m_queue;
        private Options m_options;
        private volatile Exception m_lastException;
        private Library.Interface.IEncryption m_encryption;
        private readonly object m_encryptionLock = new object();
        private Library.Interface.IBackend m_backend;
        private string m_backendurl;
        private IBackendWriter m_statwriter;
        private System.Threading.Thread m_thread;
		private DatabaseCollector m_db;
                
        public string BackendUrl { get { return m_backendurl; } }

        public BackendManager(string backendurl, Options options, IBackendWriter statwriter, LocalDatabase database)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_statwriter = statwriter;
            m_db = new DatabaseCollector(database, statwriter);

            m_backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions);
            if (m_backend == null)
            	throw new Exception(string.Format("Backend not supported: {0}", m_backendurl));

            if (!m_options.NoEncryption)
            {
                m_encryption = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions);
                if (m_encryption == null)
                	throw new Exception(string.Format("Encryption method not supported: ", m_options.EncryptionModule));
            }

            m_queue = new BlockingQueue<FileEntryItem>(options.SynchronousUpload ? 1 : (options.AsynchronousUploadLimit == 0 ? int.MaxValue : options.AsynchronousUploadLimit));
            m_thread = new System.Threading.Thread(this.ThreadRun);
            m_thread.Name = "Backend Async Worker";
            m_thread.IsBackground = true;
            m_thread.Start();
        }
        
        private void ThreadRun()
        {
            var uploadSuccess = false;
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
                            if (m_backend == null)
                                throw new Exception("Backend failed to re-load");

                            using(new Logging.Timer(string.Format("RemoteOperation{0}", item.Operation)))
                                switch (item.Operation)
                                {
                                    case OperationType.Put:
                                        DoPut(item);
                                        // We do not auto create folders,
                                        // because we know the folder exists
                                        uploadSuccess = true;
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
                            m_statwriter.AddRetryAttempt(string.Format("Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, retries, m_options.NumberOfRetries, ex.Message), ex);
                            m_statwriter.SendEvent(item.BackendActionType, retries < m_options.NumberOfRetries ? BackendEventType.Retrying : BackendEventType.Failed, item.RemoteFilename, item.Size);

							bool recovered = false;
                            if (!uploadSuccess && ex is Duplicati.Library.Interface.FolderMissingException && m_options.AutocreateFolders)
                            {
	                            try 
	                            { 
	                            	// If we successfully create the folder, we can re-use the connection
	                            	m_backend.CreateFolder(); 
	                            	recovered = true;
	                            }
	                            catch(Exception dex) 
	                            { 
	                            	m_statwriter.AddWarning(string.Format("Failed to create folder: {0}", ex.Message), dex); 
	                            }
                            }
                            
                            // To work around the Apache WEBDAV issue, we rename the file here
                            if (item.Operation == OperationType.Put && retries < m_options.NumberOfRetries && !item.NotTrackedInDb)
                                RenameFileAfterError(item);
                            
                            if (!recovered)
                            {
                                try { m_backend.Dispose(); }
                                catch(Exception dex) { m_statwriter.AddWarning(string.Format("Failed to dispose backend instance: {0}", ex.Message), dex); }
    
                                m_backend = null;
                                
                                if (retries < m_options.NumberOfRetries && m_options.RetryDelay.Ticks != 0)
                                    System.Threading.Thread.Sleep(m_options.RetryDelay);
                            }
                        }
                        

                    } while (retries < m_options.NumberOfRetries);

                    if (lastException != null)
                    {
                    	item.Exception = lastException;
                        if (item.Operation == OperationType.Put)
                        	item.DeleteLocalFile(m_statwriter);
                                                        
                        if (item.ExceptionKillsHandler)
                        {
                        	m_lastException = lastException;

							//TODO: If there are temp files in the queue, we must delete them
	                        m_queue.SetCompleted();
                       	}
                        
                    }
                    
                    item.SignalComplete();
                }
            }

            //Make sure everything in the queue is signalled
            FileEntryItem i;
            while ((i = m_queue.Dequeue()) != null)
                i.SignalComplete();
        }

        private void RenameFileAfterError(FileEntryItem item)
        {
            var p = VolumeBase.ParseFilename(item.RemoteFilename);
            var guid = VolumeWriterBase.GenerateGuid(m_options);
            var time = p.Time.Ticks == 0 ? p.Time : p.Time.AddSeconds(1);
            var newname = VolumeBase.GenerateFilename(p.FileType, p.Prefix, guid, time, p.CompressionModule, p.EncryptionModule);
            var oldname = item.RemoteFilename;
            
            m_statwriter.SendEvent(item.BackendActionType, BackendEventType.Rename, oldname, item.Size);
            m_statwriter.SendEvent(item.BackendActionType, BackendEventType.Rename, newname, item.Size);
            m_statwriter.AddMessage(string.Format("Renaming \"{0}\" to \"{1}\"", oldname, newname));
            m_db.LogDbRename(oldname, newname);
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
                    var hashsize = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm).HashSize / 8;
                    wr = new IndexVolumeWriter(m_options);
                    using(var rd = new IndexVolumeReader(p.CompressionModule, item.Indexfile.Item2.LocalFilename, m_options, hashsize))
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
        
        private void HandleProgress(long pg)
        {
            m_statwriter.BackendProgressUpdater.UpdateProgress(pg);
        }

        private void DoPut(FileEntryItem item)
        {
            if (m_encryption != null)
                lock(m_encryptionLock)
                    item.Encrypt(m_encryption, m_statwriter);
                
            if (item.UpdateHashAndSize(m_options) && !item.NotTrackedInDb)
            	m_db.LogDbUpdate(item.RemoteFilename, RemoteVolumeState.Uploading, item.Size, item.Hash);

            if (item.Indexfile != null && !item.IndexfileUpdated)
            {
                item.Indexfile.Item1.FinishVolume(item.Hash, item.Size);
                item.Indexfile.Item1.Close();
                item.IndexfileUpdated = true;
            }            

            m_db.LogDbOperation("put", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = item.Size, Hash = item.Hash }));
            m_statwriter.SendEvent(BackendActionType.Put, BackendEventType.Started, item.RemoteFilename, item.Size);

            if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
            {
                using (var fs = System.IO.File.OpenRead(item.LocalFilename))
                using (var ts = new ThrottledStream(fs, m_options.MaxDownloadPrSecond, m_options.MaxUploadPrSecond))
                using (var pgs = new Library.Utility.ProgressReportingStream(ts, item.Size, HandleProgress))
                    ((Library.Interface.IStreamingBackend)m_backend).Put(item.RemoteFilename, pgs);
            }
            else
                m_backend.Put(item.RemoteFilename, item.LocalFilename);

            if (!item.NotTrackedInDb)
			    m_db.LogDbUpdate(item.RemoteFilename, RemoteVolumeState.Uploaded, item.Size, item.Hash);
			
            m_statwriter.SendEvent(BackendActionType.Put, BackendEventType.Completed, item.RemoteFilename, item.Size);

            if (m_options.ListVerifyUploads)
            {
                var f = m_backend.List().Where(n => n.Name.Equals(item.RemoteFilename, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                if (f == null)
                    throw new Exception(string.Format("List verify failed, file was not found after upload: {0}", f.Name));
                else if (f.Size != item.Size && f.Size >= 0)
                    throw new Exception(string.Format("List verify failed for file: {0}, size was {1} but expected to be {2}", f.Name, f.Size, item.Size));
            }

			item.DeleteLocalFile(m_statwriter);
        }

        private void DoGet(FileEntryItem item)
        {
            Library.Utility.TempFile tmpfile = null;
            m_statwriter.SendEvent(BackendActionType.Get, BackendEventType.Started, item.RemoteFilename, item.Size);

            try
            {
                tmpfile = new Library.Utility.TempFile();
                if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
                {
                    using (var fs = System.IO.File.OpenWrite(tmpfile))
                    using (var ts = new ThrottledStream(fs, m_options.MaxDownloadPrSecond, m_options.MaxUploadPrSecond))
                    using (var pgs = new Library.Utility.ProgressReportingStream(ts, item.Size, HandleProgress))
                        ((Library.Interface.IStreamingBackend)m_backend).Get(item.RemoteFilename, pgs);
                }
                else
                    m_backend.Get(item.RemoteFilename, tmpfile);
                
                m_db.LogDbOperation("get", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = new System.IO.FileInfo(tmpfile).Length, Hash = FileEntryItem.CalculateFileHash(tmpfile) }));
                m_statwriter.SendEvent(BackendActionType.Get, BackendEventType.Completed, item.RemoteFilename, new System.IO.FileInfo(tmpfile).Length);

                if (!m_options.SkipFileHashChecks)
                {
                    var nl = new System.IO.FileInfo(tmpfile).Length;
                    if (item.Size >= 0)
                    {
                        if (nl != item.Size)
                            throw new Exception(string.Format(Strings.Controller.DownloadedFileSizeError, item.RemoteFilename, nl, item.Size));
                    }
                    else
                    	item.Size = nl;

                    var nh = FileEntryItem.CalculateFileHash(tmpfile);
                    if (!string.IsNullOrEmpty(item.Hash))
                    {
                        if (nh != item.Hash)
                            throw new HashMismathcException(string.Format(Strings.Controller.HashMismatchError, tmpfile, item.Hash, nh));
                    }
                    else
                    	item.Hash = nh;
                }

                // Decrypt before returning
                if (!m_options.NoEncryption)
                {
                    try
                    {
                        using(var tmpfile2 = tmpfile)
                        { 
                        	tmpfile = new Library.Utility.TempFile();
                            lock(m_encryptionLock)
                        	    m_encryption.Decrypt(tmpfile2, tmpfile);
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
            m_statwriter.SendEvent(BackendActionType.List, BackendEventType.Started, null, -1);

            var r = m_backend.List();

            StringBuilder sb = new StringBuilder();
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
            m_db.LogDbOperation("list", "", sb.ToString());
            item.Result = r;

            m_statwriter.SendEvent(BackendActionType.List, BackendEventType.Completed, null, r.Count);
        }

        private void DoDelete(FileEntryItem item)
        {
            m_statwriter.SendEvent(BackendActionType.Delete, BackendEventType.Started, item.RemoteFilename, item.Size);

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
                m_db.LogDbOperation("delete", item.RemoteFilename, result);
            }
            
            m_db.LogDbUpdate(item.RemoteFilename, RemoteVolumeState.Deleted, -1, null);
            m_statwriter.SendEvent(BackendActionType.Delete, BackendEventType.Completed, item.RemoteFilename, item.Size);
        }
        
        private void DoCreateFolder(FileEntryItem item)
        {
            m_statwriter.SendEvent(BackendActionType.CreateFolder, BackendEventType.Started, null, -1);

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
                m_db.LogDbOperation("createfolder", item.RemoteFilename, result);
            }
            
            m_statwriter.SendEvent(BackendActionType.CreateFolder, BackendEventType.Completed, null, -1);
        }
        
        public void PutUnencrypted(string remotename, string localpath)
        {
            if (m_lastException != null)
                throw m_lastException;
                
            var req = new FileEntryItem(OperationType.Put, remotename, null);
            req.SetLocalfilename(localpath);
            req.Encrypted = true; //Prevent encryption
            req.NotTrackedInDb = true; //Prevent Db updates
            
            if (m_queue.Enqueue(req) && m_options.SynchronousUpload)
            {
                req.WaitForComplete();
                if (req.Exception != null)
                    throw req.Exception;
            }
            
            if (m_lastException != null)
                throw m_lastException;
        }

        public void Put(VolumeWriterBase item, IndexVolumeWriter indexfile = null)
		{
			if (m_lastException != null)
				throw m_lastException;
            
			item.Close();
			m_db.LogDbUpdate(item.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
			var req = new FileEntryItem(OperationType.Put, item.RemoteFilename, null);
			req.LocalTempfile = item.TempFile;
						
			if (m_lastException != null)
				throw m_lastException;

            FileEntryItem req2 = null;
            
            // As the network link is the bottleneck,
            // we encrypt the dblock volume before the
            // upload is enqueue (i.e. on the worker thread)
            if (m_encryption != null)
                lock (m_encryptionLock)
                    req.Encrypt(m_encryption, m_statwriter);
                    
            req.UpdateHashAndSize(m_options);
            
            // We do not encrypt the dindex volume, because it is small,
            // and may need to be re-written if the dblock upload is retried
			if (indexfile != null)
			{
				m_db.LogDbUpdate(indexfile.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
				req2 = new FileEntryItem(OperationType.Put, indexfile.RemoteFilename);
				req2.LocalTempfile = indexfile.TempFile;
                req.Indexfile = new Tuple<IndexVolumeWriter, FileEntryItem>(indexfile, req2);
            }
            
            if (m_queue.Enqueue(req) && m_options.SynchronousUpload)
            {
                req.WaitForComplete();
                if (req.Exception != null)
                    throw req.Exception;
            }
            
            if (req2 != null && m_queue.Enqueue(req2) && m_options.SynchronousUpload)
            {
                req2.WaitForComplete();
                if (req2.Exception != null)
                    throw req2.Exception;
            }
            
            if (m_lastException != null)
                throw m_lastException;
        }

        public Library.Utility.TempFile Get(string remotename, long size, string hash)
		{
			if (m_lastException != null)
				throw m_lastException;

			var req = new FileEntryItem(OperationType.Get, remotename, size, hash);
			if (m_queue.Enqueue(req))
			{
				req.WaitForComplete();
				if (req.Exception != null)
					throw req.Exception;
			}

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
			{
				req.WaitForComplete();
				if (req.Exception != null)
					throw req.Exception;
			}

			if (m_lastException != null)
				throw m_lastException;

            return (IList<Library.Interface.IFileEntry>)req.Result;
        }

        public void WaitForComplete(LocalDatabase db, System.Data.IDbTransaction transation)
        {
        	m_db.FlushDbMessages(db, transation);
            if (m_lastException != null)
                throw m_lastException;

            var item = new FileEntryItem(OperationType.Terminate, null);
            if (m_queue.Enqueue(item))
                item.WaitForComplete();

        	m_db.FlushDbMessages(db, transation);

            if (m_lastException != null)
                throw m_lastException;
        }

        public void CreateFolder(string remotename)
		{
			if (m_lastException != null)
				throw m_lastException;

			var req = new FileEntryItem(OperationType.CreateFolder, remotename);
			if (m_queue.Enqueue(req))
			{
				req.WaitForComplete();
				if (req.Exception != null)
					throw req.Exception;
			}

			if (m_lastException != null)
				throw m_lastException;
        }

        public void Delete(string remotename, long size, bool synchronous = false)
		{
			if (m_lastException != null)
				throw m_lastException;
				
			m_db.LogDbUpdate(remotename, RemoteVolumeState.Deleting, size, null);
			var req = new FileEntryItem(OperationType.Delete, remotename, size, null);
			if (m_queue.Enqueue(req) && synchronous)
			{
				req.WaitForComplete();
				if (req.Exception != null)
					throw req.Exception;
			}

			if (m_lastException != null)
				throw m_lastException;
		}
        
        public bool FlushDbMessages(LocalDatabase database, System.Data.IDbTransaction transaction)
        {
        	return m_db.FlushDbMessages(database, transaction);
        }
        
        public bool FlushDbMessages()
        {
        	return m_db.FlushDbMessages(false);
        }

        public void Dispose()
        {
            if (m_queue != null && !m_queue.Completed)
                m_queue.SetCompleted();
            
            if (m_thread != null)
            {
                if (!m_thread.Join(TimeSpan.FromSeconds(10)))
                {
                    m_thread.Abort();
                    m_thread.Join(TimeSpan.FromSeconds(10));
                }

                m_thread = null;
            }
            
            //TODO: We cannot null this, because it will be recreated
            //Should we wait for queue completion or abort immediately?
            if (m_backend != null)
            {
                m_backend.Dispose();
                m_backend = null;
            }
            
        	try { m_db.FlushDbMessages(true); }
        	catch (Exception ex) { m_statwriter.AddError(string.Format("Backend Shutdown error: {0}", ex.Message), ex); }
        }
    }
}
