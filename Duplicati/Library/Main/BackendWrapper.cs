#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
        /// The backend url for creating a new backend instance
        /// </summary>
        private string m_backendUrl;
        /// <summary>
        /// A flag indicating if the backend instance will be re-used
        /// </summary>
        private bool m_reuse_backend;
        /// <summary>
        /// A flag indicating if the backend is being used for the first time,
        /// used to prevent disposing the initally created backend
        /// </summary>
        private bool m_first_backend_use;
        /// <summary>
        /// A flag indicating if the backend can create folders
        /// </summary>
        private bool m_backendSupportsCreateFolder;

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
        public static FilenameStrategy CreateCacheFilenameStrategy() { return new FilenameStrategy("dpl"); }

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
        /// The delete transaction marker file, null if it does not exist
        /// </summary>
        private DeleteTransactionEntry m_transactionFile;

        /// <summary>
        /// A list of files that are mentioned in the delete transaction,
        /// but are not actually deleted yet
        /// </summary>
        private List<Library.Interface.IFileEntry> m_leftovers;

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
        /// Gets the number of bytes added to a file when encrypted and transfered
        /// </summary>
        public long FileSizeOverhead { get { return m_encryption == null ? 0 : m_encryption.SizeOverhead(m_options.VolumeSize); } }

        /// <summary>
        /// Gets the filename strategy used by the backend wrapper
        /// </summary>
        public FilenameStrategy FilenameStrategy { get { return m_filenamestrategy; } }

        /// <summary>
        /// Gets the communication statistics assigned to the wrapper
        /// </summary>
        public CommunicationStatistics Statistics { get { return m_statistics; } }
  
        /// <summary>
        /// Recorder for remote operations
        /// </summary>
        private Duplicati.Library.Interface.IBackendInteraction m_backendInterfaceLogger;
        
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
            m_backendUrl = backend;

            m_backend = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(backend, m_options.RawOptions);
            if (m_backend == null)
                throw new Exception(string.Format(Strings.BackendWrapper.BackendNotFoundError, backend));

            m_reuse_backend = !m_options.NoConnectionReuse;
            m_first_backend_use = true;
            m_backendSupportsCreateFolder = m_backend is Library.Interface.IBackend_v2;

            if (m_options.AutoCleanup)
                m_orphans = new List<BackupEntryBase>();

            if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && !System.IO.Directory.Exists(m_options.SignatureCachePath))
                System.IO.Directory.CreateDirectory(m_options.SignatureCachePath);
   
            if (!string.IsNullOrEmpty(m_options.Backendlogdatabase))
            {
                m_backendInterfaceLogger = new StateVerification.StateDatabase(m_options.Backendlogdatabase, statistics);
                m_backendInterfaceLogger.BeginOperation(m_options.MainAction.ToString());
            }
            
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

        private void DisposeBackend()
        {
            if (m_backend != null)
            {
                try { m_backend.Dispose(); }
                catch (Exception ex) { m_statistics.LogWarning(string.Format(Strings.BackendWrapper.FailureWhileDisposingBackendError, ex.Message), ex); }
                finally { m_backend = null; }
            }
        }

        private void ResetBackend()
        {
            if (!m_reuse_backend)
            {
                //Avoid disposing the very first backend instance
                if (m_first_backend_use)
                    m_first_backend_use = false;
                else
                    DisposeBackend();
            }

            if (m_backend == null)
                m_backend = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(m_backendUrl, m_options.RawOptions);
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

            return GetBackupSet(Utility.Timeparser.ParseTimeInterval(timelimit, DateTime.Now));
        }

        /// <summary>
        /// Returns all files in the target backend
        /// </summary>
        /// <param name="filter">A value indicating if the results should be filtered according to the delete transaction</param>
        /// <returns>All files in the target backend</returns>
        public List<Library.Interface.IFileEntry> List(bool filter)
        {
            List<Library.Interface.IFileEntry> result = (List<Library.Interface.IFileEntry>)ProtectedInvoke("ListInternal");

            if (!filter)
                return result;

            m_transactionFile = null;
            List<DeleteTransactionEntry> found = new List<DeleteTransactionEntry>();

            foreach (Library.Interface.IFileEntry f in result)
            {
                DeleteTransactionEntry de = m_filenamestrategy.ParseAsDeleteTransaction(f);
                if (de != null)
                    found.Add(de);
            }

            if (found.Count > 0)
            {
                if (found.Count > 1)
                    throw new Exception(Strings.BackendWrapper.MultipleDeleteTransactionsFoundError);

                m_transactionFile = found[0];
                result.Remove(m_transactionFile.Fileentry);

                if (m_statistics != null)
                {
                    //Show the warning to the user unless we will
                    // actually delete the files later, as that would
                    // produce the warning twice
                    bool logWarning = true;

                    switch (m_options.MainAction)
                    {
                        case DuplicatiOperationMode.Backup:
                        case DuplicatiOperationMode.BackupFull:
                        case DuplicatiOperationMode.BackupIncremental:
                            logWarning = !m_options.AutoCleanup;
                            break;
                        
                        case DuplicatiOperationMode.CleanUp:
                        case DuplicatiOperationMode.DeleteAllButN:
                        case DuplicatiOperationMode.DeleteAllButNFull:
                        case DuplicatiOperationMode.DeleteOlderThan:
                            logWarning = false;
                            break;
                    }

                    if (logWarning)
                        m_statistics.LogWarning(Strings.BackendWrapper.DeleteTransactionFileFoundWarning, null);
                }

                Dictionary<string, string> lookup = new Dictionary<string,string>();
                try
                {
                    using (Library.Utility.TempFile tf = new Utility.TempFile())
                    {
                        this.Get(m_transactionFile, null, tf, null);
                        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                        doc.Load(tf);

                        foreach (System.Xml.XmlNode n in doc.SelectNodes("files/file")) 
                            lookup[n.InnerText] = null;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format(Strings.BackendWrapper.DeleteTransactionFileReadError, m_transactionFile.Filename, ex.Message), ex);
                }

                m_leftovers = new List<Library.Interface.IFileEntry>();

                //Filter the results reported to look as if the delete was completed,
                // and keep a list of not-yet-deleted entries
                for (int i = 0; i < result.Count; i++)
                    if (lookup.ContainsKey(result[i].Name))
                    {
                        m_leftovers.Add(result[i]);
                        result.RemoveAt(i);
                        i--;
                    }
            }

            return result;
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
            Sorter sortHelper = new Sorter();
            files.Sort(sortHelper);

            for(int i = 1; i < files.Count; i++)
                if (files[i].Name == files[i - 1].Name)
                {
                    if (m_statistics != null)
                        m_statistics.LogWarning(string.Format(Strings.BackendWrapper.DuplicateFileEntryWarning, files[i].Name), null);
                    
                    files.RemoveAt(i);
                    i--;
                }

            List<ManifestEntry> incrementals = new List<ManifestEntry>();
            List<ManifestEntry> fulls = new List<ManifestEntry>();
            Dictionary<string, List<SignatureEntry>> signatures = new Dictionary<string, List<SignatureEntry>>();
            Dictionary<string, List<ContentEntry>> contents = new Dictionary<string, List<ContentEntry>>();
            Dictionary<string, VerificationEntry> verifications = new Dictionary<string, VerificationEntry>();

            //First we parse all files into their respective classes
            foreach (Duplicati.Library.Interface.IFileEntry fe in files)
            {
                BackupEntryBase be = m_filenamestrategy.ParseFilename(fe);
                if (be == null)
                {
                    if (m_statistics != null && m_statistics.VerboseErrors && !fe.IsFolder)
                        m_statistics.LogWarning(string.Format(Strings.BackendWrapper.UnmatchedFilenameWarning, fe.Name), null);
                    continue; //Non-duplicati files
                }

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
                else if (be is VerificationEntry)
                {
                    verifications[be.TimeString] = (VerificationEntry)be;
                }
                else
                    throw new Exception(string.Format(Strings.BackendWrapper.InvalidEntryTypeError, be.GetType().FullName));
            }

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
                if (verifications.ContainsKey(key))
                {
                    be.Verification = verifications[key];
                    verifications.Remove(key);
                }

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

            foreach (VerificationEntry ve in verifications.Values)
                AddOrphan(ve);

            foreach (List<ContentEntry> lb in contents.Values)
                foreach (ContentEntry be in lb)
                    AddOrphan(be);

            foreach (List<SignatureEntry> lb in signatures.Values)
                foreach (SignatureEntry be in lb)
                    AddOrphan(be);

            //Assign the manifest to allow traversing the chain of manifests
            foreach (ManifestEntry me in fulls)
            {
                ManifestEntry previous = me;
                foreach (ManifestEntry me2 in me.Incrementals)
                {
                    me2.Previous = previous;
                    previous = me2;
                }
            }


            if (m_statistics != null)
            {
                foreach (ManifestEntry me in fulls)
                {
                    if (me.Volumes.Count == 0)
                        m_statistics.LogWarning(string.Format(Strings.BackendWrapper.EmptyManifestWarning, me.Filename), null);

                    foreach (ManifestEntry me2 in me.Incrementals)
                        if (me2.Volumes.Count == 0)
                            m_statistics.LogWarning(string.Format(Strings.BackendWrapper.EmptyManifestWarning, me.Filename), null);
                }
            }

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
                return SortAndPairSets(List(true));
        }

        public void Put(BackupEntryBase remote, string filename)
        {
            Put(remote, filename, false);
        }

        private void Put(BackupEntryBase remote, string filename, bool forcesync)
        {
            if (!remote.IsEncrypted && !m_options.NoEncryption && remote as VerificationEntry == null)
            {
                if (m_encryption == null)
                    m_encryption = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions);

                using (Utility.TempFile raw = new Duplicati.Library.Utility.TempFile(filename))
                using (Utility.TempFile enc = new Duplicati.Library.Utility.TempFile())
                {
                    m_encryption.Encrypt(raw, enc);
                    filename = enc;
                    enc.Protected = true;
                    raw.Protected = false;
                }

                remote.IsEncrypted = true;
            }

            remote.RemoteHash = Utility.Utility.CalculateHash(filename);
            remote.Filename = GenerateFilename(remote);
            remote.Filesize = new System.IO.FileInfo(filename).Length;

            if (!m_async)
                PutInternal(remote, filename);
            else
            {
                if (forcesync)
                {
                    int count;

                    lock (m_queuelock)
                        count = m_pendingOperations.Count;

                    while (count > 0)
                    {
                        m_asyncItemProcessed.WaitOne(1000 * 5, false);
                        lock (m_queuelock)
                            count = m_pendingOperations.Count;
                    }

                    PutInternal(remote, filename);
                }
                else
                {
                    bool waitForCompletion;

                    //There are 3 files in a volume (signature, content and manifest) + a verification file
                    int uploads_in_set = m_options.CreateVerificationFile ? 4 : 3;

                    lock (m_queuelock)
                    {
                        if (m_workerException != null)
                            throw m_workerException;

                        m_pendingOperations.Enqueue(new KeyValuePair<BackupEntryBase, string>(remote, filename));
                        m_asyncItemReady.Set();

                        waitForCompletion = m_options.AsynchronousUploadLimit > 0 && m_pendingOperations.Count > (m_options.AsynchronousUploadLimit * uploads_in_set);
                    }

                    while (waitForCompletion)
                    {
                        m_asyncItemProcessed.WaitOne(1000 * 5, false);

                        lock (m_queuelock)
                        {
                            if (m_workerException != null)
                                throw m_workerException;

                            waitForCompletion = m_options.AsynchronousUploadLimit > 0 && m_pendingOperations.Count > (m_options.AsynchronousUploadLimit * uploads_in_set);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets a file from the remote store, verifies the hash and decrypts the content
        /// </summary>
        /// <param name="remote">The entry to get</param>
        /// <param name="manifest">The manifest that protectes the file</param>
        /// <param name="filename">The remote filename</param>
        /// <param name="filehash">The hash of the remote file</param>
        public void Get(BackupEntryBase remote, Manifestfile manifest, string filename, Manifestfile.HashEntry hash)
        {
            ProtectedInvoke("GetInternal", remote, manifest, filename, hash);
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

        /// <summary>
        /// Writes the delete transaction file to the backend in preperation for a delete operation
        /// </summary>
        /// <param name="filename">The name of the local file with delete transaction data</param>
        public void WriteDeleteTransactionFile(string filename)
        {
            if (m_transactionFile != null)
                throw new Exception(Strings.BackendWrapper.ExistingDeleteTransactionError);

            m_transactionFile = new DeleteTransactionEntry(m_options.EncryptionModule);

            Put(m_transactionFile, filename, true);
        }

        /// <summary>
        /// Removes the delete transaction file from the backend, thus completing the delete transaction
        /// </summary>
        public void RemoveDeleteTransactionFile()
        {
            if (m_transactionFile == null)
                throw new Exception(Strings.BackendWrapper.NonExistingDeleteTransactionError);

            Delete(m_transactionFile);
            m_transactionFile = null;
        }

        /// <summary>
        /// Deletes all files listed in the delete transaction file if it exists
        /// </summary>
        /// <param name="logAsWarnings">A value indicating if a warning message should be logged</param>
        /// <returns></returns>
        public string FinishDeleteTransaction(bool logAsWarnings)
        {
            StringBuilder sb = new StringBuilder();

            if (m_transactionFile != null)
            {
                if (logAsWarnings)
                    m_statistics.LogWarning(string.Format(Strings.BackendWrapper.CompletingDeleteTransactionWarning, m_leftovers.Count), null);
                sb.AppendLine(string.Format(Strings.BackendWrapper.CompletingDeleteTransactionWarning, m_leftovers.Count));

                foreach (Library.Interface.IFileEntry s in m_leftovers)
                {
                    sb.AppendLine(string.Format(Strings.BackendWrapper.DeletingTransactionLeftoverFile, s.Name));

                    BackupEntryBase be = m_filenamestrategy.ParseFilename(s);
                    if (m_options.Force)
                        this.Delete(be ?? new SignatureEntry(s.Name, s, DateTime.Now, true, "", "", "", 0));
                }

                sb.AppendLine(string.Format(Strings.BackendWrapper.DeletingTransactionLeftoverFile, m_transactionFile.Filename));

                if (m_options.Force)
                    this.Delete(m_transactionFile);
                
                m_transactionFile = null;

                sb.AppendLine(Strings.BackendWrapper.CompletedDeleteTransaction);

                if (!m_options.Force)
                    sb.AppendLine(Strings.BackendWrapper.FilesNotForceRemovedMessage);
            }

            return sb.ToString();
        }

        public string DeleteOrphans(bool protectedCleanup)
        {
            StringBuilder sb = new StringBuilder();
            
            string res = FinishDeleteTransaction(protectedCleanup);
            if (!string.IsNullOrEmpty(res))
                sb.AppendLine(res);


            if (m_orphans == null)
                return sb.ToString();

            if (protectedCleanup && m_orphans.Count > 2)
            {
                //Figure out how many are verification files
                int count = m_orphans.Count;
                foreach (BackupEntryBase be in m_orphans)
                    if (be is VerificationEntry)
                        count--;

                if (count > 2)
                {
                    if (m_statistics != null)
                        m_statistics.LogWarning(string.Format(Strings.BackendWrapper.TooManyOrphansFoundError, m_orphans.Count), null);

                    return sb.ToString();
                }
            }

            foreach (BackupEntryBase be in m_orphans)
            {
                string mfmsg = string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, be.Filename);
                Logging.Log.WriteMessage(mfmsg, Duplicati.Library.Logging.LogMessageType.Information);
                sb.AppendLine(mfmsg);
                
                if (m_options.Force)
                {
                    if (m_statistics != null)
                        m_statistics.LogWarning(string.Format(Strings.BackendWrapper.RemoveOrphanFileWarning, be.Filename), null);
                    this.Delete(be);
                }

                if (be is ManifestEntry)
                    foreach (KeyValuePair<SignatureEntry, ContentEntry> bex in ((ManifestEntry)be).Volumes)
                    {
                        string sigmsg = string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, bex.Key.Filename);
                        string cntmsg = string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, bex.Value.Filename);
                        Logging.Log.WriteMessage(sigmsg, Duplicati.Library.Logging.LogMessageType.Information);
                        Logging.Log.WriteMessage(cntmsg, Duplicati.Library.Logging.LogMessageType.Information);

                        if (m_options.Force)
                        {
                            if (m_statistics != null)
                            {
                                m_statistics.LogWarning(string.Format(Strings.BackendWrapper.RemoveOrphanFileWarning, bex.Key.Filename), null);
                                m_statistics.LogWarning(string.Format(Strings.BackendWrapper.RemoveOrphanFileWarning, bex.Value.Filename), null);
                            }

                            this.Delete(bex.Key);
                            this.Delete(bex.Value);
                        }
                    }
            }

            if (!m_options.Force && m_orphans.Count > 0)
            {
                Logging.Log.WriteMessage(Strings.BackendWrapper.FilesNotForceRemovedMessage, Duplicati.Library.Logging.LogMessageType.Information);
                sb.AppendLine(Strings.BackendWrapper.FilesNotForceRemovedMessage);
            }

            return sb.ToString();
        }

        private List<Duplicati.Library.Interface.IFileEntry> ListInternal()
        {
            int retries = m_options.NumberOfRetries;
            Exception lastEx = null;

            do
            {
                try
                {
                    ResetBackend();
                    m_statistics.AddNumberOfRemoteCalls(1);
                    List<Duplicati.Library.Interface.IFileEntry> res = m_backend.List();
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterList(res, true, null);
                    return res;
                }
                catch (System.Threading.ThreadAbortException tex)
                {
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterList(null, false, tex.ToString());
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogRetryAttempt(ex.Message, ex);
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterList(null, false, ex.ToString());
                    DisposeBackend();

                    if (ex is Library.Interface.FolderMissingException && m_backendSupportsCreateFolder && m_options.AutocreateFolders)
                    {
                        ResetBackend();
                        try
                        {
                            m_statistics.AddNumberOfRemoteCalls(1);
                            ((Library.Interface.IBackend_v2)m_backend).CreateFolder();
                        }
                        catch(Exception exc) 
                        {
                            m_statistics.LogWarning(string.Format(Strings.BackendWrapper.AutoCreateFolderFailed, exc.Message), exc);
                            DisposeBackend();
                        }
                    }

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
                    ResetBackend();
                    m_statistics.AddNumberOfRemoteCalls(1);
                    m_backend.Delete(remote.Filename);
                    lastEx = null;
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterDelete(remote.Fileentry, true, null);
                }
                catch (System.Threading.ThreadAbortException tex)
                {
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterDelete(remote.Fileentry, false, tex.ToString());
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogRetryAttempt(ex.Message, ex);
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterDelete(remote.Fileentry, false, ex.ToString());
                    DisposeBackend();

                    retries--;
                    if (retries > 0 && m_options.RetryDelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_options.RetryDelay);
                }
            } while (lastEx != null && retries > 0);

            if (lastEx != null)
                throw new Exception(string.Format(Strings.BackendWrapper.FileDeleteError2, remote.Filename, lastEx.Message), lastEx);

            if (remote is SignatureEntry && !string.IsNullOrEmpty(m_options.SignatureCachePath))
            {
                try 
                { 
                    
                    string file = FindCacheEntry(remote as SignatureEntry);
                    try
                    {

                        while (file != null)
                        {
                            System.IO.File.Delete(file);
                            file = FindCacheEntry(remote as SignatureEntry);
                        }
                    }
                    catch (Exception ex) 
                    {
                        m_statistics.LogWarning(string.Format(Strings.BackendWrapper.DeleteCacheFileError, file), ex);
                    }

                    
                }
                catch {}
            }
        }

        /// <summary>
        /// Searches the cache directory for a matching entry, returns null if no entry matches
        /// </summary>
        /// <param name="remote">The signature entry to search for</param>
        /// <returns>The filename to the cached copy or null</returns>
        private string FindCacheEntry(SignatureEntry remote)
        {
            if (remote == null)
                return null;
            if (string.IsNullOrEmpty(m_options.SignatureCachePath))
                return null;
            
            string cachefilename = System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote));
            if (System.IO.File.Exists(cachefilename))
                return cachefilename;

            //If the new filename does not exist, see if we can parse the older style short filenames instead
            if (!System.IO.File.Exists(cachefilename) && System.IO.Directory.Exists(m_options.SignatureCachePath))
                foreach (string s in System.IO.Directory.GetFiles(m_options.SignatureCachePath))
                {
                    BackupEntryBase be = m_cachefilenamestrategy.ParseFilename(new Duplicati.Library.Interface.FileEntry(System.IO.Path.GetFileName(s)));
                    if (be is SignatureEntry)
                    {
                        if (be.Time == remote.Time && be.IsFull == remote.IsFull && ((SignatureEntry)be).Volumenumber == ((SignatureEntry)remote).Volumenumber)
                            return s;
                    }
                }

            return null;
        }

        private void GetInternal(BackupEntryBase remote, Manifestfile manifest, string filename, Manifestfile.HashEntry hash)
        {
            int retries = m_options.NumberOfRetries;
            Exception lastEx = null;
            m_statusmessage = string.Format(Strings.BackendWrapper.StatusMessageDownloading, remote.Filename);

            do
            {
                try
                {
                    if (manifest != null && !string.IsNullOrEmpty(m_options.SignatureCachePath) && hash != null && remote is SignatureEntry)
                    {
                        string cachefilename = FindCacheEntry(remote as SignatureEntry);
                        if (cachefilename != null && System.IO.File.Exists(cachefilename))
                        {
                            if ((hash.Size < 0 || new System.IO.FileInfo(cachefilename).Length == hash.Size) && Utility.Utility.CalculateHash(cachefilename) == hash.Hash)
                            {
                                if (manifest.Version > 2 && !string.IsNullOrEmpty(remote.EncryptionMode))
                                {
                                    try
                                    {
                                        using (Library.Interface.IEncryption enc = DynamicLoader.EncryptionLoader.GetModule(remote.EncryptionMode, m_options.Passphrase, m_options.RawOptions))
                                            enc.Decrypt(cachefilename, filename);

                                        return;
                                    }
                                    catch (Exception ex)
                                    {
                                        m_statistics.LogWarning(string.Format(Strings.BackendWrapper.CachedSignatureDecryptWarning, cachefilename, ex.Message), null);
                                        try { System.IO.File.Delete(cachefilename); }
                                        catch { }
                                    }
                                }
                                else
                                {
                                    //TODO: Don't copy, but just return it as write protected
                                    System.IO.File.Copy(cachefilename, filename, true);
                                    return;
                                }
                            }
                            else
                            {
                                m_statistics.LogWarning(string.Format(Strings.BackendWrapper.CachedSignatureHashMismatchWarning, cachefilename), null);
                                try { System.IO.File.Delete(cachefilename); }
                                catch { }
                            }
                        }
                    }

                    Utility.TempFile tempfile = null;
                    try
                    {
                        if (!string.IsNullOrEmpty(remote.EncryptionMode))
                            tempfile = new Duplicati.Library.Utility.TempFile();
                        else
                            tempfile = new Duplicati.Library.Utility.TempFile(filename);

                        ResetBackend();
                        m_statistics.AddNumberOfRemoteCalls(1);
                        if (m_backend is Duplicati.Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
                        {
                            using (System.IO.FileStream fs = System.IO.File.Open(tempfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                            using (Utility.ProgressReportingStream pgs = new Duplicati.Library.Utility.ProgressReportingStream(fs, remote.Fileentry.Size))
                            using (Utility.ThrottledStream ts = new Duplicati.Library.Utility.ThrottledStream(pgs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                            {
                                pgs.Progress += new Duplicati.Library.Utility.ProgressReportingStream.ProgressDelegate(pgs_Progress);
                                ts.Callback += new Duplicati.Library.Utility.ThrottledStream.ThrottledStreamCallback(ThrottledStream_Callback);
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

                        //This is required so we are sure that the file was downloaded completely and not partially,
                        // as any exception here will cause a retry, but using a partial file may cause random errors
                        if (remote.Fileentry.Size > 0 && remote.Fileentry.Size != new System.IO.FileInfo(tempfile).Length)
                            throw new Exception(string.Format(Strings.BackendWrapper.DownloadedFileSizeError, remote.Filename, remote.Fileentry.Size, new System.IO.FileInfo(tempfile).Length));

                        remote.RemoteHash = Utility.Utility.CalculateHash(tempfile);
                        
                        //Manifest version 3 has hashes WITH encryption
                        if (manifest != null && manifest.Version > 2)
                        {
                            if (hash != null && remote.RemoteHash != hash.Hash)
                                throw new HashMismathcException(string.Format(Strings.BackendWrapper.HashMismatchError, remote.Filename, hash.Hash, Utility.Utility.CalculateHash(tempfile)));

                            if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && remote is SignatureEntry)
                            {
                                string cachefilename = System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote));
                                try { System.IO.File.Copy(tempfile, cachefilename, true); }
                                catch (Exception ex) { m_statistics.LogWarning(string.Format(Strings.BackendWrapper.SaveCacheFileError, cachefilename), ex); }
                            }
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
                            tempfile = new Duplicati.Library.Utility.TempFile(filename);
                        }


                        //Manifest version 1+2 has hashes WITHOUT encryption
                        if (manifest != null && manifest.Version <= 2)
                        {
                            if (hash != null && Utility.Utility.CalculateHash(tempfile) != hash.Hash)
                                throw new HashMismathcException(string.Format(Strings.BackendWrapper.HashMismatchError, remote.Filename, hash.Hash, Utility.Utility.CalculateHash(tempfile)));

                            if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && remote is SignatureEntry)
                            {
                                string cachefilename = System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote));
                                try { System.IO.File.Copy(tempfile, cachefilename, true); }
                                catch (Exception ex) { m_statistics.LogWarning(string.Format(Strings.BackendWrapper.SaveCacheFileError, cachefilename), ex); }
                            }
                        }

                        lastEx = null;
                        tempfile.Protected = true; //Don't delete it
                        
                        if (m_backendInterfaceLogger != null)
                        {
                        
                            if (remote is ManifestEntry)
                                m_backendInterfaceLogger.RegisterGet(remote.Fileentry, true, System.IO.File.ReadAllText(tempfile));
                            else
                                m_backendInterfaceLogger.RegisterGet(remote.Fileentry, true, null);
                        }
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
                catch (System.Threading.ThreadAbortException tex)
                {
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterGet(remote.Fileentry, false, tex.ToString());
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    m_statistics.LogRetryAttempt(ex.Message, ex);
                    if (m_backendInterfaceLogger != null)
                        m_backendInterfaceLogger.RegisterGet(remote.Fileentry, false, ex.ToString());
                    DisposeBackend();

                    retries--;
                    if (retries > 0 && m_options.RetryDelay.Ticks > 0)
                        System.Threading.Thread.Sleep(m_options.RetryDelay);
                }
            } while (lastEx != null && retries > 0);

            if (lastEx != null)
                if (lastEx is HashMismathcException)
                    throw lastEx;
                else if (lastEx is System.Security.Cryptography.CryptographicException)
                    throw lastEx;
                else
                    throw new Exception(string.Format(Strings.BackendWrapper.FileDownloadError2, remote.Filename, lastEx.Message), lastEx);

            m_statistics.AddBytesDownloaded(new System.IO.FileInfo(filename).Length);
        }

        private void PutInternal(BackupEntryBase remote, string filename)
        {
            string remotename = remote.Filename;
            m_statusmessage = string.Format(Strings.BackendWrapper.StatusMessageUploading, remotename, Utility.Utility.FormatSizeString(new System.IO.FileInfo(filename).Length));
            Duplicati.Library.Interface.IFileEntry log_fe = new Duplicati.Library.Interface.FileEntry(remote.Filename, remote.Filesize, DateTime.Now, DateTime.Now);
            long sourceFileSize = new System.IO.FileInfo(filename).Length;

            try
            {
                int retries = m_options.NumberOfRetries;
                bool success = false;
                Exception lastEx = null;

                do
                {
                    try
                    {
                        ResetBackend();
                        m_statistics.AddNumberOfRemoteCalls(1);
                        if (m_backend is Library.Interface.IStreamingBackend && !m_options.DisableStreamingTransfers)
                        {
#if DEBUG_THROTTLE
                            DateTime begin = DateTime.Now;
#endif
                            using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                            using (Utility.ProgressReportingStream pgs = new Duplicati.Library.Utility.ProgressReportingStream(fs, fs.Length))
                            {   
                                pgs.Progress += new Duplicati.Library.Utility.ProgressReportingStream.ProgressDelegate(pgs_Progress);

                                using (Utility.ThrottledStream ts = new Utility.ThrottledStream(pgs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                                {
                                    ts.Callback += new Duplicati.Library.Utility.ThrottledStream.ThrottledStreamCallback(ThrottledStream_Callback);
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
                            m_backend.Put(remotename, filename);
                            if (ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                        }

                        if (m_options.ListVerifyUploads)
                        {
                            Library.Interface.FileEntry m = null;
                            foreach (Library.Interface.FileEntry fe in ListInternal())
                                if (fe.Name == remotename)
                                {
                                    m = fe;
                                    break;
                                }

                            if (m == null)
                                throw new Exception(string.Format(Strings.BackendWrapper.UploadVerificationFailure, remotename));
                            
                            long size = new System.IO.FileInfo(filename).Length;
                            if (m.Size >= 0 && m.Size != size)
                                throw new Exception(string.Format(Strings.BackendWrapper.UploadSizeVerificationFailure, remotename, m.Size, size));
                        }

                        if (remote is SignatureEntry && !string.IsNullOrEmpty(m_options.SignatureCachePath) && System.IO.File.Exists(filename))
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
                        if (m_backendInterfaceLogger != null)
                            m_backendInterfaceLogger.RegisterPut(log_fe, true, null);
                    }
                    catch (System.Threading.ThreadAbortException tex)
                    {
                        if (m_backendInterfaceLogger != null)
                            m_backendInterfaceLogger.RegisterPut(log_fe, false, tex.ToString());
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (m_backendInterfaceLogger != null)
                            m_backendInterfaceLogger.RegisterPut(log_fe, false, ex.ToString());
                        DisposeBackend();
                        
                        lastEx = ex;
                        m_statistics.LogRetryAttempt(ex.Message, ex);

                        retries--;
                        if (retries > 0 && m_options.RetryDelay.Ticks > 0)
                            System.Threading.Thread.Sleep(m_options.RetryDelay);
                    }
                } while (!success && retries > 0);

                if (!success)
                    throw new Exception(string.Format(Strings.BackendWrapper.FileUploadError2, remotename, lastEx == null ? "<null>" : lastEx.Message), lastEx);

                m_statistics.AddBytesUploaded(sourceFileSize);
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
                    if (ProgressEvent != null)
                        ProgressEvent(-1, "");
                }
                catch
                {
                }
            }

        }

        private void CreateFolderInternal()
        {
            try
            {
                ResetBackend();
                (m_backend as Library.Interface.IBackend_v2).CreateFolder();
                if (m_backendInterfaceLogger != null)
                    m_backendInterfaceLogger.RegisterCreateFolder(true, null);
            } catch (Exception ex) {
                if (m_backendInterfaceLogger != null)
                    m_backendInterfaceLogger.RegisterCreateFolder(false, ex.ToString());
                
                throw;
            }
        }

        public void CreateFolder()
        {
            if (m_backendSupportsCreateFolder)
                ProtectedInvoke("CreateFolderInternal");
            else
                throw new Exception(string.Format(Strings.BackendWrapper.BackendDoesNotSupportCreateFolder, m_backend.DisplayName, m_backend.ProtocolKey));
        }

        /// <summary>
        /// A callback from the throttled stream, used to change speed based on user adjustments
        /// </summary>
        /// <param name="sender">The stream that raised the event</param>
        void ThrottledStream_Callback(Utility.ThrottledStream sender)
        {
            sender.ReadSpeed = m_options.MaxUploadPrSecond;
            sender.WriteSpeed = m_options.MaxDownloadPrSecond;
        }

        /// <summary>
        /// Internal helper to consistenly name remote files beyond what the filenamestrategy supports
        /// </summary>
        /// <param name="remote">The entry to create a filename for</param>
        /// <returns>A filename with extensions</returns>
        public string GenerateFilename(BackupEntryBase remote)
        {
            string remotename = m_filenamestrategy.GenerateFilename(remote);
            if (remote is ManifestEntry)
                remotename += ".manifest";
            else if (remote is VerificationEntry)
                return remotename;
            else if (!(remote is DeleteTransactionEntry))
                remotename += "." + m_options.CompressionModule;

            if (!m_options.NoEncryption)
            {
                if (m_encryption == null)
                    m_encryption = DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions);

                remotename += "." + m_encryption.FilenameExtension;
            }

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

                        if (m_pendingOperations.Count > 0)
                        {
                            while (m_pendingOperations.Count != 0)
                                work.Add(m_pendingOperations.Dequeue());

                            //The top entry is probably being completed by the thread
                            m_pendingOperations.Enqueue(work[0]);
                        }
                    }

                    //Make sure the worker is awake to see the terminate message
                    m_asyncItemReady.Set();

                    //When the thread completes, disable asynchronous transfers and return the unfinished work
                    if (m_workerThread == null || !m_workerThread.IsAlive)
                    {
                        if (m_workerException != null)
                            throw m_workerException;

                        //If the thread did indeed complete the entry, remove it from the pending list
                        if (m_pendingOperations.Count == 0 && work.Count > 0)
                            work.RemoveAt(0);

                        m_async = false;
                        return work;
                    }
                }

                m_workerThread.Join(1000 * 5);
            }

        }

        private void DisposeInternal()
        {
            DisposeBackend();
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

            if (m_backendInterfaceLogger != null)
                m_backendInterfaceLogger.EndOperation();    
        }

        #endregion
    }
}
