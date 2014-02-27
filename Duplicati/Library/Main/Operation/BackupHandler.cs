using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class BackupHandler : IDisposable
    {    
        private readonly Options m_options;
        private BackendManager m_backend;
        private string m_backendurl;

        private readonly byte[] m_blockbuffer;
        private readonly byte[] m_blocklistbuffer;
        private readonly System.Security.Cryptography.HashAlgorithm m_blockhasher;
        private readonly System.Security.Cryptography.HashAlgorithm m_filehasher;

        private LocalBackupDatabase m_database;
        private System.Data.IDbTransaction m_transaction;
        private BlockVolumeWriter m_blockvolume;
        private IndexVolumeWriter m_indexvolume;
        private FilesetVolumeWriter m_filesetvolume;

        private Snapshots.ISnapshotService m_snapshot;

        private readonly IMetahash EMPTY_METADATA;
        
        private Library.Utility.IFilter m_filter;
        
        private BackupResults m_result;

        //To better record what happens with the backend, we flush the log messages regularly
        // We cannot flush immediately, because that would mess up the transaction, as the uploader
        // is on another thread
        private readonly TimeSpan FLUSH_TIMESPAN = TimeSpan.FromSeconds(10);
        private DateTime m_backendLogFlushTimer;
        
        // Speed up things by caching these
        private readonly FileAttributes m_attributeFilter;
        private readonly Options.SymlinkStrategy m_symlinkPolicy;
        private readonly int m_blocksize;

        public BackupHandler(string backendurl, Options options, BackupResults results)
        {
        	EMPTY_METADATA = Utility.WrapMetadata(new Dictionary<string, string>(), options);
        	
            m_options = options;
            m_result = results;
            m_backendurl = backendurl;

            m_attributeFilter = m_options.FileAttributeFilter;
            m_symlinkPolicy = m_options.SymlinkPolicy;
            m_blocksize = m_options.Blocksize;

            m_blockbuffer = new byte[m_options.Blocksize * Math.Max(1, m_options.FileReadBufferSize / m_options.Blocksize)];
            m_blocklistbuffer = new byte[m_options.Blocksize];

            m_blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
            m_filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FileHashAlgorithm);

			if (m_blockhasher == null)
				throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.BlockHashAlgorithm));
			if (m_filehasher == null)
				throw new Exception(string.Format(Strings.Foresthash.InvalidHashAlgorithm, m_options.FileHashAlgorithm));

            if (!m_blockhasher.CanReuseTransform)
                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.BlockHashAlgorithm));
            if (!m_filehasher.CanReuseTransform)
                throw new Exception(string.Format(Strings.Foresthash.InvalidCryptoSystem, m_options.FileHashAlgorithm));
                
            if (options.AllowPassphraseChange)
                throw new Exception(Strings.Foresthash.PassphraseChangeUnsupported);
        }
        
        
        public class FilterHandler
        {
            private Snapshots.ISnapshotService m_snapshot;
            private FileAttributes m_attributeFilter;
            private Duplicati.Library.Utility.IFilter m_filter;
            private Options.SymlinkStrategy m_symlinkPolicy;
            private ILogWriter m_logWriter;
            
            public FilterHandler(Snapshots.ISnapshotService snapshot,FileAttributes attributeFilter, Duplicati.Library.Utility.IFilter filter, Options.SymlinkStrategy symlinkPolicy, ILogWriter logWriter)
            {
                m_snapshot = snapshot;
                m_attributeFilter = attributeFilter;
                m_filter = filter;
                m_symlinkPolicy = symlinkPolicy;
                m_logWriter = logWriter;
            }
        
            public bool AttributeFilter(string rootpath, string path, FileAttributes attributes)
            {
                try
                {
                    if (m_snapshot.IsBlockDevice(path))
                    {
                        if (m_logWriter != null)
                            m_logWriter.AddVerboseMessage("Excluding block device: {0}", path);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddWarning(string.Format("Failed to process path: ", path), ex);
                    return false;
                }            
            
                if ((m_attributeFilter & attributes) != 0)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Excluding path due to attribute filter: {0}", path);
                    return false;
                }
                            
                Library.Utility.IFilter match;
                if (!Library.Utility.FilterExpression.Matches(m_filter, path, out match))
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Excluding path due to filter: {0} => {1}", path, match == null ? "null" : match.ToString());
                    return false;
                }
                else if (match != null)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Including path due to filter: {0} => {1}", path, match.ToString());
                }
                
                if (m_symlinkPolicy != Options.SymlinkStrategy.Follow && (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Excluding symlink: {0}", path);
                    return false;
                }
                                
                return true;
            }
        }

        public static Snapshots.ISnapshotService GetSnapshot(string[] sources, Options options, ILogWriter log)
        {
            try
            {
                if (options.SnapShotStrategy != Options.OptimizationStrategy.Off)
                    return Duplicati.Library.Snapshots.SnapshotUtility.CreateSnapshot(sources, options.RawOptions);
            }
            catch (Exception ex)
            {
                if (options.SnapShotStrategy == Options.OptimizationStrategy.Required)
                    throw;
                else if (options.SnapShotStrategy == Options.OptimizationStrategy.On)
                {
                    log.AddWarning(string.Format(Strings.RSyncDir.SnapshotFailedError, ex.ToString()), ex);
                }
            }

            return Library.Utility.Utility.IsClientLinux ?
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotLinux(sources, options.RawOptions)
                    :
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotWindows(sources, options.RawOptions);
        }

        private void CountFilesThread()
        {
            var updater = m_result.OperationProgressUpdater;
            var count = 0L;
            var size = 0L;
            var followSymlinks = m_options.SymlinkPolicy != Duplicati.Library.Main.Options.SymlinkStrategy.Follow;
            
            foreach(var path in m_snapshot.EnumerateFilesAndFolders(new FilterHandler(m_snapshot, m_attributeFilter, m_filter, m_symlinkPolicy, null).AttributeFilter))
            {
                var fa = FileAttributes.Normal;
                try { fa = m_snapshot.GetAttributes(path); }
                catch { }
                
                if (followSymlinks && ((fa & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint))
                    continue;
                else if ((fa & FileAttributes.Directory) == FileAttributes.Directory)
                    continue;
                    
                count++;
                
                try { size += m_snapshot.GetFileSize(path); }
                catch { }
                
                m_result.OperationProgressUpdater.UpdatefileCount(count, size, false);                    
            }
            
            m_result.OperationProgressUpdater.UpdatefileCount(count, size, true);
            
        }

        public void Run(string[] sources, Library.Utility.IFilter filter)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Begin);                        
            
            using(m_database = new LocalBackupDatabase(m_options.Dbpath, m_options))
            {
                m_result.SetDatabase(m_database);
                m_result.Dryrun = m_options.Dryrun;
                
                Utility.VerifyParameters(m_database, m_options);
                m_database.VerifyConsistency(null);
                // If there is no filter, we set an empty filter to simplify the code
                // If there is a filter, we make sure that fall-through includes the entry
                m_filter = filter ?? new Library.Utility.FilterExpression();
            	
                var lastVolumeSize = -1L;
                m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);
                System.Threading.Thread parallelScanner = null;

    
                try
                {
                    m_snapshot = GetSnapshot(sources, m_options, m_result);

                    // Start parallel scan
                    if (m_options.ChangedFilelist == null || m_options.ChangedFilelist.Length < 1)
                    {
                        parallelScanner = new System.Threading.Thread(CountFilesThread) {
                            Name = "Read ahead file counter",
                            IsBackground = true
                        };
                        parallelScanner.Start();
                    }

                    using(m_backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
                    using(m_filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
                    {
                        var incompleteFilesets = m_database.GetIncompleteFilesets(null).OrderBy(x => x.Value).ToArray();                        
                        if (incompleteFilesets.Length != 0)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreviousBackupFinalize);
                            m_result.AddMessage(string.Format("Uploading filelist from previous interrupted backup"));
                            using(var trn = m_database.BeginTransaction())
                            {
                                var incompleteSet = incompleteFilesets.Last();
                                var badIds = from n in incompleteFilesets select n.Key;
                                
                                var prevs = (from n in m_database.FilesetTimes 
                                            where 
                                                n.Key < incompleteSet.Key
                                                &&
                                                !badIds.Contains(n.Key)
                                            orderby n.Key                                                
                                            select n.Key).ToArray();
                                            
                                var prevId = prevs.Length == 0 ? -1 : prevs.Last();

                                FilesetVolumeWriter fsw = null;
                                try
                                {
                                    var s = 1;
                                    var fileTime = incompleteSet.Value + TimeSpan.FromSeconds(s);
                                    var oldFilesetID = incompleteSet.Key;
                                    
                                    // Probe for an unused filename
                                    while (s < 60)
                                    {
                                        var id = m_database.GetRemoteVolumeID(VolumeBase.GenerateFilename(RemoteVolumeType.Files, m_options, null, fileTime));
                                        if (id < 0)
                                            break;

                                        fileTime = incompleteSet.Value + TimeSpan.FromSeconds(++s);
                                    }
                                    
                                    fsw = new FilesetVolumeWriter(m_options, fileTime);
                                    fsw.VolumeID = m_database.RegisterRemoteVolume(fsw.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary, m_transaction);
                                    var newFilesetID = m_database.CreateFileset(fsw.VolumeID, fileTime, trn);
                                    m_database.LinkFilesetToVolume(newFilesetID, fsw.VolumeID, trn);
                                    m_database.AppendFilesFromPreviousSet(trn, null, newFilesetID, prevId, fileTime);
                                    
                                    m_database.WriteFileset(fsw, trn, newFilesetID);
                                    
                                    if (m_options.Dryrun)
                                    {
                                        m_result.AddDryrunMessage(string.Format("Would upload fileset: {0}, size: {1}", fsw.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(fsw.LocalFilename).Length)));
                                    }
                                    else
                                    {
                                        m_database.UpdateRemoteVolume(fsw.RemoteFilename, RemoteVolumeState.Uploading, -1, null, trn);
                                        
                                        using(new Logging.Timer("CommitUpdateFilelistVolume"))
                                            trn.Commit();
                                        
                                        m_backend.Put(fsw);
                                        fsw = null;
                                    }
                                }
                                finally
                                {
                                    if (fsw != null)
                                        try { fsw.Dispose(); }
                                        catch { fsw = null; }
                                }                          
                            }
                        }

                        if (!m_options.NoBackendverification)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreBackupVerify);
                            using(new Logging.Timer("PreBackupVerify"))
                            {
                                try
                                {
                                    FilelistProcessor.VerifyRemoteList(m_backend, m_options, m_database, m_result.BackendWriter);
                                }
                                catch (Exception ex)
                                {
                                    if (m_options.AutoCleanup)
                                    {
                                        m_result.AddWarning("Backend verification failed, attempting automatic cleanup", ex);
                                        m_result.RepairResults = new RepairResults(m_result);
                                        new RepairHandler(m_backend.BackendUrl, m_options, (RepairResults)m_result.RepairResults).Run();
        		            			
                                        m_result.AddMessage("Backend cleanup finished, retrying verification");
                                        FilelistProcessor.VerifyRemoteList(m_backend, m_options, m_database, m_result.BackendWriter);
                                    }
                                    else
                                        throw;
                                }
                            }
                        }

                        m_database.BuildLookupTable(m_options);
                        m_transaction = m_database.BeginTransaction();
    		            
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_ProcessingFiles);
                        var filesetvolumeid = m_database.RegisterRemoteVolume(m_filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary, m_transaction);
                        m_database.CreateFileset(filesetvolumeid, VolumeBase.ParseFilename(m_filesetvolume.RemoteFilename).Time, m_transaction);
    	
                        m_blockvolume = new BlockVolumeWriter(m_options);
                        m_blockvolume.VolumeID = m_database.RegisterRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, m_transaction);
    		            
                        if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
                        {
                            m_indexvolume = new IndexVolumeWriter(m_options);
                            m_indexvolume.VolumeID = m_database.RegisterRemoteVolume(m_indexvolume.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, m_transaction);
                        }
                        
                        var filterhandler = new FilterHandler(m_snapshot, m_attributeFilter, m_filter, m_symlinkPolicy, null);
    		                		                        	
                        using(new Logging.Timer("BackupMainOperation"))
                        {
                            if (m_options.ChangedFilelist != null && m_options.ChangedFilelist.Length >= 1)
                            {
                                m_result.AddVerboseMessage("Processing supplied change list instead of enumerating filesystem");
                                m_result.OperationProgressUpdater.UpdatefileCount(m_options.ChangedFilelist.Length, 0, true);
                                
                                foreach(var p in m_options.ChangedFilelist)
                                {
                                    FileAttributes fa = new FileAttributes();
                                    try
                                    {
                                        fa = m_snapshot.GetAttributes(p);
                                    }
                                    catch (Exception ex)
                                    {
                                        m_result.AddWarning(string.Format("Failed to read attributes: {0}, message: {1}", p, ex.Message), ex);
                                    }
    		
                                    if (filterhandler.AttributeFilter(null, p, fa))
                                    {                                        
                                        try
                                        {
                                            this.HandleFilesystemEntry(p, fa);
                                        }
                                        catch (Exception ex)
                                        {
                                            m_result.AddWarning(string.Format("Failed to process element: {0}, message: {1}", p, ex.Message), ex);
                                        }
                                    }
                                }
    		
                                m_database.AppendFilesFromPreviousSet(m_transaction, m_options.DeletedFilelist);
                            }
                            else
                            {                                    
                                foreach(var path in m_snapshot.EnumerateFilesAndFolders(filterhandler.AttributeFilter))
                                    this.HandleFilesystemEntry(path, m_snapshot.GetAttributes(path));
                                
                            }
                            
                            //If the scanner is still running for some reason, make sure we kill it now 
                            if (parallelScanner != null && parallelScanner.IsAlive)
                                parallelScanner.Abort();
                            
                            // We no longer need to snapshot active
                            try { m_snapshot.Dispose(); }
                            finally { m_snapshot = null; }

                            m_result.OperationProgressUpdater.UpdatefileCount(m_result.ExaminedFiles, m_result.SizeOfExaminedFiles, true);
                        }
    									
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Finalize);
                        using(new Logging.Timer("FinalizeRemoteVolumes"))
                        {
                            if (m_blockvolume.SourceSize > 0)
                            {
                                lastVolumeSize = m_blockvolume.SourceSize;
        	 					
                                if (m_options.Dryrun)
                                {
                                    m_result.AddDryrunMessage(string.Format("Would upload block volume: {0}, size: {1}", m_blockvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_blockvolume.LocalFilename).Length)));
                                    if (m_indexvolume != null)
                                    {
                                        m_blockvolume.Close();
                                        UpdateIndexVolume();
                                        m_indexvolume.FinishVolume(Library.Utility.Utility.CalculateHash(m_blockvolume.LocalFilename), new FileInfo(m_blockvolume.LocalFilename).Length);
                                        m_result.AddDryrunMessage(string.Format("Would upload index volume: {0}, size: {1}", m_indexvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_indexvolume.LocalFilename).Length)));
                                    }
                                    
                                    m_blockvolume.Dispose();
                                    m_blockvolume = null;
                                    m_indexvolume.Dispose();
                                    m_indexvolume = null;
                                }
                                else
                                {
                                    m_database.UpdateRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
                                    m_blockvolume.Close();
                                    UpdateIndexVolume();
        	                		
                                    using(new Logging.Timer("CommitUpdateRemoteVolume"))
                                        m_transaction.Commit();
                                    m_transaction = m_database.BeginTransaction();
        	                		
                                    m_backend.Put(m_blockvolume, m_indexvolume);
                                    
                                    m_blockvolume = null;
                                    m_indexvolume = null;
                                }
                            }
                            else
                            {
                                m_database.RemoveRemoteVolume(m_blockvolume.RemoteFilename, m_transaction);
                                if (m_indexvolume != null)
                                    m_database.RemoveRemoteVolume(m_indexvolume.RemoteFilename, m_transaction);
                            }
                        }
    		            
                        using(new Logging.Timer("UpdateChangeStatistics"))
                            m_database.UpdateChangeStatistics(m_result);
                        using(new Logging.Timer("VerifyConsistency"))
                            m_database.VerifyConsistency(m_transaction);
    
    
                        var changeCount = 
                            m_result.AddedFiles + m_result.ModifiedFiles + m_result.DeletedFiles +
                            m_result.AddedFolders + m_result.ModifiedFolders + m_result.DeletedFolders +
                            m_result.AddedSymlinks + m_result.ModifiedSymlinks + m_result.DeletedSymlinks;
                            
                        //Changes in the filelist triggers a filelist upload
                        if (m_options.UploadUnchangedBackups || changeCount > 0)
                        {
                            using(new Logging.Timer("Uploading a new fileset"))
                            {
                                if (!string.IsNullOrEmpty(m_options.ControlFiles))
                                    foreach(var p in m_options.ControlFiles.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                                        m_filesetvolume.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));
        	
                                m_database.WriteFileset(m_filesetvolume, m_transaction);
                                m_filesetvolume.Close();
                                
                                if (m_options.Dryrun)
                                    m_result.AddDryrunMessage(string.Format("Would upload fileset volume: {0}, size: {1}", m_filesetvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_filesetvolume.LocalFilename).Length)));
                                else
                                {
                                    m_database.UpdateRemoteVolume(m_filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
        
                                    using(new Logging.Timer("CommitUpdateRemoteVolume"))
                                        m_transaction.Commit();
                                    m_transaction = m_database.BeginTransaction();
        
                                    m_backend.Put(m_filesetvolume);
                                }
                            }
                        }
                        else
                        {
                            m_result.AddVerboseMessage("removing temp files, as no data needs to be uploaded");
                            m_database.RemoveRemoteVolume(m_filesetvolume.RemoteFilename, m_transaction);
                        }
    									
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
                        using(new Logging.Timer("Async backend wait"))
                            m_backend.WaitForComplete(m_database, m_transaction);
                            
                        if (m_options.KeepTime.Ticks > 0 || m_options.KeepVersions != 0)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Delete);
                            m_result.DeleteResults = new DeleteResults(m_result);
                            using(var db = new LocalDeleteDatabase(m_database))
                                new DeleteHandler(m_backend.BackendUrl, m_options, (DeleteResults)m_result.DeleteResults).DoRun(db, m_transaction, true, lastVolumeSize <= m_options.SmallFileSize);
                            
                        }
                        else if (lastVolumeSize <= m_options.SmallFileSize && !m_options.NoAutoCompact)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Compact);
                            m_result.CompactResults = new CompactResults(m_result);
                            using(var db = new LocalDeleteDatabase(m_database))
                                new CompactHandler(m_backend.BackendUrl, m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, m_transaction);
                        }
    		            
                        if (m_options.UploadVerificationFile)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_VerificationUpload);
                            FilelistProcessor.UploadVerificationFile(m_backend.BackendUrl, m_options, m_result.BackendWriter, m_database, m_transaction);
                        }

                        if (m_options.Dryrun)
                        {
                            m_transaction.Rollback();
                            m_transaction = null;
                        }
                        else
                        {
                            using(new Logging.Timer("CommitFinalizingBackup"))
                                m_transaction.Commit();
                                
                            m_transaction = null;
                            m_database.Vacuum();
                            
                            if (!m_options.NoBackendverification)
                            {
                                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupVerify);
                                using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
                                {
                                    using(new Logging.Timer("AfterBackupVerify"))
                                        FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter);
                                    backend.WaitForComplete(m_database, null);
                                }

                                if (m_options.BackupTestSampleCount > 0 && m_database.GetRemoteVolumes().Count() > 0)
                                {
                                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PostBackupTest);
                                    m_result.TestResults = new TestResults(m_result);

                                    using(var testdb = new LocalTestDatabase(m_database))
                                    using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, testdb))
                                        new TestHandler(m_backendurl, m_options, new TestResults(m_result))
                                            .DoRun(m_options.BackupTestSampleCount, testdb, backend);
                                }
                            }

                        }
                        
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Complete);                        
                        return;
                    }
                }
                catch (Exception ex)
                {
                    m_result.AddError("Fatal error", ex);
                    throw;
                }
                finally
                {
                    if (parallelScanner != null && parallelScanner.IsAlive)
                    {
                        parallelScanner.Abort();
                        parallelScanner.Join(500);
                        if (parallelScanner.IsAlive)
                            m_result.AddWarning("Failed to terminate filecounter thread", null);
                    }
                
                    if (m_snapshot != null)
                        try { m_snapshot.Dispose(); }
                        catch (Exception ex) { m_result.AddError(string.Format("Failed to dispose snapshot"), ex); }
                        finally { m_snapshot = null; }
                
                    if (m_transaction != null)
                        try { m_transaction.Rollback(); }
                        catch (Exception ex) { m_result.AddError(string.Format("Rollback error: {0}", ex.Message), ex); }
                } 
            }
        }
        
        private bool HandleFilesystemEntry(string path, System.IO.FileAttributes attributes)
        {
            try
            {
                m_result.OperationProgressUpdater.StartFile(path, -1);            
                
                if (m_backendLogFlushTimer < DateTime.Now)
                {
                    m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);
                    m_backend.FlushDbMessages(m_database, null);
                }
                                            
                if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Ignore)
                    {
                        m_result.AddVerboseMessage("Ignoring symlink {0}", path);
                        return false;
                    }
    
                    if (m_options.SymlinkPolicy == Options.SymlinkStrategy.Store)
                    {
                        Dictionary<string, string> metadata;
    
                        if (m_options.StoreMetadata)
                        {
                            metadata = m_snapshot.GetMetadata(path);
                            if (metadata == null)
                                metadata = new Dictionary<string, string>();
    
                            if (!metadata.ContainsKey("CoreAttributes"))
                                metadata["CoreAttributes"] = attributes.ToString();
                            if (!metadata.ContainsKey("CoreLastWritetime"))
                                metadata["CoreLastWritetime"] = m_snapshot.GetLastWriteTimeUtc(path).Ticks.ToString();
                        }
                        else
                        {
                            metadata = new Dictionary<string, string>();
                        }
    
                        if (!metadata.ContainsKey("CoreSymlinkTarget"))
                            metadata["CoreSymlinkTarget"] = m_snapshot.GetSymlinkTarget(path);
    
                        var metahash = Utility.WrapMetadata(metadata, m_options);
                        AddSymlinkToOutput(path, DateTime.UtcNow, metahash);
                        
                        m_result.AddVerboseMessage("Stored symlink {0}", path);
                        //Do not recurse symlinks
                        return false;
                    }
                }
    
                if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    IMetahash metahash;
    
                    if (m_options.StoreMetadata)
                    {
                        Dictionary<string, string> metadata = m_snapshot.GetMetadata(path);
                        if (metadata == null)
                            metadata = new Dictionary<string, string>();
    
                        if (!metadata.ContainsKey("CoreAttributes"))
                            metadata["CoreAttributes"] = attributes.ToString();
                        if (!metadata.ContainsKey("CoreLastWritetime"))
                            metadata["CoreLastWritetime"] = m_snapshot.GetLastWriteTimeUtc(path).Ticks.ToString();
                        metahash = Utility.WrapMetadata(metadata, m_options);
                    }
                    else
                    {
                        metahash = EMPTY_METADATA;
                    }
    
                    m_result.AddVerboseMessage("Adding directory {0}", path);
                    AddFolderToOutput(path, DateTime.UtcNow, metahash);
                    return true;
                }
    
                m_result.OperationProgressUpdater.UpdatefilesProcessed(++m_result.ExaminedFiles, m_result.SizeOfExaminedFiles);
                
                bool changed = false;
                
                // The time we scan
                DateTime scantime = DateTime.UtcNow;
                // Last scan time
                DateTime oldScanned;
                // Last file modification
                DateTime lastModified = m_snapshot.GetLastWriteTimeUtc(path);
                var oldId = m_database.GetFileEntry(path, out oldScanned);

                long filestatsize = m_snapshot.GetFileSize(path);
                if ((oldId < 0 || m_options.DisableFiletimeCheck || LocalDatabase.NormalizeDateTime(lastModified) >= oldScanned) && (m_options.SkipFilesLargerThan == long.MaxValue || m_options.SkipFilesLargerThan == 0 || filestatsize < m_options.SkipFilesLargerThan))
                {
                    m_result.AddVerboseMessage("Checking file for changes {0}", path);
                    m_result.OpenedFiles++;
                    
                    long filesize = 0;
                    IMetahash metahashandsize;
                    if (m_options.StoreMetadata)
                    {
                        Dictionary<string, string> metadata = m_snapshot.GetMetadata(path);
                        if (metadata == null)
                            metadata = new Dictionary<string, string>();

                        if (!metadata.ContainsKey("CoreAttributes"))
                            metadata["CoreAttributes"] = attributes.ToString();
                        if (!metadata.ContainsKey("CoreLastWritetime"))
                            metadata["CoreLastWritetime"] = lastModified.Ticks.ToString();

                        metahashandsize = Utility.WrapMetadata(metadata, m_options);
                    }
                    else
                    {
                        metahashandsize = EMPTY_METADATA;
                    }

                    var hint = m_options.GetCompressionHintFromFilename(path);
                    var oldHash = oldId < 0 ? null : m_database.GetFileHash(oldId);

                    using (var blocklisthashes = new Library.Utility.FileBackedStringList())
                    using (var hashcollector = new Library.Utility.FileBackedStringList())
                    {
                        using (var fs = new Blockprocessor(m_snapshot.OpenRead(path), m_blockbuffer))
                        {
                            try { m_result.OperationProgressUpdater.StartFile(path, fs.Length); }
                            catch (Exception ex) { m_result.AddWarning(string.Format("Failed to read file length for file {0}", path), ex); }
                            
                            int blocklistoffset = 0;

                            m_filehasher.Initialize();

                            
                            var offset = 0;
                            var remaining = fs.Readblock();
                            
                            do
                            {
                                var size = Math.Min(m_blocksize, remaining);

                                m_filehasher.TransformBlock(m_blockbuffer, offset, size, m_blockbuffer, offset);
                                var blockkey = m_blockhasher.ComputeHash(m_blockbuffer, offset, size);
                                if (m_blocklistbuffer.Length - blocklistoffset < blockkey.Length)
                                {
                                    var blkey = Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0, blocklistoffset));
                                    blocklisthashes.Add(blkey);
                                    AddBlockToOutput(blkey, m_blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                                    blocklistoffset = 0;
                                }

                                Array.Copy(blockkey, 0, m_blocklistbuffer, blocklistoffset, blockkey.Length);
                                blocklistoffset += blockkey.Length;

                                var key = Convert.ToBase64String(blockkey);
                                AddBlockToOutput(key, m_blockbuffer, offset, size, hint, false);
                                hashcollector.Add(key);
                                filesize += size;

                                m_result.OperationProgressUpdater.UpdateFileProgress(filesize);
                                remaining -= size;
                                offset += size;
                                
                                if (remaining == 0)
                                {
                                    offset = 0;
                                    remaining = fs.Readblock();
                                }

                            } while (remaining > 0);

                            //If all fits in a single block, don't bother with blocklists
                            if (hashcollector.Count > 1)
                            {
                                var blkeyfinal = Convert.ToBase64String(m_blockhasher.ComputeHash(m_blocklistbuffer, 0, blocklistoffset));
                                blocklisthashes.Add(blkeyfinal);
                                AddBlockToOutput(blkeyfinal, m_blocklistbuffer, 0, blocklistoffset, CompressionHint.Noncompressible, true);
                            }
                        }

                        m_result.SizeOfOpenedFiles += filesize;
                        m_filehasher.TransformFinalBlock(m_blockbuffer, 0, 0);

                        var filekey = Convert.ToBase64String(m_filehasher.Hash);
                        if (oldHash != filekey)
                        {
                            if (oldHash == null)
                                m_result.AddVerboseMessage("New file {0}", path);
                            else
                                m_result.AddVerboseMessage("File has changed {0}", path);
                            if (oldId < 0)
                            {
                                m_result.AddedFiles++;
                                m_result.SizeOfAddedFiles += filesize;
					            
					            if (m_options.Dryrun)
					            	m_result.AddDryrunMessage(string.Format("Would add new file {0}, size {1}", path, Library.Utility.Utility.FormatSizeString(filesize)));
                            }
                            else
                            {
                                m_result.ModifiedFiles++;
                                m_result.SizeOfModifiedFiles += filesize;
					            
					            if (m_options.Dryrun)
					            	m_result.AddDryrunMessage(string.Format("Would add changed file {0}, size {1}", path, Library.Utility.Utility.FormatSizeString(filesize)));
                            }

                            AddFileToOutput(path, filesize, scantime, metahashandsize, hashcollector, filekey, blocklisthashes);
                            changed = true;
                        }
                        else
                        {
                            // When we write the file to output, update the scan time
                            oldScanned = scantime;
                            m_result.AddVerboseMessage("File has not changed {0}", path);
                        }
                    }
                }
                else
                {
                    if (m_options.SkipFilesLargerThan == long.MaxValue || m_options.SkipFilesLargerThan == 0 || m_snapshot.GetFileSize(path) < m_options.SkipFilesLargerThan)                
                        m_result.AddVerboseMessage("Skipped checking file, because timestamp was not updated {0}", path);
                    else
                        m_result.AddVerboseMessage("Skipped checking file, because the size exceeds limit {0}", path);
                }

                if (!changed)
                    AddUnmodifiedFile(oldId, oldScanned);

                m_result.SizeOfExaminedFiles += filestatsize;
                if (filestatsize != 0)
                    m_result.OperationProgressUpdater.UpdatefilesProcessed(m_result.ExaminedFiles, m_result.SizeOfExaminedFiles);
            }
            catch (Exception ex)
            {
                m_result.AddWarning(string.Format("Failed to process path: {0}", path), ex);
                m_result.FilesWithError++;
            }

            return true;
        }

        /// <summary>
        /// Adds the found file data to the output unless the block already exists
        /// </summary>
        /// <param name="key">The block hash</param>
        /// <param name="data">The data matching the hash</param>
        /// <param name="len">The size of the data</param>
        /// <param name="offset">The offset into the data</param>
        /// <param name="hint">Hint for compression module</param>
        /// <param name="isBlocklistData">Indicates if the block is list data</param>
        private bool AddBlockToOutput(string key, byte[] data, int offset, int len, CompressionHint hint, bool isBlocklistData)
        {
            if (m_database.AddBlock(key, len, m_blockvolume.VolumeID, m_transaction))
            {
                m_blockvolume.AddBlock(key, data, offset, len, hint);
                
                //TODO: In theory a normal data block and blocklist block could be equal.
                // this would cause the index file to not contain all data,
                // if the data file is added before the blocklist data
                // ... highly theoretical ...
                if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full && isBlocklistData)
                    m_indexvolume.WriteBlocklist(key, data, offset, len);
                    
                if (m_blockvolume.Filesize > m_options.VolumeSize - m_options.Blocksize)
                {
                	if (m_options.Dryrun)
                	{
                        m_blockvolume.Close();
                		m_result.AddDryrunMessage(string.Format("Would upload block volume: {0}, size: {1}", m_blockvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_blockvolume.LocalFilename).Length)));
                		
                		if (m_indexvolume != null)
                		{
		            		UpdateIndexVolume();
                			m_indexvolume.FinishVolume(Library.Utility.Utility.CalculateHash(m_blockvolume.LocalFilename), new FileInfo(m_blockvolume.LocalFilename).Length);
                			m_result.AddDryrunMessage(string.Format("Would upload index volume: {0}, size: {1}", m_indexvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(m_indexvolume.LocalFilename).Length)));
                			m_indexvolume.Dispose();
                			m_indexvolume = null;
                		}
                        
                        m_blockvolume.Dispose();
                        m_blockvolume = null;
                        m_indexvolume.Dispose();
                        m_indexvolume = null;
                	}
                	else
                	{
	                	//When uploading a new volume, we register the volumes and then flush the transaction
	                	// this ensures that the local database and remote storage are as closely related as possible
                		m_database.UpdateRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
                        m_blockvolume.Close();
	            		UpdateIndexVolume();
	                	
	                	m_backend.FlushDbMessages(m_database, m_transaction);
        				m_backendLogFlushTimer = DateTime.Now.Add(FLUSH_TIMESPAN);

                        using(new Logging.Timer("CommitAddBlockToOutputFlush"))
	                    	m_transaction.Commit();
	                	m_transaction = m_database.BeginTransaction();
	                	
	                    m_backend.Put(m_blockvolume, m_indexvolume);
	                    m_blockvolume = null;
	                    m_indexvolume = null;
	                }
                    
                    m_blockvolume = new BlockVolumeWriter(m_options);
					m_blockvolume.VolumeID = m_database.RegisterRemoteVolume(m_blockvolume.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary, m_transaction);
					
					if (m_options.IndexfilePolicy != Options.IndexFileStrategy.None)
					{
	                    m_indexvolume = new IndexVolumeWriter(m_options);
						m_indexvolume.VolumeID = m_database.RegisterRemoteVolume(m_indexvolume.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary, m_transaction);
					}
                }

                return true;
            }

            return false;
        }

        private void AddUnmodifiedFile(long oldId, DateTime scantime)
        {
            m_database.AddUnmodifiedFile(oldId, scantime, m_transaction);
        }


        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private bool AddFolderToOutput(string filename, DateTime scantime, IMetahash meta)
        {
            long metadataid;
            bool r = false;

            //TODO: If meta.Size > blocksize...
            r |= AddBlockToOutput(meta.Hash, meta.Blob, 0, (int)meta.Size, CompressionHint.Default, false);
            r |= m_database.AddMetadataset(meta.Hash, meta.Size, out metadataid, m_transaction);

            m_database.AddDirectoryEntry(filename, metadataid, scantime, m_transaction);
            return r;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private bool AddSymlinkToOutput(string filename, DateTime scantime, IMetahash meta)
        {
            long metadataid;
            bool r = false;

            //TODO: If meta.Size > blocksize...
            r |= AddBlockToOutput(meta.Hash, meta.Blob, 0, (int)meta.Size, CompressionHint.Default, false);
            r |= m_database.AddMetadataset(meta.Hash, meta.Size, out metadataid, m_transaction);

            m_database.AddSymlinkEntry(filename, metadataid, scantime, m_transaction);
            return r;
        }

        /// <summary>
        /// Adds a file to the output, 
        /// </summary>
        /// <param name="filename">The name of the file to record</param>
        /// <param name="lastModified">The value of the lastModified timestamp</param>
        /// <param name="hashlist">The list of hashes that make up the file</param>
        /// <param name="size">The size of the file</param>
        /// <param name="fragmentoffset">The offset into a fragment block where the last few bytes are stored</param>
        /// <param name="metadata">A lookup table with various metadata values describing the file</param>
        private void AddFileToOutput(string filename, long size, DateTime scantime, IMetahash metadata, IEnumerable<string> hashlist, string filehash, IEnumerable<string> blocklisthashes)
        {
            long metadataid;
            long blocksetid;
            
            //TODO: If metadata.Size > blocksize...
            AddBlockToOutput(metadata.Hash, metadata.Blob, 0, (int)metadata.Size, CompressionHint.Default, false);
            m_database.AddMetadataset(metadata.Hash, metadata.Size, out metadataid, m_transaction);

            m_database.AddBlockset(filehash, size, m_blocksize, hashlist, blocklisthashes, out blocksetid, m_transaction);

            //m_filesetvolume.AddFile(filename, filehash, size, scantime, metadata.Hash, metadata.Size, blocklisthashes);
            m_database.AddFile(filename, scantime, blocksetid, metadataid, m_transaction);
        }


        private void UpdateIndexVolume()
        {
        	if (m_indexvolume != null)
        	{
	            m_database.AddIndexBlockLink(m_indexvolume.VolumeID, m_blockvolume.VolumeID, m_transaction);
	            m_indexvolume.StartVolume(m_blockvolume.RemoteFilename);
	            
	            foreach(var b in m_database.GetBlocks(m_blockvolume.VolumeID))
	            	m_indexvolume.AddBlock(b.Hash, b.Size);

    			m_database.UpdateRemoteVolume(m_indexvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);
        	}
        }

        public void Dispose()
        {
            if (m_blockvolume != null)
            {
                try { m_blockvolume.Dispose(); }
                catch (Exception ex) { m_result.AddError("Failed disposing block volume", ex); }
                finally { m_blockvolume = null; }
            }

            if (m_indexvolume != null)
            {
                try { m_indexvolume.Dispose(); }
                catch (Exception ex) { m_result.AddError("Failed disposing index volume", ex); }
                finally { m_indexvolume = null; }
            }

            m_result.EndTime = DateTime.Now;
        }
    }
}
