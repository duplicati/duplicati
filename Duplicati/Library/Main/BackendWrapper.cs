#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Internal class that ensures retry operations, tracks statistics and performs asyncronous operations
    /// </summary>
    internal class BackendWrapper : IDisposable
    {
        /// <summary>
        /// The actual backend that this class is wrapping
        /// </summary>
        private Duplicati.Library.Interface.IBackend m_backend;
        /// <summary>
        /// The statistics gathering object
        /// </summary>
        private CommunicationStatistics m_statistics;
        /// <summary>
        /// The set of options used by Duplicati
        /// </summary>
        private Options m_options;

        /// <summary>
        /// A list of pending async operations
        /// </summary>
        private Queue<KeyValuePair<BackupEntryBase, string>> m_pendingOperations;

        /// <summary>
        /// This event is set after an item has been removed from the queue
        /// </summary>
        private System.Threading.AutoResetEvent m_asyncItemProcessed;

        /// <summary>
        /// This event is set after an item has been placed into the queue
        /// </summary>
        private System.Threading.AutoResetEvent m_asyncItemReady;

        /// <summary>
        /// A flag used to signal the termination of the async thread
        /// </summary>
        private volatile bool m_asyncTerminate = false;

        /// <summary>
        /// The lock for items in the processing queue
        /// </summary>
        private object m_queuelock;

        /// <summary>
        /// A value describing if the backend wrapper is performing asynchronous operations
        /// </summary>
        private bool m_async;

        /// <summary>
        /// The thread performing asynchronous operations
        /// </summary>
        private System.Threading.Thread m_workerThread;

        /// <summary>
        /// The exception, if any, encountered by the worker thread
        /// </summary>
        private Exception m_workerException;

        /// <summary>
        /// The number of manifests uploaded asynchronously
        /// </summary>
        private int m_manifestUploads = 0;

        /// <summary>
        /// Temporary variable for progress reporting 
        /// </summary>
        private string m_statusmessage;

        /// <summary>
        /// The cache filename strategy used
        /// </summary>
        /// <returns>A cache filename strategy object</returns>
        public static FilenameStrategy CreateCacheFilenameStrategy() { return new FilenameStrategy("dpl", "_", true); }

        /// <summary>
        /// The filename strategy used to generate and parse filenames
        /// </summary>
        private FilenameStrategy m_filenamestrategy;
        
        /// <summary>
        /// A local version of the cache filename strategy
        /// </summary>
        private FilenameStrategy m_cachefilenamestrategy = BackendWrapper.CreateCacheFilenameStrategy();

        /// <summary>
        /// A list of items that should be removed
        /// </summary>
        private List<BackupEntryBase> m_orphans;

        /// <summary>
        /// The progress reporting event
        /// </summary>
        public event RSync.RSyncDir.ProgressEventDelegate ProgressEvent;

        /// <summary>
        /// An event that is raised after an async item has been uploaded, used to pause the uploads
        /// </summary>
        public event EventHandler AsyncItemProcessedEvent;

        /// <summary>
        /// If encryption is used, this is the instance that performs it
        /// </summary>
        private Duplicati.Library.Interface.IEncryption m_encryption = null;

        /// <summary>
        /// The number of bytes added to a file when encrypted
        /// </summary>
        private long m_encryptionSizeOverhead = 0;

        /// <summary>
        /// Gets the number of bytes added to a file when encrypted and transfered
        /// </summary>
        public long FileSizeOverhead { get { return m_encryptionSizeOverhead; } }

        /// <summary>
        /// Class to represent hash failures
        /// </summary>
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

        /// <summary>
        /// Constructs a new BackendWrapper
        /// </summary>
        /// <param name="statistics">The statistics logging module, may be null</param>
        /// <param name="backend">The url to the backend to wrap</param>
        /// <param name="options">A set of backend options</param>
        public BackendWrapper(CommunicationStatistics statistics, string backend, Options options)
        {
            m_statistics = statistics;
            m_options = options;

            m_filenamestrategy = new FilenameStrategy(m_options);

            m_backend = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(backend, m_options.RawOptions);
            if (m_backend == null)
                throw new Exception(string.Format(Strings.BackendWrapper.BackendNotFoundError, m_backend));

            if (!m_options.NoEncryption)
            {
                if (string.IsNullOrEmpty(m_options.Passphrase))
                    throw new Exception(Strings.BackendWrapper.PassphraseMissingError);

                m_encryption = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions);

                m_encryptionSizeOverhead = m_encryption.SizeOverhead(m_options.VolumeSize);
            }

            if (m_options.AutoCleanup)
                m_orphans = new List<BackupEntryBase>();

            if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && !System.IO.Directory.Exists(m_options.SignatureCachePath))
                System.IO.Directory.CreateDirectory(m_options.SignatureCachePath);

            m_async = m_options.AsynchronousUpload;
            if (m_async)
            {
                //If we are using async operations, the entire class is actually threadsafe,
                //utilizing a common exclusive lock on all operations. But the implementation does
                //not prevent starvation, so it should not be called by multiple threads.
                m_pendingOperations = new Queue<KeyValuePair<BackupEntryBase, string>>();
                m_asyncItemProcessed = new System.Threading.AutoResetEvent(false);
                m_asyncItemReady = new System.Threading.AutoResetEvent(false);
                m_workerThread = new System.Threading.Thread(ProcessQueue);
                m_workerThread.Name = "AsyncUploaderThread";
                m_queuelock = new object();
                m_workerThread.Start();
            }
        }

        public void AddOrphan(BackupEntryBase entry)
        {
            if (m_orphans != null)
                m_orphans.Add(entry);
            else
                Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.PartialFileFoundMessage, entry.Filename), Duplicati.Library.Logging.LogMessageType.Warning);
        }

        public ManifestEntry GetBackupSet(string timelimit)
        {
            if (string.IsNullOrEmpty(timelimit))
                timelimit = "now";

            return GetBackupSet(Core.Timeparser.ParseTimeInterval(timelimit, DateTime.Now));
        }

        /// <summary>
        /// Gets the manifest entry that represents the most recent full backup, with a list of incrementals in the chain.
        /// </summary>
        /// <param name="timelimit">The oldest allowed time for a backup, set to DateTime.Now to get the most recent entry</param>
        /// <returns>The manifest entry with incrementals that contains all changes up to the timelimit</returns>
        public ManifestEntry GetBackupSet(DateTime timelimit)
        {
            List<ManifestEntry> backups = GetBackupSets();

            if (backups.Count == 0)
                throw new Exception(Strings.BackendWrapper.NoBackupsFoundError);

            ManifestEntry bestFit = backups[0];
            List<ManifestEntry> additions = new List<ManifestEntry>();
            foreach (ManifestEntry be in backups)
                if (be.Time <= timelimit)
                {
                    bestFit = be;
                    additions.Clear();
                    foreach (ManifestEntry bex in be.Incrementals)
                        if (bex.Time <= timelimit)
                            additions.Add(bex);

                }

            if (bestFit.Volumes.Count == 0)
                throw new Exception(Strings.BackendWrapper.FilenameParseError);
            
            bestFit.Incrementals.Clear();
            bestFit.Incrementals.AddRange(additions);
            return bestFit;

        }

        /// <summary>
        /// Parses the filelist into a list of full backups, each with a chain of incrementals attached
        /// </summary>
        /// <param name="files">The list of filenames found on the backend</param>
        /// <returns>A list of full backups</returns>
        public List<ManifestEntry> SortAndPairSets(List<Duplicati.Library.Interface.IFileEntry> files)
        {
            List<ManifestEntry> incrementals = new List<ManifestEntry>();
            List<ManifestEntry> fulls = new List<ManifestEntry>();
            Dictionary<string, List<SignatureEntry>> signatures = new Dictionary<string, List<SignatureEntry>>();
            Dictionary<string, List<ContentEntry>> contents = new Dictionary<string, List<ContentEntry>>();

            //First we parse all files into their respective classes
            foreach (Duplicati.Library.Interface.IFileEntry fe in files)
            {
                BackupEntryBase be = m_filenamestrategy.ParseFilename(fe);
                if (be == null)
                    continue; //Non-duplicati files

                if (!string.IsNullOrEmpty(be.EncryptionMode) && Array.IndexOf<string>(DynamicLoader.EncryptionLoader.Keys, be.EncryptionMode) < 0)
                    continue;

                if (be is PayloadEntryBase && Array.IndexOf<string>(DynamicLoader.CompressionLoader.Keys,((PayloadEntryBase)be).Compression) < 0)
                    continue;

                if (be is ManifestEntry)
                {
                    if (((ManifestEntry)be).IsFull)
                        fulls.Add((ManifestEntry)be);
                    else
                        incrementals.Add((ManifestEntry)be);
                }
                else if (be is ContentEntry)
                {
                    string key = be.TimeString;
                    if (!contents.ContainsKey(key))
                        contents[key] = new List<ContentEntry>();
                    contents[key].Add((ContentEntry)be);
                }
                else if (be is SignatureEntry)
                {
                    string key = be.TimeString;
                    if (!signatures.ContainsKey(key))
                        signatures[key] = new List<SignatureEntry>();
                    signatures[key].Add((SignatureEntry)be);
                }
                else
                    throw new Exception(string.Format(Strings.BackendWrapper.InvalidEntryTypeError, be.GetType().FullName));
            }

            Sorter sortHelper = new Sorter();
            fulls.Sort(sortHelper);
            incrementals.Sort(sortHelper);

            //Pair up the manifests in primary/alternate pairs
            foreach(List<ManifestEntry> mfl in new List<ManifestEntry>[] {fulls, incrementals})
                for (int i = 0; i < mfl.Count - 1; i++)
                {
                    if (mfl[i].TimeString == mfl[i + 1].TimeString && mfl[i].IsPrimary != mfl[i + 1].IsPrimary)
                    {
                        if (mfl[i].IsPrimary)
                        {
                            mfl[i].Alternate = mfl[i + 1];
                            mfl.RemoveAt(i + 1);
                        }
                        else
                        {
                            mfl[i + 1].Alternate = mfl[i];
                            mfl.RemoveAt(i);
                        }
                    }
                }


            //Attach volumes to the full backups
            for(int i = 0; i < fulls.Count; i++)
            {
                ManifestEntry be = fulls[i];

                string key = be.TimeString;
                if (contents.ContainsKey(key) && signatures.ContainsKey(key))
                {
                    List<SignatureEntry> signature = signatures[key];
                    List<ContentEntry> content = contents[key];
                    signature.Sort(sortHelper);
                    content.Sort(sortHelper);

                    int volCount = Math.Min(content.Count, signature.Count);

                    for (int j = 0; j < volCount; j++)
                        if (signature[0].Volumenumber == (j + 1) && content[0].Volumenumber == (j + 1))
                        {
                            be.Volumes.Add(new KeyValuePair<SignatureEntry, ContentEntry>(signature[0], content[0]));
                            signature.RemoveAt(0);
                            content.RemoveAt(0);
                        }
                }

                fulls[i] = SwapManifestAlternates(be);
            }


            //Attach volumes to the incrementals, and attach incrementals to the fulls
            int index = 0;
            foreach (ManifestEntry be in incrementals)
            {
                string key = be.TimeString;
                if (contents.ContainsKey(key) && signatures.ContainsKey(key))
                {
                    List<SignatureEntry> signature = signatures[key];
                    List<ContentEntry> content = contents[key];
                    signature.Sort(sortHelper);
                    content.Sort(sortHelper);

                    int volCount = Math.Min(content.Count, signature.Count);

                    for (int j = 0; j < volCount; j++)
                        if (signature[0].Volumenumber == (j + 1) && content[0].Volumenumber == (j + 1))
                        {
                            be.Volumes.Add(new KeyValuePair<SignatureEntry, ContentEntry>(signature[0], content[0]));
                            signature.RemoveAt(0);
                            content.RemoveAt(0);
                        }
                }

                if (index >= fulls.Count || be.Time <= fulls[index].Time)
                {
                    if (m_orphans == null)
                        Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.OrphanIncrementalFoundMessage, be.Filename), Duplicati.Library.Logging.LogMessageType.Warning);
                    else
                        m_orphans.Add(be);
                    continue;
                }
                else
                {
                    while (index < fulls.Count - 1 && be.Time > fulls[index + 1].Time)
                        index++;
                    fulls[index].Incrementals.Add(SwapManifestAlternates(be));
                }
            }

            foreach (List<ContentEntry> lb in contents.Values)
                foreach (ContentEntry be in lb)
                    AddOrphan(be);

            foreach (List<SignatureEntry> lb in signatures.Values)
                foreach (SignatureEntry be in lb)
                    AddOrphan(be);

            return fulls;
        }

        private ManifestEntry SwapManifestAlternates(ManifestEntry m)
        {
            if (m.Volumes.Count % 2 != 1 && m.Alternate != null)
            {
                m.Alternate.Incrementals.Clear();
                m.Alternate.Incrementals.AddRange(m.Incrementals);
                m.Alternate.Volumes.Clear();
                m.Alternate.Volumes.AddRange(m.Volumes);

                m.Incrementals.Clear();
                m.Volumes.Clear();

                m.Alternate.Alternate = m;
                return m.Alternate;
            }

            return m;
        }

        public List<ManifestEntry> GetBackupSets()
        {
            using (new Logging.Timer("Getting and sorting filelist from " + m_backend.DisplayName))
                return SortAndPairSets((List<Duplicati.Library.Interface.IFileEntry>)ProtectedInvoke("ListInternal"));
        }

        public void Put(BackupEntryBase remote, string filename)
        {
            if (!m_async)
                PutInternal(remote, filename);
            else
            {
                bool waitForCompletion;

                lock (m_queuelock)
                {
                    if (m_workerException != null)
                        throw m_workerException;

                    m_pendingOperations.Enqueue(new KeyValuePair<BackupEntryBase, string>( remote, filename ));
                    m_asyncItemReady.Set();

                    //The *3 is there because a volume consists of 3 files (signature, content and manifest)
                    waitForCompletion = m_options.AsynchronousUploadLimit > 0 && m_pendingOperations.Count > (m_options.AsynchronousUploadLimit * 3);
                }

                while (waitForCompletion)
                {
                    m_asyncItemProcessed.WaitOne(1000 * 5, false);

                    lock (m_queuelock)
                    {
                        if (m_workerException != null)
                            throw m_workerException;

                        waitForCompletion = m_options.AsynchronousUploadLimit > 0 && m_pendingOperations.Count > (m_options.AsynchronousUploadLimit * 3);
                    }
                }
            }
        }

        public void Get(BackupEntryBase remote, string filename, string filehash)
        {
            ProtectedInvoke("GetInternal", remote, filename, filehash);
        }

        public void Delete(BackupEntryBase remote)
        {
            ProtectedInvoke("DeleteInternal", remote);
        }

        /// <summary>
        /// This method invokes another method in such a way that no more than one thread is ever 
        /// executing on the same backend at any time.
        /// </summary>
        /// <param name="methodname">The method to invoke</param>
        /// <param name="arguments">The methods arguments</param>
        /// <returns>The return value of the invoked function</returns>
        private object ProtectedInvoke(string methodname, params object[] arguments)
        {
            System.Reflection.MethodInfo method = this.GetType().GetMethod(methodname);
            if (method == null)
                method = this.GetType().GetMethod(methodname, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (method == null)
                throw new Exception(string.Format(Strings.BackendWrapper.FunctionLookupError, methodname));


            //If the code is not async, just invoke it
            if (!m_async)
            {
                try
                {
                    return method.Invoke(this, arguments);
                }
                catch (System.Reflection.TargetInvocationException tex)
                {
                    //Unwrap those annoying invocation exceptions
                    if (tex.InnerException != null)
                        throw tex.InnerException;
                    else
                        throw;
                }
            }
            else
            {
                while (true)
                {
                    //Get the lock
                    lock (m_queuelock)
                    {
                        //If the lock is free, just run, but keep the lock
                        if (m_workerThread == null || !m_workerThread.IsAlive || m_pendingOperations.Count == 0)
                        {
                            try
                            {
                                return method.Invoke(this, arguments);
                            }
                            catch (System.Reflection.TargetInvocationException tex)
                            {
                                //Unwrap those annoying invocation exceptions
                                if (tex.InnerException != null)
                                    throw tex.InnerException;
                                else
                                    throw;
                            }
                        }
                    }

                    //Otherwise, wait for the worker to signal completion
                    //We wait without holding the lock, because the worker cannot signal completion while
                    //we hold the lock
                    m_workerThread.Join(1000);
                    //Now re-acquire the lock, and re-check the state of the event
                }
            }

        }

        private void DeleteSignatureCacheCopy(BackupEntryBase entry)
        {
            if (!string.IsNullOrEmpty(m_options.SignatureCachePath))
            {
                string cacheFilename = m_cachefilenamestrategy.GenerateFilename(entry);
                if (System.IO.File.Exists(cacheFilename))
                    try { System.IO.File.Delete(cacheFilename); }
                    catch { }
            }
        }

        public void DeleteOrphans()
        {
            if (m_orphans == null)
                return;

            foreach (BackupEntryBase be in m_orphans)
            {
                Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, be.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                if (m_options.Force)
                {
                    m_backend.Delete(be.Filename);
                    DeleteSignatureCacheCopy(be);
                }

                if (be is ManifestEntry)
                    foreach (KeyValuePair<SignatureEntry, ContentEntry> bex in ((ManifestEntry)be).Volumes)
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, bex.Key.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                        Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, bex.Value.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                        if (m_options.Force)
                        {
                            m_backend.Delete(bex.Key.Filename);
                            m_backend.Delete(bex.Value.Filename);
                            DeleteSignatureCacheCopy(bex.Key);
                        }
                    }
            }

            if (!m_options.Force && m_orphans.Count > 0)
                Logging.Log.WriteMessage(Strings.BackendWrapper.FilesNotForceRemovedMessage, Duplicati.Library.Logging.LogMessageType.Information);
        }

        private List<Duplicati.Library.Interface.IFileEntry> ListInternal()
        {
            int retries = m_options.NumberOfRetries;
            Exception lastEx = null;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    return m_backend.List();
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_options.RetryDelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_options.RetryDelay);
                }
            } while (retries > 0);

            throw new Exception(string.Format(Strings.BackendWrapper.FileListingError, lastEx.Message), lastEx);
        }

        private void DeleteInternal(BackupEntryBase remote)
        {
            int retries = m_options.NumberOfRetries;
            Exception lastEx = null;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    m_backend.Delete(remote.Filename);
                    lastEx = null;
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_options.RetryDelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_options.RetryDelay);
                }
            } while (lastEx != null && retries > 0);

            if (lastEx != null)
                throw new Exception(string.Format(Strings.BackendWrapper.FileDeleteError, lastEx.Message), lastEx);

            if (remote is SignatureEntry && !string.IsNullOrEmpty(m_options.SignatureCachePath))
            {
                try 
                { 
                    if (System.IO.File.Exists(System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote))))
                        System.IO.File.Delete(System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote))); 
                }
                catch {}
            }
        }

        private void GetInternal(BackupEntryBase remote, string filename, string filehash)
        {
            int retries = m_options.NumberOfRetries;
            Exception lastEx = null;
            m_statusmessage = string.Format(Strings.BackendWrapper.StatusMessageDownloading, remote.Filename);

            do
            {
                try
                {
                    if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && remote is SignatureEntry)
                    {
                        string cachefilename = System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote));

                        if (System.IO.File.Exists(cachefilename))
                            if (filehash != null && Core.Utility.CalculateHash(cachefilename) == filehash)
                            {
                                //TODO: Don't copy, but just return it as write protected
                                System.IO.File.Copy(cachefilename, filename, true); //TODO: Warn on hash mismatch?
                                return;
                            }
                    }

                    Core.TempFile tempfile = null;
                    try
                    {
                        if (!string.IsNullOrEmpty(remote.EncryptionMode))
                            tempfile = new Duplicati.Library.Core.TempFile();
                        else
                            tempfile = new Duplicati.Library.Core.TempFile(filename);

                        m_statistics.NumberOfRemoteCalls++;
                        if (m_backend is Duplicati.Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
                        {
                            using (System.IO.FileStream fs = System.IO.File.Open(tempfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                            using (Core.ProgressReportingStream pgs = new Duplicati.Library.Core.ProgressReportingStream(fs, remote.Fileentry.Size))
                            using (Core.ThrottledStream ts = new Duplicati.Library.Core.ThrottledStream(pgs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                            {
                                pgs.Progress += new Duplicati.Library.Core.ProgressReportingStream.ProgressDelegate(pgs_Progress);
                                ts.Callback += new Duplicati.Library.Core.ThrottledStream.ThrottledStreamCallback(ThrottledStream_Callback);
                                ((Duplicati.Library.Interface.IStreamingBackend)m_backend).Get(remote.Filename, ts);
                            }
                        }
                        else
                        {
                            if (!m_async && ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                            m_backend.Get(remote.Filename, tempfile);
                            if (!m_async && ProgressEvent != null)
                                ProgressEvent(100, m_statusmessage);
                        }

                        if (!string.IsNullOrEmpty(remote.EncryptionMode))
                        {
                            try
                            {
                                using (Library.Interface.IEncryption enc = DynamicLoader.EncryptionLoader.GetModule(remote.EncryptionMode, m_options.Passphrase, m_options.RawOptions))
                                    enc.Decrypt(tempfile, filename);
                            }
                            catch (Exception ex)
                            {
                                //If we fail here, make sure that we throw a crypto exception
                                if (ex is System.Security.Cryptography.CryptographicException)
                                    throw;
                                else
                                    throw new System.Security.Cryptography.CryptographicException(ex.Message, ex);
                            }

                            tempfile.Dispose(); //Remove the encrypted file

                            //Wrap the new file as a temp file
                            tempfile = new Duplicati.Library.Core.TempFile(filename);
                        }

                        if (filehash != null && Core.Utility.CalculateHash(tempfile) != filehash)
                            throw new HashMismathcException(string.Format(Strings.BackendWrapper.HashMismatchError, remote.Filename, filehash, Core.Utility.CalculateHash(tempfile)));

                        if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && remote is SignatureEntry)
                        {
                            string cachefilename = System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote));
                            System.IO.File.Copy(tempfile, cachefilename);
                        }

                        lastEx = null;
                        tempfile.Protected = true; //Don't delete it
                    }
                    finally
                    {
                        try
                        {
                            if (tempfile != null)
                                tempfile.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogError(ex.Message);

                    retries--;
                    if (retries > 0 && m_options.RetryDelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_options.RetryDelay);
                }
            } while (lastEx != null && retries > 0);

            if (lastEx != null)
                if (lastEx is HashMismathcException)
                    throw lastEx;
                else
                    throw new Exception(string.Format(Strings.BackendWrapper.FileDownloadError, lastEx.Message), lastEx);

            m_statistics.NumberOfBytesDownloaded += new System.IO.FileInfo(filename).Length;
        }

        private void PutInternal(BackupEntryBase remote, string filename)
        {
            string remotename = GenerateFilename(remote);
            m_statusmessage = string.Format(Strings.BackendWrapper.StatusMessageUploading, remotename, Core.Utility.FormatSizeString(new System.IO.FileInfo(filename).Length));

            string encryptedFile = filename;

            try
            {
                if (m_encryption != null)
                {
                    using (Core.TempFile tf = new Duplicati.Library.Core.TempFile()) //If exception is thrown, tf will be deleted
                    {
                        m_encryption.Encrypt(filename, tf);
                        tf.Protected = true; //Done, keep file
                        encryptedFile = tf;
                    }

                }

                int retries = m_options.NumberOfRetries;
                bool success = false;
                Exception lastEx = null;

                do
                {
                    try
                    {
                        m_statistics.NumberOfRemoteCalls++;
                        if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
                        {
#if DEBUG_THROTTLE
                            DateTime begin = DateTime.Now;
#endif
                            using (System.IO.FileStream fs = System.IO.File.Open(encryptedFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                            using (Core.ProgressReportingStream pgs = new Duplicati.Library.Core.ProgressReportingStream(fs, fs.Length))
                            {   
                                pgs.Progress += new Duplicati.Library.Core.ProgressReportingStream.ProgressDelegate(pgs_Progress);

                                using (Core.ThrottledStream ts = new Core.ThrottledStream(pgs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                                {
                                    ts.Callback += new Duplicati.Library.Core.ThrottledStream.ThrottledStreamCallback(ThrottledStream_Callback);
                                    ((Library.Interface.IStreamingBackend)m_backend).Put(remotename, ts);
                                }
                            }

#if DEBUG_THROTTLE
                            TimeSpan duration = DateTime.Now - begin;
                            long size = new System.IO.FileInfo(encryptedFile).Length;
                            Console.WriteLine("Transferred " + Core.Utility.FormatSizeString(size) + " in " + duration.TotalSeconds.ToString() + ", yielding : " + ((size / (double)1024.0) / duration.TotalSeconds) + " kb/s");
#endif
                        }
                        else
                        {
                            if (ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                            m_backend.Put(remotename, encryptedFile);
                            if (ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                        }

                        if (remote is SignatureEntry && !string.IsNullOrEmpty(m_options.SignatureCachePath))
                            System.IO.File.Copy(filename, System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote)), true);

                        if (remote is ManifestEntry)
                        {
                            if (m_queuelock != null)
                            {
                                lock (m_queuelock)
                                    m_manifestUploads++;
                            }
                            else
                                m_manifestUploads++;
                        }

                        success = true;
                        lastEx = null;
                    }
                    catch (System.Threading.ThreadAbortException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        m_statistics.LogError(ex.Message);

                        retries--;
                        if (retries > 0 && m_options.RetryDelay.Ticks > 0)
                            System.Threading.Thread.Sleep(m_options.RetryDelay);
                    }
                } while (!success && retries > 0);

                if (!success)
                    throw new Exception(string.Format(Strings.BackendWrapper.FileUploadError, lastEx == null ? "<null>" : lastEx.Message), lastEx);

                m_statistics.NumberOfBytesUploaded += new System.IO.FileInfo(filename).Length;
            }
            finally
            {
                //We delete the file here, because the backend leaves the file, 
                //in the case where we use async methods
                try
                {
                    if (System.IO.File.Exists(filename))
                        System.IO.File.Delete(filename);
                }
                catch { }

                try
                {
                    if (System.IO.File.Exists(encryptedFile))
                        System.IO.File.Delete(encryptedFile);
                }
                catch { }

                try
                {
                    if (ProgressEvent != null)
                        ProgressEvent(-1, "");
                }
                catch
                {
                }
            }

        }

        /// <summary>
        /// A callback from the throttled stream, used to change speed based on user adjustments
        /// </summary>
        /// <param name="sender">The stream that raised the event</param>
        void ThrottledStream_Callback(Core.ThrottledStream sender)
        {
            sender.ReadSpeed = m_options.MaxUploadPrSecond;
            sender.WriteSpeed = m_options.MaxDownloadPrSecond;
        }

        /// <summary>
        /// Internal helper to consistenly name remote files beyond what the filenamestrategy supports
        /// </summary>
        /// <param name="remote">The entry to create a filename for</param>
        /// <returns>A filename with extensions</returns>
        private string GenerateFilename(BackupEntryBase remote)
        {
            string remotename = m_filenamestrategy.GenerateFilename(remote);
            if (remote is ManifestEntry)
                remotename += ".manifest";
            else
                remotename += "." + m_options.CompressionModule;

            if (m_encryption != null)
                remotename += "." + m_encryption.FilenameExtension;

            return remotename;
        }

        private void pgs_Progress(int progress)
        {
            if (ProgressEvent != null)
                ProgressEvent(progress, m_statusmessage);
        }

        /// <summary>
        /// Worker Thread entry that empties the request queue
        /// </summary>
        /// <param name="dummy">Unused required parameter</param>
        private void ProcessQueue(object dummy)
        {
            try
            {
                lock (m_queuelock)
                    m_workerException = null;

                while (!m_asyncTerminate)
                {
                    KeyValuePair<BackupEntryBase, string> args = new KeyValuePair<BackupEntryBase,string>(null, null);

                    while (args.Key == null)
                    {
                        if (m_asyncTerminate)
                            return;

                        //Obtain the lock for the queue
                        lock (m_queuelock)
                            if (m_pendingOperations.Count > 0)
                                args = m_pendingOperations.Peek();

                        if (args.Key == null)
                            m_asyncItemReady.WaitOne(1000, false);
                    }

                    //Pause if requested
                    if (AsyncItemProcessedEvent != null)
                        AsyncItemProcessedEvent(this, null);

                    PutInternal(args.Key, args.Value);

                    lock (m_queuelock)
                    {
                        m_pendingOperations.Dequeue();
                        m_asyncItemProcessed.Set();
                    }
                }
            }
            catch (Exception ex)
            {
                lock (m_queuelock)
                {
                    m_workerException = ex;
                    m_asyncItemProcessed.Set();
                }
            }
        }

        /// <summary>
        /// Gets the number of uploads performed asynchronously
        /// </summary>
        public int ManifestUploads 
        { 
            get 
            {
                if (m_queuelock != null)
                {
                    lock (m_queuelock)
                        return m_manifestUploads;
                }
                else
                    return m_manifestUploads;
            } 
        }

        /// <summary>
        /// This function attemtps to forcefully abort all ongoing async operations
        /// </summary>
        public void AbortAll()
        {
            //If we are not running async, just return
            if (!m_async)
                return;

            List<KeyValuePair<BackupEntryBase, string>> pending = new List<KeyValuePair<BackupEntryBase, string>>();

            m_asyncTerminate = true;
            
            if (m_workerThread != null && m_workerThread.IsAlive)
                m_workerThread.Join(1000);


            lock (m_queuelock)
            {
                if (m_workerThread != null && m_workerThread.IsAlive)
                    m_workerThread.Abort();

                //Extract all unprocessed items
                while (m_pendingOperations.Count > 0)
                    pending.Add(m_pendingOperations.Dequeue());
            }

            //Clean up all temporary files from pending operations
            foreach (KeyValuePair<BackupEntryBase, string> p in pending)
                try
                {
                    if (System.IO.File.Exists(p.Value))
                        System.IO.File.Delete(p.Value);
                }
                catch { }

        }

        /// <summary>
        /// This function suspends the calling thread until the 
        /// ongoing transfer has completed, then disables asynchronous
        /// transfers and returns the non-transferred items
        /// </summary>
        public List<KeyValuePair<BackupEntryBase, string>> ExtractPendingUploads()
        {
            List<KeyValuePair<BackupEntryBase, string>> work = null;

            while (true)
            {
                lock (m_queuelock)
                {
                    if (m_workerException != null)
                        throw m_workerException;

                    //On the first run, we empty the queue and signal the stop
                    if (work == null)
                    {
                        m_asyncTerminate = true;

                        work = new List<KeyValuePair<BackupEntryBase, string>>();
                        if (m_pendingOperations.Count > 1)
                        {
                            while (m_pendingOperations.Count != 0)
                                work.Add(m_pendingOperations.Dequeue());

                            //The top entry is being completed by the thread
                            m_pendingOperations.Enqueue(work[0]);
                            work.RemoveAt(0);
                        }
                    }

                    //When the thread completes, disable asynchronous transfers and return the unfinished work
                    if (m_workerThread == null || !m_workerThread.IsAlive)
                    {
                        m_async = false;
                        return work;
                    }
                }

                m_workerThread.Join(1000 * 5);
            }

        }

        private void DisposeInternal()
        {
            if (m_backend != null)
            {
                m_backend.Dispose();
                m_backend = null;
            }
        }

        #region IDisposable Members
        
        public void Dispose()
        {
            if (m_async && m_queuelock != null)
                AbortAll();

            if (m_backend != null)
                ProtectedInvoke("DisposeInternal");
            if (m_encryption != null)
                m_encryption.Dispose();
        }

        #endregion
    }
}
