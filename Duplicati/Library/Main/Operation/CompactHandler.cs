//  Copyright (C) 2013, The Duplicati Team

//  http://www.duplicati.com, opensource@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Volumes;
using System.Text;
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.Main.Operation
{
    internal static class CompactHandler
    {
        public static void Run(string backendurl, Options options, CompactResults result)
        {
            RunAsync(backendurl, options, result).WaitForTaskOrThrow();
        }

        public static async Task RunAsync(string backendurl, Options options, CompactResults result)
        {
            if (!System.IO.File.Exists(options.Dbpath))
                throw new Exception(string.Format("Database file does not exist: {0}", options.Dbpath));


            using (new IsolatedChannelScope())
            {
				var lh = Common.LogHandler.Run(result);
				using (var coredb = new LocalDeleteDatabase(options.Dbpath, "Compact"))
                using (var db = new Compact.CompactDatabase(coredb, options))
                using (var stats = new Compact.CompactStatsCollector(result))
                using (var backend = new Common.BackendHandler(options, backendurl, db, stats, result.TaskReader))
                // Keep a reference to this channel to avoid shutdown
                using (var logtarget = ChannelManager.GetChannel(Common.Channels.LogChannel.ForWrite))
                {
                    result.SetDatabase(coredb);
                    Utility.UpdateOptionsFromDb(coredb, options);
                    Utility.VerifyParameters(coredb, options);

                    var changed = await DoCompactAsync(db, false, backend, options, stats, result.TaskReader);

                    if (changed && options.UploadVerificationFile)
                        await FilelistProcessor.UploadVerificationFileAsync(backend, options, db);

                    await db.WriteResultsAsync();
                    await db.CommitTransactionAsync("CommitCompact", false);
                    if (changed && !options.Dryrun && options.AutoVacuum)
                        await db.VacuumAsync();
                }

				await lh;
			}

        }
        
        internal static async Task<bool> DoCompactAsync(Compact.CompactDatabase db, bool hasVerifiedBackend, Common.BackendHandler backend, Options options, Compact.CompactStatsCollector stat, Common.ITaskReader taskreader)
        {
            using (var log = new Common.LogWrapper())
            {
                var report = await db.GetCompactReportAsync(options.VolumeSize, options.Threshold, options.SmallFileSize, options.SmallFileMaxCount);
                await report.ReportCompactDataAsync(options.Verbose);

                if (report.ShouldReclaim || report.ShouldCompact)
                {
                    if (!hasVerifiedBackend && !options.NoBackendverification)
                        await FilelistProcessor.VerifyRemoteListAsync(backend, options, db, stat);

                    BlockVolumeWriter newvol = new BlockVolumeWriter(options);
                    newvol.VolumeID = await db.RegisterRemoteVolumeAsync(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);

                    IndexVolumeWriter newvolindex = null;
                    if (options.IndexfilePolicy != Options.IndexFileStrategy.None)
                    {
                        newvolindex = new IndexVolumeWriter(options);
                        newvolindex.VolumeID = await db.RegisterRemoteVolumeAsync(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);
                        await db.AddIndexBlockLinkAsync(newvolindex.VolumeID, newvol.VolumeID);
                        newvolindex.StartVolume(newvol.RemoteFilename);
                    }

                    long blocksInVolume = 0;
                    long discardedBlocks = 0;
                    long discardedSize = 0;
                    byte[] buffer = new byte[options.Blocksize];
                    var remoteList = (await db.GetRemoteVolumesAsync()).Where(n => n.State == RemoteVolumeState.Uploaded || n.State == RemoteVolumeState.Verified).ToArray();

                    //These are for bookkeeping
                    var uploadedVolumes = new List<KeyValuePair<string, long>>();
                    var deletedVolumes = new List<KeyValuePair<string, long>>();
                    var downloadedVolumes = new List<KeyValuePair<string, long>>();

                    //We start by deleting unused volumes to save space before uploading new stuff
                    var fullyDeleteable = (from v in remoteList
                                           where report.DeleteableVolumes.Contains(v.Name)
                                           select (IRemoteVolume)v).ToList();

                    deletedVolumes.AddRange(await DoDeleteAsync(db, backend, fullyDeleteable, options));

                    // This list is used to pick up unused volumes,
                    // so they can be deleted once the upload of the
                    // required fragments is complete
                    var deleteableVolumes = new List<IRemoteVolume>();

                    if (report.ShouldCompact)
                    {
                        var volumesToDownload = (from v in remoteList
                                                 where report.CompactableVolumes.Contains(v.Name)
                                                 select (IRemoteVolume)v).ToList();

                        using (var q = await db.CreateBlockQueryHelperAsync(options))
                        using(var pre = new Common.PrefetchDownloader(volumesToDownload, backend))
                        {
                            IAsyncDownloadedFile entry;

                            while((entry = await pre.GetNextAsync()) != null)
                                using (var tmpfile = entry.TempFile)
                                {
                                    if (!await taskreader.ProgressAsync)
                                    {
                                        await pre.StopAsync();
                                        return false;
                                    }

                                    downloadedVolumes.Add(new KeyValuePair<string, long>(entry.Name, entry.Size));
                                    var inst = VolumeBase.ParseFilename(entry.Name);
                                    using (var f = new BlockVolumeReader(inst.CompressionModule, tmpfile, options))
                                    {
                                        foreach (var e in f.Blocks)
                                        {
                                            if (await q.UseBlockAsync(e.Key, e.Value))
                                            {
                                                //TODO: How do we get the compression hint? Reverse query for filename in db?
                                                var s = f.ReadBlock(e.Key, buffer);
                                                if (s != e.Value)
                                                    throw new Exception(string.Format("Size mismatch problem for block {0}, {1} vs {2}", e.Key, s, e.Value));

                                                newvol.AddBlock(e.Key, buffer, 0, s, Duplicati.Library.Interface.CompressionHint.Compressible);
                                                if (newvolindex != null)
                                                    newvolindex.AddBlock(e.Key, e.Value);

                                                await db.MoveBlockToNewVolumeAsync(e.Key, e.Value, newvol.VolumeID);
                                                blocksInVolume++;

                                                if (newvol.Filesize > options.VolumeSize)
                                                {
                                                    uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, newvol.Filesize));
                                                    if (newvolindex != null)
                                                        uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, newvolindex.Filesize));

                                                    if (!options.Dryrun)
                                                        await backend.UploadFileAsync(newvol, (arg) => Task.FromResult(newvolindex));
                                                    else
                                                        await log.WriteDryRunAsync(string.Format("Would upload generated blockset of size {0}", Library.Utility.Utility.FormatSizeString(newvol.Filesize)));


                                                    newvol = new BlockVolumeWriter(options);
                                                    newvol.VolumeID = await db.RegisterRemoteVolumeAsync(newvol.RemoteFilename, RemoteVolumeType.Blocks, RemoteVolumeState.Temporary);

                                                    if (options.IndexfilePolicy != Options.IndexFileStrategy.None)
                                                    {
                                                        newvolindex = new IndexVolumeWriter(options);
                                                        newvolindex.VolumeID = await db.RegisterRemoteVolumeAsync(newvolindex.RemoteFilename, RemoteVolumeType.Index, RemoteVolumeState.Temporary);
                                                        await db.AddIndexBlockLinkAsync(newvolindex.VolumeID, newvol.VolumeID);
                                                        newvolindex.StartVolume(newvol.RemoteFilename);
                                                    }

                                                    blocksInVolume = 0;

                                                    //After we upload this volume, we can delete all previous encountered volumes
                                                    deletedVolumes.AddRange(await DoDeleteAsync(db, backend, deleteableVolumes, options));
                                                    deleteableVolumes = new List<IRemoteVolume>();
                                                }
                                            }
                                            else
                                            {
                                                discardedBlocks++;
                                                discardedSize += e.Value;
                                            }
                                        }
                                    }

                                    deleteableVolumes.Add(entry);
                                }

                            if (blocksInVolume > 0)
                            {
                                uploadedVolumes.Add(new KeyValuePair<string, long>(newvol.RemoteFilename, newvol.Filesize));
                                if (newvolindex != null)
                                    uploadedVolumes.Add(new KeyValuePair<string, long>(newvolindex.RemoteFilename, newvolindex.Filesize));
                                if (!options.Dryrun)
                                    await backend.UploadFileAsync(newvol, arg => Task.FromResult(newvolindex));
                                else
                                    await log.WriteDryRunAsync(string.Format("Would upload generated blockset of size {0}", Library.Utility.Utility.FormatSizeString(newvol.Filesize)));
                            }
                            else
                            {
                                await db.RemoveRemoteVolumeAsync(newvol.RemoteFilename);
                                if (newvolindex != null)
                                {
                                    await db.RemoveRemoteVolumeAsync(newvolindex.RemoteFilename);
                                    newvolindex.FinishVolume(null, 0);
                                }
                            }
                        }
                    }

                    deletedVolumes.AddRange(await DoDeleteAsync(db, backend, deleteableVolumes, options));

                    var downloadSize = downloadedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a, x) => a + x.Value);
                    var deletedSize = deletedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a, x) => a + x.Value);
                    var uploadSize = uploadedVolumes.Where(x => x.Value >= 0).Aggregate(0L, (a, x) => a + x.Value);

                    await stat.SetResultAsync(
                        deletedVolumes.Count,
                        downloadedVolumes.Count,
                        uploadedVolumes.Count,
                        deletedSize,
                        downloadSize,
                        uploadSize,
                        options.Dryrun);

                    if (stat.Dryrun)
                    {
                        if (downloadedVolumes.Count == 0)
                            await log.WriteDryRunAsync(string.Format("Would delete {0} files, which would reduce storage by {1}", stat.DeletedFileCount, Library.Utility.Utility.FormatSizeString(stat.DeletedFileSize)));
                        else
                            await log.WriteDryRunAsync(string.Format("Would download {0} file(s) with a total size of {1}, delete {2} file(s) with a total size of {3}, and compact to {4} file(s) with a size of {5}, which would reduce storage by {6} file(s) and {7}",
                                                                    stat.DownloadedFileCount,
                                                                    Library.Utility.Utility.FormatSizeString(stat.DownloadedFileSize),
                                                                    stat.DeletedFileCount,
                                                                    Library.Utility.Utility.FormatSizeString(stat.DeletedFileSize), stat.UploadedFileCount,
                                                                    Library.Utility.Utility.FormatSizeString(stat.UploadedFileSize),
                                                                    stat.DeletedFileCount - stat.UploadedFileCount,
                                                                    Library.Utility.Utility.FormatSizeString(stat.DeletedFileSize - stat.UploadedFileSize)));
                    }
                    else
                    {
                        if (stat.DownloadedFileCount == 0)
                            await log.WriteInformationAsync(string.Format("Deleted {0} files, which reduced storage by {1}", stat.DeletedFileCount, Library.Utility.Utility.FormatSizeString(stat.DeletedFileSize)));
                        else
                            await log.WriteInformationAsync(string.Format("Downloaded {0} file(s) with a total size of {1}, deleted {2} file(s) with a total size of {3}, and compacted to {4} file(s) with a size of {5}, which reduced storage by {6} file(s) and {7}",
                                                              stat.DownloadedFileCount,
                                                              Library.Utility.Utility.FormatSizeString(stat.DownloadedFileSize),
                                                              stat.DeletedFileCount,
                                                              Library.Utility.Utility.FormatSizeString(stat.DeletedFileSize),
                                                              stat.UploadedFileCount,
                                                              Library.Utility.Utility.FormatSizeString(stat.UploadedFileSize),
                                                              stat.DeletedFileCount - stat.UploadedFileCount,
                                                              Library.Utility.Utility.FormatSizeString(stat.DeletedFileSize - stat.UploadedFileSize)));
                    }

                    await stat.SetEndTimeAsync();
                    return (stat.DeletedFileCount + stat.UploadedFileCount) > 0;
                }
                else
                {
					await stat.SetEndTimeAsync();
					return false;
                }
            }
        }

        private static async Task<IEnumerable<KeyValuePair<string, long>>> DoDeleteAsync(Compact.CompactDatabase db, Common.BackendHandler backend, IEnumerable<IRemoteVolume> deleteableVolumes, Options options)
        {
            // Mark all volumes as disposable
            foreach(var f in deleteableVolumes)
                await db.UpdateRemoteVolumeAsync(f.Name, RemoteVolumeState.Deleting, f.Size, f.Hash);

            // Before we commit the current state, make sure the backend has caught up
            await backend.ReadyAsync();

            // Sync the database before we actually delete stuff
            await db.CommitTransactionAsync("PrepareForDelete");

            return await PerformDeleteAsync(backend, await db.GetDeletableVolumesAsync(deleteableVolumes), options);
        }
            
        
        private static async Task<IEnumerable<KeyValuePair<string, long>>> PerformDeleteAsync(Common.BackendHandler backend, IEnumerable<IRemoteVolume> list, Options options)
        {
            var res = new List<KeyValuePair<string, long>>();
            using (var log = new Common.LogWrapper())
            {
                foreach (var f in list)
                {
                    if (!options.Dryrun)
                        await backend.DeleteFileAsync(f.Name);
                    else
                        await log.WriteDryRunAsync(string.Format("Would delete remote file: {0}, size: {1}", f.Name, Library.Utility.Utility.FormatSizeString(f.Size)));

                    res.Add(new KeyValuePair<string, long>(f.Name, f.Size));
                }
            }

            return res;
        }
    }
}

