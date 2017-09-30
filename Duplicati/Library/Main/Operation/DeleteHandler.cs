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
        private DeleteResults m_result;
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
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath));

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
                        using(new Logging.Timer("CommitDelete"))
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
                    m_result.AddMessage(string.Format("Preventing removal of last fileset, use --{0} to allow removal ...", "allow-full-removal"));
                    toDelete = toDelete.Skip(1).ToArray();
                }

                if (toDelete != null && toDelete.Length > 0)
                    m_result.AddMessage(string.Format("Deleting {0} remote fileset(s) ...", toDelete.Length));

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
                        m_result.AddDryrunMessage(string.Format("Would delete remote fileset: {0}", f.Key));
                }

                if (sharedManager == null)
                    backend.WaitForComplete(db, transaction);
                else
                    backend.WaitForEmpty(db, transaction);
                
                var count = lst.Length;
                if (!m_options.Dryrun)
                {
                    if (count == 0)
                        m_result.AddMessage("No remote filesets were deleted");
                    else
                        m_result.AddMessage(string.Format("Deleted {0} remote fileset(s)", count));
                }
                else
                {
                
                    if (count == 0)
                        m_result.AddDryrunMessage("No remote filesets would be deleted");
                    else
                        m_result.AddDryrunMessage(string.Format("{0} remote fileset(s) would be deleted", count));

                    if (count > 0 && m_options.Dryrun)
                        m_result.AddDryrunMessage("Remove --dry-run to actually delete files");
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
        /// <param name="backups">The list of backups that can be deleted</param>
        private DateTime[] GetFilesetsToDelete(DateTime[] backups)
        {
            if (backups.Length == 0)
                return backups;

            if (backups.Distinct().Count() != backups.Length)
                throw new Exception(string.Format("List of backup timestamps contains duplicates: {0}", string.Join(", ", backups.Select(x => x.ToString()))));

            List<DateTime> res = new List<DateTime>();

            var versions = m_options.Version;
            if (versions != null && versions.Length > 0)
                foreach (var ix in versions.Distinct())
                    if (ix >= 0 && ix < backups.Length)
                        res.Add(backups[ix]);

            var keepVersions = m_options.KeepVersions;
            if (keepVersions > 0 && keepVersions < backups.Length)
                res.AddRange(backups.Skip(keepVersions));

            var keepTime = m_options.KeepTime;
            if (keepTime.Ticks > 0)
                res.AddRange(backups.SkipWhile(x => x >= keepTime));

            res.AddRange(ApplyRetentionPolicy(backups));

            var filtered = res.Distinct().OrderByDescending(x => x).AsEnumerable();

            var removeCount = filtered.Count();
            if (removeCount > backups.Length)
                throw new Exception(string.Format("Too many entries {0} vs {1}, lists: {2} vs {3}", removeCount, backups.Length, string.Join(", ", filtered.Select(x => x.ToString())), string.Join(", ", backups.Select(x => x.ToString()))));

            return filtered.ToArray();
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
            Dictionary<TimeSpan, TimeSpan> retentionPolicyOptionValue = m_options.RetentionPolicy;
            if (retentionPolicyOptionValue.Count == 0 || backups.Length == 0)
            {
                return new List<DateTime>(); // don't delete any backups
            }

            Logging.Log.WriteMessage("[Retention Policy]: Starting to thin out backups", Logging.LogMessageType.Information);

            // Work with a copy to not modify the enumeration that the caller passed
            List<DateTime> clonedBackups = new List<DateTime>(backups);

            // Make sure the backups are in descending order (newest backup in the beginning)
            clonedBackups = clonedBackups.OrderByDescending(x => x).ToList();

            // Most current backup should never get deleted due to the thinning out, so exclude it
            clonedBackups.RemoveAt(0);

            // Calculate the date for each period based on the current DateTime
            var timeFramesIntervales = new List<KeyValuePair<DateTime, TimeSpan>>();
            foreach (var configEntry in retentionPolicyOptionValue.ToList())
            {
                var period = configEntry.Key;
                var interval = configEntry.Value;

                DateTime periodEnd;
                if (period > TimeSpan.Zero)
                {
                    periodEnd = DateTime.Now - period;
                }
                else
                {
                    periodEnd = DateTime.MinValue; // periods equal or below 0 mean "biggest time frame possible"
                }
                timeFramesIntervales.Add(new KeyValuePair<DateTime, TimeSpan>(periodEnd, interval));
            }

            timeFramesIntervales = timeFramesIntervales.OrderByDescending(x => x.Key).ToList();

            Logging.Log.WriteMessage(string.Format("[Retention Policy]: Time frames and intervals pairs: {0}",
                string.Join(", ", timeFramesIntervales.Select(x => x.Key + " / " + x.Value))), Logging.LogMessageType.Information);
            Logging.Log.WriteMessage(string.Format("[Retention Policy]: Backups to consider: {0}",
                string.Join(", ", clonedBackups)), Logging.LogMessageType.Information);

            // For each period collect all potentiel backups in the time frame and thin out 
            // according to the specified interval, starting with the oldest backup in the time frame
            // If backups are not within any time frame, they will NOT be deleted here.
            // The --keep-time and --keep-versions switched should be used to ultimately delete backups that are too old
            List<DateTime> backupsToDelete = new List<DateTime>();
            foreach (var timeFrameInterval in timeFramesIntervales)
            {
                DateTime timeFrame = timeFrameInterval.Key;
                TimeSpan interval = timeFrameInterval.Value;

                Logging.Log.WriteMessage(string.Format("[Retention Policy]: Next time frame and interval pair: {0} / {1}", timeFrame, interval), Logging.LogMessageType.Profiling);

                List<DateTime> backupsInTimeFrame = new List<DateTime>();
                while (clonedBackups.Count > 0 && clonedBackups[0] >= timeFrame)
                {
                    backupsInTimeFrame.Insert(0, clonedBackups[0]); // Insert at begining to reverse order, which is nessecary for next step
                    clonedBackups.RemoveAt(0); // remove from here to not handle the same backup in two time frames
                }

                Logging.Log.WriteMessage(string.Format("[Retention Policy]: Backups in this time frame: {0}",
                    string.Join(", ", backupsInTimeFrame)), Logging.LogMessageType.Information);

                // Run through backups in this time frame
                DateTime? lastKept = null;
                foreach (DateTime backup in backupsInTimeFrame)
                {
                    // Keep this backup if
                    // - no backup has yet been added to the time frame (keeps at least the oldest backup in a time frame)
                    // - difference between last added backup and this backup is bigger than the specified interval
                    if (lastKept == null || (backup - lastKept.Value) >= interval)
                    {
                        Logging.Log.WriteMessage(string.Format("[Retention Policy]: Keeping backup: {0}", backup), Logging.LogMessageType.Profiling);
                        lastKept = backup;
                    }
                    else
                    {
                        Logging.Log.WriteMessage(string.Format("[Retention Policy]: Marking backup for deletion: {0}", backup), Logging.LogMessageType.Profiling);
                        backupsToDelete.Add(backup);
                    }
                }
            }

            Logging.Log.WriteMessage(string.Format("[Retention Policy]: Backups outside of all time frames and thus not checked: {0}",
                    string.Join(", ", clonedBackups)), Logging.LogMessageType.Profiling);

            Logging.Log.WriteMessage(string.Format("[Retention Policy]: Backups to delete: {0}",
                string.Join(", ", backupsToDelete.OrderByDescending(x => x))), Logging.LogMessageType.Information);

            return backupsToDelete;
        }
    }
}

