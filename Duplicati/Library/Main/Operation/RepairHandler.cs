using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation
{
    internal class RepairHandler
    {
        private string m_backendurl;
        private Options m_options;
        private RepairResults m_result;

        public RepairHandler(string backend, Options options, RepairResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
            
            if (options.AllowPassphraseChange)
                throw new Exception(Strings.Common.PassphraseChangeUnsupported);
        }
        
        public void Run(Library.Utility.IFilter filter = null)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
            {
                RunRepairLocal(filter);
                RunRepairCommon();
                return;
            }

            long knownRemotes = -1;
            try
            {        
                using(var db = new LocalRepairDatabase(m_options.Dbpath))
                    knownRemotes = db.GetRemoteVolumes().Count();
            }
            catch (Exception ex)
            {
                m_result.AddWarning(string.Format("Failed to read local db {0}, error: {1}", m_options.Dbpath, ex.Message), ex);
            }
            
            if (knownRemotes <= 0)
            {                
                if (m_options.Dryrun)
                {
                    m_result.AddDryrunMessage("Performing dryrun recreate");
                }
                else
                {
                    var baseName = System.IO.Path.ChangeExtension(m_options.Dbpath, "backup");
                    var i = 0;
                    while (System.IO.File.Exists(baseName) && i++ < 1000)
                        baseName = System.IO.Path.ChangeExtension(m_options.Dbpath, "backup-" + i.ToString());
                        
                    m_result.AddMessage(string.Format("Renaming existing db from {0} to {1}", m_options.Dbpath, baseName));
                    System.IO.File.Move(m_options.Dbpath, baseName);
                }
                
                RunRepairLocal(filter);
                RunRepairCommon();
            }
            else
            {
                RunRepairCommon();
                RunRepairRemote();
            }

        }
        
        public void RunRepairLocal(Library.Utility.IFilter filter = null)
        {
            m_result.RecreateDatabaseResults = new RecreateDatabaseResults(m_result);
            using(new Logging.Timer("Recreate database for repair"))
            using(var f = m_options.Dryrun ? new Library.Utility.TempFile() : null)
            {
                if (f != null && System.IO.File.Exists(f))
                    System.IO.File.Delete(f);
                
                var filelistfilter = RestoreHandler.FilterNumberedFilelist(m_options.Time, m_options.Version);

                new RecreateDatabaseHandler(m_backendurl, m_options, (RecreateDatabaseResults)m_result.RecreateDatabaseResults)
                    .Run(m_options.Dryrun ? (string)f : m_options.Dbpath, filter, filelistfilter);
            }
        }

        public void RunRepairRemote()
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));

            m_result.OperationProgressUpdater.UpdateProgress(0);

            using(var db = new LocalRepairDatabase(m_options.Dbpath))
            using(var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                m_result.SetDatabase(db);
                Utility.UpdateOptionsFromDb(db, m_options);
                Utility.VerifyParameters(db, m_options);

                var tp = FilelistProcessor.RemoteListAnalysis(backend, m_options, db, m_result.BackendWriter);
                var buffer = new byte[m_options.Blocksize];
                var blockhasher = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
                var hashsize = blockhasher.HashSize / 8;

                if (blockhasher == null)
                    throw new Exception(Strings.Common.InvalidHashAlgorithm(m_options.BlockHashAlgorithm));
                if (!blockhasher.CanReuseTransform)
                    throw new Exception(Strings.Common.InvalidCryptoSystem(m_options.BlockHashAlgorithm));
				
                var progress = 0;
                var targetProgess = tp.ExtraVolumes.Count() + tp.MissingVolumes.Count() + tp.VerificationRequiredVolumes.Count();

                if (m_options.Dryrun)
                {
                    if (tp.ParsedVolumes.Count() == 0 && tp.OtherVolumes.Count() > 0)
                    {
                        if (tp.BackupPrefixes.Length == 1)
                            throw new Exception(string.Format("Found no backup files with prefix {0}, but files with prefix {1}, did you forget to set the backup-prefix?", m_options.Prefix, tp.BackupPrefixes[0]));
                        else
                            throw new Exception(string.Format("Found no backup files with prefix {0}, but files with prefixes {1}, did you forget to set the backup-prefix?", m_options.Prefix, string.Join(", ", tp.BackupPrefixes)));
                    }
                    else if (tp.ParsedVolumes.Count() == 0 && tp.ExtraVolumes.Count() > 0)
                    {
                        throw new Exception(string.Format("No files were missing, but {0} remote files were, found, did you mean to run recreate-database?", tp.ExtraVolumes.Count()));
                    }
                }

                if (tp.ExtraVolumes.Count() > 0 || tp.MissingVolumes.Count() > 0 || tp.VerificationRequiredVolumes.Count() > 0)
                {
                    if (tp.VerificationRequiredVolumes.Any())
                    {
                        using(var testdb = new LocalTestDatabase(db))
                        {
                            foreach(var n in tp.VerificationRequiredVolumes)
                                try
                                {
                                    if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                                    {
                                        backend.WaitForComplete(db, null);
                                        return;
                                    }

                                    progress++;
                                    m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);

                                    long size;
                                    string hash;
                                    KeyValuePair<string, IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>>> res;
                                   
                                    using (var tf = backend.GetWithInfo(n.Name, out size, out hash))
                                        res = TestHandler.TestVolumeInternals(testdb, n, tf, m_options, m_result, 1);

                                    if (res.Value.Any())
                                        throw new Exception(string.Format("Remote verification failure: {0}", res.Value.First()));

                                    if (!m_options.Dryrun)
                                    {
                                        m_result.AddMessage(string.Format("Sucessfully captured hash for {0}, updating database", n.Name));
                                        db.UpdateRemoteVolume(n.Name, RemoteVolumeState.Verified, size, hash);
                                    }

                                }
                                catch (Exception ex)
                                {
                                    m_result.AddError(string.Format("Failed to perform verification for file: {0}, please run verify; message: {1}", n.Name, ex.Message), ex);
                                    if (ex is System.Threading.ThreadAbortException)
                                        throw;
                                }
                        }
                    }

                    // TODO: It is actually possible to use the extra files if we parse them
                    foreach(var n in tp.ExtraVolumes)
                        try
                        {
                            if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            {
                                backend.WaitForComplete(db, null);
                                return;
                            }

                            progress++;
                            m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);
                        
                            if (!m_options.Dryrun)
                            {
                                db.RegisterRemoteVolume(n.File.Name, n.FileType, n.File.Size, RemoteVolumeState.Deleting);
                                backend.Delete(n.File.Name, n.File.Size);
                            }
                            else
                                m_result.AddDryrunMessage(string.Format("would delete file {0}", n.File.Name));
                        }
                        catch (Exception ex)
                        {
                            m_result.AddError(string.Format("Failed to perform cleanup for extra file: {0}, message: {1}", n.File.Name, ex.Message), ex);
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }
							
                    foreach(var n in tp.MissingVolumes)
                    {
                        IDisposable newEntry = null;
                        
                        try
                        {  
                            if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            {
                                backend.WaitForComplete(db, null);
                                return;
                            }    

                            progress++;
                            m_result.OperationProgressUpdater.UpdateProgress((float)progress / targetProgess);

                            if (n.Type == RemoteVolumeType.Files)
                            {
                                var filesetId = db.GetFilesetIdFromRemotename(n.Name);
                                var w = new FilesetVolumeWriter(m_options, DateTime.UtcNow);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);
								
                                db.WriteFileset(w, null, filesetId);
	
                                w.Close();
                                if (m_options.Dryrun)
                                    m_result.AddDryrunMessage(string.Format("would re-upload fileset {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size)));
                                else
                                {
                                    db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
                                    backend.Put(w);
                                }
                            }
                            else if (n.Type == RemoteVolumeType.Index)
                            {
                                var w = new IndexVolumeWriter(m_options);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);

                                var h = System.Security.Cryptography.HashAlgorithm.Create(m_options.BlockHashAlgorithm);
								
                                foreach(var blockvolume in db.GetBlockVolumesFromIndexName(n.Name))
                                {								
                                    w.StartVolume(blockvolume.Name);
                                    var volumeid = db.GetRemoteVolumeID(blockvolume.Name);
									
                                    foreach(var b in db.GetBlocks(volumeid))
                                        w.AddBlock(b.Hash, b.Size);
										
                                    w.FinishVolume(blockvolume.Hash, blockvolume.Size);
                                    
                                    if (m_options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                                        foreach(var b in db.GetBlocklists(volumeid, m_options.Blocksize, hashsize))
                                        {
                                            var bh = Convert.ToBase64String(h.ComputeHash(b.Item2, 0, b.Item3));
                                            if (bh != b.Item1)
                                                throw new Exception(string.Format("Internal consistency check failed, generated index block has wrong hash, {0} vs {1}", bh, b.Item1));
                                            
                                            w.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);
                                        }
                                }
								
                                w.Close();
								
                                if (m_options.Dryrun)
                                    m_result.AddDryrunMessage(string.Format("would re-upload index file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size)));
                                else
                                {
                                    db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
                                    backend.Put(w);
                                }
                            }
                            else if (n.Type == RemoteVolumeType.Blocks)
                            {
                                var w = new BlockVolumeWriter(m_options);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);
                                
                                using(var mbl = db.CreateBlockList(n.Name))
                                {
                                    //First we grab all known blocks from local files
                                    foreach(var block in mbl.GetSourceFilesWithBlocks(m_options.Blocksize))
                                    {
                                        var hash = block.Hash;
                                        var size = (int)block.Size;
                                        
                                        foreach(var source in block.Sources)
                                        {
                                            var file = source.File;
                                            var offset = source.Offset;
                                            
                                            try
                                            {
                                                if (System.IO.File.Exists(file))
                                                    using(var f = System.IO.File.OpenRead(file))
                                                    {
                                                        f.Position = offset;
                                                        if (size == Library.Utility.Utility.ForceStreamRead(f, buffer, size))
                                                        {
                                                            var newhash = Convert.ToBase64String(blockhasher.ComputeHash(buffer, 0, size));
                                                            if (newhash == hash)
                                                            {
                                                                if (mbl.SetBlockRestored(hash, size))
                                                                    w.AddBlock(hash, buffer, 0, size, Duplicati.Library.Interface.CompressionHint.Default);
                                                                break;
                                                            }
                                                        }
                                                    }
                                            }
                                            catch (Exception ex)
                                            {
                                                m_result.AddError(string.Format("Failed to access file: {0}", file), ex);
                                            }
                                        }
                                    }
                                    
                                    //Then we grab all remote volumes that have the missing blocks
                                    foreach(var vol in new AsyncDownloader(mbl.GetMissingBlockSources().ToList(), backend))
                                    {
                                        try
                                        {
                                            using(var tmpfile = vol.TempFile)
                                            using(var f = new BlockVolumeReader(RestoreHandler.GetCompressionModule(vol.Name), tmpfile, m_options))
                                                foreach(var b in f.Blocks)
                                                    if (mbl.SetBlockRestored(b.Key, b.Value))
                                                        if (f.ReadBlock(b.Key, buffer) == b.Value)
                                                            w.AddBlock(b.Key, buffer, 0, (int)b.Value, Duplicati.Library.Interface.CompressionHint.Default);
                                        }
                                        catch (Exception ex)
                                        {
                                            m_result.AddError(string.Format("Failed to access remote file: {0}", vol.Name), ex);
                                        }
                                    }
                                    
                                    // If we managed to recover all blocks, NICE!
                                    var missingBlocks = mbl.GetMissingBlocks().Count();
                                    if (missingBlocks > 0)
                                    {                                    
                                        //TODO: How do we handle this situation?
                                        m_result.AddMessage(string.Format("Repair cannot acquire {0} required blocks for volume {1}, which are required by the following filesets: ", missingBlocks, n.Name));
                                        foreach(var f in mbl.GetFilesetsUsingMissingBlocks())
                                            m_result.AddMessage(f.Name);
                                        
                                        if (!m_options.Dryrun)
                                        {
                                            m_result.AddMessage("This may be fixed by deleting the filesets and running repair again");
                                            
                                            throw new Exception(string.Format("Repair not possible, missing {0} blocks!!!", missingBlocks));
                                        }
                                    }
                                    else
                                    {
                                        if (m_options.Dryrun)
                                            m_result.AddDryrunMessage(string.Format("would re-upload block file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size)));
                                        else
                                        {
                                            db.UpdateRemoteVolume(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null, null);
                                            backend.Put(w);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (newEntry != null)
                                try { newEntry.Dispose(); }
                                catch { }
                                finally { newEntry = null; }
                                
                            m_result.AddError(string.Format("Failed to perform cleanup for missing file: {0}, message: {1}", n.Name, ex.Message), ex);
                            
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }
                    }
                }
                else
                {
                    m_result.AddMessage("Destination and database are synchronized, not making any changes");
                }

                m_result.OperationProgressUpdater.UpdateProgress(1);				
				backend.WaitForComplete(db, null);
                db.WriteResults();
			}
        }

        public void RunRepairCommon()
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", m_options.Dbpath));

            m_result.OperationProgressUpdater.UpdateProgress(0);

            using(var db = new LocalRepairDatabase(m_options.Dbpath))
            {
                db.SetResult(m_result);
                db.FixDuplicateMetahash();
                db.FixDuplicateFileentries();
                db.FixDuplicateBlocklistHashes(m_options.Blocksize, m_options.BlockhashSize);
                db.FixMissingBlocklistHashes(m_options.BlockHashAlgorithm, m_options.Blocksize);
            }
        }
    }
}
