// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Utility;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Newtonsoft.Json;
using Duplicati.Library.Localization.Short;
using System.Threading;
using System.Net;
using Duplicati.Library.Interface;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main
{
    internal class BackendManager : IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackendManager>();

        /// <summary>
        /// Class to represent hash failures
        /// </summary>
        [Serializable]
        public class HashMismatchException : Exception
        {
            /// <summary>
            /// Default constructor, sets a generic string as the message
            /// </summary>
            public HashMismatchException() : base() { }

            /// <summary>
            /// Constructor with non-default message
            /// </summary>
            /// <param name="message">The exception message</param>
            public HashMismatchException(string message) : base(message) { }

            /// <summary>
            /// Constructor with non-default message and inner exception details
            /// </summary>
            /// <param name="message">The exception message</param>
            /// <param name="innerException">The exception that caused this exception</param>
            public HashMismatchException(string message, Exception innerException) : base(message, innerException) { }
        }

        private enum OperationType
        {
            Get,
            Put,
            List,
            Delete,
            CreateFolder,
            Terminate,
            Nothing
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
            public readonly OperationType Operation;
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
            public readonly bool ExceptionKillsHandler;
            /// <summary>
            /// A flag indicating if the file is a extra metadata file
            /// that has no entry in the database
            /// </summary>
            public bool NotTrackedInDb;
            /// <summary>
            /// A flag that indicates that the download is only checked for the hash and the file is not decrypted or returned
            /// </summary>            
            public bool VerifyHashOnly;

            /// <summary>
            /// The event that is signaled once the operation is complete or has failed
            /// </summary>
            private readonly System.Threading.ManualResetEvent DoneEvent;

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

            public bool UpdateHashAndSize(Options options)
            {
                if (Hash == null || Size < 0)
                {
                    Hash = CalculateFileHash(this.LocalFilename, options);
                    Size = new System.IO.FileInfo(this.LocalFilename).Length;
                    return true;
                }

                return false;
            }

            public void DeleteLocalFile(IBackendWriter stat)
            {
                if (this.LocalTempfile != null)
                    try { this.LocalTempfile.Dispose(); }
                    catch (Exception ex) { Logging.Log.WriteWarningMessage(LOGTAG, "DeleteTemporaryFileError", ex, "Failed to dispose temporary file: {0}", this.LocalTempfile); }
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
            private readonly object m_dbqueuelock = new object();
            private readonly LocalDatabase m_database;
            private readonly System.Threading.Thread m_callerThread;
            private List<IDbEntry> m_dbqueue;

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

            public DatabaseCollector(LocalDatabase database)
            {
                m_database = database;
                m_dbqueue = new List<IDbEntry>();
                if (m_database != null)
                    m_callerThread = System.Threading.Thread.CurrentThread;
            }


            public void LogDbOperation(string action, string file, string result)
            {
                lock (m_dbqueuelock)
                    m_dbqueue.Add(new DbOperation() { Action = action, File = file, Result = result });
            }

            public void LogDbUpdate(string remotename, RemoteVolumeState state, long size, string hash)
            {
                lock (m_dbqueuelock)
                    m_dbqueue.Add(new DbUpdate() { Remotename = remotename, State = state, Size = size, Hash = hash });
            }

            public void LogDbRename(string oldname, string newname)
            {
                lock (m_dbqueuelock)
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
                lock (m_dbqueuelock)
                    if (m_dbqueue.Count == 0)
                        return false;
                    else
                    {
                        entries = m_dbqueue;
                        m_dbqueue = new List<IDbEntry>();
                    }

                // collect removed volumes for final db cleanup.
                HashSet<string> volsRemoved = new HashSet<string>();

                //As we replace the list, we can now freely access the elements without locking
                foreach (var e in entries)
                    if (e is DbOperation operation)
                        db.LogRemoteOperation(operation.Action, operation.File, operation.Result, transaction);
                    else if (e is DbUpdate update && update.State == RemoteVolumeState.Deleted)
                    {
                        db.UpdateRemoteVolume(update.Remotename, RemoteVolumeState.Deleted, update.Size, update.Hash, true, TimeSpan.FromHours(2), transaction);
                        volsRemoved.Add(update.Remotename);
                    }
                    else if (e is DbUpdate dbUpdate)
                        db.UpdateRemoteVolume(dbUpdate.Remotename, dbUpdate.State, dbUpdate.Size, dbUpdate.Hash, transaction);
                    else if (e is DbRename rename)
                        db.RenameRemoteFile(rename.Oldname, rename.Newname, transaction);
                    else if (e != null)
                        Logging.Log.WriteErrorMessage(LOGTAG, "InvalidQueueElement", null, "Queue had element of type: {0}, {1}", e.GetType(), e);

                // Finally remove volumes from DB.
                if (volsRemoved.Count > 0)
                    db.RemoveRemoteVolumes(volsRemoved);

                return true;
            }
        }

        private readonly BlockingQueue<FileEntryItem> m_queue;
        private readonly Options m_options;
        private volatile Exception m_lastException;
        private readonly Library.Interface.IEncryption m_encryption;
        private readonly object m_encryptionLock = new object();
        private Library.Interface.IBackend m_backend;
        private readonly string m_backendurl;
        private readonly IBackendWriter m_statwriter;
        private System.Threading.Thread m_thread;
        private readonly ITaskReader m_taskReader;
        private readonly DatabaseCollector m_db;

        // Cache these
        private readonly int m_numberofretries;
        private readonly TimeSpan m_retrydelay;
        private readonly Boolean m_retrywithexponentialbackoff;

        public string BackendUrl { get { return m_backendurl; } }

        public BackendManager(string backendurl, Options options, IBackendWriter statwriter, LocalDatabase database)
        {
            m_options = options;
            m_backendurl = backendurl;
            m_statwriter = statwriter;
            m_taskReader = (statwriter as BasicResults).TaskControl;
            m_numberofretries = options.NumberOfRetries;
            m_retrydelay = options.RetryDelay;
            m_retrywithexponentialbackoff = options.RetryWithExponentialBackoff;

            m_db = new DatabaseCollector(database);

            m_backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions);
            if (m_backend == null)
            {
                string shortname = m_backendurl;

                // Try not to leak hostnames or other information in the error messages
                try { shortname = new Library.Utility.Uri(shortname).Scheme; }
                catch { }

                throw new Duplicati.Library.Interface.UserInformationException(string.Format("Backend not supported: {0}", shortname), "BackendNotSupported");
            }

            if (!m_options.NoEncryption)
            {
                m_encryption = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions);
                if (m_encryption == null)
                    throw new Duplicati.Library.Interface.UserInformationException(string.Format("Encryption method not supported: {0}", m_options.EncryptionModule), "EncryptionMethodNotSupported");
            }

            m_queue = new BlockingQueue<FileEntryItem>(options.SynchronousUpload ? 1 : (options.AsynchronousUploadLimit == 0 ? int.MaxValue : options.AsynchronousUploadLimit));
            m_thread = new System.Threading.Thread(this.ThreadRun);
            m_thread.Name = "Backend Async Worker";
            m_thread.IsBackground = true;
            m_thread.Start();
        }

        public static string CalculateFileHash(string filename, Options options)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                return CalculateFileHash(fs, options);
        }

        /// <summary> Calculate file hash directly on stream object (for piping) </summary>
        public static string CalculateFileHash(System.IO.Stream stream, Options options)
        {
            using (var hasher = HashFactory.CreateHasher(options.FileHashAlgorithm))
                return Convert.ToBase64String(hasher.ComputeHash(stream));
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
                            m_taskReader?.ProgressRendevouz().Await();

                            if (m_options.NoConnectionReuse && m_backend != null)
                            {
                                m_backend.Dispose();
                                m_backend = null;
                            }

                            if (m_backend == null)
                                m_backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions);
                            if (m_backend == null)
                                throw new Exception("Backend failed to re-load");

                            using (new Logging.Timer(LOGTAG, string.Format("RemoteOperation{0}", item.Operation), string.Format("RemoteOperation{0}", item.Operation)))
                                switch (item.Operation)
                                {
                                    case OperationType.Put:
                                        DoPutAsync(item).Await();
                                        // We do not auto create folders,
                                        // because we know the folder exists
                                        uploadSuccess = true;
                                        break;
                                    case OperationType.Get:
                                        DoGetAsync(item).Await();
                                        break;
                                    case OperationType.List:
                                        DoList(item);
                                        break;
                                    case OperationType.Delete:
                                        DoDeleteAsync(item).Await();
                                        break;
                                    case OperationType.CreateFolder:
                                        DoCreateFolderAsync(item).Await();
                                        break;
                                    case OperationType.Terminate:
                                        m_queue.SetCompleted();
                                        break;
                                    case OperationType.Nothing:
                                        item.SignalComplete();
                                        break;
                                }

                            lastException = null;
                            retries = m_numberofretries;
                        }
                        catch (Exception ex)
                        {
                            retries++;
                            lastException = ex;
                            Logging.Log.WriteRetryMessage(LOGTAG, $"Retry{item.Operation}", ex, "Operation {0} with file {1} attempt {2} of {3} failed with message: {4}", item.Operation, item.RemoteFilename, retries, m_numberofretries, ex.Message);

                            // If the thread is aborted, we exit here
                            if (ex is System.Threading.ThreadAbortException)
                            {
                                m_queue.SetCompleted();
                                item.Exception = ex;
                                item.SignalComplete();
                                throw;
                            }

                            if (ex is WebException exception)
                            {
                                // Refresh DNS name if we fail to connect in order to prevent issues with incorrect DNS entries
                                if (exception.Status == System.Net.WebExceptionStatus.NameResolutionFailure)
                                {
                                    try
                                    {
                                        var names = m_backend.GetDNSNamesAsync(m_taskReader.TransferToken).Await() ?? new string[0];
                                        foreach (var name in names)
                                            if (!string.IsNullOrWhiteSpace(name))
                                                System.Net.Dns.GetHostEntry(name);
                                    }
                                    catch
                                    {
                                    }
                                }
                            }

                            m_statwriter.SendEvent(item.BackendActionType, retries < m_numberofretries ? BackendEventType.Retrying : BackendEventType.Failed, item.RemoteFilename, item.Size);

                            bool recovered = false;
                            if (!uploadSuccess && ex is Duplicati.Library.Interface.FolderMissingException && m_options.AutocreateFolders)
                            {
                                try
                                {
                                    // If we successfully create the folder, we can re-use the connection
                                    m_backend.CreateFolderAsync(m_taskReader.TransferToken).Await();
                                    recovered = true;
                                }
                                catch (Exception dex)
                                {
                                    Logging.Log.WriteWarningMessage(LOGTAG, "FolderCreateError", dex, "Failed to create folder: {0}", ex.Message);
                                }
                            }

                            // To work around the Apache WEBDAV issue, we rename the file here
                            if (item.Operation == OperationType.Put && retries < m_numberofretries && !item.NotTrackedInDb)
                                RenameFileAfterError(item);

                            if (!recovered)
                            {
                                try { m_backend.Dispose(); }
                                catch (Exception dex) { Logging.Log.WriteWarningMessage(LOGTAG, "BackendDisposeError", dex, "Failed to dispose backend instance: {0}", ex.Message); }

                                m_backend = null;

                                if (retries < m_numberofretries && m_retrydelay.Ticks != 0)
                                {
                                    var delay = Library.Utility.Utility.GetRetryDelay(m_retrydelay, retries, m_retrywithexponentialbackoff);
                                    var target = DateTime.Now.Add(delay);

                                    while (target > DateTime.Now)
                                    {
                                        if (m_taskReader?.ProgressToken.IsCancellationRequested ?? false)
                                            break;

                                        System.Threading.Thread.Sleep(500);
                                    }
                                }
                            }
                        }


                    } while (retries < m_numberofretries);

                    if (lastException != null && !(lastException is Duplicati.Library.Interface.FileMissingException) && item.Operation == OperationType.Delete)
                    {
                        Logging.Log.WriteInformationMessage(LOGTAG, "DeleteFileFailed", LC.L("Failed to delete file {0}, testing if file exists", item.RemoteFilename));
                        try
                        {
                            if (!m_backend.List().Select(x => x.Name).Contains(item.RemoteFilename))
                            {
                                lastException = null;
                                Logging.Log.WriteInformationMessage(LOGTAG, "DeleteFileFailureRecovered", LC.L("Recovered from problem with attempting to delete non-existing file {0}", item.RemoteFilename));
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "DeleteFileFailure", ex, LC.L("Failed to recover from error deleting file {0}", item.RemoteFilename), ex);
                        }
                    }

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
            var guid = VolumeWriterBase.GenerateGuid();
            var time = p.Time.Ticks == 0 ? p.Time : p.Time.AddSeconds(1);
            var newname = VolumeBase.GenerateFilename(p.FileType, p.Prefix, guid, time, p.CompressionModule, p.EncryptionModule);
            var oldname = item.RemoteFilename;

            m_statwriter.SendEvent(item.BackendActionType, BackendEventType.Rename, oldname, item.Size);
            m_statwriter.SendEvent(item.BackendActionType, BackendEventType.Rename, newname, item.Size);
            Logging.Log.WriteInformationMessage(LOGTAG, "RenameRemoteTargetFile", "Renaming \"{0}\" to \"{1}\"", oldname, newname);
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
                    var hashsize = HashFactory.HashSizeBytes(m_options.BlockHashAlgorithm);
                    wr = new IndexVolumeWriter(m_options);
                    using (var rd = new IndexVolumeReader(p.CompressionModule, item.Indexfile.Item2.LocalFilename, m_options, hashsize))
                        wr.CopyFrom(rd, x => x == oldname ? newname : x);
                    item.Indexfile.Item1.Dispose();
                    item.Indexfile = new Tuple<IndexVolumeWriter, FileEntryItem>(wr, item.Indexfile.Item2);
                    item.Indexfile.Item2.LocalTempfile.Dispose();
                    item.Indexfile.Item2.LocalTempfile = wr.TempFile;
                    wr.Close();
                }
                catch
                {
                    wr?.Dispose();
                    throw;
                }
            }
        }

        private string m_lastThrottleUploadValue = null;
        private string m_lastThrottleDownloadValue = null;

        private void HandleProgress(ThrottledStream ts, long pg)
        {
            // This pauses and throws on cancellation, but ignores stop
            m_taskReader?.TransferRendevouz();

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

            m_statwriter.BackendProgressUpdater.UpdateProgress(pg);
        }

        private async Task DoPutAsync(FileEntryItem item)
        {
            if (m_encryption != null)
                lock (m_encryptionLock)
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

            var begin = DateTime.Now;

            if (m_backend is Library.Interface.IStreamingBackend streamingBackend && !m_options.DisableStreamingTransfers)
            {
                using (var fs = System.IO.File.OpenRead(item.LocalFilename))
                using (var act = new Duplicati.StreamUtil.TimeoutObservingStream(fs) { ReadTimeout = m_backend is ITimeoutExemptBackend ? Timeout.Infinite : m_options.ReadWriteTimeout })
                using (var ts = new ThrottledStream(act, m_options.MaxUploadPrSecond, 0))
                using (var pgs = new Library.Utility.ProgressReportingStream(ts, pg => HandleProgress(ts, pg)))
                using (var linkedToken = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(m_taskReader.TransferToken, act.TimeoutToken))
                    await streamingBackend.PutAsync(item.RemoteFilename, pgs, linkedToken.Token);
            }
            else
                await m_backend.PutAsync(item.RemoteFilename, item.LocalFilename, m_taskReader.TransferToken);

            var duration = DateTime.Now - begin;
            Logging.Log.WriteProfilingMessage(LOGTAG, "UploadSpeed", "Uploaded {0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(item.Size), duration, Library.Utility.Utility.FormatSizeString((long)(item.Size / duration.TotalSeconds)));

            if (!item.NotTrackedInDb)
                m_db.LogDbUpdate(item.RemoteFilename, RemoteVolumeState.Uploaded, item.Size, item.Hash);

            m_statwriter.SendEvent(BackendActionType.Put, BackendEventType.Completed, item.RemoteFilename, item.Size);

            if (m_options.ListVerifyUploads)
            {
                var f = m_backend.List().FirstOrDefault(n => n.Name.Equals(item.RemoteFilename, StringComparison.OrdinalIgnoreCase));
                if (f == null)
                    throw new Exception(string.Format("List verify failed, file was not found after upload: {0}", item.RemoteFilename));
                else if (f.Size != item.Size && f.Size >= 0)
                    throw new Exception(string.Format("List verify failed for file: {0}, size was {1} but expected to be {2}", f.Name, f.Size, item.Size));
            }

            item.DeleteLocalFile(m_statwriter);
        }

        private async Task<(TempFile tempFile, long downloadSize, string remotehash)> DoGetFileAsync(FileEntryItem item, IEncryption useDecrypter)
        {
            TempFile retTarget, dlTarget = null, decryptTarget = null;
            long retDownloadSize;
            string retHashcode;
            try
            {
                dlTarget = new Library.Utility.TempFile();
                if (m_backend is Library.Interface.IStreamingBackend streamingBackend && !m_options.DisableStreamingTransfers)
                {
                    // extended to use stacked streams
                    using (var fs = System.IO.File.OpenWrite(dlTarget))
                    using (var act = new Duplicati.StreamUtil.TimeoutObservingStream(fs) { WriteTimeout = m_backend is ITimeoutExemptBackend ? Timeout.Infinite : m_options.ReadWriteTimeout })
                    using (var hasher = HashFactory.CreateHasher(m_options.FileHashAlgorithm))
                    using (var hs = new HashCalculatingStream(act, hasher))
                    using (var ss = new ShaderStream(hs, true))
                    using (var linkedToken = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(m_taskReader.TransferToken, act.TimeoutToken))
                    {
                        // NOTE: It is possible to hash the file in parallel with download
                        // but this requires some careful handling of buffers and threads/tasks
                        // to avoid adding more overhead than what is gained.

                        using (var ts = new ThrottledStream(ss, 0, m_options.MaxDownloadPrSecond))
                        using (var pgs = new Library.Utility.ProgressReportingStream(ts, pg => HandleProgress(ts, pg)))
                        { await streamingBackend.GetAsync(item.RemoteFilename, pgs, linkedToken.Token); }
                        ss.Flush();
                        retDownloadSize = ss.TotalBytesWritten;
                        retHashcode = Convert.ToBase64String(hs.GetFinalHash());
                    }
                }
                else
                {
                    await m_backend.GetAsync(item.RemoteFilename, dlTarget, m_taskReader.TransferToken);
                    retDownloadSize = new System.IO.FileInfo(dlTarget).Length;
                    retHashcode = CalculateFileHash(dlTarget, m_options);
                }

                // Decryption is not placed in the stream stack because there seemed to be an effort
                // to throw a CryptographicException on fail. If in main stack, we cannot differentiate
                // in which part of the stack the source of an exception resides.
                if (useDecrypter != null)
                {
                    decryptTarget = new Library.Utility.TempFile();
                    lock (m_encryptionLock)
                    {
                        try { useDecrypter.Decrypt(dlTarget, decryptTarget); }
                        // If we fail here, make sure that we throw a crypto exception
                        catch (System.Security.Cryptography.CryptographicException) { throw; }
                        catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }
                    }
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

        private async Task DoGetAsync(FileEntryItem item)
        {
            Library.Utility.TempFile tmpfile = null;
            m_statwriter.SendEvent(BackendActionType.Get, BackendEventType.Started, item.RemoteFilename, item.Size);

            try
            {
                var begin = DateTime.Now;

                // We already know the filename, so we put the decision about if and which decryptor to
                // use prior to download. This allows to set up stacked streams or a pipe doing decryption
                Interface.IEncryption useDecrypter = null;
                if (!item.VerifyHashOnly && !m_options.NoEncryption)
                {
                    useDecrypter = m_encryption;
                    {
                        lock (m_encryptionLock)
                        {
                            try
                            {
                                // Auto-guess the encryption module
                                var ext = (System.IO.Path.GetExtension(item.RemoteFilename) ?? "").TrimStart('.');
                                if (!m_encryption.FilenameExtension.Equals(ext, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Check if the file is encrypted with something else
                                    if (DynamicLoader.EncryptionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                    {
                                        Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", using matching encryption module", ext, m_options.EncryptionModule);
                                        useDecrypter = DynamicLoader.EncryptionLoader.GetModule(ext, m_options.Passphrase, m_options.RawOptions);
                                        useDecrypter = useDecrypter ?? m_encryption;
                                    }
                                    // Check if the file is not encrypted
                                    else if (DynamicLoader.CompressionLoader.Keys.Contains(ext, StringComparer.OrdinalIgnoreCase))
                                    {
                                        Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", guessing that it is not encrypted", ext, m_options.EncryptionModule);
                                        useDecrypter = null;
                                    }
                                    // Fallback, lets see what happens...
                                    else
                                    {
                                        Logging.Log.WriteVerboseMessage(LOGTAG, "AutomaticDecryptionDetection", "Filename extension \"{0}\" does not match encryption module \"{1}\", attempting to use specified encryption module as no others match", ext, m_options.EncryptionModule);
                                    }
                                }
                            }
                            // If we fail here, make sure that we throw a crypto exception
                            catch (System.Security.Cryptography.CryptographicException) { throw; }
                            catch (Exception ex) { throw new System.Security.Cryptography.CryptographicException(ex.Message, ex); }
                        }
                    }
                }

                (tmpfile, var dataSizeDownloaded, var fileHash) = await DoGetFileAsync(item, useDecrypter);

                var duration = DateTime.Now - begin;
                Logging.Log.WriteProfilingMessage(LOGTAG, "DownloadSpeed", "Downloaded {3}{0} in {1}, {2}/s", Library.Utility.Utility.FormatSizeString(dataSizeDownloaded),
                    duration, Library.Utility.Utility.FormatSizeString((long)(dataSizeDownloaded / duration.TotalSeconds)),
                    useDecrypter == null ? "" : "and decrypted ");

                m_db.LogDbOperation("get", item.RemoteFilename, JsonConvert.SerializeObject(new { Size = dataSizeDownloaded, Hash = fileHash }));
                m_statwriter.SendEvent(BackendActionType.Get, BackendEventType.Completed, item.RemoteFilename, dataSizeDownloaded);

                if (!m_options.SkipFileHashChecks)
                {
                    if (item.Size >= 0)
                    {
                        if (dataSizeDownloaded != item.Size)
                            throw new Exception(Strings.Controller.DownloadedFileSizeError(item.RemoteFilename, dataSizeDownloaded, item.Size));
                    }
                    else
                        item.Size = dataSizeDownloaded;

                    if (!string.IsNullOrEmpty(item.Hash))
                    {
                        if (fileHash != item.Hash)
                            throw new HashMismatchException(Strings.Controller.HashMismatchError(tmpfile, item.Hash, fileHash));
                    }
                    else
                        item.Hash = fileHash;
                }

                if (item.VerifyHashOnly)
                {
                    tmpfile.Dispose();
                }
                else
                {
                    item.Result = tmpfile;
                    tmpfile = null;
                }

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

            var r = m_backend.List().ToList();

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

        private async Task DoDeleteAsync(FileEntryItem item)
        {
            m_statwriter.SendEvent(BackendActionType.Delete, BackendEventType.Started, item.RemoteFilename, item.Size);

            string result = null;
            try
            {
                await m_backend.DeleteAsync(item.RemoteFilename, m_taskReader.TransferToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var isFileMissingException = ex is Library.Interface.FileMissingException || ex is System.IO.FileNotFoundException;
                var wr = ex as System.Net.WebException == null ? null : (ex as System.Net.WebException).Response as System.Net.HttpWebResponse;

                if (isFileMissingException || (wr != null && wr.StatusCode == System.Net.HttpStatusCode.NotFound))
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileFailed", LC.L("Delete operation failed for {0} with FileNotFound, listing contents", item.RemoteFilename));
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
                        Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileSuccess", LC.L("Listing indicates file {0} was deleted correctly", item.RemoteFilename));
                        return;
                    }
                    else
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "DeleteRemoteFileFailed", ex, LC.L("Listing confirms file {0} was not deleted", item.RemoteFilename));
                    }
                }

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

        private async Task DoCreateFolderAsync(FileEntryItem item)
        {
            m_statwriter.SendEvent(BackendActionType.CreateFolder, BackendEventType.Started, null, -1);

            string result = null;
            try
            {
                await m_backend.CreateFolderAsync(m_taskReader.TransferToken).ConfigureAwait(false);
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

            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                if (m_queue.Enqueue(req) && m_options.SynchronousUpload)
                {
                    req.WaitForComplete();
                    if (req.Exception != null)
                        throw req.Exception;
                }
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }

            if (m_lastException != null)
                throw m_lastException;
        }

        public void Put(VolumeWriterBase item, IndexVolumeWriter indexfile = null, Action indexVolumeFinishedCallback = null, bool synchronous = false)
        {
            if (m_lastException != null)
                throw m_lastException;

            item.Close();
            var req = new FileEntryItem(OperationType.Put, item.RemoteFilename, null);
            req.LocalTempfile = item.TempFile;

            if (m_lastException != null)
                throw m_lastException;

            FileEntryItem req2 = null;

            // As the network link is the bottleneck,
            // we encrypt the dblock volume before the
            // upload is enqueued (i.e. on the worker thread)
            if (m_encryption != null)
                lock (m_encryptionLock)
                    req.Encrypt(m_encryption, m_statwriter);

            req.UpdateHashAndSize(m_options);
            m_db.LogDbUpdate(item.RemoteFilename, RemoteVolumeState.Uploading, req.Size, req.Hash);

            // We do not encrypt the dindex volume, because it is small,
            // and may need to be re-written if the dblock upload is retried
            if (indexfile != null)
            {
                m_db.LogDbUpdate(indexfile.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                req2 = new FileEntryItem(OperationType.Put, indexfile.RemoteFilename);
                req2.LocalTempfile = indexfile.TempFile;
                req.Indexfile = new Tuple<IndexVolumeWriter, FileEntryItem>(indexfile, req2);

                indexfile.FinishVolume(req.Hash, req.Size);
                indexVolumeFinishedCallback?.Invoke();
                indexfile.Close();
                req.IndexfileUpdated = true;
            }

            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                m_db.FlushDbMessages(true);

                if (m_queue.Enqueue(req) && (m_options.SynchronousUpload || synchronous))
                {
                    req.WaitForComplete();
                    if (req.Exception != null)
                        throw req.Exception;
                }

                if (req2 != null && m_queue.Enqueue(req2) && (m_options.SynchronousUpload || synchronous))
                {
                    req2.WaitForComplete();
                    if (req2.Exception != null)
                        throw req2.Exception;
                }
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }

            if (m_lastException != null)
                throw m_lastException;
        }

        public Library.Utility.TempFile GetWithInfo(string remotename, out long size, out string hash)
        {
            if (m_lastException != null)
                throw m_lastException;

            hash = null; size = -1;
            var req = new FileEntryItem(OperationType.Get, remotename, -1, null);
            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                if (m_queue.Enqueue(req))
                    ((IDownloadWaitHandle)req).Wait(out hash, out size);
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }

            if (m_lastException != null)
                throw m_lastException;

            return (Library.Utility.TempFile)req.Result;
        }

        public Library.Utility.TempFile Get(string remotename, long size, string hash)
        {
            if (m_lastException != null)
                throw m_lastException;

            var req = new FileEntryItem(OperationType.Get, remotename, size, hash);
            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                if (m_queue.Enqueue(req))
                    ((IDownloadWaitHandle)req).Wait();
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
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
            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                if (m_queue.Enqueue(req))
                    return req;
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }

            if (m_lastException != null)
                throw m_lastException;
            else
                throw new InvalidOperationException("GetAsync called after backend is shut down");
        }

        public void GetForTesting(string remotename, long size, string hash)
        {
            if (m_lastException != null)
                throw m_lastException;

            if (string.IsNullOrWhiteSpace(hash))
                throw new InvalidOperationException("Cannot test a file without the hash");

            var req = new FileEntryItem(OperationType.Get, remotename, size, hash);
            req.VerifyHashOnly = true;
            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                if (m_queue.Enqueue(req))
                {
                    req.WaitForComplete();
                    if (req.Exception != null)
                        throw req.Exception;
                }
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }

            if (m_lastException != null)
                throw m_lastException;
        }

        public IList<Library.Interface.IFileEntry> List()
        {
            if (m_lastException != null)
                throw m_lastException;

            var req = new FileEntryItem(OperationType.List, null);
            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                if (m_queue.Enqueue(req))
                {
                    req.WaitForComplete();
                    if (req.Exception != null)
                        throw req.Exception;
                }
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }

            if (m_lastException != null)
                throw m_lastException;

            return (IList<Library.Interface.IFileEntry>)req.Result;
        }

        public void WaitForComplete(LocalDatabase db, System.Data.IDbTransaction transation)
        {
            m_statwriter.BackendProgressUpdater.SetBlocking(true);
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

        public void WaitForEmpty(LocalDatabase db, System.Data.IDbTransaction transation)
        {
            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                m_db.FlushDbMessages(db, transation);
                if (m_lastException != null)
                    throw m_lastException;

                var item = new FileEntryItem(OperationType.Nothing, null);
                if (m_queue.Enqueue(item))
                    item.WaitForComplete();

                m_db.FlushDbMessages(db, transation);

                if (m_lastException != null)
                    throw m_lastException;
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }
        }

        public void Delete(string remotename, long size, bool synchronous = false)
        {
            if (m_lastException != null)
                throw m_lastException;

            m_db.LogDbUpdate(remotename, RemoteVolumeState.Deleting, size, null);
            var req = new FileEntryItem(OperationType.Delete, remotename, size, null);
            try
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(true);
                if (m_queue.Enqueue(req) && synchronous)
                {
                    req.WaitForComplete();
                    if (req.Exception != null)
                        throw req.Exception;
                }
            }
            finally
            {
                m_statwriter.BackendProgressUpdater.SetBlocking(false);
            }

            if (m_lastException != null)
                throw m_lastException;
        }

        public Task<IQuotaInfo> GetQuotaInfoAsync(CancellationToken cancelToken)
            => (m_backend as IQuotaEnabledBackend)?.GetQuotaInfoAsync(cancelToken) ?? Task.FromResult<IQuotaInfo>(null);

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
                    m_thread.Interrupt();
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
            catch (Exception ex) { Logging.Log.WriteErrorMessage(LOGTAG, "ShutdownError", ex, "Backend Shutdown error: {0}", ex.Message); }
        }
    }
}
