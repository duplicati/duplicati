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
        private Backend.IBackend m_backend;
        /// <summary>
        /// The statistics gathering object
        /// </summary>
        private CommunicationStatistics m_statistics;
        /// <summary>
        /// The set of options used by Duplicati
        /// </summary>
        private Options m_options;

        //These keep track of async operations
        private Queue<KeyValuePair<BackupEntry, string>> m_pendingOperations;
        
        //TODO: Figure out if the linux implementation uses the pthreads model, where signals
        //are lost if there are no waiters at the signaling time
        private System.Threading.ManualResetEvent m_asyncWait;
        
        /// <summary>
        /// The lock for items in the processing queue
        /// </summary>
        private object m_queuelock;

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
        private List<BackupEntry> m_orphans;

        /// <summary>
        /// The progress reporting event
        /// </summary>
        public event RSync.RSyncDir.ProgressEventDelegate ProgressEvent;

        /// <summary>
        /// If encryption is used, this is the instance that performs it
        /// </summary>
        private Encryption.IEncryption m_encryption = null;

        public BackendWrapper(CommunicationStatistics statistics, string backend, Options options)
        {
            m_statistics = statistics;
            m_options = options;

            m_filenamestrategy = new FilenameStrategy(m_options);

            m_backend = Backend.BackendLoader.GetBackend(backend, m_options.RawOptions);
            if (m_backend == null)
                throw new Exception(string.Format(Strings.BackendWrapper.BackendNotFoundError, m_backend));

            if (!options.NoEncryption)
            {
                if (string.IsNullOrEmpty(options.Passphrase))
                    throw new Exception(Strings.BackendWrapper.PassphraseMissingError);

                if (options.GPGEncryption)
                {
                    if (!string.IsNullOrEmpty(options.GPGPath))
                        Library.Encryption.GPGEncryption.PGP_PROGRAM = options.GPGPath;
                    m_encryption = new Library.Encryption.GPGEncryption(options.Passphrase, options.GPGSignKey);
                }
                else
                    m_encryption = new Library.Encryption.AESEncryption(options.Passphrase);
            }

            if (m_options.AutoCleanup)
                m_orphans = new List<BackupEntry>();

            if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && !System.IO.Directory.Exists(m_options.SignatureCachePath))
                System.IO.Directory.CreateDirectory(m_options.SignatureCachePath);

            if (m_options.AsynchronousUpload)
            {
                //If we are using async operations, the entire class is actually threadsafe,
                //utilizing a common exclusive lock on all operations. But the implementation does
                //not prevent starvation, so it should not be called by multiple threads.
                m_pendingOperations = new Queue<KeyValuePair<BackupEntry, string>>();
                m_asyncWait = new System.Threading.ManualResetEvent(true);
                m_queuelock = new object();
            }
        }

        public void AddOrphan(BackupEntry entry)
        {
            if (m_orphans != null)
                m_orphans.Add(entry);
            else
                Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.PartialFileFoundMessage, entry.Filename), Duplicati.Library.Logging.LogMessageType.Warning);
        }

        public BackupEntry GetBackupSet(string timelimit)
        {
            if (string.IsNullOrEmpty(timelimit))
                timelimit = "now";

            return GetBackupSet(Core.Timeparser.ParseTimeInterval(timelimit, DateTime.Now));
        }

        public BackupEntry GetBackupSet(DateTime timelimit)
        {
            List<BackupEntry> backups = GetBackupSets();

            if (backups.Count == 0)
                throw new Exception(Strings.BackendWrapper.NoBackupsFoundError);

            BackupEntry bestFit = backups[0];
            List<BackupEntry> additions = new List<BackupEntry>();
            foreach (BackupEntry be in backups)
                if (be.Time < timelimit)
                {
                    bestFit = be;
                    additions.Clear();
                    foreach (BackupEntry bex in be.Incrementals)
                        if (bex.Time <= timelimit)
                            additions.Add(bex);

                }

           if (bestFit.SignatureFile.Count == 0 || bestFit.ContentVolumes.Count == 0)
                throw new Exception(Strings.BackendWrapper.FilenameParseError);

            bestFit.Incrementals = additions;
            return bestFit;

        }

        public List<BackupEntry> SortAndPairSets(List<Duplicati.Library.Backend.FileEntry> files)
        {
            List<BackupEntry> incrementals = new List<BackupEntry>();
            List<BackupEntry> fulls = new List<BackupEntry>();
            Dictionary<string, List<BackupEntry>> signatures = new Dictionary<string, List<BackupEntry>>();
            Dictionary<string, List<BackupEntry>> contents = new Dictionary<string, List<BackupEntry>>();

            foreach (Duplicati.Library.Backend.FileEntry fe in files)
            {
                BackupEntry be = m_filenamestrategy.DecodeFilename(fe);
                if (be == null)
                    continue; //Non-duplicati files

                //Other type file
                if (m_options.NoEncryption != string.IsNullOrEmpty(be.EncryptionMode))
                    continue;

                if (!m_options.NoEncryption)
                {
                    if (m_options.GPGEncryption && be.EncryptionMode != "gpg")
                        continue;

                    if (!m_options.GPGEncryption && be.EncryptionMode != "aes")
                        continue;
                }

                //TODO: Compression mode should not be manifest
                if (be.CompressionMode != "manifest" && be.CompressionMode != "zip")
                    continue;


                if (be.Type == BackupEntry.EntryType.Content)
                {
                    //TODO: The timezone may change if executed during the switch from/to daylight savings time
                    string content = m_filenamestrategy.GenerateFilename(BackupEntry.EntryType.Manifest, be.IsFull, be.IsShortName, be.Time) + ".manifest";
                    if (be.EncryptionMode != null)
                        content += "." + be.EncryptionMode;

                    if (!contents.ContainsKey(content))
                        contents[content] = new List<BackupEntry>();
                    contents[content].Add(be);
                }
                else if (be.Type == BackupEntry.EntryType.Signature)
                {
                    //TODO: The timezone may change if executed during the switch from/to daylight savings time
                    string content = m_filenamestrategy.GenerateFilename(BackupEntry.EntryType.Manifest, be.IsFull, be.IsShortName, be.Time) + ".manifest";
                    if (be.EncryptionMode != null)
                        content += "." + be.EncryptionMode;

                    if (!signatures.ContainsKey(content))
                        signatures[content] = new List<BackupEntry>();
                    signatures[content].Add(be);
                }
                else if (be.Type != BackupEntry.EntryType.Manifest)
                    throw new Exception(string.Format(Strings.BackendWrapper.InvalidEntryTypeError, be.Type));
                else if (be.IsFull)
                    fulls.Add(be);
                else
                    incrementals.Add(be);
            }

            fulls.Sort(new Sorter());
            incrementals.Sort(new Sorter());

            foreach (BackupEntry be in fulls)
            {
                //TODO: The timezone may change if executed during the switch from/to daylight savings time
                string filename = m_filenamestrategy.GenerateFilename(BackupEntry.EntryType.Manifest, be.IsFull, be.IsShortName, be.Time) + ".manifest";
                if (be.EncryptionMode != null)
                    filename += "." + be.EncryptionMode;

                if (contents.ContainsKey(filename))
                {
                    be.ContentVolumes.AddRange(contents[filename]);
                    contents.Remove(filename);
                }

                if (signatures.ContainsKey(filename))
                {
                    be.SignatureFile.AddRange(signatures[filename]);
                    signatures.Remove(filename);
                }
            }


            int index = 0;
            foreach (BackupEntry be in incrementals)
            {
                //TODO: The timezone may change if executed during the switch from/to daylight savings time
                string filename = m_filenamestrategy.GenerateFilename(BackupEntry.EntryType.Manifest, be.IsFull, be.IsShortName, be.Time) + ".manifest";
                if (be.EncryptionMode != null)
                    filename += "." + be.EncryptionMode;

                if (contents.ContainsKey(filename))
                {
                    be.ContentVolumes.AddRange(contents[filename]);
                    contents.Remove(filename);
                }

                if (signatures.ContainsKey(filename))
                {
                    be.SignatureFile.AddRange(signatures[filename]);
                    signatures.Remove(filename);
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
                    fulls[index].Incrementals.Add(be);
                }
            }

            foreach (BackupEntry be in fulls)
            {
                be.ContentVolumes.Sort(new Sorter());
                be.SignatureFile.Sort(new Sorter());
            }

            foreach (BackupEntry be in incrementals)
            {
                be.ContentVolumes.Sort(new Sorter());
                be.SignatureFile.Sort(new Sorter());
            }

            if (m_orphans != null)
            {
                foreach (List<BackupEntry> lb in contents.Values)
                    m_orphans.AddRange(lb);
                foreach (List<BackupEntry> lb in signatures.Values)
                    m_orphans.AddRange(lb);
            }

            return fulls;
        }

        public List<BackupEntry> GetBackupSets()
        {
            using (new Logging.Timer("Getting and sorting filelist from " + m_backend.DisplayName))
                return SortAndPairSets((List<Duplicati.Library.Backend.FileEntry>)ProtectedInvoke("ListInternal"));
        }

        public void Put(BackupEntry remote, string filename)
        {
            if (!m_options.AsynchronousUpload)
                PutInternal(remote, filename);
            else
            {
                lock (m_queuelock)
                {
                    m_pendingOperations.Enqueue(new KeyValuePair<BackupEntry, string>( remote, filename ));
                    if (m_asyncWait.WaitOne(0, false))
                    {
                        m_asyncWait.Reset();
                        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(ProcessQueue));
                    }
                }
            }
        }

        public void Get(BackupEntry remote, string filename, string filehash)
        {
            ProtectedInvoke("GetInternal", remote, filename, filehash);
        }

        public void Delete(BackupEntry remote)
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
            if (!m_options.AsynchronousUpload)
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
                        if (m_asyncWait.WaitOne(0, false))
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
                    m_asyncWait.WaitOne();
                    //Now re-acquire the lock, and re-check the state of the event
                }
            }

        }

        private void DeleteSignatureCacheCopy(BackupEntry entry)
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

            foreach (BackupEntry be in m_orphans)
            {
                Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, be.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                if (m_options.Force)
                {
                    m_backend.Delete(be.Filename);
                    DeleteSignatureCacheCopy(be);
                }

                foreach (BackupEntry bex in be.SignatureFile)
                {
                    Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, bex.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                    if (m_options.Force)
                    {
                        m_backend.Delete(bex.Filename);
                        DeleteSignatureCacheCopy(bex);
                    }
                }
                foreach (BackupEntry bex in be.ContentVolumes)
                {
                    Logging.Log.WriteMessage(string.Format(Strings.BackendWrapper.RemovingLeftoverFileMessage, bex.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                    if (m_options.Force)
                    {
                        m_backend.Delete(bex.Filename);
                        DeleteSignatureCacheCopy(bex);
                    }
                }
            }

            if (!m_options.Force && m_orphans.Count > 0)
                Logging.Log.WriteMessage(Strings.BackendWrapper.FilesNotForceRemovedMessage, Duplicati.Library.Logging.LogMessageType.Information);
        }

        private List<Duplicati.Library.Backend.FileEntry> ListInternal()
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

        private void DeleteInternal(BackupEntry remote)
        {
            string remotename = GetFullFilename(remote);
            int retries = m_options.NumberOfRetries;
            Exception lastEx = null;

            do
            {
                try
                {
                    m_statistics.NumberOfRemoteCalls++;
                    m_backend.Delete(remotename);
                    lastEx = null;
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

            if (remote.Type == BackupEntry.EntryType.Signature && !string.IsNullOrEmpty(m_options.SignatureCachePath))
            {
                try 
                { 
                    if (System.IO.File.Exists(System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote))))
                        System.IO.File.Delete(System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote))); 
                }
                catch {}
            }
        }

        private void GetInternal(BackupEntry remote, string filename, string filehash)
        {
            string remotename = GetFullFilename(remote);
            int retries = m_options.NumberOfRetries;
            Exception lastEx = null;
            m_statusmessage = string.Format(Strings.BackendWrapper.StatusMessageDownloading, remotename);

            do
            {
                try
                {
                    if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && remote.Type == BackupEntry.EntryType.Signature)
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
                        if (m_encryption != null)
                            tempfile = new Duplicati.Library.Core.TempFile();
                        else
                            tempfile = new Duplicati.Library.Core.TempFile(filename);

                        m_statistics.NumberOfRemoteCalls++;
                        if (m_backend is Backend.IStreamingBackend)
                        {
                            //TODO: How can we guess the remote file size for progress reporting?
                            using (System.IO.FileStream fs = System.IO.File.Open(tempfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                            using (Core.ThrottledStream ts = new Duplicati.Library.Core.ThrottledStream(fs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                            {
                                ts.Callback += new Duplicati.Library.Core.ThrottledStream.ThrottledStreamCallback(ThrottledStream_Callback);
                                ((Backend.IStreamingBackend)m_backend).Get(remotename, ts);
                            }
                        }
                        else
                        {
                            if (!m_options.AsynchronousUpload && ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                            m_backend.Get(remotename, tempfile);
                            if (!m_options.AsynchronousUpload && ProgressEvent != null)
                                ProgressEvent(100, m_statusmessage);
                        }

                        if (m_encryption != null)
                        {
                            m_encryption.Decrypt(tempfile, filename);
                            tempfile.Dispose(); //Remove the encrypted file

                            //Wrap the new file as a temp file
                            tempfile = new Duplicati.Library.Core.TempFile(filename);
                        }

                        if (filehash != null && Core.Utility.CalculateHash(tempfile) != filehash)
                            throw new Exception(string.Format(Strings.BackendWrapper.HashMismatchError, remotename, filehash, Core.Utility.CalculateHash(tempfile)));

                        if (!string.IsNullOrEmpty(m_options.SignatureCachePath) && remote.Type == BackupEntry.EntryType.Signature)
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
                throw new Exception(string.Format(Strings.BackendWrapper.FileDownloadError, lastEx.Message), lastEx);

            m_statistics.NumberOfBytesDownloaded += new System.IO.FileInfo(filename).Length;
        }

        private void PutInternal(BackupEntry remote, string filename)
        {
            string remotename = GetFullFilename(remote);
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
                        if (m_backend is Backend.IStreamingBackend)
                        {
#if DEBUG
                            DateTime begin = DateTime.Now;
#endif
                            using (System.IO.FileStream fs = System.IO.File.Open(encryptedFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                            using (Core.ProgressReportingStream pgs = new Duplicati.Library.Core.ProgressReportingStream(fs, fs.Length))
                            {
                                if (!m_options.AsynchronousUpload)
                                    pgs.Progress += new Duplicati.Library.Core.ProgressReportingStream.ProgressDelegate(pgs_Progress);

                                using (Core.ThrottledStream ts = new Core.ThrottledStream(pgs, m_options.MaxUploadPrSecond, m_options.MaxDownloadPrSecond))
                                {
                                    ts.Callback += new Duplicati.Library.Core.ThrottledStream.ThrottledStreamCallback(ThrottledStream_Callback);
                                    ((Backend.IStreamingBackend)m_backend).Put(remotename, ts);
                                }
                            }

#if DEBUG
                            TimeSpan duration = DateTime.Now - begin;
                            long size = new System.IO.FileInfo(encryptedFile).Length;
                            Console.WriteLine("Transferred " + Core.Utility.FormatSizeString(size) + " in " + duration.TotalSeconds.ToString() + ", yielding : " + ((size / (double)1024.0) / duration.TotalSeconds) + " kb/s");
#endif
                        }
                        else
                        {
                            if (!m_options.AsynchronousUpload && ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                            m_backend.Put(remotename, encryptedFile);
                            if (!m_options.AsynchronousUpload && ProgressEvent != null)
                                ProgressEvent(50, m_statusmessage);
                        }

                        if (remote.Type == BackupEntry.EntryType.Signature && !string.IsNullOrEmpty(m_options.SignatureCachePath))
                            System.IO.File.Copy(filename, System.IO.Path.Combine(m_options.SignatureCachePath, m_cachefilenamestrategy.GenerateFilename(remote)), true);

                        success = true;
                        lastEx = null;
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
                    throw new Exception(string.Format(Strings.BackendWrapper.FileUploadError, lastEx == null ? "<null>" : lastEx.Message));

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

        private string GetFullFilename(BackupEntry remote)
        {
            //TODO: Remember to change filename when tar is supported
            string remotename = m_filenamestrategy.GenerateFilename(remote);
            if (remote.Type == BackupEntry.EntryType.Manifest)
                remotename += ".manifest";
            else
                remotename += ".zip";

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
            while (true)
            {
                KeyValuePair<BackupEntry, string> args;

                //Obtain the lock for the queue
                lock (m_queuelock)
                {
                    if (m_pendingOperations.Count == 0)
                    {
                        //Signal that we are done
                        m_asyncWait.Set();
                        return;
                    }
                    else
                        args = m_pendingOperations.Dequeue();

                }

                PutInternal(args.Key, args.Value);
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
            if (m_backend != null)
                ProtectedInvoke("DisposeInternal");
        }

        #endregion
    }
}
