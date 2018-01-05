using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;

namespace Duplicati.Library.Main.Operation
{
    internal static class RepairHandler
    {
        public static async Task RunAsync(string backendurl, Options options, RepairResults result, Library.Utility.IFilter filter = null)
        {
			if (options.AllowPassphraseChange)
				throw new UserInformationException(Strings.Common.PassphraseChangeUnsupported);

            using (new IsolatedChannelScope())
            {
                var lh = Common.LogHandler.Run(result);

                using (var coredb = new LocalRepairDatabase(options.Dbpath))
				using (var db = new Repair.RepairDatabase(coredb, options))
                using(var stats = new Repair.RepairStatsCollector(result))
                using (var backend = new Common.BackendHandler(options, backendurl, db, stats, result.TaskReader))
				// Keep a reference to this channel to avoid shutdown
				using (var logtarget = ChannelManager.GetChannel(Common.Channels.LogChannel.ForWrite))
                using(var log = new Common.LogWrapper())
				{

                    if (!System.IO.File.Exists(options.Dbpath))
                    {
                        await RunRepairLocalAsync(backend, options, stats, result.TaskReader, filter);
                        await RunRepairCommonAsync(options, db, stats, result.TaskReader);
                        await stats.SetEndTimeAsync();
                        return;
                    }

                    long knownRemotes = -1;
                    try
                    {
                        using (var lrdb = new LocalRepairDatabase(options.Dbpath))
                            knownRemotes = lrdb.GetRemoteVolumes().Count();
                    }
                    catch (Exception ex)
                    {
                        await log.WriteWarningAsync(string.Format("Failed to read local db {0}, error: {1}", options.Dbpath, ex.Message), ex);
                    }

                    if (knownRemotes <= 0)
                    {
                        if (options.Dryrun)
                        {
                            await log.WriteDryRunAsync("Performing dryrun recreate");
                        }
                        else
                        {
                            var baseName = System.IO.Path.ChangeExtension(options.Dbpath, "backup");
                            var i = 0;
                            while (System.IO.File.Exists(baseName) && i++ < 1000)
                                baseName = System.IO.Path.ChangeExtension(options.Dbpath, "backup-" + i.ToString());

                            await log .WriteInformationAsync(string.Format("Renaming existing db from {0} to {1}", options.Dbpath, baseName));
                            System.IO.File.Move(options.Dbpath, baseName);
                        }

                        await RunRepairLocalAsync(backend, options, stats, result.TaskReader, filter);
                        await RunRepairCommonAsync(options, db, stats, result.TaskReader);
                    }
                    else
                    {
                        await RunRepairCommonAsync(options, db, stats, result.TaskReader);
                        await RunRepairRemote(backend, options, db, stats, result.TaskReader);
                    }

                    await stats.SetEndTimeAsync();
                }

                await lh;
            }
        }
        
        public static async Task RunRepairLocalAsync(Common.BackendHandler backend, Options options, Repair.RepairStatsCollector stats, Common.ITaskReader taskreader, Library.Utility.IFilter filter = null)
        {
            using(new Logging.Timer("Recreate database for repair"))
            using(var f = options.Dryrun ? new Library.Utility.TempFile() : null)
            {
                if (f != null && System.IO.File.Exists(f))
                    System.IO.File.Delete(f);
                
                var filelistfilter = RestoreHandler.FilterNumberedFilelist(options.Time, options.Version);

                using (var coredb = new LocalRecreateDatabase(options.Dryrun ? (string)f : options.Dbpath, options))
                using (var db = new Recreate.RecreateDatabase(coredb, options))
                    await RecreateDatabaseHandler.DoRunAsync(db, backend, options, false, stats, taskreader, filter, filelistfilter);
            }
        }

        public static async Task RunRepairRemote(Common.BackendHandler backend, Options options, Repair.RepairDatabase db, Repair.RepairStatsCollector stats, Common.ITaskReader taskreader)
        {
            if (!System.IO.File.Exists(options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", options.Dbpath));

            stats.UpdateProgress(0);

            using (var log = new Common.LogWrapper())
            {
                await db.UpdateOptionsFromDbAsync(options);
                await db.VerifyParametersAsync(options);

                if (await db.GetPartiallyRecreatedAsync())
                    throw new UserInformationException("The database was only partially recreated. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.");

                if (await db.GetRepairInProgressAsync())
                    throw new UserInformationException("The database was attempted repaired, but the repair did not complete. This database may be incomplete and the repair process is not allowed to alter remote files as that could result in data loss.");

                var tp = await FilelistProcessor.RemoteListAnalysisAsync(backend, options, db, stats, null);
                var buffer = new byte[options.Blocksize];
                var blockhasher = Library.Utility.HashAlgorithmHelper.Create(options.BlockHashAlgorithm);
                var hashsize = blockhasher.HashSize / 8;

                if (blockhasher == null)
                    throw new UserInformationException(Strings.Common.InvalidHashAlgorithm(options.BlockHashAlgorithm));
                if (!blockhasher.CanReuseTransform)
                    throw new UserInformationException(Strings.Common.InvalidCryptoSystem(options.BlockHashAlgorithm));
                
                var progress = 0;
                var targetProgess = tp.ExtraVolumes.Count() + tp.MissingVolumes.Count() + tp.VerificationRequiredVolumes.Count();

                if (options.Dryrun)
                {
                    if (tp.ParsedVolumes.Count() == 0 && tp.OtherVolumes.Any())
                    {
                        if (tp.BackupPrefixes.Length == 1)
                            throw new UserInformationException(string.Format("Found no backup files with prefix {0}, but files with prefix {1}, did you forget to set the backup prefix?", options.Prefix, tp.BackupPrefixes[0]));
                        else
                            throw new UserInformationException(string.Format("Found no backup files with prefix {0}, but files with prefixes {1}, did you forget to set the backup prefix?", options.Prefix, string.Join(", ", tp.BackupPrefixes)));
                    }
                    else if (tp.ParsedVolumes.Count() == 0 && tp.ExtraVolumes.Any())
                    {
                        throw new UserInformationException(string.Format("No files were missing, but {0} remote files were, found, did you mean to run recreate-database?", tp.ExtraVolumes.Count()));
                    }
                }

                if (tp.ExtraVolumes.Any() || tp.MissingVolumes.Any() || tp.VerificationRequiredVolumes.Any())
                {
                    if (tp.VerificationRequiredVolumes.Any())
                    {
                        using(var testdb = db.GetTestDatabase())
                        {
                            foreach(var n in tp.VerificationRequiredVolumes)
                                try
                                {
                                    if (!await taskreader.ProgressAsync)
                                    {
                                        await backend.ReadyAsync();
                                        return;
                                    }

                                    progress++;
                                    stats.UpdateProgress((float)progress / targetProgess);

                                    KeyValuePair<string, IEnumerable<KeyValuePair<Duplicati.Library.Interface.TestEntryStatus, string>>> res;

                                    var tr = await backend.GetFileWithInfoAsync(n.Name);
                                    using(var tf = tr.Item1)
                                        res = await TestHandler.TestVolumeInternalsAsync(testdb, n, tf, options, 1);

                                    if (res.Value.Any())
                                        throw new Exception(string.Format("Remote verification failure: {0}", res.Value.First()));

                                    if (!options.Dryrun)
                                    {
                                        await log.WriteInformationAsync(string.Format("Sucessfully captured hash for {0}, updating database", n.Name));
                                        await db.UpdateRemoteVolumeAsync(n.Name, RemoteVolumeState.Verified, tr.Item2, tr.Item3);
                                    }

                                }
                                catch (Exception ex)
                                {
                                    await log.WriteErrorAsync(string.Format("Failed to perform verification for file: {0}, please run verify; message: {1}", n.Name, ex.Message), ex);
                                    if (ex is System.Threading.ThreadAbortException)
                                        throw;
                                }
                        }
                    }

                    // TODO: It is actually possible to use the extra files if we parse them
                    foreach(var n in tp.ExtraVolumes)
                        try
                        {
                            if (!await taskreader.ProgressAsync)
                            {
                                await backend.ReadyAsync();
                                return;
                            }

                            progress++;
                            stats.UpdateProgress((float)progress / targetProgess);

                            // If this is a new index file, we can accept it if it matches our local data
                            // This makes it possible to augment the remote store with new index data
                            if (n.FileType == RemoteVolumeType.Index && options.IndexfilePolicy != Options.IndexFileStrategy.None)
                            {
                                try
                                {
                                    var tr = await backend.GetFileWithInfoAsync(n.File.Name);
                                    using(var tf = tr.Item1)
                                    using(var ifr = new IndexVolumeReader(n.CompressionModule, tf, options, options.BlockhashSize))
                                    {
                                        foreach(var rv in ifr.Volumes)
                                        {
                                            var entry = await db.GetRemoteVolumeAsync(rv.Filename);
                                            if (entry.ID < 0)
                                                throw new Exception(string.Format("Unknown remote file {0} detected", rv.Filename));
                                            
                                            if (!new [] { RemoteVolumeState.Uploading, RemoteVolumeState.Uploaded, RemoteVolumeState.Verified }.Contains(entry.State))
                                                throw new Exception(string.Format("Volume {0} has local state {1}", rv.Filename, entry.State));
                                        
                                            if (entry.Hash != rv.Hash || entry.Size != rv.Length || ! new [] { RemoteVolumeState.Uploading, RemoteVolumeState.Uploaded, RemoteVolumeState.Verified }.Contains(entry.State))
                                                throw new Exception(string.Format("Volume {0} hash/size mismatch ({1} - {2}) vs ({3} - {4})", rv.Filename, entry.Hash, entry.Size, rv.Hash, rv.Length));

                                            await db.CheckAllBlocksAreInVolumeAsync(rv.Filename, rv.Blocks);
                                        }

                                        var blocksize = options.Blocksize;
                                        foreach(var ixb in ifr.BlockLists)
                                            await db.CheckBlocklistCorrectAsync(ixb.Hash, ixb.Length, ixb.Blocklist, blocksize, hashsize);

                                        var selfid = await db.GetRemoteVolumeIDAsync(n.File.Name);
                                        foreach(var rv in ifr.Volumes)
                                            await db.AddIndexBlockLinkAsync(selfid, await db.GetRemoteVolumeIDAsync(rv.Filename));
                                    }
                                    
                                    // All checks fine, we accept the new index file
                                    await log.WriteInformationAsync(string.Format("Accepting new index file {0}", n.File.Name));
                                    await db.RegisterRemoteVolumeAsync(n.File.Name, RemoteVolumeType.Index, tr.Item2, RemoteVolumeState.Uploading);
                                    await db.UpdateRemoteVolumeAsync(n.File.Name, RemoteVolumeState.Verified, tr.Item2, tr.Item3);
                                    continue;
                                }
                                catch (Exception rex)
                                {
                                    await log.WriteErrorAsync(string.Format("Failed to accept new index file: {0}, message: {1}", n.File.Name, rex.Message), rex);
                                }
                            }
                        
                            if (!options.Dryrun)
                            {
                                await db.RegisterRemoteVolumeAsync(n.File.Name, n.FileType, n.File.Size, RemoteVolumeState.Deleting);
                                await backend.DeleteFileAsync(n.File.Name);
                            }
                            else
                                await log.WriteDryRunAsync(string.Format("would delete file {0}", n.File.Name));
                        }
                        catch (Exception ex)
                        {
                            await log.WriteErrorAsync(string.Format("Failed to perform cleanup for extra file: {0}, message: {1}", n.File.Name, ex.Message), ex);
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }
                            
                    foreach(var n in tp.MissingVolumes)
                    {
                        IDisposable newEntry = null;
                        
                        try
                        {  
                            if (!await taskreader.ProgressAsync)
                            {
                                await backend.ReadyAsync();
                                return;
                            }

                            progress++;
                            stats.UpdateProgress((float)progress / targetProgess);

                            if (n.Type == RemoteVolumeType.Files)
                            {
                                var filesetId = await db.GetFilesetIdFromRemotenameAsync(n.Name);
                                var w = new FilesetVolumeWriter(options, DateTime.UtcNow);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);

                                await db.WriteFilesetAsync(w, filesetId);
	
                                w.Close();
                                if (options.Dryrun)
                                    await log.WriteDryRunAsync(string.Format("would re-upload fileset {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size)));
                                else
                                {
                                    await db.UpdateRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                                    await backend.UploadFileAsync(w);
                                }
                            }
                            else if (n.Type == RemoteVolumeType.Index)
                            {
                                var w = new IndexVolumeWriter(options);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);

                                var h = Library.Utility.HashAlgorithmHelper.Create(options.BlockHashAlgorithm);
                                
                                foreach(var blockvolume in await db.GetBlockVolumesFromIndexNameAsync(n.Name))
                                {                               
                                    w.StartVolume(blockvolume.Name);
                                    var volumeid = await db.GetRemoteVolumeIDAsync(blockvolume.Name);
                                    
                                    foreach(var b in await db.GetBlocksAsync(volumeid))
                                        w.AddBlock(b.Hash, b.Size);
                                        
                                    w.FinishVolume(blockvolume.Hash, blockvolume.Size);
                                    
                                    if (options.IndexfilePolicy == Options.IndexFileStrategy.Full)
                                        foreach(var b in await db.GetBlocklistsAsync(volumeid, options.Blocksize, hashsize))
                                        {
                                            var bh = Convert.ToBase64String(h.ComputeHash(b.Item2, 0, b.Item3));
                                            if (bh != b.Item1)
                                                throw new Exception(string.Format("Internal consistency check failed, generated index block has wrong hash, {0} vs {1}", bh, b.Item1));
                                            
                                            w.WriteBlocklist(b.Item1, b.Item2, 0, b.Item3);
                                        }
                                }
                                
                                w.Close();
                                
                                if (options.Dryrun)
                                    await log.WriteDryRunAsync(string.Format("would re-upload index file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size)));
                                else
                                {
                                    await db.UpdateRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                                    await backend.UploadFileAsync(w);
                                }
                            }
                            else if (n.Type == RemoteVolumeType.Blocks)
                            {
                                var w = new BlockVolumeWriter(options);
                                newEntry = w;
                                w.SetRemoteFilename(n.Name);
                                
                                using(var mbl = await db.CreateBlockListAsync(n.Name))
                                {
                                    //First we grab all known blocks from local files
                                    foreach(var block in mbl.GetSourceFilesWithBlocks(options.Blocksize))
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
                                                await log.WriteErrorAsync(string.Format("Failed to access file: {0}", file), ex);
                                            }
                                        }
                                    }

                                    //Then we grab all remote volumes that have the missing blocks
                                    IAsyncDownloadedFile vol;
                                    using(var pr = new Common.PrefetchDownloader(mbl.GetMissingBlockSources().ToList(), backend))
                                    while((vol = await pr.GetNextAsync()) != null)
                                    {
                                        try
                                        {
                                            using(var tmpfile = vol.TempFile)
                                            using(var f = new BlockVolumeReader(RestoreHandler.GetCompressionModule(vol.Name), tmpfile, options))
                                                foreach(var b in f.Blocks)
                                                    if (mbl.SetBlockRestored(b.Key, b.Value))
                                                        if (f.ReadBlock(b.Key, buffer) == b.Value)
                                                            w.AddBlock(b.Key, buffer, 0, (int)b.Value, Duplicati.Library.Interface.CompressionHint.Default);
                                        }
                                        catch (Exception ex)
                                        {
                                            await log.WriteErrorAsync(string.Format("Failed to access remote file: {0}", vol.Name), ex);
                                        }
                                    }
                                    
                                    // If we managed to recover all blocks, NICE!
                                    var missingBlocks = mbl.GetMissingBlocks().Count();
                                    if (missingBlocks > 0)
                                    {                                    
                                        await log.WriteInformationAsync(string.Format("Repair cannot acquire {0} required blocks for volume {1}, which are required by the following filesets: ", missingBlocks, n.Name));
                                        foreach(var f in mbl.GetFilesetsUsingMissingBlocks())
                                            await log.WriteInformationAsync(f.Name);

                                        var recoverymsg = string.Format("If you want to continue working with the database, you can use the \"{0}\" and \"{1}\" commands to purge the missing data from the database and the remote storage.", "list-broken-files", "purge-broken-files");

                                        if (!options.Dryrun)
                                        {
                                            await log.WriteInformationAsync("This may be fixed by deleting the filesets and running repair again");

                                            throw new UserInformationException(string.Format("Repair not possible, missing {0} blocks.\n" + recoverymsg, missingBlocks));
                                        }
                                        else
                                        {
                                            await log.WriteInformationAsync(recoverymsg);
                                        }
                                    }
                                    else
                                    {
                                        if (options.Dryrun)
                                            await log.WriteDryRunAsync(string.Format("would re-upload block file {0}, with size {1}, previous size {2}", n.Name, Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(w.LocalFilename).Length), Library.Utility.Utility.FormatSizeString(n.Size)));
                                        else
                                        {
                                            await db.UpdateRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
                                            await backend.UploadFileAsync(w);
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
                                
                            await log.WriteErrorAsync(string.Format("Failed to perform cleanup for missing file: {0}, message: {1}", n.Name, ex.Message), ex);
                            
                            if (ex is System.Threading.ThreadAbortException)
                                throw;
                        }
                    }
                }
                else
                {
                    await log.WriteInformationAsync("Destination and database are synchronized, not making any changes");
                }

                stats.UpdateProgress(1);
            }
        }

        public static async Task RunRepairCommonAsync(Options options, Repair.RepairDatabase db, Repair.RepairStatsCollector stats, Common.ITaskReader taskreader)
        {
            if (!System.IO.File.Exists(options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", options.Dbpath));

            stats.UpdateProgress(0);
            await db.UpdateOptionsFromDbAsync(options);
            using (var log = new Common.LogWrapper())
            {
                if (await db.GetRepairInProgressAsync() || await db.GetPartiallyRecreatedAsync())
                    await log.WriteWarningAsync("The database is marked as \"in-progress\" and may be incomplete.", null);

                await db.FixDuplicateMetahashAsync();
                await db.FixDuplicateFileentriesAsync();
                await db.FixDuplicateBlocklistHashesAsync(options.Blocksize, options.BlockhashSize);
                await db.FixMissingBlocklistHashesAsync(options.BlockHashAlgorithm, options.Blocksize);
            }
        }
    }
}
