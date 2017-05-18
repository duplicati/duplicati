﻿using System;
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
    /// <summary>
    /// The backup handler is the primary function,
    /// which performs a backup of the given sources
    /// to the chosen destination
    /// </summary>
    internal class BackupHandler : IDisposable
    {    
        private readonly Options m_options;
        private string m_backendurl;


        private LocalBackupDatabase m_database;
        private System.Data.IDbTransaction m_transaction;
       
        private Library.Utility.IFilter m_filter;
        private Library.Utility.IFilter m_sourceFilter;

        private BackupResults m_result;

        public BackupHandler(string backendurl, Options options, BackupResults results)
        {
            m_options = options;
            m_result = results;
            m_backendurl = backendurl;
                            
            if (options.AllowPassphraseChange)
                throw new UserInformationException(Strings.Common.PassphraseChangeUnsupported);
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
                    log.AddWarning(Strings.Common.SnapshotFailedError(ex.ToString()), ex);
            }

            return Library.Utility.Utility.IsClientLinux ?
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotLinux(sources, options.RawOptions)
                    :
                (Library.Snapshots.ISnapshotService)new Duplicati.Library.Snapshots.NoSnapshotWindows(sources, options.RawOptions);   
        }

        private void PreBackupVerify(BackendManager backend)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreBackupVerify);
            using (new Logging.Timer("PreBackupVerify"))
            {
                try
                {
                    if (m_options.NoBackendverification)
                    {
                        FilelistProcessor.VerifyLocalList(backend, m_options, m_database, m_result.BackendWriter);
                        UpdateStorageStatsFromDatabase();
                    }
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


        private void PreBackupVerify(BackendManager backend, string protectedfile)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_PreBackupVerify);
            using(new Logging.Timer("PreBackupVerify"))
            {
                try
                {
					if (m_options.NoBackendverification)
					{
						FilelistProcessor.VerifyLocalList(backend, m_options, m_database, m_result.BackendWriter);
						UpdateStorageStatsFromDatabase();
					}
					else
                        FilelistProcessor.VerifyRemoteList(backend, m_options, m_database, m_result.BackendWriter, protectedfile);
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

        /// <summary>
        /// Performs the bulk of work by starting all relevant processes
        /// </summary>
        private static async Task RunMainOperation(Snapshots.ISnapshotService snapshot, Backup.BackupDatabase database, Backup.BackupStatsCollector stats, Options options, IFilter sourcefilter, IFilter filter, BackupResults result, Common.ITaskReader taskreader)
        {
            using(new Logging.Timer("BackupMainOperation"))
            {
                // Make sure the CompressionHints table is initialized, otherwise all workers will initialize it
                var tb = options.CompressionHints.Count;

                Task all;
                using(new ChannelScope())
                {
                    all = Task.WhenAll(
                        new [] 
                        {
                            Backup.DataBlockProcessor.Run(database, options, taskreader),
                            Backup.FileBlockProcessor.Run(snapshot, options, database, stats, taskreader),
                            Backup.StreamBlockSplitter.Run(options, database, taskreader),
                            Backup.FileEnumerationProcess.Run(snapshot, options.FileAttributeFilter, sourcefilter, filter, options.SymlinkPolicy, options.HardlinkPolicy, options.ChangedFilelist, taskreader),
                            Backup.FilePreFilterProcess.Run(snapshot, options, stats, database),
                            Backup.MetadataPreProcess.Run(snapshot, options, database),
                            Backup.SpillCollectorProcess.Run(options, database, taskreader),
                            Backup.ProgressHandler.Run(result)
                        }
                        // Spawn additional block hashers
                        .Union(
                            Enumerable.Range(0, options.ConcurrencyBlockHashers - 1).Select(x => Backup.StreamBlockSplitter.Run(options, database, taskreader))
                        )
                        // Spawn additional compressors
                        .Union(
                            Enumerable.Range(0, options.ConcurrencyCompressors - 1).Select(x => Backup.DataBlockProcessor.Run(database, options, taskreader))
                        )
                    );
                }

                await all;

                if (options.ChangedFilelist != null && options.ChangedFilelist.Length >= 1)
                    await database.AppendFilesFromPreviousSetAsync(options.DeletedFilelist);
                
                result.OperationProgressUpdater.UpdatefileCount(result.ExaminedFiles, result.SizeOfExaminedFiles, true);
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
                    new TestHandler(m_backendurl, m_options, (TestResults)m_result.TestResults)
                        .DoRun(m_options.BackupTestSampleCount, testdb, backend);
            }
        }

		/// <summary>
		/// Handler for computing backend statistics, without relying on a remote folder listing
		/// </summary>
		private void UpdateStorageStatsFromDatabase()
		{
			if (m_result.BackendWriter != null)
			{
				m_result.BackendWriter.KnownFileCount = m_database.GetRemoteVolumes().Count();
				m_result.BackendWriter.KnownFileSize = m_database.GetRemoteVolumes().Select(x => Math.Max(0, x.Size)).Sum();

				m_result.BackendWriter.UnknownFileCount = 0;
				m_result.BackendWriter.UnknownFileSize = 0;

				m_result.BackendWriter.BackupListCount = m_database.FilesetTimes.Count();
				m_result.BackendWriter.LastBackupDate = m_database.FilesetTimes.FirstOrDefault().Value.ToLocalTime();

				// TODO: If we have a BackendManager, we should query through that
				using (var backend = DynamicLoader.BackendLoader.GetBackend(m_backendurl, m_options.RawOptions))
					if (backend is Library.Interface.IQuotaEnabledBackend)
					{
						m_result.BackendWriter.TotalQuotaSpace = ((Library.Interface.IQuotaEnabledBackend)backend).TotalQuotaSpace;
						m_result.BackendWriter.FreeQuotaSpace = ((Library.Interface.IQuotaEnabledBackend)backend).FreeQuotaSpace;
				}
			}

			m_result.BackendWriter.AssignedQuotaSpace = m_options.QuotaSize;
		}

        public void Run(string[] sources, Library.Utility.IFilter filter)
        {
            RunAsync(sources, filter).WaitForTaskOrThrow();
        }

        private static Exception BuildException(Exception source, params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
                return source;

            var ex = new List<Exception>();
            ex.Add(source);

            foreach(var t in tasks)
                if (t != null)
                {
                    if (!t.IsCompleted && !t.IsFaulted && !t.IsCanceled)
                        t.Wait(500);

                    if (t.IsFaulted && t.Exception != null)
                        ex.Add(t.Exception);
                }

            if (ex.Count == 1)
                return ex.First();
            else
                return new AggregateException(ex.First().Message, ex);
        }

        private static async Task<long> FlushBackend(BackupResults result, IWriteChannel<Backup.IUploadRequest> uploadtarget, Task uploader)
        {
            var flushReq = new Backup.FlushRequest();

            // Wait for upload completion
            result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
            await uploadtarget.WriteAsync(flushReq).ConfigureAwait(false);

            // In case the uploader crashes, we grab the exception here
            if (await Task.WhenAny(uploader, flushReq.LastWriteSizeAync) == uploader)
                await uploader;

            // Grab the size of the last uploaded volume
            return await flushReq.LastWriteSizeAync;
        }

        private async Task RunAsync(string[] sources, Library.Utility.IFilter filter)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_Begin);                        
            
            // New isolated scope for each operation
            using(new IsolatedChannelScope())
            using(m_database = new LocalBackupDatabase(m_options.Dbpath, m_options))
            {
                // Start the log handler
                var lh = Common.LogHandler.Run(m_result);

                m_result.SetDatabase(m_database);
                m_result.Dryrun = m_options.Dryrun;

                // Check the database integrity
                Utility.UpdateOptionsFromDb(m_database, m_options);
                Utility.VerifyParameters(m_database, m_options);

                if (m_database.PartiallyRecreated)
                    throw new Exception("The database was only partially recreated. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.");
                
                if (m_database.RepairInProgress)
                    throw new Exception("The database was attempted repaired, but the repair did not complete. This database may be incomplete and the backup process cannot continue. You may delete the local database and attempt to repair it again.");

                // If there is no filter, we set an empty filter to simplify the code
                // If there is a filter, we make sure that the sources are included
                m_filter = filter ?? new Library.Utility.FilterExpression();
                m_sourceFilter = new Library.Utility.FilterExpression(sources, true);

                Task parallelScanner = null;
                Task uploader = null;
                try
                {
                    // Setup runners and instances here
                    using(var db = new Backup.BackupDatabase(m_database, m_options))
                    using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, m_database))
                    using(var filesetvolume = new FilesetVolumeWriter(m_options, m_database.OperationTimestamp))
                    using(var stats = new Backup.BackupStatsCollector(m_result))
                    using(var bk = new Common.BackendHandler(m_options, m_backendurl, db, stats, m_result.TaskReader))
                    // Keep a reference to these channels to avoid shutdown
                    using(var logtarget = ChannelManager.GetChannel(Common.Channels.LogChannel.ForWrite))
                    using(var uploadtarget = ChannelManager.GetChannel(Backup.Channels.BackendRequest.ForWrite))
                    {
                        long filesetid;
                        var counterToken = new CancellationTokenSource();
                        using(var snapshot = GetSnapshot(sources, m_options, m_result))
                        {
                            try
                            {
                                // Start the parallel scanner
                                parallelScanner = Backup.CountFilesHandler.Run(snapshot, m_result, m_options, m_sourceFilter, m_filter, m_result.TaskReader, counterToken.Token);

                                // Make sure the database is sane
                                await db.VerifyConsistencyAsync(m_options.Blocksize, m_options.BlockhashSize, true);

                                // Start the uploader process
                                uploader = Backup.BackendUploader.Run(bk, m_options, db, m_result, m_result.TaskReader);

                                // TODO: Rewrite to using the uploader process, or the BackendHandler interface
                                // Do a remote verification, unless disabled
                                PreBackupVerify(backend);

                                // If the previous backup was interrupted, send a synthetic list
                                await Backup.UploadSyntheticFilelist.Run(db, m_options, m_result, m_result.TaskReader);

                                // This should be removed as the lookups are no longer used
                                m_database.BuildLookupTable(m_options);

                                // Prepare the operation by registering the filelist
                                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_ProcessingFiles);

                                var repcnt = 0;
                                while(repcnt < 100 && await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                    filesetvolume.ResetRemoteFilename(m_options, m_database.OperationTimestamp.AddSeconds(repcnt++));

                                if (await db.GetRemoteVolumeIDAsync(filesetvolume.RemoteFilename) >= 0)
                                    throw new Exception("Unable to generate a unique fileset name");

                                var filesetvolumeid = await db.RegisterRemoteVolumeAsync(filesetvolume.RemoteFilename, RemoteVolumeType.Files, RemoteVolumeState.Temporary);
                                filesetid = await db.CreateFilesetAsync(filesetvolumeid, VolumeBase.ParseFilename(filesetvolume.RemoteFilename).Time);

                                // Run the backup operation
                                if (await m_result.TaskReader.ProgressAsync)
                                    await RunMainOperation(snapshot, db, stats, m_options, m_sourceFilter, m_filter, m_result, m_result.TaskReader);
                            }
                            finally
                            {
                                //If the scanner is still running for some reason, make sure we kill it now 
                                counterToken.Cancel();
                            }
                        }

                        // Ensure the database is in a sane state after adding data
                        using(new Logging.Timer("VerifyConsistency"))
                            await db.VerifyConsistencyAsync(m_options.Blocksize, m_options.BlockhashSize, false);

                        // Send the actual filelist
                        if (await m_result.TaskReader.ProgressAsync)
                            await Backup.UploadRealFilelist.Run(m_result, db, m_options, filesetvolume, filesetid, m_result.TaskReader);

                        // Wait for upload completion
                        m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Backup_WaitForUpload);
                        var lastVolumeSize = await FlushBackend(m_result, uploadtarget, uploader);

                        // Make sure we have the database up-to-date
                        await db.CommitTransactionAsync("CommitAfterUpload", false);

                        // TODO: Remove this later
                        m_transaction = m_database.BeginTransaction();
    		                                        
                        if (await m_result.TaskReader.ProgressAsync)
                            CompactIfRequired(backend, lastVolumeSize);

                        if (m_options.UploadVerificationFile && await m_result.TaskReader.ProgressAsync)
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

							if (m_result.TaskControlRendevouz() != TaskControlState.Stop)
							{
								if (m_options.NoBackendverification)
									UpdateStorageStatsFromDatabase();
								else
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
                    var aex = BuildException(ex, uploader, parallelScanner);
                    m_result.AddError("Fatal error", aex);
                    if (aex == ex)
                        throw;
                    
                    throw aex;
                }
                finally
                {
                    if (parallelScanner != null && !parallelScanner.IsCompleted)
                        parallelScanner.Wait(500);

                    // TODO: We want to commit? always?
                    if (m_transaction != null)
                        try { m_transaction.Rollback(); }
                        catch (Exception ex) { m_result.AddError(string.Format("Rollback error: {0}", ex.Message), ex); }

                    lh.Wait(500);
                }
            }
        }

        public void Dispose()
        {
            m_result.EndTime = DateTime.UtcNow;
        }
    }
}
