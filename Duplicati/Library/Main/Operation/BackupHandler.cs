using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Interface;
using System.Threading.Tasks;
using CoCoL;
using System.Threading;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    internal class BackupHandler : IDisposable
    {    
        private readonly Options m_options;
        private string m_backendurl;

        private byte[] m_blockbuffer;
        private byte[] m_blocklistbuffer;
        private System.Security.Cryptography.HashAlgorithm m_blockhasher;
        private System.Security.Cryptography.HashAlgorithm m_filehasher;

        private LocalBackupDatabase m_database;
        private System.Data.IDbTransaction m_transaction;
        private BlockVolumeWriter m_blockvolume;
        private IndexVolumeWriter m_indexvolume;

        private readonly IMetahash EMPTY_METADATA;
        
        private Library.Utility.IFilter m_filter;
        private Library.Utility.IFilter m_sourceFilter;

        private BackupResults m_result;

        // Speed up things by caching this
        private int m_blocksize;

        public BackupHandler(string backendurl, Options options, BackupResults results)
        {
        	EMPTY_METADATA = Utility.WrapMetadata(new Dictionary<string, string>(), options);
        	
            m_options = options;
            m_result = results;
            m_backendurl = backendurl;
                            
            if (options.AllowPassphraseChange)
                throw new Exception(Strings.Foresthash.PassphraseChangeUnsupported);
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
                    log.AddWarning(Strings.RSyncDir.SnapshotFailedError(ex.ToString()), ex);
            }

            return Library.Utility.Utility.IsClientLinux ?
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotLinux(sources, options.RawOptions)
                    :
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotWindows(sources, options.RawOptions);
        }

        private static Task CountFilesHandler(Snapshots.ISnapshotService snapshot, BackupResults result, Options options, IFilter sourcefilter, IFilter filter, CancellationToken token = default(CancellationToken))
        {
            using(new ChannelScope())
            {
                var enumeratorTask = new Backup.FileEnumerationProcess(snapshot, options.FileAttributeFilter, sourcefilter, filter, options.SymlinkPolicy, options.HardlinkPolicy, options.ChangedFilelist).RunAsync();

                var counterTask = AutomationExtensions.RunTask(new 
                    {
                        Input = ChannelMarker.ForRead<string>("SourcePaths")
                    },
                    async self =>
                    {
                        var count = 0L;
                        var size = 0L;

                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                var path = await self.Input.ReadAsync();

                                count++;

                                try
                                {
                                    size += snapshot.GetFileSize(path);
                                }
                                catch
                                {
                                }

                                result.OperationProgressUpdater.UpdatefileCount(count, size, false);                    
                            }
                        }
                        finally
                        {
                            result.OperationProgressUpdater.UpdatefileCount(count, size, true);
                        }
                    }
                );

                return Task.WhenAll(enumeratorTask, counterTask);
            }
        }

        private void PreBackupVerify(BackendManager backend)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreBackupVerify);
            using(new Logging.Timer("PreBackupVerify"))
            {
                try
                {
                    if (m_options.NoBackendverification) 
                        FilelistProcessor.VerifyLocalList(backend, m_options, m_database, m_result.BackendWriter);
                    else
                        FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter);
                }
                catch (Exception ex)
                {
                    if (m_options.AutoCleanup)
                    {
                        m_result.AddWarning("Backend verification failed, attempting automatic cleanup", ex);
                        m_result.RepairResults = new RepairResults(m_result);
                        new RepairHandler(backend.BackendUrl, m_options, (RepairResults)m_result.RepairResults).Run();

                        m_result.AddMessage("Backend cleanup finished, retrying verification");
                        FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter);
                    }
                    else
                        throw;
                }
            }
        }

        private static async Task RunMainOperation(Snapshots.ISnapshotService snapshot, Backup.BackupDatabase database, Common.BackendHandler backend, Backup.BackupStatsCollector stats, Options options, IFilter sourcefilter, IFilter filter, BackupResults result)
        {
            using(new Logging.Timer("BackupMainOperation"))
            {
                Task all;
                using(new ChannelScope())
                {
                    all = Task.WhenAll(
                        Backup.DataBlockProcessor.Run(database, options),
                        Backup.FileBlockProcessor.Start(snapshot, options, database, stats),
                        new Backup.FileEnumerationProcess(snapshot, options.FileAttributeFilter, sourcefilter, filter, options.SymlinkPolicy, options.HardlinkPolicy, options.ChangedFilelist).RunAsync(),
                        Backup.FilePreFilterProcess.Start(snapshot, options, stats),
                        new Backup.MetadataPreProcess(snapshot, options, database).RunAsync(),
                        Backup.SpillCollectorProcess.Run(options, database),
                        Backup.ProgressHandler.Run(result),
                        Backup.BackendUploader.Run(backend, options, database, result)
                    );
                }

                await all;

                if (options.ChangedFilelist != null && options.ChangedFilelist.Length >= 1)
                    await database.AppendFilesFromPreviousSetAsync(options.DeletedFilelist);
                
                result.OperationProgressUpdater.UpdatefileCount(result.ExaminedFiles, result.SizeOfExaminedFiles, true);
            }
        }

        private void UploadRealFileList(BackendManager backend, FilesetVolumeWriter filesetvolume)
        {
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
                            filesetvolume.AddControlFile(p, m_options.GetCompressionHintFromFilename(p));

                    m_database.WriteFileset(filesetvolume, m_transaction);
                    filesetvolume.Close();

                    if (m_options.Dryrun)
                        m_result.AddDryrunMessage(string.Format("Would upload fileset volume: {0}, size: {1}", filesetvolume.RemoteFilename, Library.Utility.Utility.FormatSizeString(new FileInfo(filesetvolume.LocalFilename).Length)));
                    else
                    {
                        m_database.UpdateRemoteVolume(filesetvolume.RemoteFilename, RemoteVolumeState.Uploading, -1, null, m_transaction);

                        using(new Logging.Timer("CommitUpdateRemoteVolume"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();

                        backend.Put(filesetvolume);

                        using(new Logging.Timer("CommitUpdateRemoteVolume"))
                            m_transaction.Commit();
                        m_transaction = m_database.BeginTransaction();

                    }
                }
            }
            else
            {
                m_result.AddVerboseMessage("removing temp files, as no data needs to be uploaded");
                m_database.RemoveRemoteVolume(filesetvolume.RemoteFilename, m_transaction);
            }
        }

        private void CompactIfRequired(BackendManager backend, long lastVolumeSize)
        {
            var currentIsSmall = lastVolumeSize != -1 && lastVolumeSize <= m_options.SmallFileSize;

            if (m_options.KeepTime.Ticks > 0 || m_options.KeepVersions != 0)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Delete);
                m_result.DeleteResults = new DeleteResults(m_result);
                using(var db = new LocalDeleteDatabase(m_database))
                    new DeleteHandler(backend.BackendUrl, m_options, (DeleteResults)m_result.DeleteResults).DoRun(db, ref m_transaction, true, currentIsSmall);

            }
            else if (currentIsSmall && !m_options.NoAutoCompact)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Compact);
                m_result.CompactResults = new CompactResults(m_result);
                using(var db = new LocalDeleteDatabase(m_database))
                    new CompactHandler(backend.BackendUrl, m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, ref m_transaction);
            }
        }

        private void PostBackupVerification()
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

        private static async Task<long> CreateFilesetAsync(Backup.BackupDatabase database, string filename)
        {
            var filesetvolumeid = await database.RegisterRemoteVolumeAsync(filename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
            await database.CreateFilesetAsync(filesetvolumeid, VolumeBase.ParseFilename(filename).Time);

            return filesetvolumeid;
        }

        public void Run(string[] sources, Library.Utility.IFilter filter)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Begin);                        
            
            // New isolated scope for each operation
            using(new ChannelScope(true))
            using(m_database = new LocalBackupDatabase(m_options.Dbpath, m_options))
            {
                m_result.SetDatabase(m_database);
                m_result.Dryrun = m_options.Dryrun;

                Utility.UpdateOptionsFromDb(m_database, m_options);
                Utility.VerifyParameters(m_database, m_options);

                m_blocksize = m_options.Blocksize;

                m_blockbuffer = new byte[m_options.Blocksize * Math.Max(1, m_options.FileReadBufferSize / m_options.Blocksize)];
                m_blocklistbuffer = new byte[m_options.Blocksize];

                m_blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
                m_filehasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.FileHashAlgorithm);

                if (m_blockhasher == null)
                    throw new Exception(Strings.Foresthash.InvalidHashAlgorithm(m_options.BlockHashAlgorithm));
                if (m_filehasher == null)
                    throw new Exception(Strings.Foresthash.InvalidHashAlgorithm(m_options.FileHashAlgorithm));

                if (!m_blockhasher.CanReuseTransform)
                    throw new Exception(Strings.Foresthash.InvalidCryptoSystem(m_options.BlockHashAlgorithm));
                if (!m_filehasher.CanReuseTransform)
                    throw new Exception(Strings.Foresthash.InvalidCryptoSystem(m_options.FileHashAlgorithm));

                m_database.VerifyConsistency(null, m_options.Blocksize, m_options.BlockhashSize);
                // If there is no filter, we set an empty filter to simplify the code
                // If there is a filter, we make sure that the sources are included
                m_filter = filter ?? new Library.Utility.FilterExpression();
                m_sourceFilter = new Library.Utility.FilterExpression(sources, true);
            	

                Task parallelScanner = null;
                try
                {
                    using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
                    using(var filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
                    using(var logtarget = ChannelManager.GetChannel<Common.LogMessage>("LogChannel").AsWriteOnly())
                    {
                        var lh = Common.LogHandler.Run(m_result);
                        long lastVolumeSize = -1L;

                        using(var snapshot = GetSnapshot(sources, m_options, m_result))
                        {
                            var counterToken = new CancellationTokenSource();

                            try
                            {
                                // Start the parallel scanner
                                parallelScanner = CountFilesHandler(snapshot, m_result, m_options, m_sourceFilter, m_filter, counterToken.Token);

                                // Do a remote verification, unless disabled
                                PreBackupVerify(backend);
                                            
                                using(var db = new Backup.BackupDatabase(m_database))
                                using(var stats = new Backup.BackupStatsCollector(m_result))
                                using(var bk = new Common.BackendHandler(m_options, m_backendurl, db, stats))
                                {
                                    // Verify before uploading a synthetic list
                                    m_database.VerifyConsistency(null, m_options.Blocksize, m_options.BlockhashSize);

                                    var res = Backup.UploadSyntheticFilelist.Run(bk, db, m_options, m_result).WaitForTask();
                                    if (res.IsFaulted)
                                    {
                                        if (res.Exception.Flatten().InnerExceptions.Count == 1)
                                            throw res.Exception.Flatten().InnerExceptions.First();
                                        else
                                            throw res.Exception;
                                    }

                                    m_database.BuildLookupTable(m_options);

                                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_ProcessingFiles);

                                    var filesetvolumeid = CreateFilesetAsync(db, filesetvolume.RemoteFilename).Result;

                                    res = RunMainOperation(snapshot, db, bk, stats, m_options, m_sourceFilter, m_filter, m_result).WaitForTask();
                                    if (res.IsFaulted)
                                    {
                                        if (res.Exception.Flatten().InnerExceptions.Count == 1)
                                            throw res.Exception.Flatten().InnerExceptions.First();
                                        else
                                            throw res.Exception;
                                    }
                                }
                            }
                            finally
                            {
                                //If the scanner is still running for some reason, make sure we kill it now 
                                counterToken.Cancel();
                            }
                        }

                        // TODO: Implement this
                        //var lastVolumeSize = FinalizeRemoteVolumes(backend);

                        m_transaction = m_database.BeginTransaction();
    		            
                        using(new Logging.Timer("UpdateChangeStatistics"))
                            m_database.UpdateChangeStatistics(m_result);
                        using(new Logging.Timer("VerifyConsistency"))
                            m_database.VerifyConsistency(m_transaction, m_options.Blocksize, m_options.BlockhashSize);
    
                        UploadRealFileList(backend, filesetvolume);
    									
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
                        using(new Logging.Timer("Async backend wait"))
                            backend.WaitForComplete(m_database, m_transaction);
                            
                        if (m_result.TaskControlRendevouz() != TaskControlState.Stop) 
                            CompactIfRequired(backend, lastVolumeSize);
    		            
                        if (m_options.UploadVerificationFile)
                        {
                            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_VerificationUpload);
                            FilelistProcessor.UploadVerificationFile(backend.BackendUrl, m_options, m_result.BackendWriter, m_database, m_transaction);
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
                            
                            if (m_result.TaskControlRendevouz() != TaskControlState.Stop && !m_options.NoBackendverification)
                            {
                                PostBackupVerification();
                            }

                        }
                        
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Complete);
                        m_database.WriteResults();                    
                        m_database.PurgeLogData(m_options.LogRetention);
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
                    if (parallelScanner != null && !parallelScanner.IsCompleted)
                        parallelScanner.Wait(500);

                    // TODO: We want to commit? always?
                    if (m_transaction != null)
                        try { m_transaction.Rollback(); }
                        catch (Exception ex) { m_result.AddError(string.Format("Rollback error: {0}", ex.Message), ex); }
                }
            }
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

            m_result.EndTime = DateTime.UtcNow;
        }
    }
}
