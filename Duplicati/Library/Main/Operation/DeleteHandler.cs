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
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Operation
{
    internal class DeleteHandler
    {   
        /// <summary>
        /// The tag used for logging
        /// </summary>
        internal static readonly string LOGTAG = Logging.Log.LogTagFromType<DeleteHandler>();

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

                IListResultFileset[] filesets = db.FilesetsWithBackupVersion.ToArray();
                List<IListResultFileset> versionsToDelete = new List<IListResultFileset>();
                versionsToDelete.AddRange(new SpecificVersionsRemover(this.m_options).GetFilesetsToDelete(filesets));
                versionsToDelete.AddRange(new KeepTimeRemover(this.m_options).GetFilesetsToDelete(filesets));
                versionsToDelete.AddRange(new RetentionPolicyRemover(this.m_options).GetFilesetsToDelete(filesets));

                // When determining the number of full versions to keep, we need to ignore the versions already marked for removal.
                versionsToDelete.AddRange(new KeepVersionsRemover(this.m_options).GetFilesetsToDelete(filesets.Except(versionsToDelete)));

                if (!m_options.AllowFullRemoval && filesets.Length == versionsToDelete.Count)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "PreventingLastFilesetRemoval", "Preventing removal of last fileset, use --{0} to allow removal ...", "allow-full-removal");
                    versionsToDelete = versionsToDelete.OrderBy(x => x.Version).Skip(1).ToList();
                }

                if (versionsToDelete.Count > 0)
                    Logging.Log.WriteInformationMessage(LOGTAG, "DeleteRemoteFileset", "Deleting {0} remote fileset(s) ...", versionsToDelete.Count);

                var lst = db.DropFilesetsFromTable(versionsToDelete.Select(x => x.Time).ToArray(), transaction).ToArray();
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
                
                if (!m_options.NoAutoCompact && (forceCompact || versionsToDelete.Count > 0))
                {
                    m_result.CompactResults = new CompactResults(m_result);
                    new CompactHandler(m_backendurl, m_options, (CompactResults)m_result.CompactResults).DoCompact(db, true, ref transaction, sharedManager);
                }

                m_result.SetResults(versionsToDelete.Select(v => new Tuple<long, DateTime>(v.Version, v.Time)), m_options.Dryrun);
            }
        }
    }

    public abstract class FilesetRemover
    {
        protected readonly Options Options;

        protected FilesetRemover(Options options)
        {
            this.Options = options;
        }

        public abstract IEnumerable<IListResultFileset> GetFilesetsToDelete(IEnumerable<IListResultFileset> filesets);
    }

    /// <summary>
    /// Remove versions specified by the --version option.
    /// </summary>
    public class SpecificVersionsRemover : FilesetRemover
    {
        public SpecificVersionsRemover(Options options) : base(options)
        {
        }

        public override IEnumerable<IListResultFileset> GetFilesetsToDelete(IEnumerable<IListResultFileset> filesets)
        {
            ISet<long> versionsToDelete = new HashSet<long>(this.Options.Version ?? new long[0]);
            return filesets.Where(x => versionsToDelete.Contains(x.Version));
        }
    }

    /// <summary>
    /// Keep backups that are newer than the date specified by the --keep-time option.
    /// If none of the retained versions are full backups, then continue to keep versions
    /// until we have a full backup.
    /// </summary>
    public class KeepTimeRemover : FilesetRemover
    {
        public KeepTimeRemover(Options options) : base(options)
        {
        }

        public override IEnumerable<IListResultFileset> GetFilesetsToDelete(IEnumerable<IListResultFileset> filesets)
        {
            IListResultFileset[] sortedFilesets = filesets.OrderByDescending(x => x.Time).ToArray();
            List<IListResultFileset> versionsToDelete = new List<IListResultFileset>();

            DateTime earliestTime = this.Options.KeepTime;
            if (earliestTime.Ticks > 0)
            {
                bool haveFullBackup = false;
                versionsToDelete.AddRange(sortedFilesets.SkipWhile(x =>
                {
                    bool keepBackup = (x.Time >= earliestTime) || !haveFullBackup;
                    haveFullBackup = haveFullBackup || (x.IsFullBackup == BackupType.FULL_BACKUP);
                    return keepBackup;
                }));
            }

            return versionsToDelete;
        }
    }

    /// <summary>
    /// Keep a number of recent full backups as specified by the --keep-versions option.
    /// Partial backups that are surrounded by full backups will also be removed.
    /// </summary>
    public class KeepVersionsRemover : FilesetRemover
    {
        public KeepVersionsRemover(Options options) : base(options)
        {
        }

        public override IEnumerable<IListResultFileset> GetFilesetsToDelete(IEnumerable<IListResultFileset> filesets)
        {
            IListResultFileset[] sortedFilesets = filesets.OrderByDescending(x => x.Time).ToArray();
            List<IListResultFileset> versionsToDelete = new List<IListResultFileset>();

            // Check how many full backups will be remaining after the previous steps
            // and remove oldest backups while there are still more backups than should be kept as specified via option
            int fullVersionsToKeep = this.Options.KeepVersions;
            if (fullVersionsToKeep > 0 && fullVersionsToKeep < sortedFilesets.Length)
            {
                int fullVersionsKept = 0;
                ISet<IListResultFileset> intermediatePartials = new HashSet<IListResultFileset>();

                // Enumerate the collection starting from the most recent full backup.
                foreach (IListResultFileset fileset in sortedFilesets.SkipWhile(x => x.IsFullBackup == BackupType.PARTIAL_BACKUP))
                {
                    if (fullVersionsKept >= fullVersionsToKeep)
                    {
                        // If we have enough full backups, delete all older backups.
                        versionsToDelete.Add(fileset);
                    }
                    else if (fileset.IsFullBackup == BackupType.FULL_BACKUP)
                    {
                        // We can delete partial backups that are surrounded by full backups.
                        versionsToDelete.AddRange(intermediatePartials);
                        intermediatePartials.Clear();
                        fullVersionsKept++;
                    }
                    else
                    {
                        intermediatePartials.Add(fileset);
                    }
                }
            }

            return versionsToDelete;
        }
    }

    /// <summary>
    /// Remove backups according to the --retention-policy option.
    /// Backups that are not within any of the specified time frames will will NOT be deleted.
    /// Partial backups are not removed.
    /// </summary>
    public class RetentionPolicyRemover : FilesetRemover
    {
        private static readonly string LOGTAG_RETENTION = DeleteHandler.LOGTAG + ":RetentionPolicy";

        public RetentionPolicyRemover(Options options) : base(options)
        {
        }

        public override IEnumerable<IListResultFileset> GetFilesetsToDelete(IEnumerable<IListResultFileset> filesets)
        {
            IListResultFileset[] sortedFilesets = filesets.OrderByDescending(x => x.Time).ToArray();
            List<IListResultFileset> versionsToDelete = new List<IListResultFileset>();

            List<Options.RetentionPolicyValue> retentionPolicyOptionValues = this.Options.RetentionPolicy;
            if (retentionPolicyOptionValues.Count == 0 || sortedFilesets.Length == 0)
            {
                return versionsToDelete;
            }

            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "StartCheck", "Start checking if backups can be removed");

            // Work with a copy to not modify the enumeration that the caller passed
            List<IListResultFileset> clonedBackupList = new List<IListResultFileset>(sortedFilesets);

            // Most recent backup usually should never get deleted in this process, so exclude it for now,
            // but keep a reference to potential delete it when allow-full-removal is set
            IListResultFileset mostRecentBackup = clonedBackupList.ElementAt(0);
            clonedBackupList.RemoveAt(0);
            bool deleteMostRecentBackup = this.Options.AllowFullRemoval;

            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "FramesAndIntervals", "Time frames and intervals pairs: {0}", string.Join(", ", retentionPolicyOptionValues));
            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "BackupList", "Backups to consider: {0}", string.Join(", ", clonedBackupList.Select(x => x.Time)));

            // Collect all potential backups in each time frame and thin out according to the specified interval,
            // starting with the oldest backup in that time frame.
            // The order in which the time frames values are checked has to be from the smallest to the largest.
            DateTime now = DateTime.Now;
            foreach (Options.RetentionPolicyValue singleRetentionPolicyOptionValue in retentionPolicyOptionValues.OrderBy(x => x.Timeframe))
            {
                // The timeframe in the retention policy option is only a timespan which has to be applied to the current DateTime to get the actual lower bound
                DateTime timeFrame = (singleRetentionPolicyOptionValue.IsUnlimtedTimeframe()) ? DateTime.MinValue : (now - singleRetentionPolicyOptionValue.Timeframe);

                Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "NextTimeAndFrame", "Next time frame and interval pair: {0}", singleRetentionPolicyOptionValue);

                List<IListResultFileset> backupsInTimeFrame = new List<IListResultFileset>();
                while (clonedBackupList.Count > 0 && clonedBackupList[0].Time >= timeFrame)
                {
                    backupsInTimeFrame.Insert(0, clonedBackupList[0]); // Insert at beginning to reverse order, which is necessary for next step
                    clonedBackupList.RemoveAt(0); // remove from here to not handle the same backup in two time frames
                }

                Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "BackupsInFrame", "Backups in this time frame: {0}", string.Join(", ", backupsInTimeFrame.Select(x => x.Time)));

                // Run through backups in this time frame
                IListResultFileset lastKept = null;
                foreach (IListResultFileset fileset in backupsInTimeFrame)
                {
                    bool isFullBackup = fileset.IsFullBackup == BackupType.FULL_BACKUP;

                    // Keep this backup if
                    // - no backup has yet been added to the time frame (keeps at least the oldest backup in a time frame)
                    // - difference between last added backup and this backup is bigger than the specified interval
                    if (lastKept == null || singleRetentionPolicyOptionValue.IsKeepAllVersions() || (fileset.Time - lastKept.Time) >= singleRetentionPolicyOptionValue.Interval)
                    {
                        Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "KeepBackups", $"Keeping {(isFullBackup ? "" : "partial")} backup: {fileset.Time}", Logging.LogMessageType.Profiling);
                        if (isFullBackup)
                        {
                            lastKept = fileset;
                        }
                    }
                    else
                    {
                        if (isFullBackup)
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "DeletingBackups", "Deleting backup: {0}", fileset.Time);
                            versionsToDelete.Add(fileset);
                        }
                        else
                        {
                            Logging.Log.WriteProfilingMessage(LOGTAG_RETENTION, "KeepBackups", $"Keeping partial backup: {fileset.Time}", Logging.LogMessageType.Profiling);
                        }
                    }
                }

                // Check if most recent backup is outside of this time frame (meaning older/smaller)
                deleteMostRecentBackup &= (mostRecentBackup.Time < timeFrame);
            }

            // Delete all remaining backups
            versionsToDelete.AddRange(clonedBackupList);
            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "BackupsToDelete", "Backups outside of all time frames and thus getting deleted: {0}", string.Join(", ", clonedBackupList.Select(x => x.Time)));

            // Delete most recent backup if allow-full-removal is set and the most current backup is outside of any time frame
            if (deleteMostRecentBackup)
            {
                versionsToDelete.Add(mostRecentBackup);
                Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "DeleteMostRecent", "Deleting most recent backup: {0}", mostRecentBackup.Time);
            }

            Logging.Log.WriteInformationMessage(LOGTAG_RETENTION, "AllBackupsToDelete", "All backups to delete: {0}", string.Join(", ", versionsToDelete.Select(x => x.Time).OrderByDescending(x => x)));

            return versionsToDelete;
        }
    }
}

