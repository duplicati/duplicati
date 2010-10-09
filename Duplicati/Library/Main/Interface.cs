#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// The operations that Duplicati can report
    /// </summary>
    public enum DuplicatiOperation
    {
        /// <summary>
        /// Indicates that the operations is a full or incremental backup
        /// </summary>
        Backup,
        /// <summary>
        /// Indicates that the operation is a restore type operation
        /// </summary>
        Restore,
        /// <summary>
        /// Indicates that the operation is a list type operation
        /// </summary>
        List,
        /// <summary>
        /// Indicates that the operation is a delete type operation
        /// </summary>
        Remove
    };

    /// <summary>
    /// The actual operation that Duplicati supports
    /// </summary>
    public enum DuplicatiOperationMode
    {
        /// <summary>
        /// A backup operation, either full or incremental
        /// </summary>
        Backup,
        /// <summary>
        /// A full backup
        /// </summary>
        BackupFull,
        /// <summary>
        /// An incremental backup
        /// </summary>
        BackupIncremental,
        /// <summary>
        /// A restore operation
        /// </summary>
        Restore,
        /// <summary>
        /// A restore operation for control files
        /// </summary>
        RestoreControlfiles,
        /// <summary>
        /// A backend file listing
        /// </summary>
        List,
        /// <summary>
        /// A list of backup chains found on the backend
        /// </summary>
        GetBackupSets,
        /// <summary>
        /// A list of files found in a specific backup set, produced by summing through incremental signature files
        /// </summary>
        ListCurrentFiles,
        /// <summary>
        /// A list of the source folders found in a specific backup set
        /// </summary>
        ListSourceFolders,
        /// <summary>
        /// A list of files found in a specific backup set, only shows content from the single backup set and not the entire chain
        /// </summary>
        ListActualSignatureFiles,
        /// <summary>
        /// A delete operation performed by looking at the number of existing full backups
        /// </summary>
        DeleteAllButNFull,
        /// <summary>
        /// A delete operation performed by looking at the age of existing backups
        /// </summary>
        DeleteOlderThan,
        /// <summary>
        /// A cleanup operation that removes orphan files
        /// </summary>
        CleanUp
    }

    /// <summary>
    /// A delegate for reporting progress from within the Duplicati module
    /// </summary>
    /// <param name="caller">The instance that is running</param>
    /// <param name="operation">The overall operation type</param>
    /// <param name="specificoperation">A more specific type of operation</param>
    /// <param name="progress">The current overall progress of the operation</param>
    /// <param name="subprogress">The progress of a transfer</param>
    /// <param name="message">A message describing the current operation</param>
    /// <param name="submessage">A message describing the current transfer operation</param>
    public delegate void OperationProgressEvent(Interface caller, DuplicatiOperation operation, DuplicatiOperationMode specificoperation, int progress, int subprogress, string message, string submessage);

    public class Interface : IDisposable, LiveControl.ILiveControl
    {
        /// <summary>
        /// The amount of progressbar allocated for reading incremental data
        /// </summary>
        private const double INCREMENAL_COST = 0.10;
        /// <summary>
        /// The amount of progressbar allocated for uploading async volumes
        /// </summary>
        private const double ASYNC_RESERVED = 0.10;

        private string m_backend;
        private Options m_options;

        /// <summary>
        /// The amount of progressbar allocated for reading incremental data
        /// </summary>
        private double m_incrementalFraction = INCREMENAL_COST;
        /// <summary>
        /// The amount of progressbar allocated for uploading the remaining volumes in asynchronous mode
        /// </summary>
        private double m_asyncReserved = 0.0;
        /// <summary>
        /// The current overall progress without taking the reserved amounts into account
        /// </summary>
        private double m_progress = 0.0;
        /// <summary>
        /// The number of restore patches
        /// </summary>
        private int m_restorePatches = 0;

        private string m_lastProgressMessage = "";

        /// <summary>
        /// A flag toggling if the upload progress is reported, 
        /// used to prevent showing the progress bar when performing
        /// ansynchronous uploads
        /// </summary>
        private bool m_allowUploadProgress = true;
        /// <summary>
        /// When allowing the upload progress to be reported,
        /// there can be a flicker because the upload progresses,
        /// before the flag is disabled again. This value
        /// is set to DateTime.Now.AddSeconds(1) before
        /// entering a potentially blocking operation, which
        /// allows the progress to be reported if the call blocks,
        /// but prevents flicker from non-blocking calls.
        /// </summary>
        private DateTime m_allowUploadProgressAfter = DateTime.Now;

        public event OperationProgressEvent OperationStarted;
        public event OperationProgressEvent OperationCompleted;
        public event OperationProgressEvent OperationProgress;
        public event OperationProgressEvent OperationError;

        /// <summary>
        /// The live control interface
        /// </summary>
        private LiveControl.LiveControl m_liveControl;

        /// <summary>
        /// Returns the current overall operation mode based on the actual operation mode
        /// </summary>
        /// <returns>The overall operation type</returns>
        private DuplicatiOperation GetOperationType()
        {
            switch (m_options.MainAction)
            {
                case DuplicatiOperationMode.Backup:
                case DuplicatiOperationMode.BackupFull:
                case DuplicatiOperationMode.BackupIncremental:
                    return DuplicatiOperation.Backup;
                case DuplicatiOperationMode.Restore:
                case DuplicatiOperationMode.RestoreControlfiles:
                    return DuplicatiOperation.Restore;
                case DuplicatiOperationMode.List:
                case DuplicatiOperationMode.GetBackupSets:
                case DuplicatiOperationMode.ListCurrentFiles:
                case DuplicatiOperationMode.ListSourceFolders:
                case DuplicatiOperationMode.ListActualSignatureFiles:
                    return DuplicatiOperation.List;
                case DuplicatiOperationMode.DeleteAllButNFull:
                case DuplicatiOperationMode.DeleteOlderThan:
                case DuplicatiOperationMode.CleanUp:
                    return DuplicatiOperation.Remove;
                default:
                    throw new Exception(string.Format(Strings.Interface.UnexpectedOperationTypeError, m_options.MainAction));
            }
        }

        /// <summary>
        /// Constructs a new interface for performing backup and restore operations
        /// </summary>
        /// <param name="backend">The url for the backend to use</param>
        /// <param name="options">All required options</param>
        public Interface(string backend, Dictionary<string, string> options)
        {
            m_backend = backend;
            m_options = new Options(options);
            m_liveControl = new LiveControl.LiveControl(System.Threading.Thread.CurrentThread, m_options);
            OperationProgress += new OperationProgressEvent(Interface_OperationProgress);
        }

        /// <summary>
        /// Event handler for the OperationProgres, used to store the last status message
        /// </summary>
        private void Interface_OperationProgress(Interface caller, DuplicatiOperation operation, DuplicatiOperationMode specificoperation, int progress, int subprogress, string message, string submessage)
        {
            m_lastProgressMessage = message;
        }

        /// <summary>
        /// Internal helper to pause and stop when requested
        /// </summary>
        private void CheckLiveControl()
        {
            m_liveControl.PauseIfRequested();

            if (m_liveControl.IsStopRequested)
                throw new LiveControl.ExecutionStoppedException();
        }

        public string Backup(string[] sources)
        {
            BackupStatistics bs = new BackupStatistics(DuplicatiOperationMode.Backup);
            SetupCommonOptions(bs);

            BackendWrapper backend = null;

            if (m_options.DontReadManifests)
                throw new Exception(Strings.Interface.ManifestsMustBeReadOnBackups);

            if (sources == null || sources.Length == 0)
                throw new Exception(Strings.Interface.NoSourceFoldersError);

            //Make sure they all have the same format
            for (int i = 0; i < sources.Length; i++)
                sources[i] = Core.Utility.AppendDirSeparator(sources[i]);

            //Sanity check for duplicate folders and multiple inclusions of the same folder
            for (int i = 0; i < sources.Length - 1; i++)
            {
                if (!System.IO.Directory.Exists(sources[i]))
                    throw new System.IO.IOException(String.Format(Strings.Interface.SourceFolderIsMissingError, sources[i]));

                for (int j = i + 1; j < sources.Length; j++)
                    if (sources[i].Equals(sources[j], Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                        throw new Exception(string.Format(Strings.Interface.SourceDirIsIncludedMultipleTimesError, sources[i]));
                    else if (sources[i].StartsWith(sources[j], Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                        throw new Exception(string.Format(Strings.Interface.SourceDirsAreRelatedError, sources[i], sources[j]));
            }

            if (m_options.AsynchronousUpload)
            {
                m_asyncReserved = ASYNC_RESERVED;
                m_allowUploadProgress = false;
            }

            using (new Logging.Timer("Backup from " + string.Join(";", sources) + " to " + m_backend))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Backup, bs.OperationMode, -1, -1, Strings.Interface.StatusLoadingFilelist, "");
                    OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, -1, -1, Strings.Interface.StatusLoadingFilelist, "");

                    CheckLiveControl();

                    bool full = m_options.Full;

                    backend = new BackendWrapper(bs, m_backend, m_options);
                    backend.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupTransfer_ProgressEvent);
                    backend.AsyncItemProcessedEvent += new EventHandler(backend_AsyncItemProcessedEvent);

                    m_progress = 0.0;

                    OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");
                    
                    CheckLiveControl();

                    List<ManifestEntry> backupsets = backend.GetBackupSets();

                    if (backupsets.Count == 0)
                        full = true;
                    else
                    {
                        //A prioir backup exists, extract the compression and encryption modules used in the most recent entry
                        string compression = null;
                        string encryption = null;
                        for (int i = backupsets.Count - 1; compression == null && i >= 0; i--)
                        {
                            for (int j = backupsets[i].Incrementals.Count - 1; compression == null && j >= 0; j--)
                                for (int k = backupsets[i].Incrementals[j].Volumes.Count - 1; compression == null && k >= 0; k--)
                                {
                                    compression = backupsets[i].Incrementals[j].Volumes[k].Key.Compression;
                                    encryption = backupsets[i].Incrementals[j].Volumes[k].Key.EncryptionMode;
                                }

                            for (int k = backupsets[i].Volumes.Count - 1; compression == null && k >= 0; k--)
                            {
                                compression = backupsets[i].Volumes[k].Key.Compression;
                                encryption = backupsets[i].Volumes[k].Key.EncryptionMode;
                            }
                        }

                        if (compression != null)
                        {
                            m_options.SetEncryptionModuleDefault(encryption);
                            m_options.SetCompressionModuleDefault(compression);
                        }
                    }

                    if (!full)
                        full = DateTime.Now > m_options.FullIfOlderThan(backupsets[backupsets.Count - 1].Time);

                    List<string> controlfiles = new List<string>();
                    if (!string.IsNullOrEmpty(m_options.SignatureControlFiles))
                        controlfiles.AddRange(m_options.SignatureControlFiles.Split(System.IO.Path.PathSeparator));

                    int vol = 0;
                    long totalsize = 0;
                    Manifestfile manifest = new Manifestfile();

                    using (Core.TempFolder tempfolder = new Duplicati.Library.Core.TempFolder())
                    {
                        List<Library.Interface.ICompression> patches = new List<Duplicati.Library.Interface.ICompression>();
                        if (!full)
                        {
                            m_incrementalFraction = INCREMENAL_COST;
                            List<ManifestEntry> entries = new List<ManifestEntry>();
                            entries.Add(backupsets[backupsets.Count - 1]);
                            entries.AddRange(backupsets[backupsets.Count - 1].Incrementals);

                            //Check before we start the download
                            CheckLiveControl();
                            patches = FindPatches(backend, entries, tempfolder, true, bs);

                            //Check before we start the download
                            CheckLiveControl();
                            Manifestfile latest = GetManifest(backend, backupsets[backupsets.Count - 1]);

                            //Manifest version 1 does not support multiple folders
                            if (latest.Version == 1) 
                                latest.SourceDirs = new string[] { sources[0] };

                            if (latest.SourceDirs.Length != sources.Length)
                            {
                                if (m_options.FullIfSourceFolderChanged)
                                {
                                    Logging.Log.WriteMessage("Source folder count changed, issuing full backup", Duplicati.Library.Logging.LogMessageType.Information);
                                    full = true;
                                }
                                else
                                    throw new Exception(string.Format(Strings.Interface.NumberOfSourceFoldersHasChangedError, latest.SourceDirs.Length, sources.Length));
                            }
                            else
                            {

                                if (!m_options.AllowSourceFolderChange)
                                {
                                    foreach (string s1 in latest.SourceDirs)
                                    {
                                        bool found = false;
                                        foreach (string s2 in sources)
                                            if (s1.Equals(s2, Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                                            {
                                                found = true;
                                                break;
                                            }

                                        if (!found)
                                        {
                                            if (m_options.FullIfSourceFolderChanged)
                                            {
                                                Logging.Log.WriteMessage("Source folders changed, issuing full backup", Duplicati.Library.Logging.LogMessageType.Information);
                                                full = true;
                                                break; //Exit the folder loop
                                            }
                                            else
                                                throw new Exception(string.Format(Strings.Interface.SourceFoldersHasChangedError, s1));
                                        }
                                    }

                                    manifest.SourceDirs = latest.SourceDirs;
                                }
                                else
                                {
                                    manifest.SourceDirs = sources;
                                }
                            }
                        }

                        if (full)
                        {
                            patches.Clear();
                            m_incrementalFraction = 0.0;
                            manifest.SourceDirs = sources;
                        }

                        DateTime backuptime = DateTime.Now;

                        OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, -1, -1, Strings.Interface.StatusBuildingFilelist, "");

                        using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(manifest.SourceDirs, bs, m_options.Filter, patches))
                        {
                            CheckLiveControl();

                            dir.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupRSyncDir_ProgressEvent);

                            dir.DisableFiletimeCheck = m_options.DisableFiletimeCheck;
                            dir.MaxFileSize = m_options.SkipFilesLargerThan;
                            using (new Logging.Timer("Initiating multipass"))
                                dir.InitiateMultiPassDiff(full, m_options.SnapShotStrategy, m_options.ExcludeEmptyFolders);

                            string tempVolumeFolder = m_options.AsynchronousUpload ? m_options.AsynchronousUploadFolder : (m_options.TempDir ?? Core.TempFolder.SystemTempPath);

                            bool done = false;
                            while (!done && totalsize < m_options.MaxSize)
                            {
                                using (new Logging.Timer("Multipass " + (vol + 1).ToString()))
                                using (Core.TempFile signaturefile = new Duplicati.Library.Core.TempFile(System.IO.Path.Combine(tempVolumeFolder, Guid.NewGuid().ToString())))
                                using (Core.TempFile contentfile = new Duplicati.Library.Core.TempFile(System.IO.Path.Combine(tempVolumeFolder, Guid.NewGuid().ToString())))
                                {
                                    OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusCreatingVolume, vol + 1), "");

                                    CheckLiveControl();

                                    using (Library.Interface.ICompression signaturearchive = DynamicLoader.CompressionLoader.GetModule(m_options.CompressionModule, signaturefile, m_options.RawOptions))
                                    using (Library.Interface.ICompression contentarchive = DynamicLoader.CompressionLoader.GetModule(m_options.CompressionModule, contentfile, m_options.RawOptions))
                                    {
                                        //If we are all out, stop now, this may cause incomplete partial files
                                        if (m_options.MaxSize - totalsize < (contentarchive.FlushBufferSize + backend.FileSizeOverhead))
                                            break;

                                        //Add signature files to archive
                                        foreach (string s in controlfiles)
                                            if (!string.IsNullOrEmpty(s))
                                                using (System.IO.Stream cs = signaturearchive.CreateFile(System.IO.Path.Combine(RSync.RSyncDir.CONTROL_ROOT, System.IO.Path.GetFileName(s))))
                                                using (System.IO.FileStream fs = System.IO.File.OpenRead(s))
                                                    Core.Utility.CopyStream(fs, cs);

                                        //Only add control files to the very first volume
                                        controlfiles.Clear();

                                        done = dir.MakeMultiPassDiff(signaturearchive, contentarchive, (Math.Min(m_options.VolumeSize, m_options.MaxSize - totalsize)) - backend.FileSizeOverhead);

                                        //TODO: This is not the correct size, we need to account for file size overhead as well
                                        totalsize += signaturearchive.Size;
                                        totalsize += contentarchive.Size;

                                        //TODO: This is not the best way to determine this
                                        if (totalsize >= m_options.MaxSize)
                                            dir.FinalizeMultiPass(signaturearchive, contentarchive, long.MaxValue);

                                    }

                                    if (m_options.AsynchronousUpload)
                                    {
                                        m_lastProgressMessage = Strings.Interface.StatusWaitingForUpload;
                                        m_allowUploadProgress = true;
                                        m_allowUploadProgressAfter = DateTime.Now.AddSeconds(1);
                                    }
                                    else
                                        OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingContentVolume, vol + 1), "");

                                    //Last check before we upload, we do not interrupt transfers
                                    CheckLiveControl();

                                    //The backendwrapper will remove these
                                    signaturefile.Protected = true;
                                    contentfile.Protected = true;

                                    manifest.ContentHashes.Add(Core.Utility.CalculateHash(contentfile));
                                    using (new Logging.Timer("Writing delta file " + (vol + 1).ToString()))
                                        backend.Put(new ContentEntry(backuptime, full, vol + 1), contentfile);

                                    if (!m_options.AsynchronousUpload)
                                        OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingSignatureVolume, vol + 1), "");
                                    
                                    manifest.SignatureHashes.Add(Core.Utility.CalculateHash(signaturefile));
                                    using (new Logging.Timer("Writing remote signatures"))
                                        backend.Put(new SignatureEntry(backuptime, full, vol + 1), signaturefile);
                                }

                                //The backend wrapper will remove these
                                Core.TempFile mf = new Duplicati.Library.Core.TempFile();
                                mf.Protected = true;

                                using (new Logging.Timer("Writing manifest " + backuptime.ToUniversalTime().ToString("yyyyMMddTHHmmssK")))
                                {
                                    manifest.Save(mf);

                                    if (!m_options.AsynchronousUpload)
                                        OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingManifestVolume, vol + 1), "");

                                    //Alternate primary/secondary
                                    backend.Put(new ManifestEntry(backuptime, full, manifest.SignatureHashes.Count % 2 != 0), mf);
                                }

                                if (m_options.AsynchronousUpload)
                                    m_allowUploadProgress = false;

                                //The file volume counter
                                vol++;
                            }
                        }

                        //If we are running asynchronous, we now enter the end-game
                        if (m_options.AsynchronousUpload)
                        {
                            m_lastProgressMessage = Strings.Interface.StatusWaitingForUpload;
                            m_allowUploadProgress = true;
                            m_allowUploadProgressAfter = DateTime.Now;

                            //Before we clear the temp folder, we need to ensure that all volumes are uploaded.
                            //To allow the UI to show some progress while uploading, we perform the remaining 
                            // uploads synchronous
                            List<KeyValuePair<BackupEntryBase, string>> pendingUploads = backend.ExtractPendingUploads();

                            //Figure out what volume number we are at
                            foreach (KeyValuePair<BackupEntryBase, string> p in pendingUploads)
                                if (p.Key is ManifestEntry)
                                    vol--;

                            double unitcost = m_asyncReserved / pendingUploads.Count;

                            //The upload each remaining volume in order
                            foreach (KeyValuePair<BackupEntryBase, string> p in pendingUploads)
                            {
                                string msg;
                                if (p.Key is ManifestEntry)
                                {
                                    vol++;
                                    msg = string.Format(Strings.Interface.StatusUploadingManifestVolume, vol);
                                }
                                else if (p.Key is SignatureEntry)
                                    msg = string.Format(Strings.Interface.StatusUploadingSignatureVolume, ((SignatureEntry)p.Key).Volumenumber);
                                else if (p.Key is ContentEntry)
                                {
                                    msg = string.Format(Strings.Interface.StatusUploadingContentVolume, ((ContentEntry)p.Key).Volumenumber);

                                    //We allow a stop or pause request here
                                    CheckLiveControl();
                                }
                                else
                                    throw new InvalidOperationException();

                                OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, msg, "");
                                backend.Put(p.Key, p.Value);
                                m_asyncReserved -= unitcost;
                                m_progress += unitcost;
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                        //If this is a controlled user-requested stop, wait for the current upload to complete
                    if (backend != null && ex is LiveControl.ExecutionStoppedException)
                    {
                        try
                        {
                            if (m_options.AsynchronousUpload)
                            {
                                m_lastProgressMessage = Strings.Interface.StatusWaitingForUpload;
                                m_allowUploadProgress = true;
                                m_allowUploadProgressAfter = DateTime.Now;

                                //Wait for the current upload to complete and then delete all remaining temporary files
                                foreach (KeyValuePair<BackupEntryBase, string> p in backend.ExtractPendingUploads())
                                    try
                                    {
                                        if (System.IO.File.Exists(p.Value))
                                            System.IO.File.Delete(p.Value);
                                    }
                                    catch { } //Better to delete as many as possible rather than choke on a single file
                            }

                        }
                        catch { } //We already have an exception, just go with that
                    }

                    if (backend == null || backend.ManifestUploads == 0)
                        throw; //This also activates "finally", unlike in other languages...

                    bs.LogError(string.Format(Strings.Interface.PartialUploadMessage, backend.ManifestUploads, ex.Message), ex);
                }
                finally
                {
                    m_progress = 100.0;
                    if (backend != null)
                        try { backend.Dispose(); }
                        catch { }

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Backup, bs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");
                    
                    OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");
                }
            }

            bs.EndTime = DateTime.Now;
            return bs.ToString();
        }

        /// <summary>
        /// Event handler that is activated when an asynchronous upload is about to being,
        /// used to pause uploads if the user has requested a pause
        /// </summary>
        /// <param name="sender">Unused sender argument</param>
        /// <param name="e">Unused event argument</param>
        private void backend_AsyncItemProcessedEvent(object sender, EventArgs e)
        {
            m_liveControl.PauseIfRequested();
        }

        /// <summary>
        /// Event handler for reporting upload/download progress
        /// </summary>
        /// <param name="progress">The upload/download progress in percent</param>
        /// <param name="filename">The name of the file being transfered</param>
        private void BackupTransfer_ProgressEvent(int progress, string filename)
        {
            if (m_allowUploadProgress && DateTime.Now > m_allowUploadProgressAfter)
                OperationProgress(this, GetOperationType(), m_options.MainAction, (int)(m_progress * 100), progress, m_lastProgressMessage, filename);
        }

        /// <summary>
        /// Event handler for reporting backup progress
        /// </summary>
        /// <param name="progress">The total progress in percent</param>
        /// <param name="filename">The file currently being examined</param>
        private void BackupRSyncDir_ProgressEvent(int progress, string filename)
        {
            m_progress = ((1.0 - m_incrementalFraction) * (progress / (double)100.0)) + m_incrementalFraction;
            m_progress *= (1.0 - m_asyncReserved);

            OperationProgress(this, GetOperationType(), m_options.MainAction, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusProcessing, filename), "");

            CheckLiveControl();
        }

        /// <summary>
        /// Event handler for reporting restore progress
        /// </summary>
        /// <param name="progress">The total progress in percent</param>
        /// <param name="filename">The file currently being examined</param>
        private void RestoreRSyncDir_ProgressEvent(int progress, string filename)
        {
            double pgPrPatch = ((1.0 - INCREMENAL_COST) / m_restorePatches);
            int fullProgress = (int)((m_progress * 100) + (pgPrPatch * (progress / 100.0)));

            OperationProgress(this, GetOperationType(), m_options.MainAction, fullProgress, -1, string.Format(Strings.Interface.StatusProcessing, filename), "");

            CheckLiveControl();
        }

        /// <summary>
        /// Will attempt to read the manifest file, optionally reverting to the secondary manifest if reading one fails.
        /// </summary>
        /// <param name="backend">The backendwrapper to read from</param>
        /// <param name="entry">The manifest to read</param>
        /// <returns>The parsed manifest</returns>
        private Manifestfile GetManifest(BackendWrapper backend, ManifestEntry entry)
        {
            if (m_options.DontReadManifests)
            {
                Manifestfile mf = new Manifestfile();
                mf.SignatureHashes = null;
                mf.ContentHashes = null;
                return mf;
            }

            bool parsingError = false;

            using (new Logging.Timer("Get " + entry.Filename))
            using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
            {
                try
                {
                    backend.Get(entry, tf, null);
                    
                    //We now have the file decrypted, if the next step fails,
                    // its a broken xml or invalid content
                    parsingError = true;
                    Manifestfile mf = new Manifestfile(tf);
                    if (m_options.SkipFileHashChecks)
                    {
                        mf.SignatureHashes = null;
                        mf.ContentHashes = null;
                    }
                    return mf;
                }
                catch (Exception ex)
                {
                    //Only try secondary if the parsing/decrypting fails, not if the transfer fails
                    if (entry.Alternate != null && (ex is System.Security.Cryptography.CryptographicException || parsingError))
                    {
                        //TODO: If it is a version error, there is no need to read the alternate version
                        Logging.Log.WriteMessage(string.Format(Strings.Interface.PrimaryManifestReadErrorLogMessage, entry.Filename, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning);
                        try
                        {
                            Logging.Log.WriteMessage(string.Format(Strings.Interface.ReadingSecondaryManifestLogMessage, entry.Alternate.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                            return GetManifest(backend, entry.Alternate);
                        }
                        catch (Exception ex2)
                        {
                            Logging.Log.WriteMessage(string.Format(Strings.Interface.SecondaryManifestReadErrorLogMessage, entry.Alternate.Filename, ex2.Message), Duplicati.Library.Logging.LogMessageType.Warning);
                        }
                    }

                    //Report the original error
                    throw;
                }
            }
        }

        public string Restore(string[] target)
        {
            RestoreStatistics rs = new RestoreStatistics(DuplicatiOperationMode.Restore);
            SetupCommonOptions(rs);

            m_progress = 0;
            BackendWrapper backend = null;
            m_restorePatches = 0;

            using (new Logging.Timer("Restore from " + m_backend + " to " + target))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, rs.OperationMode, -1, -1, Strings.Interface.StatusStarted, "");
                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, -1, -1, Strings.Interface.StatusStarted, "");

                    Core.FilenameFilter filter = m_options.Filter;

                    //Filter is prefered, if both file and filter is specified
                    if (!m_options.HasFilter && !string.IsNullOrEmpty(m_options.FileToRestore))
                    {
                        List<Core.IFilenameFilter> list = new List<Duplicati.Library.Core.IFilenameFilter>();
                        list.Add(new Core.FilelistFilter(true, m_options.FileToRestore.Split(System.IO.Path.PathSeparator)));
                        list.Add(new Core.RegularExpressionFilter(false, ".*"));

                        filter = new Duplicati.Library.Core.FilenameFilter(list);
                    }

                    backend = new BackendWrapper(rs, m_backend, m_options);
                    backend.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupTransfer_ProgressEvent);

                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");

                    ManifestEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);

                    m_progress = INCREMENAL_COST;

                    List<ManifestEntry> entries = new List<ManifestEntry>();
                    entries.Add(bestFit);
                    entries.AddRange(bestFit.Incrementals);
                    int patchno = 0;
                    
                    foreach (ManifestEntry be in entries)
                        m_restorePatches += be.Volumes.Count;

                    using (RSync.RSyncDir sync = new Duplicati.Library.Main.RSync.RSyncDir(target, rs, filter))
                    {
                        sync.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(RestoreRSyncDir_ProgressEvent);

                        foreach (ManifestEntry be in entries)
                        {
                            m_progress = ((1.0 - INCREMENAL_COST) * (patchno / (double)m_restorePatches)) + INCREMENAL_COST;

                            OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusReadingManifest, be.Filename), "");
                            
                            CheckLiveControl();

                            Manifestfile manifest = GetManifest(backend, be);

                            CheckLiveControl();

                            foreach (KeyValuePair<SignatureEntry, ContentEntry> vol in be.Volumes)
                            {
                                ContentEntry contentVol = vol.Value;
                                SignatureEntry signatureVol = vol.Key;

                                m_progress = ((1.0 - INCREMENAL_COST) * (patchno / (double)m_restorePatches)) + INCREMENAL_COST;

                                //Skip nonlisted
                                if (manifest.ContentHashes != null && contentVol.Volumenumber > manifest.ContentHashes.Count)
                                {
                                    Logging.Log.WriteMessage(string.Format(Strings.Interface.SkippedContentVolumeLogMessage, contentVol.Volumenumber), Duplicati.Library.Logging.LogMessageType.Warning);
                                    rs.LogWarning(string.Format(Strings.Interface.SkippedContentVolumeLogMessage, contentVol.Volumenumber), null);
                                    patchno++;
                                    continue;
                                }

                                using (Core.TempFile patchzip = new Duplicati.Library.Core.TempFile())
                                {
                                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusPatching, patchno + 1), "");

                                    CheckLiveControl();

                                     if (m_options.HasFilter || !string.IsNullOrEmpty(m_options.FileToRestore))
                                     {
                                         bool hasFiles = false;

                                         using (Core.TempFile sigFile = new Duplicati.Library.Core.TempFile())
                                         {
                                             OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusDownloadingSignatureVolume, patchno + 1), "");

                                             try
                                             {
                                                 using (new Logging.Timer("Get " + signatureVol.Filename))
                                                     backend.Get(signatureVol, sigFile, manifest.SignatureHashes == null ? null : manifest.SignatureHashes[signatureVol.Volumenumber - 1]);
                                             }
                                             catch (BackendWrapper.HashMismathcException hme)
                                             {
                                                 hasFiles = true;
                                                 rs.LogError(string.Format(Strings.Interface.FileHashFailure, hme.Message), hme);
                                             }

                                             if (!hasFiles)
                                                 using (Library.Interface.ICompression patch = DynamicLoader.CompressionLoader.GetModule(signatureVol.Compression, sigFile, m_options.RawOptions))
                                                 {
                                                     foreach(KeyValuePair<RSync.RSyncDir.PatchFileType, string> k in sync.ListPatchFiles(patch))
                                                         if (filter.ShouldInclude("", System.IO.Path.DirectorySeparatorChar.ToString() + k.Value))
                                                         {
                                                             //TODO: Perhaps a bit much to download the content archive
                                                             // if the file is only marked for deletion?
                                                             hasFiles = true; 
                                                             break;
                                                         }
                                                 }
                                         }

                                         if (!hasFiles)
                                             continue; //Avoid downloading the content file
                                    }

                                     OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusDownloadingContentVolume, patchno + 1), "");

                                    using (new Logging.Timer("Get " + contentVol.Filename))
                                        backend.Get(contentVol, patchzip, manifest.ContentHashes == null ? null : manifest.ContentHashes[contentVol.Volumenumber - 1]);

                                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusPatching, patchno + 1), "");
                                    
                                    using (new Logging.Timer((patchno == 0 ? "Full restore to: " : "Incremental restore " + patchno.ToString() + " to: ") + string.Join(System.IO.Path.PathSeparator.ToString(), target)))
                                    using (Library.Interface.ICompression patch = DynamicLoader.CompressionLoader.GetModule(contentVol.Compression, patchzip, m_options.RawOptions))
                                        sync.Patch(target, patch);
                                }
                                patchno++;
                            }

                            //Make sure there are no partial files, as partial files are not allowed to span backup sets
                            sync.FinalizeRestore();
                        }
                    }
                }
                finally
                {
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Restore, rs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");

                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");
                }
            } 

            rs.EndTime = DateTime.Now;

            //TODO: The RS should have the number of restored files, and the size of those
            //but that is a little difficult, because some may be restored, and then removed

            return rs.ToString();
        }

        /// <summary>
        /// Restores control files added to a backup.
        /// </summary>
        /// <param name="target">The folder into which to restore the files</param>
        /// <returns>A restore report</returns>
        public string RestoreControlFiles(string target)
        {
            RestoreStatistics rs = new RestoreStatistics(DuplicatiOperationMode.RestoreControlfiles);
            SetupCommonOptions(rs);

            BackendWrapper backend = null;

            using (new Logging.Timer("Restore control files from " + m_backend + " to " + target))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, rs.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

                    backend = new BackendWrapper(rs, m_backend, m_options);

                    List<ManifestEntry> attempts = backend.GetBackupSets();

                    List<ManifestEntry> flatlist = new List<ManifestEntry>();
                    foreach (ManifestEntry be in attempts)
                    {
                        flatlist.Add(be);
                        flatlist.AddRange(be.Incrementals);
                    }

                    flatlist.Reverse();

                    string prefix = Core.Utility.AppendDirSeparator(RSync.RSyncDir.CONTROL_ROOT);

                    foreach (ManifestEntry be in flatlist)
                    {
                        if (be.Volumes.Count > 0)
                            using(Core.TempFile z = new Duplicati.Library.Core.TempFile())
                            {
                                OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, 0, -1, string.Format(Strings.Interface.StatusReadingIncrementalFile, be.Volumes[0].Key.Filename), "");

                                Manifestfile mf = GetManifest(backend, be);

                                using (new Logging.Timer("Get " + be.Volumes[0].Key.Filename))
                                    backend.Get(be.Volumes[0].Key, z, mf.SignatureHashes == null ? null : mf.SignatureHashes[0]);
                                
                                using(Library.Interface.ICompression fz = DynamicLoader.CompressionLoader.GetModule(be.Volumes[0].Key.Compression, z, m_options.RawOptions))
                                {
                                    bool any = false;
                                    foreach (string f in fz.ListFiles(prefix))
                                    {
                                        any = true;
                                        using (System.IO.Stream s1 = fz.OpenRead(f))
                                        using (System.IO.Stream s2 = System.IO.File.Create(System.IO.Path.Combine(target, f.Substring(prefix.Length))))
                                            Core.Utility.CopyStream(s1, s2);
                                    }

                                    if (any)
                                        break;

                                    rs.LogError(string.Format(Strings.Interface.FailedToFindControlFilesMessage, be.Volumes[0].Key.Filename), null);
                                }
                            }
                    }

                }
                finally
                {
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Restore, rs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");
                }
            }

            rs.EndTime = DateTime.Now;

            return rs.ToString();
        }

        public string DeleteAllButNFull()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.DeleteAllButNFull);
            SetupCommonOptions(stats);

            int x = Math.Max(0, m_options.DeleteAllButNFull);

            StringBuilder sb = new StringBuilder();

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, stats.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

                List<ManifestEntry> entries = backend.GetBackupSets();
                List<ManifestEntry> toremove = new List<ManifestEntry>();

                while (entries.Count > x)
                {
                    if (entries.Count == 1 && !m_options.AllowFullRemoval)
                    {
                        sb.AppendLine(string.Format(Strings.Interface.NotDeletingLastFullMessage, entries[0].Time));
                        break;
                    }

                    ManifestEntry be = entries[0];
                    entries.RemoveAt(0);

                    be.Incrementals.Reverse();
                    toremove.AddRange(be.Incrementals);
                    toremove.Add(be);
                }

                if (entries.Count == 0 && toremove.Count > 0 && !m_options.AllowFullRemoval)
                    throw new Exception(Strings.Interface.InternalDeleteCountError);

                sb.Append(RemoveBackupSets(backend, toremove));
            }
            finally
            {
                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.Remove, stats.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");
            }

            return sb.ToString();
        }

        public string DeleteOlderThan()
        {
            StringBuilder sb = new StringBuilder();
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.DeleteOlderThan);
            SetupCommonOptions(stats);

            DateTime expires = m_options.RemoveOlderThan;

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, stats.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

                List<ManifestEntry> entries = backend.GetBackupSets();
                List<ManifestEntry> toremove = new List<ManifestEntry>();

                while (entries.Count > 0 && entries[0].Time <= expires)
                {
                    if (entries.Count == 1 && !m_options.AllowFullRemoval)
                    {
                        sb.AppendLine(string.Format(Strings.Interface.NotDeletingLastFullMessage, entries[0].Time));
                        break;
                    }

                    ManifestEntry be = entries[0];
                    entries.RemoveAt(0);

                    bool hasNewer = false;
                    foreach (ManifestEntry bex in be.Incrementals)
                        if (bex.Time >= expires)
                        {
                            hasNewer = true;
                            break;
                        }

                    if (hasNewer)
                    {
                        List<ManifestEntry> t = new List<ManifestEntry>(be.Incrementals);
                        t.Insert(0, be);

                        for (int i = 0; i < t.Count; i++)
                            if (t[i].Time <= expires)
                                sb.AppendLine(string.Format(Strings.Interface.NotDeletingBackupSetMessage, t[i].Time.ToString(System.Globalization.CultureInfo.InvariantCulture)));

                        break;
                    }
                    else
                    {
                        be.Incrementals.Reverse();
                        toremove.AddRange(be.Incrementals);
                        toremove.Add(be);
                    }
                }

                if (entries.Count == 0 && toremove.Count > 0 && !m_options.AllowFullRemoval)
                    throw new Exception(Strings.Interface.InternalDeleteCountError);

                sb.Append(RemoveBackupSets(backend, toremove));
            }
            finally
            {
                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.Remove, stats.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");
            }

            return sb.ToString();
        }

        private string RemoveBackupSets(BackendWrapper backend, List<ManifestEntry> entries)
        {
            StringBuilder sb = new StringBuilder();

            foreach (ManifestEntry me in entries)
            {
                sb.AppendLine(string.Format(Strings.Interface.DeletingBackupSetMessage, me.Time.ToString(System.Globalization.CultureInfo.InvariantCulture)));

                if (m_options.Force)
                {
                    //Delete manifest
                    if (me.Alternate != null)
                        backend.Delete(me.Alternate);

                    backend.Delete(me);

                    foreach (KeyValuePair<SignatureEntry, ContentEntry> kx in me.Volumes)
                    {
                        backend.Delete(kx.Key);
                        backend.Delete(kx.Value);
                    }
                }
            }

            if (!m_options.Force && entries.Count > 0)
                sb.AppendLine(Strings.Interface.FilesAreNotForceDeletedMessage);

            return sb.ToString();
        }


        public string[] List()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.List);
            SetupCommonOptions(stats);

            List<string> res = new List<string>();
            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.List, stats.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

                foreach (Duplicati.Library.Interface.IFileEntry fe in backend.List())
                    res.Add(fe.Name);

                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.List, stats.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");

                return res.ToArray();
            }
        }

        public string Cleanup()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.CleanUp);
            SetupCommonOptions(stats);

            bool anyRemoved = false;
            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            {
                List<ManifestEntry> sorted = backend.GetBackupSets();

                List<ManifestEntry> entries = new List<ManifestEntry>();
                entries.AddRange(sorted);
                foreach (ManifestEntry be in sorted)
                    entries.AddRange(be.Incrementals);

                backend.DeleteOrphans();

                if (m_options.SkipFileHashChecks)
                    throw new Exception(Strings.Interface.CannotCleanWithoutHashesError);
                if (m_options.DontReadManifests)
                    throw new Exception(Strings.Interface.CannotCleanWithoutHashesError);

                //Now compare the actual filelist with the manifest
                foreach (ManifestEntry be in entries)
                {
                    Manifestfile manifest = GetManifest(backend, be);

                    int count = manifest.ContentHashes.Count;

                    for (int i = count - 1; i < be.Volumes.Count; i++)
                    {
                        anyRemoved = true;
                        Logging.Log.WriteMessage(string.Format(Strings.Interface.RemovingPartialFilesMessage, be.Volumes[i].Key.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                        Logging.Log.WriteMessage(string.Format(Strings.Interface.RemovingPartialFilesMessage, be.Volumes[i].Value.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                        if (m_options.Force)
                        {
                            backend.Delete(be.Volumes[i].Key);
                            backend.Delete(be.Volumes[i].Value);
                        }
                    }
                }
            }

            if (!m_options.Force && anyRemoved)
                Logging.Log.WriteMessage(Strings.Interface.FilesAreNotForceDeletedMessage, Duplicati.Library.Logging.LogMessageType.Information);

            return ""; //TODO: Write a message here?
        }

        public IList<string> ListCurrentFiles()
        {
            RestoreStatistics rs = new RestoreStatistics(DuplicatiOperationMode.ListCurrentFiles);
            SetupCommonOptions(rs);

            Core.FilenameFilter filter = m_options.Filter;
            DateTime timelimit = m_options.RestoreTime;

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, rs.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

            List<string> res;

            using (BackendWrapper backend = new BackendWrapper(rs, m_backend, m_options))
            using (Core.TempFolder basefolder = new Duplicati.Library.Core.TempFolder())
            {
                ManifestEntry bestFit = backend.GetBackupSet(timelimit);

                List<ManifestEntry> entries = new List<ManifestEntry>();
                entries.Add(bestFit);
                entries.AddRange(bestFit.Incrementals);

                List<Library.Interface.ICompression> patches = FindPatches(backend, entries, basefolder, false, rs);

                using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(new string[] { basefolder }, rs, filter, patches))
                    res = dir.UnmatchedFiles();
            }

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, rs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");

            return res;
        }

        public string[] ListSourceFolders()
        {
            RestoreStatistics rs = new RestoreStatistics(DuplicatiOperationMode.ListSourceFolders);
            SetupCommonOptions(rs);

            Core.FilenameFilter filter = m_options.Filter;
            DateTime timelimit = m_options.RestoreTime;

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, rs.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

            string[] res;

            using (BackendWrapper backend = new BackendWrapper(rs, m_backend, m_options))
            using (Core.TempFile mfile = new Duplicati.Library.Core.TempFile())
            {
                ManifestEntry bestFit = backend.GetBackupSet(timelimit);

                backend.Get(bestFit, mfile, null);
                res = new Manifestfile(mfile).SourceDirs;
            }

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, rs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");

            return res;
        }


        private void SetupCommonOptions(CommunicationStatistics stats)
        {
            m_options.MainAction = stats.OperationMode;

            Library.Logging.Log.LogLevel = m_options.Loglevel;

            if (!string.IsNullOrEmpty(m_options.Logfile))
                Library.Logging.Log.CurrentLog = new Library.Logging.StreamLog(m_options.Logfile);

            if (stats != null)
                stats.VerboseErrors = m_options.DebugOutput;

            if (!string.IsNullOrEmpty(m_options.TempDir))
                Core.TempFolder.SystemTempPath = m_options.TempDir;

            if (!string.IsNullOrEmpty(m_options.ThreadPriority))
                System.Threading.Thread.CurrentThread.Priority = Core.Utility.ParsePriority(m_options.ThreadPriority);

            //Load all generic modules
            m_options.LoadedModules.Clear();

            foreach (Library.Interface.IGenericModule m in DynamicLoader.GenericLoader.Modules)
                m_options.LoadedModules.Add(new KeyValuePair<bool, Library.Interface.IGenericModule>(Array.IndexOf<string>(m_options.DisableModules, m.Key.ToLower()) < 0 && (m.LoadAsDefault || Array.IndexOf<string>(m_options.EnableModules, m.Key.ToLower()) >= 0), m));

            ValidateOptions(stats);

            foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                if (mx.Key)
                    mx.Value.Configure(m_options.RawOptions);
        }

        /// <summary>
        /// Downloads all required signature files from the backend.
        /// </summary>
        /// <param name="backend">The backend to read from</param>
        /// <param name="entries">The flattened list of manifests</param>
        /// <param name="tempfolder">The tempfolder set for this operation</param>
        /// <param name="allowHashFail">True to ignore files with failed hash signature</param>
        /// <returns>A list of file archives</returns>
        private List<Library.Interface.ICompression> FindPatches(BackendWrapper backend, List<ManifestEntry> entries, string tempfolder, bool allowHashFail, CommunicationStatistics stat)
        {
            List<Library.Interface.ICompression> patches = new List<Library.Interface.ICompression>();

            using (new Logging.Timer("Reading incremental data"))
            {
                OperationProgress(this, GetOperationType(), stat.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementalData, "");

                //Calculate the total number of files to download
                //, and verify their order
                int incCount = 0;
                foreach (ManifestEntry be in entries)
                {
                    int volNo = 0;
                    //Prevent order based bugs
                    if (entries.IndexOf(be) > 0)
                        if (entries[entries.IndexOf(be) - 1].Time >= be.Time)
                            throw new Exception(Strings.Interface.BadSortingDetectedError);

                    incCount++;
                    foreach (KeyValuePair<SignatureEntry, ContentEntry> bes in be.Volumes)
                    {
                        incCount++;
                        if (volNo + 1 != bes.Key.Volumenumber || bes.Key.Volumenumber != bes.Value.Volumenumber)
                            throw new Exception(Strings.Interface.BadVolumeSortOrder);

                        volNo++;
                    }
                }

                //The incremental part has a fixed cost, and each file has a fixed fraction of that
                double unitCost = m_incrementalFraction / incCount;

                foreach (ManifestEntry be in entries)
                {
                    m_progress += unitCost;

                    OperationProgress(this, GetOperationType(), stat.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusReadingManifest, be.Time.ToShortDateString() + " " + be.Time.ToShortTimeString()), "");

                    Manifestfile manifest = GetManifest(backend, be);

                    foreach (KeyValuePair<SignatureEntry, ContentEntry> bes in be.Volumes)
                    {
                        m_progress += unitCost;

                        //Skip non-listed incrementals
                        if (manifest.SignatureHashes != null && bes.Key.Volumenumber > manifest.SignatureHashes.Count)
                        {
                            backend.AddOrphan(bes.Key);
                            backend.AddOrphan(bes.Value);
                            continue;
                        }

                        OperationProgress(this, GetOperationType(), stat.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusReadingSignatureFile, be.Time.ToShortDateString() + " " + be.Time.ToShortTimeString(), bes.Key.Volumenumber), "");

                        string filename = System.IO.Path.Combine(tempfolder, "patch-" + patches.Count.ToString() + ".zip");

                        //Check just before we download stuff
                        CheckLiveControl();
                        try
                        {
                            using (new Logging.Timer("Get " + bes.Key.Filename))
                                backend.Get(bes.Key, filename, manifest.SignatureHashes == null ? null : manifest.SignatureHashes[bes.Key.Volumenumber - 1]);
                        }
                        catch (BackendWrapper.HashMismathcException hme)
                        {
                            if (allowHashFail)
                            {
                                if (stat != null)
                                    stat.LogError(string.Format(Strings.Interface.FileHashFailure, hme.Message), hme);
                                continue;
                            }
                            else
                                throw;
                        }

                        patches.Add(DynamicLoader.CompressionLoader.GetModule(bes.Key.Compression, filename, m_options.RawOptions));
                    }
                }
            }

            backend.DeleteOrphans();

            return patches;
        }

        public void PurgeSignatureCache()
        {
            if (string.IsNullOrEmpty(m_options.SignatureCachePath))
                throw new Exception(Strings.Interface.SignatureCachePathMissingError);
            else
                RemoveSignatureFiles(m_options.SignatureCachePath);
        }

        public List<ManifestEntry> GetBackupSets()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.GetBackupSets);
            SetupCommonOptions(stats);

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
                return backend.GetBackupSets();
        }

        public List<KeyValuePair<RSync.RSyncDir.PatchFileType, string>> ListActualSignatureFiles()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.ListActualSignatureFiles);
            SetupCommonOptions(stats);

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            {
                ManifestEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);
                if (bestFit.Incrementals.Count > 0) //Get the most recent incremental
                    bestFit = bestFit.Incrementals[bestFit.Incrementals.Count - 1];

                using (Core.TempFolder folder = new Duplicati.Library.Core.TempFolder())
                {
                    List<Library.Interface.ICompression> patches = FindPatches(backend, new List<ManifestEntry>(new ManifestEntry[] { bestFit }), folder, false, stats);
                    using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(new string[] { folder }, stats, null))
                        return dir.ListPatchFiles(patches);
                }
            }

        }

        /// <summary>
        /// This function will examine all options passed on the commandline, and test for unsupported or deprecated values.
        /// Any errors will be logged into the statistics module.
        /// </summary>
        /// <param name="options">The commandline options given</param>
        /// <param name="backend">The backend url</param>
        /// <param name="stats">The statistics into which warnings are written</param>
        private void ValidateOptions(CommunicationStatistics stats)
        {
            //No point in going through with this if we can't report
            if (stats == null)
                return;

            //Keep a list of all supplied options
            Dictionary<string, string> ropts = m_options.RawOptions;
            
            //Keep a list of all supported options
            Dictionary<string, Library.Interface.ICommandLineArgument> supportedOptions = new Dictionary<string, Library.Interface.ICommandLineArgument>();

            //There are a few internal options that are not accessible from outside, and thus not listed
            foreach (string s in Options.InternalOptions)
                supportedOptions[s] = null;

            //Figure out what module options are supported in the current setup
            List<Library.Interface.ICommandLineArgument> moduleOptions = new List<Duplicati.Library.Interface.ICommandLineArgument>();
            Dictionary<string, string> disabledModuleOptions = new Dictionary<string, string>();

            foreach (KeyValuePair<bool, Library.Interface.IGenericModule> m in m_options.LoadedModules)
                if (m.Value.SupportedCommands != null)
                    if (m.Key)
                        moduleOptions.AddRange(m.Value.SupportedCommands);
                    else
                    {
                        foreach (Library.Interface.ICommandLineArgument c in m.Value.SupportedCommands)
                        {
                            disabledModuleOptions[c.Name] = m.Value.DisplayName + " (" + m.Value.Key + ")";

                            if (c.Aliases != null)
                                foreach (string s in c.Aliases)
                                    disabledModuleOptions[s] = disabledModuleOptions[c.Name];
                        }
                    }

            //Now run through all supported options, and look for deprecated options
            foreach (IList<Library.Interface.ICommandLineArgument> l in new IList<Library.Interface.ICommandLineArgument>[] { 
                m_options.SupportedCommands, 
                DynamicLoader.BackendLoader.GetSupportedCommands(new Uri(m_backend).Scheme.ToLower()), 
                m_options.NoEncryption ? null : DynamicLoader.EncryptionLoader.GetSupportedCommands(m_options.EncryptionModule),
                moduleOptions,
                DynamicLoader.CompressionLoader.GetSupportedCommands(m_options.CompressionModule) })
            {
                if (l != null)
                    foreach (Library.Interface.ICommandLineArgument a in l)
                    {
                        if (supportedOptions.ContainsKey(a.Name) && Array.IndexOf(Options.KnownDuplicates, a.Name.ToLower()) < 0)
                            stats.LogWarning(string.Format(Strings.Interface.DuplicateOptionNameWarning, a.Name), null);

                        supportedOptions[a.Name] = a;

                        if (a.Aliases != null)
                            foreach (string s in a.Aliases)
                            {
                                if (supportedOptions.ContainsKey(s) && Array.IndexOf(Options.KnownDuplicates, s.ToLower()) < 0)
                                    stats.LogWarning(string.Format(Strings.Interface.DuplicateOptionNameWarning, s), null);

                                supportedOptions[s] = a;
                            }

                        if (a.Deprecated)
                        {
                            List<string> aliases = new List<string>();
                            aliases.Add(a.Name);
                            if (a.Aliases != null)
                                aliases.AddRange(a.Aliases);

                            foreach (string s in aliases)
                                if (ropts.ContainsKey(s))
                                {
                                    string optname = a.Name;
                                    if (a.Name != s)
                                        optname += " (" + s + ")";

                                    stats.LogWarning(string.Format(Strings.Interface.DeprecatedOptionUsedWarning, optname, a.DeprecationMessage), null);
                                }

                        }
                    }
            }

            //Now look for options that were supplied but not supported
            foreach (string s in ropts.Keys)
                if (!supportedOptions.ContainsKey(s))
                    if (disabledModuleOptions.ContainsKey(s))
                        stats.LogWarning(string.Format(Strings.Interface.UnsupportedOptionDisabledModuleWarning, s, disabledModuleOptions[s]), null);
                    else
                        stats.LogWarning(string.Format(Strings.Interface.UnsupportedOptionWarning, s), null);

            //Look at the value supplied for each argument and see if is valid according to its type
            foreach (string s in ropts.Keys)
            {
                Library.Interface.ICommandLineArgument arg;
                if (supportedOptions.TryGetValue(s, out arg) && arg != null)
                {
                    string validationMessage = ValidateOptionValue(arg, s, ropts[s]);
                    if (validationMessage != null)
                        stats.LogWarning(validationMessage, null);
                }
            }
        }

        #region Static interface

        /// <summary>
        /// Checks if the value passed to an option is actually valid.
        /// </summary>
        /// <param name="arg">The argument being validated</param>
        /// <param name="optionname">The name of the option to validate</param>
        /// <param name="value">The value to check</param>
        /// <returns>Null if no errors are found, an error message otherwise</returns>
        public static string ValidateOptionValue(Library.Interface.ICommandLineArgument arg, string optionname, string value)
        {
            if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration)
            {
                bool found = false;
                foreach (string v in arg.ValidValues ?? new string[0])
                    if (string.Equals(v, value, StringComparison.CurrentCultureIgnoreCase))
                    {
                        found = true;
                        break;
                    }

                if (!found)
                    return string.Format(Strings.Interface.UnsupportedEnumerationValue, optionname, value, string.Join(", ", arg.ValidValues ?? new string[0]));

            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean)
            {
                if (!string.IsNullOrEmpty(value) && Core.Utility.ParseBool(value, true) != Core.Utility.ParseBool(value, false))
                    return string.Format(Strings.Interface.UnsupportedBooleanValue, optionname, value);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Integer)
            {
                long l;
                if (!long.TryParse(value, out l))
                    return string.Format(Strings.Interface.UnsupportedIntegerValue, optionname, value);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path)
            {
                foreach (string p in value.Split(System.IO.Path.DirectorySeparatorChar))
                    if (p.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                        return string.Format(Strings.Interface.UnsupportedPathValue, optionname, p);
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Size)
            {
                try
                {
                    Core.Sizeparser.ParseSize(value);
                }
                catch
                {
                    return string.Format(Strings.Interface.UnsupportedSizeValue, optionname, value);
                }
            }
            else if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan)
            {
                try
                {
                    Core.Timeparser.ParseTimeSpan(value);
                }
                catch
                {
                    return string.Format(Strings.Interface.UnsupportedTimeValue, optionname, value);
                }
            }

            return null;
        }

        public static void RemoveSignatureFiles(string folder)
        {
            FilenameStrategy cachenames = BackendWrapper.CreateCacheFilenameStrategy();
            foreach (string s in Core.Utility.EnumerateFiles(folder))
            {
                BackupEntryBase e = cachenames.ParseFilename(new Duplicati.Library.Interface.FileEntry(System.IO.Path.GetFileName(s)));
                if (e is SignatureEntry)
                {
                    try { System.IO.File.Delete(s); }
                    catch {}
                }
            }
        }
            
        //Static helpers

        public static string[] List(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.List();
        }

        public static string Backup(string[] source, string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.Backup(source);
        }

        public static string Restore(string source, string[] target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(source, options))
                return i.Restore(target);
        }

        /// <summary>
        /// Returns a list of full backup manifests, with a list of volumes and an incremental chain
        /// </summary>
        /// <param name="target">The backend to read the data from</param>
        /// <param name="options">A list of options</param>
        /// <returns>A list of full backup manifests</returns>
        public static List<ManifestEntry> ParseFileList(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.GetBackupSets();
        }

        public static IList<string> ListCurrentFiles(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.ListCurrentFiles();
        }

        public static string[] ListSourceFolders(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.ListSourceFolders();
        }

        public static string DeleteAllButNFull(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.DeleteAllButNFull();
        }

        public static string DeleteOlderThan(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.DeleteOlderThan();
        }

        public static string Cleanup(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.Cleanup();
        }

        public static string RestoreControlFiles(string source, string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(source, options))
                return i.RestoreControlFiles(target);
        }

        public static void PurgeSignatureCache(Dictionary<string, string> options)
        {
            Options opt = new Options(options);
            if (string.IsNullOrEmpty(opt.SignatureCachePath))
                throw new Exception(Strings.Interface.SignatureCachePathMissingError);
            else
                System.IO.Directory.Delete(opt.SignatureCachePath, true);
        }

        public static List<KeyValuePair<RSync.RSyncDir.PatchFileType, string>> ListActualSignatureFiles(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.ListActualSignatureFiles();
        }
        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region ILiveControl Members

        public void Pause()
        {
            m_liveControl.Pause();
        }

        public void Resume()
        {
            m_liveControl.Resume();
        }

        public void Stop()
        {
            m_liveControl.Stop();
        }

        public void Terminate()
        {
            m_liveControl.Terminate();
        }

        public bool IsStopRequested
        {
            get { return m_liveControl.IsStopRequested; } 
        }

        public void SetUploadLimit(string limit)
        {
            m_liveControl.SetUploadLimit(limit);
        }

        public void SetDownloadLimit(string limit)
        {
            m_liveControl.SetDownloadLimit(limit);
        }

        public void SetThreadPriority(System.Threading.ThreadPriority priority)
        {
            m_liveControl.SetThreadPriority(priority);
        }

        public void UnsetThreadPriority()
        {
            m_liveControl.UnsetThreadPriority();
        }

        #endregion
    }
}
