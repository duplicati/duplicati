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
        protected string m_backendurl;
        protected Options m_options;
    
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
                var toDelete = GetFilesetsToDelete(sets);

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
        private DateTime[] GetFilesetsToDelete(DateTime[] allBackups)
        {
            if (allBackups.Length == 0)
                return allBackups;

            if (allBackups.Select(x => x.ToUniversalTime()).Distinct().Count() != allBackups.Length)
                throw new Exception(string.Format("List of backup timestamps contains duplicates: {0}", string.Join(", ", allBackups.Select(x => x.ToString()))));

            List<DateTime> toDelete = new List<DateTime>();

            // Remove backups explicitely specified via option
            var versions = m_options.Version;
            if (versions != null && versions.Length > 0)
                foreach (var ix in versions.Distinct())
                    if (ix >= 0 && ix < allBackups.Length)
                        toDelete.Add(allBackups[ix]);

            // Remove backups that are older than date specified via option
            var keepTime = m_options.KeepTime;
            if (keepTime.Ticks > 0)
                toDelete.AddRange(allBackups.SkipWhile(x => x >= keepTime));

            // Remove backups via rentention policy option
            toDelete.AddRange(ApplyRetentionPolicy(allBackups));

            // Check how many backups will be remaining after the previous steps
            // and remove oldest backups while there are still more backups than should be kept as specified via option
            var backupsRemaining = allBackups.Except(toDelete).ToList();
            var keepVersions = m_options.KeepVersions;
            if (keepVersions > 0 && keepVersions < backupsRemaining.Count())
                toDelete.AddRange(backupsRemaining.Skip(keepVersions));

            var toDeleteDistinct = toDelete.Distinct().OrderByDescending(x => x.ToUniversalTime()).AsEnumerable();

            var removeCount = toDeleteDistinct.Count();
            if (removeCount > allBackups.Length)
                throw new Exception(string.Format("Too many entries {0} vs {1}, lists: {2} vs {3}", removeCount, allBackups.Length, string.Join(", ", toDeleteDistinct.Select(x => x.ToString())), string.Join(", ", allBackups.Select(x => x.ToString()))));

            return toDeleteDistinct.ToArray();
        }

        /// <summary>
        /// Deletes backups according to the retention policy configuration.
        /// Backups that are not within any of the specified time frames will will NOT be deleted.
        /// </summary>
        /// <returns>The filesets to delete</returns>
        /// <param name="backups">The list of backups that can be deleted</param>
        private List<DateTime> ApplyRetentionPolicy(DateTime[] backups)
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
            // but keep a reference to potentiall delete it when allow-full-removal is set
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
                    backupsInTimeFrame.Insert(0, clonedBackupList[0]); // Insert at begining to reverse order, which is nessecary for next step
                    clonedBackupList.RemoveAt(0); // remove from here to not handle the same backup in two time frames
                }

                Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "BackupsInFrame", "Backups in this time frame: {0}",
                    string.Join(", ", backupsInTimeFrame));

                // Run through backups in this time frame
                DateTime? lastKept = null;
                foreach (DateTime backup in backupsInTimeFrame)
                {
                    // Keep this backup if
                    // - no backup has yet been added to the time frame (keeps at least the oldest backup in a time frame)
                    // - difference between last added backup and this backup is bigger than the specified interval
                    if (lastKept == null || singleRetentionPolicyOptionValue.IsKeepAllVersions() || (backup - lastKept.Value) >= singleRetentionPolicyOptionValue.Interval)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "KeepBackups", string.Format("Keeping backup: {0}", backup), Logging.LogMessageType.Profiling);
                        lastKept = backup;
                    }
                    else
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "DeletingBackups", "Deleting backup: {0}", backup);
                        backupsToDelete.Add(backup);
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

