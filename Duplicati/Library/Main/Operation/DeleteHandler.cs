#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class DeleteHandler
    {   
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<DeleteHandler>();
        /// <summary>
        /// The tag used for logging retention policy messages
        /// </summary>
        private static readonly string LOGTAG_RETENTION = LOGTAG + ":RetentionPolicy";

        private readonly DeleteResults m_result;
        protected readonly string m_backendurl;
        protected readonly Options m_options;
    
        public DeleteHandler(string backend, Options options, DeleteResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }
        
        public void Run()
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseFileMissing");

            using(var db = new Database.LocalDeleteDatabase(m_options.Dbpath, "Delete"))
            {
                var tr = db.BeginTransaction();
                try
                {
                    m_result.SetDatabase(db);
                    Utility.UpdateOptionsFromDb(db, m_options);
                    Utility.VerifyParameters(db, m_options);
                    
                    DoRun(db, ref tr, false, false, null);
                    
                    if (!m_options.Dryrun)
                    {
                        using(new Logging.Timer(LOGTAG, "CommitDelete", "CommitDelete"))
                            tr.Commit();

                        db.WriteResults();
                    }
                    else
                        tr.Rollback();

                    tr = null;
                }
                finally
                {
                    if (tr != null)
                        try { tr.Rollback(); }
                        catch { }
                }
            }
        }

        public void DoRun(Database.LocalDeleteDatabase db, ref System.Data.IDbTransaction transaction, bool hasVerifiedBacked, bool forceCompact, BackendManager sharedManager)
        {
            // Workaround where we allow a running backendmanager to be used
            using(var bk = sharedManager == null ? new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db) : null)
            {
                var backend = bk ?? sharedManager;

                if (!hasVerifiedBacked && !m_options.NoBackendverification)
                    FilelistProcessor.VerifyRemoteList(backend, m_options, db, m_result.BackendWriter); 
                
                var filesetNumbers = db.FilesetTimes.Zip(Enumerable.Range(0, db.FilesetTimes.Count()), (a, b) => new Tuple<long, DateTime>(b, a.Value)).ToList();
                var sets = db.FilesetTimes.Select(x => x.Value).ToArray();
                var toDelete = GetFilesetsToDelete(db, sets);

                if (!m_options.AllowFullRemoval && sets.Length == toDelete.Length)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "PreventingLastFilesetRemoval", "Preventing removal of last fileset, use --{0} to allow removal ...", "allow-full-removal");
                    toDelete = toDelete.Skip(1).ToArray();
                }

                if (toDelete != null && toDelete.Length > 0)
                    Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileset", "Deleting {0} remote fileset(s) ...", toDelete.Length);

                var lst = db.DropFilesetsFromTable(toDelete, transaction).ToArray();
                foreach(var f in lst)
                    db.UpdateRemoteVolume(f.Key, RemoteVolumeState.Deleting, f.Value, null, transaction);

                if (!m_options.Dryrun)
                {
                    transaction.Commit();
                    transaction = db.BeginTransaction();
                }

                foreach(var f in lst)
                {
                    if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                    {
                        backend.WaitForComplete(db, transaction);
                        return;
                    }

                    if (!m_options.Dryrun)
                        backend.Delete(f.Key, f.Value);
                    else
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteRemoteFileset", "Would delete remote fileset: {0}", f.Key);
                }

                if (sharedManager == null)
                    backend.WaitForComplete(db, transaction);
                else
                    backend.WaitForEmpty(db, transaction);
                
                var count = lst.Length;
                if (!m_options.Dryrun)
                {
                    if (count == 0)
                        Logging.Log.WriteInformationMessage(LOGTAG, "DeleteResults", "No remote filesets were deleted");
                    else
                        Logging.Log.WriteInformationMessage(LOGTAG, "DeleteResults", "Deleted {0} remote fileset(s)", count);
                }
                else
                {
                
                    if (count == 0)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteResults", "No remote filesets would be deleted");
                    else
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteResults", "{0} remote fileset(s) would be deleted", count);

                    if (count > 0 && m_options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldDeleteHelp", "Remove --dry-run to actually delete files");
                }
                
                if (!m_options.NoAutoCompact && (forceCompact || (toDelete != null && toDelete.Length > 0)))
                {
                    m_result.CompactResults = new CompactResults(m_result);
                    new CompactHandler(m_backendurl, m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, ref transaction, sharedManager);
                }
                
                m_result.SetResults(
                    from n in filesetNumbers
                    where toDelete.Contains(n.Item2)
                    select n, 
                    m_options.Dryrun);
            }
        }

        /// <summary>
        /// Gets the filesets selected for deletion
        /// </summary>
        /// <returns>The filesets to delete</returns>
        /// <param name="allBackups">The list of backups that can be deleted</param>
        private DateTime[] GetFilesetsToDelete(Database.LocalDeleteDatabase db, DateTime[] allBackups)
        {
            if (allBackups.Length == 0)
            {
                return allBackups;
            }

            DateTime[] sortedAllBackups = allBackups.OrderByDescending(x => x.ToUniversalTime()).ToArray();

            if (sortedAllBackups.Select(x => x.ToUniversalTime()).Distinct().Count() != sortedAllBackups.Length)
            {
                throw new Exception($"List of backup timestamps contains duplicates: {string.Join(", ", sortedAllBackups.Select(x => x.ToString()))}");
            }

            List<DateTime> toDelete = new List<DateTime>();

            // Remove backups explicitly specified via option
            var versions = m_options.Version;
            if (versions != null && versions.Length > 0)
            {
                foreach (var ix in versions.Distinct())
                {
                    if (ix >= 0 && ix < sortedAllBackups.Length)
                    {
                        toDelete.Add(sortedAllBackups[ix]);
                    }
                }
            }

            // Remove backups that are older than date specified via option
            var keepTime = m_options.KeepTime;
            if (keepTime.Ticks > 0)
            {
                toDelete.AddRange(sortedAllBackups.SkipWhile(x => x >= keepTime));
            }

            // Remove backups via retention policy option
            toDelete.AddRange(ApplyRetentionPolicy(db, sortedAllBackups));

            // Check how many full backups will be remaining after the previous steps
            // and remove oldest backups while there are still more backups than should be kept as specified via option
            var backupsRemaining = sortedAllBackups.Except(toDelete).ToList();
            var fullVersionsToKeep = m_options.KeepVersions;
            var fullVersionsKeptCount = 0;
            if (fullVersionsToKeep > 0 && fullVersionsToKeep < backupsRemaining.Count)
            {
                ISet<DateTime> intermediatePartials = new HashSet<DateTime>();
                bool haveFullBackup = false;

                // Keep the number of full backups specified in fullVersionsToKeep.
                // Remove partial backups that are surrounded by full backups.
                // Once enough versions are kept, delete all older backups.
                foreach (var backup in backupsRemaining)
                {
                    bool isFullBackup = db.IsFilesetFullBackup(backup);
                    if (isFullBackup)
                    {
                        if (haveFullBackup)
                        {
                            toDelete.AddRange(intermediatePartials);
                            intermediatePartials.Clear();
                        }
                        haveFullBackup = true;
                    }
                    else
                    {
                        intermediatePartials.Add(backup);
                    }

                    if (fullVersionsKeptCount < fullVersionsToKeep)
                    {
                        // count only a full backup
                        if (fullVersionsKeptCount < fullVersionsToKeep && isFullBackup)
                        {
                            fullVersionsKeptCount++;
                        }
                    }
                    else
                    {
                        toDelete.Add(backup);
                    }
                }
            }

            var toDeleteDistinct = toDelete.Distinct().OrderByDescending(x => x.ToUniversalTime()).ToArray();
            var removeCount = toDeleteDistinct.Length;
            if (removeCount > sortedAllBackups.Length)
            {
                throw new Exception($"Too many entries {removeCount} vs {sortedAllBackups.Length}, lists: {string.Join(", ", toDeleteDistinct.Select(x => x.ToString(CultureInfo.InvariantCulture)))} vs {string.Join(", ", sortedAllBackups.Select(x => x.ToString(CultureInfo.InvariantCulture)))}");
            }

            return toDeleteDistinct;
        }

        /// <summary>
        /// Deletes backups according to the retention policy configuration.
        /// Backups that are not within any of the specified time frames will will NOT be deleted.
        /// </summary>
        /// <returns>The filesets to delete</returns>
        /// <param name="backups">The list of backups that can be deleted</param>
        private List<DateTime> ApplyRetentionPolicy(Database.LocalDeleteDatabase db, DateTime[] backups)
        {
            // Any work to do?
            var retentionPolicyOptionValues = m_options.RetentionPolicy;
            if (retentionPolicyOptionValues.Count == 0 || backups.Length == 0)
            {
                return new List<DateTime>(); // don't delete any backups
            }

            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "StartCheck", "Start checking if backups can be removed");

            // Work with a copy to not modify the enumeration that the caller passed
            List<DateTime> clonedBackupList = new List<DateTime>(backups);

            // Make sure the backups are in descending order (newest backup in the beginning)
            clonedBackupList = clonedBackupList.OrderByDescending(x => x).ToList();

            // Most recent backup usually should never get deleted in this process, so exclude it for now,
            // but keep a reference to potential delete it when allow-full-removal is set
            var mostRecentBackup = clonedBackupList.ElementAt(0);
            clonedBackupList.RemoveAt(0);
            var deleteMostRecentBackup = m_options.AllowFullRemoval;

            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "FramesAndIntervals", "Time frames and intervals pairs: {0}",
                string.Join(", ", retentionPolicyOptionValues));

            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "BackupList", "Backups to consider: {0}",
                string.Join(", ", clonedBackupList));

            // Collect all potential backups in each time frame and thin out according to the specified interval,
            // starting with the oldest backup in that time frame.
            // The order in which the time frames values are checked has to be from the smallest to the largest.
            List<DateTime> backupsToDelete = new List<DateTime>();
            var now = DateTime.Now;
            foreach (var singleRetentionPolicyOptionValue in retentionPolicyOptionValues.OrderBy(x => x.Timeframe))
            {
                // The timeframe in the retention policy option is only a timespan which has to be applied to the current DateTime to get the actual lower bound
                DateTime timeFrame = (singleRetentionPolicyOptionValue.IsUnlimtedTimeframe()) ? DateTime.MinValue : (now - singleRetentionPolicyOptionValue.Timeframe);

                Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "NextTimeAndFrame", "Next time frame and interval pair: {0}", singleRetentionPolicyOptionValue.ToString());

                List<DateTime> backupsInTimeFrame = new List<DateTime>();
                while (clonedBackupList.Count > 0 && clonedBackupList[0] >= timeFrame)
                {
                    backupsInTimeFrame.Insert(0, clonedBackupList[0]); // Insert at beginning to reverse order, which is necessary for next step
                    clonedBackupList.RemoveAt(0); // remove from here to not handle the same backup in two time frames
                }

                Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "BackupsInFrame", "Backups in this time frame: {0}",
                    string.Join(", ", backupsInTimeFrame));

                // Run through backups in this time frame
                DateTime? lastKept = null;
                foreach (DateTime backup in backupsInTimeFrame)
                {
                    var isFullBackup = db.IsFilesetFullBackup(backup);

                    // Keep this backup if
                    // - no backup has yet been added to the time frame (keeps at least the oldest backup in a time frame)
                    // - difference between last added backup and this backup is bigger than the specified interval
                    if (lastKept == null || singleRetentionPolicyOptionValue.IsKeepAllVersions() || (backup - lastKept.Value) >= singleRetentionPolicyOptionValue.Interval)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "KeepBackups", $"Keeping {(isFullBackup ? "" : "partial")} backup: {backup}", Logging.LogMessageType.Profiling);
                        if (isFullBackup)
                        {
                            lastKept = backup;
                        }
                    }
                    else
                    {
                        if (isFullBackup)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "DeletingBackups",
                                "Deleting backup: {0}", backup);
                            backupsToDelete.Add(backup);
                        }
                        else
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "KeepBackups", $"Keeping partial backup: {backup}", Logging.LogMessageType.Profiling);
                        }
                    }
                }

                // Check if most recent backup is outside of this time frame (meaning older/smaller)
                deleteMostRecentBackup &= (mostRecentBackup < timeFrame);
            }

            // Delete all remaining backups
            backupsToDelete.AddRange(clonedBackupList);
            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "BackupsToDelete", "Backups outside of all time frames and thus getting deleted: {0}",
                    string.Join(", ", clonedBackupList));

            // Delete most recent backup if allow-full-removal is set and the most current backup is outside of any time frame
            if (deleteMostRecentBackup)
            {
                backupsToDelete.Add(mostRecentBackup);
                Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "DeleteMostRecent", "Deleting most recent backup: {0}",
                    mostRecentBackup);
            }

            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "AllBackupsToDelete", "All backups to delete: {0}",
                    string.Join(", ", backupsToDelete.OrderByDescending(x => x)));

            return backupsToDelete;
        }
    }
}

