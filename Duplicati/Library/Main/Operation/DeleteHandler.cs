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
using System.Text;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.Main.Operation
{
    internal static class DeleteHandler
    {   
		public static void Run(DeleteResults results, string backendurl, Options options)
        {
            RunAsync(results, backendurl, options).WaitForTaskOrThrow();
        }

        public static async Task RunAsync(DeleteResults result, string backendurl, Options options)
        {
            if (!System.IO.File.Exists(options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", options.Dbpath));

            using (new IsolatedChannelScope())
            {
				var lh = Common.LogHandler.Run(result);
				using (var dbcore = new Database.LocalDeleteDatabase(options.Dbpath, "Delete"))
                using (var db = new Delete.DeleteDatabase(dbcore, options))
                using (var stats = new Delete.DeleteStatsCollector(result))
                using (var backend = new Common.BackendHandler(options, backendurl, db, stats, result.TaskReader))
                // Keep a reference to this channel to avoid shutdown
                using (var logtarget = ChannelManager.GetChannel(Common.Channels.LogChannel.ForWrite))
                {
                    result.SetDatabase(dbcore);
                    Utility.UpdateOptionsFromDb(dbcore, options);
                    Utility.VerifyParameters(dbcore, options);

                    await DoRunAsync(db, false, false, backend, options, result, stats);
                    await db.WriteResultsAsync();
                    await db.CommitTransactionAsync("Finalize Delete operation", false);
                }
				await lh;
			}

        }

        public static async Task DoRunAsync(Delete.DeleteDatabase db, bool hasVerifiedBacked, bool forceCompact, Common.BackendHandler backend, Options options, DeleteResults result, Delete.DeleteStatsCollector stats)
        {
            using (var log = new Common.LogWrapper())
            {
                // Workaround where we allow a running backendmanager to be used
                if (!hasVerifiedBacked && !options.NoBackendverification)
                    await FilelistProcessor.VerifyRemoteListAsync(backend, options, db, stats);

                var filesettimes = (await db.GetFilesetTimesAsync()).ToList();

                var filesetNumbers = filesettimes.Zip(Enumerable.Range(0, filesettimes.Count), (a, b) => new Tuple<long, DateTime>(b, a.Value)).ToList();
                var sets = filesettimes.Select(x => x.Value).ToArray();
                var toDelete = options.GetFilesetsToDelete(sets);

                if (!options.AllowFullRemoval && sets.Length == toDelete.Length)
                {
                    await log.WriteInformationAsync(string.Format("Preventing removal of last fileset, use --{0} to allow removal ...", "allow-full-removal"));
                    toDelete = toDelete.Skip(1).ToArray();
                }

                if (toDelete != null && toDelete.Length > 0)
                    await log.WriteInformationAsync(string.Format("Deleting {0} remote fileset(s) ...", toDelete.Length));

                var lst = (await db.DropFilesetsFromTableAsync(toDelete)).ToArray();
                foreach (var f in lst)
                    await db.UpdateRemoteVolumeAsync(f.Key, RemoteVolumeState.Deleting, f.Value, null);

                await db.CommitTransactionAsync("After fileset dropped");

                foreach (var f in lst)
                {
                    if (!await result.TaskReader.ProgressAsync)
                        return;

                    if (!options.Dryrun)
                        await backend.DeleteFileAsync(f.Key);
                    else
                        await log.WriteDryRunAsync(string.Format("Would delete remote fileset: {0}", f.Key));
                }

                var count = lst.Length;
                if (!options.Dryrun)
                {
                    if (count == 0)
                        await log.WriteInformationAsync("No remote filesets were deleted");
                    else
                        await log.WriteInformationAsync(string.Format("Deleted {0} remote fileset(s)", count));
                }
                else
                {
                    if (count == 0)
                        await log.WriteDryRunAsync("No remote filesets would be deleted");
                    else
                       await log.WriteDryRunAsync(string.Format("{0} remote fileset(s) would be deleted", count));

                    if (count > 0 && options.Dryrun)
                        await log.WriteDryRunAsync("Remove --dry-run to actually delete files");
                }

                if (!options.NoAutoCompact && (forceCompact || (toDelete != null && toDelete.Length > 0)))
                {
                    var cr = new CompactResults(result);
                    result.CompactResults = cr;
                    using(var cs = new Compact.CompactStatsCollector(cr))
                    using(var cdb = new Compact.CompactDatabase(db.BackingDatabase, options))
                        await CompactHandler.DoCompactAsync(cdb, true, backend, options, cs, result.TaskReader);
                }

                await stats.SetResultAsync(
                    filesetNumbers.Where(x => toDelete.Contains(x.Item2)),
                    options.Dryrun);
            }
        }
    }
}

