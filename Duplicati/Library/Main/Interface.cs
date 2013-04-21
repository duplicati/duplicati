#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
using Duplicati.Library.Utility;

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
    }

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
        /// A delete operation performed by looking at the number of existing backups
        /// </summary>
        DeleteAllButN,
        /// <summary>
        /// A delete operation performed by looking at the age of existing backups
        /// </summary>
        DeleteOlderThan,
        /// <summary>
        /// A cleanup operation that removes orphan files
        /// </summary>
        CleanUp,
        /// <summary>
        /// A request to create the underlying folder
        /// </summary>
        CreateFolder,
        /// <summary>
        /// A search for files in signature files
        /// </summary>
        FindLastFileVersion,
        /// <summary>
        /// Verifies the hashes and backup chain
        /// </summary>
        Verify
    }

    /// <summary>
    /// An enum that describes the level of verification done by the verify command
    /// </summary>
    public enum VerificationLevel
    {
        /// <summary>
        /// Just verify the manifest chain
        /// </summary>
        Manifest,
        /// <summary>
        /// Verify the manifest chain and all signature files
        /// </summary>
        Signature,
        /// <summary>
        /// Verify everything, including the content files, requires download of all files
        /// </summary>
        Full,
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
        /// <summary>
        /// Cache of the last progress message
        /// </summary>
        private string m_lastProgressMessage = "";
        /// <summary>
        /// A flag indicating if logging has been set, used to dispose the logging
        /// </summary>
        private bool m_hasSetLogging = false;

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
        /// This gets called whenever execution of an operation is started or stopped; it currently handles the AllowSleep option
        /// </summary>
        /// <param name="isRunning">Flag indicating execution state</param>
        private void OperationRunning(bool isRunning)
        {          
            if (m_options!=null && !m_options.AllowSleep && !Duplicati.Library.Utility.Utility.IsClientLinux)
                try
                {
                    Win32.SetThreadExecutionState(Win32.EXECUTION_STATE.ES_CONTINUOUS | (isRunning ? Win32.EXECUTION_STATE.ES_SYSTEM_REQUIRED : 0));
                }
                catch { } //TODO: Report this somehow
        }   

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
                case DuplicatiOperationMode.FindLastFileVersion:
                case DuplicatiOperationMode.Verify:
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
            if (m_liveControl.IsPauseRequested) 
            {
                OperationRunning(false);
                try { m_liveControl.PauseIfRequested(); }
                finally { OperationRunning(true); }
            }

            if (m_liveControl.IsStopRequested)
                throw new LiveControl.ExecutionStoppedException();
        }

        public string Backup(string[] sources)
        {
            BackupStatistics bs = new BackupStatistics(DuplicatiOperationMode.Backup);
            SetupCommonOptions(bs);

            BackendWrapper backend = null;
            VerificationFile verification = null;

            if (m_options.DontReadManifests)
                throw new Exception(Strings.Interface.ManifestsMustBeReadOnBackups);
            if (m_options.SkipFileHashChecks)
                throw new Exception(Strings.Interface.CannotSkipHashChecksOnBackup);

            if (sources == null || sources.Length == 0)
                throw new Exception(Strings.Interface.NoSourceFoldersError);

            //Make sure they all have the same format and exist
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = Utility.Utility.AppendDirSeparator(System.IO.Path.GetFullPath(sources[i]));

                if (!System.IO.Directory.Exists(sources[i]))
                    throw new System.IO.IOException(String.Format(Strings.Interface.SourceFolderIsMissingError, sources[i]));
            }

            //Sanity check for duplicate folders and multiple inclusions of the same folder
            for (int i = 0; i < sources.Length - 1; i++)
            {
                for (int j = i + 1; j < sources.Length; j++)
                    if (sources[i].Equals(sources[j], Utility.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                        throw new Exception(string.Format(Strings.Interface.SourceDirIsIncludedMultipleTimesError, sources[i]));
                    else if (sources[i].StartsWith(sources[j], Utility.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                        throw new Exception(string.Format(Strings.Interface.SourceDirsAreRelatedError, sources[i], sources[j]));
            }

            if (m_options.AsynchronousUpload)
            {
                m_asyncReserved = ASYNC_RESERVED;
                m_allowUploadProgress = false;
            }
   
            //Unused, but triggers errors in the encryption setup here
            Library.Interface.IEncryption encryptionModule = m_options.NoEncryption ? null : DynamicLoader.EncryptionLoader.GetModule(m_options.EncryptionModule, m_options.Passphrase, m_options.RawOptions);

            using (new Logging.Timer("Backup from " + string.Join(";", sources) + " to " + m_backend))
            {
                try
                {                    
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Backup, bs.OperationMode, -1, -1, Strings.Interface.StatusLoadingFilelist, "");
                    OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, -1, -1, Strings.Interface.StatusLoadingFilelist, "");

                    CheckLiveControl();

                    bool full = m_options.Full;
                    if (full)
                        bs.SetTypeReason(string.Format(Strings.Interface.FullBecauseFlagWasSet, "full"));

                    backend = new BackendWrapper(bs, m_backend, m_options);
                    backend.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupTransfer_ProgressEvent);
                    backend.AsyncItemProcessedEvent += new EventHandler(backend_AsyncItemProcessedEvent);

                    m_progress = 0.0;

                    OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");
                    
                    CheckLiveControl();

                    List<ManifestEntry> backupsets;

                    if (full)
                    {
                        //This will create the target folder
                        backend.List(false);
                        backupsets = new List<ManifestEntry>();
                    }
                    else
                    {
                        //This will list all files on the backend and create the target folder
                        backupsets = backend.GetBackupSets();
                    }

                    if (backupsets.Count == 0)
                    {
                        if (!full)
                            bs.SetTypeReason(Strings.Interface.FullBecauseBackendIsEmpty);
                        full = true;
                    }
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

                                    if (compression != null)
                                        break;
                                }

                            for (int k = backupsets[i].Volumes.Count - 1; compression == null && k >= 0; k--)
                            {
                                compression = backupsets[i].Volumes[k].Key.Compression;
                                encryption = backupsets[i].Volumes[k].Key.EncryptionMode;

                                if (compression != null)
                                    break;
                            }
                        }

                        if (compression != null)
                        {
                            m_options.SetEncryptionModuleDefault(encryption);
                            m_options.SetCompressionModuleDefault(compression);
                        }
                    }

                    string fullCriteria1 = null;
                    string fullCriteria2 = null;
                    if (!full)
                    {
                        full = DateTime.Now > m_options.FullIfOlderThan(backupsets[backupsets.Count - 1].Time);
                        if (full)
                            bs.SetTypeReason(string.Format(Strings.Interface.FullBecauseLastFullIsFrom, backupsets[backupsets.Count - 1].Time, m_options.FullIfOlderThanValue));
                        else if (!string.IsNullOrEmpty(m_options.FullIfOlderThanValue))
                            fullCriteria1 = string.Format(Strings.Interface.IncrementalBecauseLastFullIsFrom, backupsets[backupsets.Count - 1].Time, m_options.FullIfOlderThanValue);
                    }
                    
                    if (!full && m_options.FullIfMoreThanNIncrementals > 0)
                    {
                        full = backupsets[backupsets.Count - 1].Incrementals.Count >= m_options.FullIfMoreThanNIncrementals;
                        if (full)
                            bs.SetTypeReason(string.Format(Strings.Interface.FullBecauseThereAreNIncrementals, backupsets[backupsets.Count - 1].Incrementals.Count, m_options.FullIfMoreThanNIncrementals));
                        else
                            fullCriteria2 = string.Format(Strings.Interface.IncrementalBecauseThereAreNIncrementals, backupsets[backupsets.Count - 1].Incrementals.Count, m_options.FullIfMoreThanNIncrementals);

                    }
                    bs.Full = full;
                    if (!full)
                    {
                        if (fullCriteria1 == null && fullCriteria2 == null)
                            bs.SetTypeReason(Strings.Interface.IncrementalBecauseNoFlagsWereSet);
                        else if (fullCriteria2 == null)
                            bs.SetTypeReason(fullCriteria1);
                        else if (fullCriteria1 == null)
                            bs.SetTypeReason(fullCriteria2);
                        else
                            bs.SetTypeReason(fullCriteria1 + ". " + fullCriteria2);

                    }

                    List<string> controlfiles = new List<string>();
                    if (!string.IsNullOrEmpty(m_options.SignatureControlFiles))
                        controlfiles.AddRange(m_options.SignatureControlFiles.Split(System.IO.Path.PathSeparator));

                    int vol = 0;
                    long totalsize = 0;
                    Manifestfile manifest = new Manifestfile();

                    using (Utility.TempFolder tempfolder = new Duplicati.Library.Utility.TempFolder())
                    {
                        List<KeyValuePair<ManifestEntry, Library.Interface.ICompression>> patches = new List<KeyValuePair<ManifestEntry, Duplicati.Library.Interface.ICompression>>();
                        if (!full)
                        {
                            m_incrementalFraction = INCREMENAL_COST;
                            List<ManifestEntry> entries = new List<ManifestEntry>();
                            entries.Add(backupsets[backupsets.Count - 1]);
                            entries.AddRange(backupsets[backupsets.Count - 1].Incrementals);

                            //Check before we start the download
                            CheckLiveControl();

                            VerifyBackupChainWithFiles(backend, entries[entries.Count - 1]);
                            if (m_options.CreateVerificationFile)
                                verification = new VerificationFile(entries, backend.FilenameStrategy);

                            OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");

                            patches = FindPatches(backend, entries, tempfolder, false, bs);

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
                                    if (!full)
                                        bs.SetTypeReason(Strings.Interface.FullBecauseSourceFoldersChanged);
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
                                            if (s1.Equals(s2, Utility.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                                            {
                                                found = true;
                                                break;
                                            }

                                        if (!found)
                                        {
                                            if (m_options.FullIfSourceFolderChanged)
                                            {
                                                Logging.Log.WriteMessage("Source folders changed, issuing full backup", Duplicati.Library.Logging.LogMessageType.Information);
                                                if (!full)
                                                    bs.SetTypeReason(Strings.Interface.FullBecauseSourceFoldersChanged);
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

                        DateTime backuptime = DateTime.Now;
                        DateTime backupchaintime;

                        if (full)
                        {
                            patches.Clear();
                            m_incrementalFraction = 0.0;
                            manifest.SourceDirs = sources;
                            if (m_options.CreateVerificationFile)
                                verification = new VerificationFile(new ManifestEntry[0], backend.FilenameStrategy);
                            backupchaintime = backuptime;
                        }
                        else
                        {
                            backupchaintime = patches[0].Key.Time;
                            manifest.PreviousManifestFilename = patches[patches.Count - 1].Key.Filename;
                            manifest.PreviousManifestHash = patches[patches.Count - 1].Key.RemoteHash;
                        }


                        OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, -1, -1, Strings.Interface.StatusBuildingFilelist, "");

                        bool completedWithoutChanges;

                        using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(manifest.SourceDirs, bs, m_options.Filter, patches))
                        {
                            CheckLiveControl();

                            dir.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupRSyncDir_ProgressEvent);

                            dir.DisableFiletimeCheck = m_options.DisableFiletimeCheck;
                            dir.MaxFileSize = m_options.SkipFilesLargerThan;
                            using (new Logging.Timer("Initiating multipass"))
                                dir.InitiateMultiPassDiff(full, m_options);

                            string tempVolumeFolder = m_options.AsynchronousUpload ? m_options.AsynchronousUploadFolder : m_options.TempDir;

                            bool done = false;
                            while (!done && totalsize < m_options.MaxSize)
                            {
                                using (new Logging.Timer("Multipass " + (vol + 1).ToString()))
                                using (Utility.TempFile signaturefile = new Duplicati.Library.Utility.TempFile(System.IO.Path.Combine(tempVolumeFolder, Guid.NewGuid().ToString())))
                                using (Utility.TempFile contentfile = new Duplicati.Library.Utility.TempFile(System.IO.Path.Combine(tempVolumeFolder, Guid.NewGuid().ToString())))
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
                                                    Utility.Utility.CopyStream(fs, cs);

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

                                    completedWithoutChanges = done && !dir.AnyChangesFound;

                                    if (m_options.UploadUnchangedBackups || full)
                                        completedWithoutChanges = false;

                                    if (!completedWithoutChanges)
                                    {

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

                                        ContentEntry ce = new ContentEntry(backuptime, full, vol + 1);
                                        SignatureEntry se = new SignatureEntry(backuptime, full, vol + 1);

                                        using (new Logging.Timer("Writing delta file " + (vol + 1).ToString()))
                                            backend.Put(ce, contentfile);

                                        if (!m_options.AsynchronousUpload)
                                            OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingSignatureVolume, vol + 1), "");

                                        using (new Logging.Timer("Writing remote signatures"))
                                            backend.Put(se, signaturefile);

                                        manifest.AddEntries(ce, se);

                                        if (verification != null)
                                        {
                                            verification.AddFile(ce);
                                            verification.AddFile(se);
                                        }
                                    }
                                }

                                if (!completedWithoutChanges)
                                {
                                    //The backend wrapper will remove these
                                    Utility.TempFile mf = new Duplicati.Library.Utility.TempFile();

                                    using (new Logging.Timer("Writing manifest " + backuptime.ToUniversalTime().ToString("yyyyMMddTHHmmssK")))
                                    {
                                        //Alternate primary/secondary
                                        ManifestEntry mfe = new ManifestEntry(backuptime, full, manifest.SignatureHashes.Count % 2 != 0);
                                        manifest.SelfFilename = backend.GenerateFilename(mfe);
                                        manifest.Save(mf);

                                        if (!m_options.AsynchronousUpload)
                                            OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingManifestVolume, vol + 1), "");

                                        //Write the file
                                        mf.Protected = true;
                                        backend.Put(mfe, mf);

                                        if (verification != null)
                                            verification.UpdateManifest(mfe);
                                    }

                                    if (verification != null)
                                    {
                                        using (new Logging.Timer("Writing verification " + backuptime.ToUniversalTime().ToString("yyyyMMddTHHmmssK")))
                                        {
                                            Utility.TempFile vt = new Duplicati.Library.Utility.TempFile();

                                            verification.Save(vt);

                                            if (!m_options.AsynchronousUpload)
                                                OperationProgress(this, DuplicatiOperation.Backup, bs.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusUploadingVerificationVolume, "");

                                            vt.Protected = true;
                                            backend.Put(new VerificationEntry(backupchaintime), vt);
                                        }
                                    }

                                    if (m_options.AsynchronousUpload)
                                        m_allowUploadProgress = false;

                                    //The file volume counter
                                    vol++;
                                }
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
                                else if (p.Key is VerificationEntry)
                                    msg = Strings.Interface.StatusUploadingVerificationVolume;
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
                    {
                        Logging.Log.WriteMessage(string.Format(Strings.Interface.ErrorRunningBackup, ex.Message), Logging.LogMessageType.Error);
                        throw; //This also activates "finally", unlike in other languages...
                    }

                    Logging.Log.WriteMessage(string.Format(Strings.Interface.PartialUploadMessage, backend.ManifestUploads, ex.Message), Logging.LogMessageType.Warning);
                    bs.LogError(string.Format(Strings.Interface.PartialUploadMessage, backend.ManifestUploads, ex.Message), ex);
                    bs.PartialBackup = true;
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

            // Sanity check to ensure that errors are found as early as possible
            List<ManifestEntry> newsets = GetBackupSets();
            List<ManifestEntry> newentries = new List<ManifestEntry>();
            newentries.Add(newsets[newsets.Count - 1]);
            newentries.AddRange(newsets[newsets.Count - 1].Incrementals);
            VerifyBackupChainWithFiles(backend, newentries[newentries.Count - 1]);

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
        /// Validates the manifest chain, starting by evaluating the current manifest and going backwards in the chain
        /// </summary>
        /// <param name="backend">The backend wrapper</param>
        /// <param name="entry">The entry to verify</param>
        private void VerifyManifestChain(BackendWrapper backend, ManifestEntry entry)
        {
            if (m_options.DontReadManifests)
                throw new InvalidOperationException(Strings.Interface.CannotVerifyChain);

            while (entry.Previous != null)
            {
                Manifestfile parsed = GetManifest(backend, entry);

                //If this manifest is not version 3, the chain verification stops here
                if (parsed.Version < 3)
                    return;

                ManifestEntry previous = entry.Previous;
                if (entry.Previous.Alternate != null && entry.Previous.Alternate.Filename == parsed.PreviousManifestFilename)
                    previous = entry.Previous.Alternate;

                if (parsed.PreviousManifestFilename != previous.Filename)
                    throw new System.IO.InvalidDataException(string.Format(Strings.Interface.PreviousManifestFilenameMismatchError, entry.Filename, parsed.PreviousManifestFilename, previous.Filename));

                if (!m_options.SkipFileHashChecks)
                {
                    //Load the Remotehash property
                    GetManifest(backend, previous);

                    if (parsed.PreviousManifestHash != previous.RemoteHash)
                        throw new System.IO.InvalidDataException(string.Format(Strings.Interface.PreviousManifestHashMismatchError, entry.Filename, parsed.PreviousManifestHash, previous.RemoteHash));
                }

                entry = previous;
            }
        }
        
        private Exception CreateMissingFileException(Manifestfile parsed, ManifestEntry entry, string errorMessage)
        {
            if (parsed.SignatureHashes.Count != parsed.ContentHashes.Count)
                return new Exception(string.Format(Strings.Interface.InvalidManifestFileCount, entry.Filename, parsed.SignatureHashes.Count, parsed.ContentHashes.Count));
            
            Dictionary<string, bool> lookup = new Dictionary<string, bool>();
            foreach(Manifestfile.HashEntry he in parsed.ContentHashes)
                lookup[he.Name] = false;
            foreach(Manifestfile.HashEntry he in parsed.SignatureHashes)
                lookup[he.Name] = false;
            
            StringBuilder sbextra = new StringBuilder();
            foreach(KeyValuePair<SignatureEntry, ContentEntry> kvp in entry.Volumes)
            {
                if (lookup.ContainsKey(kvp.Key.Filename))
                    lookup[kvp.Key.Filename] = true;
                else
                    sbextra.AppendLine(kvp.Key.Filename);
                    
                if (lookup.ContainsKey(kvp.Value.Filename))
                    lookup[kvp.Value.Filename] = true;
                else
                    sbextra.AppendLine(kvp.Value.Filename);
            }
            
            StringBuilder sbmissing = new StringBuilder();
            sbmissing.AppendLine();
            foreach(KeyValuePair<string, bool> he in lookup)
                if (!he.Value)
                    sbmissing.AppendLine(he.Key);
                    
            if (sbextra.Length > 0)
            {
                sbmissing.AppendLine();
                sbmissing.AppendLine(Strings.Interface.ExtraFilesMessage);
                sbmissing.Append(sbextra);
            }
        
            return new Exception(
                string.Format(Strings.Interface.MissingFilesDetected, entry.Filename, parsed.ContentHashes.Count, sbmissing.ToString())
                + errorMessage
                );
        }

        /// <summary>
        /// Verifies the backup chain for producing a new backup on top.
        /// This will check that all files are accounted for in the file list.
        /// </summary>
        /// <param name="entry">The newest entry to check</param>
        private void VerifyBackupChainWithFiles(BackendWrapper backend, ManifestEntry entry)
        {
            VerifyManifestChain(backend, entry);

            string errorMessage = Environment.NewLine + Strings.Interface.DeleteManifestsSuggestion + Environment.NewLine + Environment.NewLine;

            while (entry != null)
            {
                Manifestfile parsed = GetManifest(backend, entry);

                errorMessage += entry.Filename + Environment.NewLine;

                if (entry.Volumes.Count != parsed.SignatureHashes.Count || entry.Volumes.Count != parsed.ContentHashes.Count)
                {
                    //If we have an extra set, the connection could have died right before the manifest was uploaded
                    if (parsed.SignatureHashes.Count == parsed.ContentHashes.Count && entry.Volumes.Count - 1 == parsed.ContentHashes.Count)
                    {
                        backend.AddOrphan(entry.Volumes[entry.Volumes.Count - 1].Value);
                        backend.AddOrphan(entry.Volumes[entry.Volumes.Count - 1].Key);
                        entry.Volumes.RemoveAt(entry.Volumes.Count - 1);
                    }
                    else
                    {
                        throw CreateMissingFileException(parsed, entry, errorMessage);
                    }
                }

                for(int i = 0; i < entry.Volumes.Count; i++)
                {
                    if (entry.Volumes[i].Key.Filesize > 0 && parsed.SignatureHashes[i].Size > 0 && entry.Volumes[i].Key.Filesize != parsed.SignatureHashes[i].Size)
                        throw new Exception(
                            string.Format(Strings.Interface.FileSizeMismatchError, entry.Volumes[i].Key.Filename, entry.Volumes[i].Key.Filesize, parsed.SignatureHashes[i].Size)
                            + errorMessage
                            );

                    if (entry.Volumes[i].Value.Filesize >= 0 && parsed.ContentHashes[i].Size >= 0 && entry.Volumes[i].Value.Filesize != parsed.ContentHashes[i].Size)
                        throw new Exception(
                            string.Format(Strings.Interface.FileSizeMismatchError, entry.Volumes[i].Value.Filename, entry.Volumes[i].Value.Filesize, parsed.ContentHashes[i].Size)
                            + errorMessage
                            );

                    if (!string.IsNullOrEmpty(parsed.SignatureHashes[i].Name) && !parsed.SignatureHashes[i].Name.Equals(entry.Volumes[i].Key.Fileentry.Name, StringComparison.InvariantCultureIgnoreCase))
                        throw new Exception(
                            string.Format(Strings.Interface.FilenameMismatchError, parsed.SignatureHashes[i].Name, entry.Volumes[i].Key.Fileentry.Name)
                            + errorMessage
                            );

                    if (!string.IsNullOrEmpty(parsed.ContentHashes[i].Name) && !parsed.ContentHashes[i].Name.Equals(entry.Volumes[i].Value.Fileentry.Name, StringComparison.InvariantCultureIgnoreCase))
                        throw new Exception(
                            string.Format(Strings.Interface.FilenameMismatchError, parsed.ContentHashes[i].Name, entry.Volumes[i].Value.Fileentry.Name)
                            + errorMessage
                            );
                }

                entry = entry.Previous;
            }
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

            if (entry.ParsedManifest != null)
                return entry.ParsedManifest;
            else if (entry.Alternate != null && entry.Alternate.ParsedManifest != null)
                return entry.Alternate.ParsedManifest;

            if (OperationProgress != null && backend.Statistics != null)
                OperationProgress(this, GetOperationType(), backend.Statistics.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusReadingManifest, entry.Time.ToShortDateString() + " " + entry.Time.ToShortTimeString()), "");

            bool tryAlternateManifest = false;

            //This method has some very special logic to ensure correct handling of errors
            //The assumption is that it is possible to determine if the error occurred due to a 
            // transfer problem or a corrupt file. If the former happens, the operation should
            // be retried, and thus an exception is thrown. If the latter, the file should 
            // be ignored and the backup file should be used.
            //
            //We detect a parsing error, either directly or indirectly through CryptographicException,
            // and assume that a parsing error is an indication of a broken file.
            //All other errors are assumed to be transfer problems, and throws exceptions.
            //
            //This holds as long as the backend always throws an exception if a partial file
            // was downloaded. The FTP backend may not honor this, and some webservers
            // may ommit the "Content-Length" header, which will cause problems.
            //There is a guard agains partial downloads in BackendWrapper.GetInternal()

            using (new Logging.Timer("Get " + entry.Filename))
            using (Utility.TempFile tf = new Duplicati.Library.Utility.TempFile())
            {
                try
                {
                    backend.Get(entry, null, tf, null);
                    
                    //We now have the file decrypted, if the next step fails,
                    // its a broken xml or invalid content
                    tryAlternateManifest = true;
                    Manifestfile mf = new Manifestfile(tf, m_options.SkipFileHashChecks);

                    if (string.IsNullOrEmpty(mf.SelfFilename))
                        mf.SelfFilename = entry.Filename;

                    if (mf.ContentHashes != null && entry.Alternate != null)
                    {
                        //Special case, the manifest has not recorded all volumes,
                        // we must see if the alternate manifest has more volumes
                        if (entry.Volumes.Count > mf.ContentHashes.Count)
                        {
                            //Do not try the alternate, we just did
                            tryAlternateManifest = false;
                            Logging.Log.WriteMessage(string.Format(Strings.Interface.ReadingSecondaryManifestLogMessage, entry.Alternate.Filename), Duplicati.Library.Logging.LogMessageType.Information);
                                
                            Manifestfile amf = null;

                            //Read the alternate file and try to differentiate between a defect file or a partial one
                            bool defectFile = false;

                            try 
                            {
                                System.IO.File.Delete(tf);
                                backend.Get(entry.Alternate, null, tf, null);
                            }
                            catch (System.Security.Cryptography.CryptographicException cex) 
                            {
                                //We assume that CryptoException means partial file
                                Logging.Log.WriteMessage(string.Format(Strings.Interface.SecondaryManifestReadErrorLogMessage, entry.Alternate.Filename, cex), Duplicati.Library.Logging.LogMessageType.Warning);
                                defectFile = true;
                            }

                            if (!defectFile)
                            {
                                try
                                {
                                    amf = new Manifestfile(tf, m_options.SkipFileHashChecks);
                                }
                                catch (Exception ex)
                                {
                                    //Parsing error means partial file
                                    Logging.Log.WriteMessage(string.Format(Strings.Interface.SecondaryManifestReadErrorLogMessage, entry.Alternate.Filename, ex), Duplicati.Library.Logging.LogMessageType.Warning);
                                    defectFile = true;
                                }
                            }

                            //If the alternate manifest is correct, assign it so we have a copy
                            if (!defectFile && amf != null)
                            {
                                if (string.IsNullOrEmpty(amf.SelfFilename))
                                    amf.SelfFilename = entry.Alternate.Filename;

                                //If the alternate manifest has more files than the primary, we use that one
                                if (amf.ContentHashes != null && amf.ContentHashes.Count > mf.ContentHashes.Count)
                                {
                                    entry.Alternate.ParsedManifest = amf;

                                    if (m_options.SkipFileHashChecks)
                                    {
                                        mf.SignatureHashes = null;
                                        mf.ContentHashes = null;
                                    }

                                    return amf;

                                }
                            }
                        }
                    }

                    if (m_options.SkipFileHashChecks)
                    {
                        mf.SignatureHashes = null;
                        mf.ContentHashes = null;
                    }

                    entry.ParsedManifest = mf;
                    return mf;
                }
                catch (Exception ex)
                {
                    //Only try secondary if the parsing/decrypting fails, not if the transfer fails
                    if (entry.Alternate != null && (ex is System.Security.Cryptography.CryptographicException || tryAlternateManifest))
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

            using (new Logging.Timer("Restore from " + m_backend + " to " + string.Join(System.IO.Path.PathSeparator.ToString(), target)))
            {
                try
                {                    
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, rs.OperationMode, -1, -1, Strings.Interface.StatusStarted, "");
                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, -1, -1, Strings.Interface.StatusStarted, "");

                    Utility.FilenameFilter filter = m_options.Filter;

                    //Filter is prefered, if both file and filter is specified
                    if (!m_options.HasFilter && !string.IsNullOrEmpty(m_options.FileToRestore))
                    {
                        List<Utility.IFilenameFilter> list = new List<Duplicati.Library.Utility.IFilenameFilter>();
                        list.Add(new Utility.FilelistFilter(true, m_options.FileToRestore.Split(System.IO.Path.PathSeparator)));
                        list.Add(new Utility.RegularExpressionFilter(false, ".*"));

                        filter = new Duplicati.Library.Utility.FilenameFilter(list);
                    }

                    backend = new BackendWrapper(rs, m_backend, m_options);
                    backend.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupTransfer_ProgressEvent);

                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");

                    ManifestEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);

                    //We will need all the manifests downloaded anyway
                    if (!m_options.DontReadManifests)
                    {
                        if (bestFit.Incrementals.Count > 0)
                            VerifyManifestChain(backend, bestFit.Incrementals[bestFit.Incrementals.Count - 1]);
                        else
                            VerifyManifestChain(backend, bestFit);

                        OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");
                    }

                    m_progress = INCREMENAL_COST;

                    List<ManifestEntry> entries = new List<ManifestEntry>();
                    entries.Add(bestFit);
                    entries.AddRange(bestFit.Incrementals);
                    int patchno = 0;
                    
                    foreach (ManifestEntry be in entries)
                        m_restorePatches += be.Volumes.Count;

                    Manifestfile rootManifest = GetManifest(backend, bestFit);

                    int sourceDirCount = (rootManifest.SourceDirs == null || rootManifest.SourceDirs.Length == 0) ? 1 : rootManifest.SourceDirs.Length;

                    //After reading the first manifest, we know the source folder count
                    if ((rootManifest.SourceDirs == null || rootManifest.SourceDirs.Length == 0) && target.Length > 1)
                    {
                        //V1 support
                        rs.LogWarning(string.Format(Strings.Interface.TooManyTargetFoldersWarning, 1, target.Length), null);
                        Array.Resize(ref target, 1);
                    }
                    else if (target.Length > sourceDirCount)
                    {
                        //If we get too many, we can just cut them off
                        rs.LogWarning(string.Format(Strings.Interface.TooManyTargetFoldersWarning, sourceDirCount, target.Length), null);
                        Array.Resize(ref target, rootManifest.SourceDirs.Length);
                    }
                    else if (target.Length != 1 && target.Length < sourceDirCount)
                    {
                        //If we get too few, we have to bail
                        throw new Exception(string.Format(Strings.Interface.TooFewTargetFoldersError, sourceDirCount, target.Length));
                    }
                    else if (target.Length == 1 && sourceDirCount > 1)
                    {
                        //If there is just one target folder, we automatically compose target subfolders
                        string[] newtargets = new string[rootManifest.SourceDirs.Length];

                        List<string> suggestions = new List<string>();
                        for (int i = 0; i < rootManifest.SourceDirs.Length; i++)
                        {
                            string s = rootManifest.SourceDirs[i];
                            //HACK: We use a leading / in the path name to detect source OS
                            // all paths are absolute, so this detects all unix like systems
                            string dirSepChar = s.StartsWith("/") ? "/" : "\\";

                            if (s.EndsWith(dirSepChar))
                                s = s.Substring(0, s.Length - 1);

                            int lix = s.LastIndexOf(dirSepChar);
                            if (lix < 0 || lix + 1 >= s.Length)
                                s = i.ToString();
                            else
                                s = s.Substring(lix + 1);

                            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                                s = s.Replace(c, '_');

                            suggestions.Add(s);
                        }

                        Dictionary<string, int> duplicates = new Dictionary<string, int>(Library.Utility.Utility.ClientFilenameStringComparer);
                        for (int i = 0; i < suggestions.Count; i++)
                            if (duplicates.ContainsKey(suggestions[i]))
                                duplicates[suggestions[i]]++;
                            else
                                duplicates[suggestions[i]] = 1;

                        for (int i = 0; i < newtargets.Length; i++)
                        {
                            string suffix = duplicates[suggestions[i]] > 1 ? i.ToString() : suggestions[i];
                            newtargets[i] = System.IO.Path.Combine(target[0], suffix);
                        }

                        target = newtargets;
                    }

                    //Make sure all targets exist
                    foreach(string s in target)
                        if (!System.IO.Directory.Exists(s))
                            System.IO.Directory.CreateDirectory(s);

                    using (RSync.RSyncDir sync = new Duplicati.Library.Main.RSync.RSyncDir(target, rs, filter))
                    {
                        sync.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(RestoreRSyncDir_ProgressEvent);

                        foreach (ManifestEntry be in entries)
                        {
                            m_progress = ((1.0 - INCREMENAL_COST) * (patchno / (double)m_restorePatches)) + INCREMENAL_COST;

                            CheckLiveControl();

                            Manifestfile manifest = be == bestFit ? rootManifest : GetManifest(backend, be);

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

                                using (Utility.TempFile patchzip = new Duplicati.Library.Utility.TempFile())
                                {
                                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusPatching, patchno + 1), "");

                                    CheckLiveControl();

                                     if (m_options.HasFilter || !string.IsNullOrEmpty(m_options.FileToRestore))
                                     {
                                         bool hasFiles = false;

                                         using (Utility.TempFile sigFile = new Duplicati.Library.Utility.TempFile())
                                         {
                                             OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusDownloadingSignatureVolume, patchno + 1), "");

                                             try
                                             {
                                                 using (new Logging.Timer("Get " + signatureVol.Filename))
                                                     backend.Get(signatureVol, manifest, sigFile, manifest.SignatureHashes == null ? null : manifest.SignatureHashes[signatureVol.Volumenumber - 1]);
                                             }
                                             catch (BackendWrapper.HashMismathcException hme)
                                             {
                                                 hasFiles = true;
                                                 rs.LogError(string.Format(Strings.Interface.FileHashFailure, hme.Message), hme);
                                             }
                                             catch(Exception ex)
                                             {
                                                if (m_options.BestEffortRestore)
                                                {
                                                    //Assume that we need something here
                                                    hasFiles = true;
                                                    rs.LogWarning(string.Format(Strings.Interface.SignatureDownloadBestEffortError, signatureVol.Filename, ex.Message), ex);
                                                }
                                                else
                                                    throw;
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
                                         {
                                             //Avoid downloading the content file
                                             patchno++;
                                             continue; 
                                         }
                                    }

                                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusDownloadingContentVolume, patchno + 1), "");

                                    try
                                    {
                                        using (new Logging.Timer("Get " + contentVol.Filename))
                                            backend.Get(contentVol, manifest, patchzip, manifest.ContentHashes == null ? null : manifest.ContentHashes[contentVol.Volumenumber - 1]);
    
	                                    OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusPatching, patchno + 1), "");
	                                    
	                                    using (new Logging.Timer((patchno == 0 ? "Full restore to: " : "Incremental restore " + patchno.ToString() + " to: ") + string.Join(System.IO.Path.PathSeparator.ToString(), target)))
	                                    using (Library.Interface.ICompression patch = DynamicLoader.CompressionLoader.GetModule(contentVol.Compression, patchzip, m_options.RawOptions))
	                                        sync.Patch(target, patch);
                                    }
                                    catch(Exception ex)
                                    {
                                        if (m_options.BestEffortRestore)
                                            rs.LogWarning(string.Format(Strings.Interface.PatchProcessingBestEffortError, contentVol.Filename, ex.Message), ex);
                                        else
                                            throw;
                                    }
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

                    string prefix = Utility.Utility.AppendDirSeparator(RSync.RSyncDir.CONTROL_ROOT);

                    foreach (ManifestEntry be in flatlist)
                    {
                        if (be.Volumes.Count > 0)
                            using(Utility.TempFile z = new Duplicati.Library.Utility.TempFile())
                            {
                                OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, 0, -1, string.Format(Strings.Interface.StatusReadingIncrementalFile, be.Volumes[0].Key.Filename), "");

                                Manifestfile mf = GetManifest(backend, be);

                                OperationProgress(this, DuplicatiOperation.Restore, rs.OperationMode, 0, -1, string.Format(Strings.Interface.StatusReadingIncrementalFile, be.Volumes[0].Key.Filename), "");

                                using (new Logging.Timer("Get " + be.Volumes[0].Key.Filename))
                                    backend.Get(be.Volumes[0].Key, mf, z, mf.SignatureHashes == null ? null : mf.SignatureHashes[0]);
                                
                                using(Library.Interface.ICompression fz = DynamicLoader.CompressionLoader.GetModule(be.Volumes[0].Key.Compression, z, m_options.RawOptions))
                                {
                                    bool any = false;
                                    foreach (string f in fz.ListFiles(prefix))
                                    {
                                        any = true;
                                        using (System.IO.Stream s1 = fz.OpenRead(f))
                                        using (System.IO.Stream s2 = System.IO.File.Create(System.IO.Path.Combine(target, f.Substring(prefix.Length))))
                                            Utility.Utility.CopyStream(s1, s2);
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

        public string DeleteAllButN()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.DeleteAllButN);
            SetupCommonOptions(stats);

            int x = Math.Max(0, m_options.DeleteAllButNFull);

            StringBuilder sb = new StringBuilder();

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Remove, stats.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

                    List<ManifestEntry> flatlist = new List<ManifestEntry>();
                    List<ManifestEntry> entries =  backend.GetBackupSets();

                    //Get all backups as a flat list
                    foreach (ManifestEntry me in entries)
                    {
                        flatlist.Add(me);
                        flatlist.AddRange(me.Incrementals);
                    }

                    //Now remove all but those requested
                    List<ManifestEntry> toremove = new List<ManifestEntry>();
                    while (flatlist.Count > x)
                    {
                        toremove.Add(flatlist[0]);
                        flatlist.RemoveAt(0);
                    }

                    //If there are still chains left, make sure we do not end up with a partial chain
                    if (!m_options.AllowFullRemoval || flatlist.Count != 0)
                    {
                        //Go back until we have a full chain
                        while (toremove.Count > 0 && (flatlist.Count == 0 || !flatlist[0].IsFull))
                        {
                            sb.AppendLine(string.Format(Strings.Interface.NotDeletingBackupSetMessage, toremove[toremove.Count - 1].Time));
                            flatlist.Insert(0, toremove[toremove.Count - 1]);
                            toremove.RemoveAt(toremove.Count - 1);
                        }
                    }

                    if (toremove.Count > 0 && !m_options.AllowFullRemoval && (flatlist.Count == 0 || !flatlist[0].IsFull))
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

            sb.Append(backend.FinishDeleteTransaction(false));

            if (entries.Count > 0)
            {
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                System.Xml.XmlNode root = doc.AppendChild(doc.CreateElement("files"));
                root.Attributes.Append(doc.CreateAttribute("version")).Value = "1";

                foreach (ManifestEntry me in entries)
                {
                    if (me.Alternate != null)
                        root.AppendChild(doc.CreateElement("file")).InnerText = me.Alternate.Filename;
                    root.AppendChild(doc.CreateElement("file")).InnerText = me.Filename;

                    if (me.Verification != null)
                        root.AppendChild(doc.CreateElement("file")).InnerText = me.Verification.Filename;

                    foreach (KeyValuePair<SignatureEntry, ContentEntry> kx in me.Volumes)
                    {
                        root.AppendChild(doc.CreateElement("file")).InnerText = kx.Key.Filename;
                        root.AppendChild(doc.CreateElement("file")).InnerText = kx.Value.Filename;
                    }
                }

                if (m_options.Force)
                {
                    using (TempFile tf = new TempFile())
                    {
                        doc.Save(tf);
                        tf.Protected = true;
                        backend.WriteDeleteTransactionFile(tf);
                    }
                }

                foreach (ManifestEntry me in entries)
                {
                    sb.AppendLine(string.Format(Strings.Interface.DeletingBackupSetMessage, me.Time.ToString(System.Globalization.CultureInfo.InvariantCulture)));

                    if (m_options.Force)
                    {
                        //Delete manifest
                        if (me.Alternate != null)
                            backend.Delete(me.Alternate);

                        backend.Delete(me);

                        if (me.Verification != null)
                            backend.Delete(me.Verification);

                        foreach (KeyValuePair<SignatureEntry, ContentEntry> kx in me.Volumes)
                        {
                            backend.Delete(kx.Key);
                            backend.Delete(kx.Value);
                        }
                    }
                }

                if (m_options.Force)
                    backend.RemoveDeleteTransactionFile();

                if (!m_options.Force && entries.Count > 0)
                    sb.AppendLine(Strings.Interface.FilesAreNotForceDeletedMessage);
            }

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

                foreach (Duplicati.Library.Interface.IFileEntry fe in backend.List(false))
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
            StringBuilder sb = new StringBuilder();
            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            {
                List<ManifestEntry> sorted = backend.GetBackupSets();

                List<ManifestEntry> entries = new List<ManifestEntry>();
                entries.AddRange(sorted);
                foreach (ManifestEntry be in sorted)
                    entries.AddRange(be.Incrementals);

                string cleanup = backend.DeleteOrphans(false);
                if (!string.IsNullOrEmpty(cleanup))
                    sb.AppendLine(cleanup);

                if (m_options.SkipFileHashChecks)
                    throw new Exception(Strings.Interface.CannotCleanWithoutHashesError);
                if (m_options.DontReadManifests)
                    throw new Exception(Strings.Interface.CannotCleanWithoutHashesError);

                //We need the manifests anyway, so we verify the chain
                if (entries.Count > 0)
                    VerifyManifestChain(backend, entries[0]);

                //Now compare the actual filelist with the manifest
                foreach (ManifestEntry be in entries)
                {
                    Manifestfile manifest = GetManifest(backend, be);

                    int count = manifest.ContentHashes.Count;

                    for (int i = count - 1; i < be.Volumes.Count; i++)
                    {
                        anyRemoved = true;

                        string sigmsg = string.Format(Strings.Interface.RemovingPartialFilesMessage, be.Volumes[i].Key.Filename);
                        string cntmsg = string.Format(Strings.Interface.RemovingPartialFilesMessage, be.Volumes[i].Value.Filename);

                        Logging.Log.WriteMessage(sigmsg, Duplicati.Library.Logging.LogMessageType.Information);
                        Logging.Log.WriteMessage(cntmsg, Duplicati.Library.Logging.LogMessageType.Information);

                        sb.AppendLine(sigmsg);
                        sb.AppendLine(cntmsg);

                        if (m_options.Force)
                        {
                            backend.Delete(be.Volumes[i].Key);
                            backend.Delete(be.Volumes[i].Value);
                        }
                    }
                }
            }

            if (!m_options.Force && anyRemoved)
            {
                Logging.Log.WriteMessage(Strings.Interface.FilesAreNotForceDeletedMessage, Duplicati.Library.Logging.LogMessageType.Information);
                sb.AppendLine(Strings.Interface.FilesAreNotForceDeletedMessage);
            }

            return sb.ToString(); //TODO: Write a message here?
        }

        public IList<string> ListCurrentFiles()
        {
            RestoreStatistics rs = new RestoreStatistics(DuplicatiOperationMode.ListCurrentFiles);
            SetupCommonOptions(rs);

            Utility.FilenameFilter filter = m_options.Filter;
            DateTime timelimit = m_options.RestoreTime;

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, rs.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

            List<string> res;

            using (BackendWrapper backend = new BackendWrapper(rs, m_backend, m_options))
            using (Utility.TempFolder basefolder = new Duplicati.Library.Utility.TempFolder())
            {
                ManifestEntry bestFit = backend.GetBackupSet(timelimit);

                List<ManifestEntry> entries = new List<ManifestEntry>();
                entries.Add(bestFit);
                entries.AddRange(bestFit.Incrementals);

                List<KeyValuePair<ManifestEntry, Library.Interface.ICompression>> patches = FindPatches(backend, entries, basefolder, false, rs);

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

            if (m_options.DontReadManifests)
                throw new Exception(Strings.Interface.ManifestsMustBeRead);

            DateTime timelimit = m_options.RestoreTime;

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, rs.OperationMode, 0, -1, Strings.Interface.StatusStarted, "");

            string[] res;

            using (BackendWrapper backend = new BackendWrapper(rs, m_backend, m_options))
            using (Utility.TempFile mfile = new Duplicati.Library.Utility.TempFile())
            {
                ManifestEntry bestFit = backend.GetBackupSet(timelimit);

                backend.Get(bestFit, null, mfile, null);
                res = new Manifestfile(mfile, m_options.SkipFileHashChecks).SourceDirs;
            }

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, rs.OperationMode, 100, -1, Strings.Interface.StatusCompleted, "");

            return res;
        }


        private void SetupCommonOptions(CommunicationStatistics stats)
        {            
            m_options.MainAction = stats.OperationMode;
            
            switch (m_options.MainAction)
            {
                case DuplicatiOperationMode.Backup:
                case DuplicatiOperationMode.BackupFull:
                case DuplicatiOperationMode.BackupIncremental:
                    break;
                
                default:
                    //It only makes sense to enable auto-creation if we are writing files.
                    if (!m_options.RawOptions.ContainsKey("disable-autocreate-folder"))
                        m_options.RawOptions["disable-autocreate-folder"] = "true";
                    break;
            }

            Library.Logging.Log.LogLevel = m_options.Loglevel;

            OperationRunning(true);

            if (!string.IsNullOrEmpty(m_options.Logfile))
            {
                m_hasSetLogging = true;
                Library.Logging.Log.CurrentLog = new Library.Logging.StreamLog(m_options.Logfile);
            }

            if (stats != null)
            {
                stats.VerboseErrors = m_options.DebugOutput;
                stats.VerboseRetryErrors = m_options.VerboseRetryErrors;
            }

            if (m_options.HasTempDir)
                Utility.TempFolder.SystemTempPath = m_options.TempDir;

            if (!string.IsNullOrEmpty(m_options.ThreadPriority))
                System.Threading.Thread.CurrentThread.Priority = Utility.Utility.ParsePriority(m_options.ThreadPriority);

            //Load all generic modules
            m_options.LoadedModules.Clear();

            foreach (Library.Interface.IGenericModule m in DynamicLoader.GenericLoader.Modules)
                m_options.LoadedModules.Add(new KeyValuePair<bool, Library.Interface.IGenericModule>(Array.IndexOf<string>(m_options.DisableModules, m.Key.ToLower()) < 0 && (m.LoadAsDefault || Array.IndexOf<string>(m_options.EnableModules, m.Key.ToLower()) >= 0), m));

            ValidateOptions(stats);

            foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                if (mx.Key)
                    mx.Value.Configure(m_options.RawOptions);

            Library.Logging.Log.WriteMessage(string.Format(Strings.Interface.StartingOperationMessage, m_options.MainAction), Logging.LogMessageType.Information);
        }

        /// <summary>
        /// Downloads all required signature files from the backend.
        /// </summary>
        /// <param name="backend">The backend to read from</param>
        /// <param name="entries">The flattened list of manifests</param>
        /// <param name="tempfolder">The tempfolder set for this operation</param>
        /// <param name="allowHashFail">True to ignore files with failed hash signature</param>
        /// <returns>A list of file archives</returns>
        private List<KeyValuePair<ManifestEntry, Library.Interface.ICompression>> FindPatches(BackendWrapper backend, List<ManifestEntry> entries, string tempfolder, bool allowHashFail, CommunicationStatistics stat)
        {
            List<KeyValuePair<ManifestEntry, Library.Interface.ICompression>> patches = new List<KeyValuePair<ManifestEntry, Library.Interface.ICompression>>();

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

                //Ensure that the manifest chain has not been tampered with
                // since we will read all manifest files anyway, there is no harm in doing it here
                if (!m_options.DontReadManifests && entries.Count > 0)
                    VerifyManifestChain(backend, entries[entries.Count - 1]);

                foreach (ManifestEntry be in entries)
                {
                    m_progress += unitCost;

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
                                backend.Get(bes.Key, manifest, filename, manifest.SignatureHashes == null ? null : manifest.SignatureHashes[bes.Key.Volumenumber - 1]);
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

                        patches.Add(new KeyValuePair<ManifestEntry,Duplicati.Library.Interface.ICompression>(be, DynamicLoader.CompressionLoader.GetModule(bes.Key.Compression, filename, m_options.RawOptions)));
                    }
                }
            }

            backend.DeleteOrphans(true);

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

                using (Utility.TempFolder folder = new Duplicati.Library.Utility.TempFolder())
                {
                    List<Library.Interface.ICompression> patches = new List<Duplicati.Library.Interface.ICompression>();
                    foreach (KeyValuePair<ManifestEntry, Library.Interface.ICompression> entry in FindPatches(backend, new List<ManifestEntry>(new ManifestEntry[] { bestFit }), folder, false, stats))
                        patches.Add(entry.Value);

                    using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(new string[] { folder }, stats, null))
                        return dir.ListPatchFiles(patches);
                }
            }

        }

        private void CreateFolder()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.CreateFolder);
            SetupCommonOptions(stats);

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
                backend.CreateFolder();
        }

        public List<KeyValuePair<BackupEntryBase, Exception>> VerifyBackupChain()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.Verify);
            SetupCommonOptions(stats);

            List<KeyValuePair<BackupEntryBase, Exception>> results = new List<KeyValuePair<BackupEntryBase, Exception>>();

            if (m_options.DontReadManifests)
                throw new InvalidOperationException(Strings.Interface.ManifestsMustBeRead);
            if (m_options.SkipFileHashChecks)
                throw new InvalidOperationException(Strings.Interface.CannotVerifyWithoutHashes);

            if (!string.IsNullOrEmpty(m_options.SignatureCachePath))
            {
                stats.LogWarning(Strings.Interface.DisablingSignatureCacheForVerification, null);
                m_options.SignatureCachePath = null;
            }

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            {
                //Find the spot in the chain where we start
                ManifestEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);

                //Get the list of manifests to validate
                List<ManifestEntry> entries = new List<ManifestEntry>();
                entries.Add(bestFit);
                entries.AddRange(bestFit.Incrementals);

                entries.Reverse();

                foreach (ManifestEntry me in entries)
                {
                    Manifestfile mf = null;

                    try
                    {
                        mf = GetManifest(backend, me);
                        VerifyBackupChainWithFiles(backend, me);

                        if (mf.SignatureHashes.Count != me.Volumes.Count)
                            results.Add(new KeyValuePair<BackupEntryBase,Exception>(me, CreateMissingFileException(mf, me, "")));
                        else
                            results.Add(new KeyValuePair<BackupEntryBase,Exception>(me, null));
                    }
                    catch (Exception ex)
                    {
                        results.Add(new KeyValuePair<BackupEntryBase,Exception>(me, ex));
                    }

                    if (mf != null)
                    {
                        int volumes = Math.Min(mf.SignatureHashes.Count, me.Volumes.Count);
                        for(int i = 0; i <volumes; i++)
                        {
                            if (m_options.Verificationlevel == VerificationLevel.Signature || m_options.Verificationlevel == VerificationLevel.Full)
                            {
                                try 
                                { 
                                    using(Utility.TempFile tf = new Duplicati.Library.Utility.TempFile())
                                        backend.Get(me.Volumes[i].Key, mf, tf, mf.SignatureHashes[i]); 
                                    results.Add(new KeyValuePair<BackupEntryBase, Exception>(me.Volumes[i].Key, null));
                                }
                                catch (Exception ex) { results.Add(new KeyValuePair<BackupEntryBase,Exception>(me.Volumes[i].Key, ex)); }
                            }

                            if (m_options.Verificationlevel == VerificationLevel.Full)
                            {
                                try 
                                { 
                                    using(Utility.TempFile tf = new Duplicati.Library.Utility.TempFile())
                                        backend.Get(me.Volumes[i].Value, mf, tf, mf.ContentHashes[i]); 
                                    results.Add(new KeyValuePair<BackupEntryBase, Exception>(me.Volumes[i].Value, null));
                                }
                                catch (Exception ex) { results.Add(new KeyValuePair<BackupEntryBase,Exception>(me.Volumes[i].Value, ex)); }

                            }
                        }
                    }

                }

                //Re-generate verification file
                if (m_options.CreateVerificationFile)
                {
                    //Stop any async operations
                    if (m_options.AsynchronousUpload)
                        backend.ExtractPendingUploads();

                    VerificationFile vf = new VerificationFile(entries, new FilenameStrategy(m_options));
                    using (Utility.TempFile tf = new Duplicati.Library.Utility.TempFile())
                    {
                        vf.Save(tf);
                        tf.Protected = true;
                        backend.Put(new VerificationEntry(entries[entries.Count - 1].Time), tf);
                    }
                }

            }

            return results;
        }


        /// <summary>
        /// Reads through a backup and finds the last backup entry that has a specific file
        /// </summary>
        /// <returns></returns>
        public List<KeyValuePair<string, DateTime>> FindLastFileVersion()
        {
            CommunicationStatistics stats = new CommunicationStatistics(DuplicatiOperationMode.FindLastFileVersion);
            SetupCommonOptions(stats);

            if (m_options.DontReadManifests)
                throw new Exception(Strings.Interface.ManifestsMustBeRead);

            if (string.IsNullOrEmpty(m_options.FileToRestore))
                throw new Exception(Strings.Interface.NoFilesGivenError);

            string[] filesToFind = m_options.FileToRestore.Split(System.IO.Path.PathSeparator);
            KeyValuePair<string, DateTime>[] results = new KeyValuePair<string, DateTime>[filesToFind.Length];
            for (int i = 0; i < results.Length; i++)
                results[i] = new KeyValuePair<string, DateTime>(filesToFind[i], new DateTime(0));

            using (BackendWrapper backend = new BackendWrapper(stats, m_backend, m_options))
            {
                //Extract the full backup set list
                List<ManifestEntry> fulls = backend.GetBackupSets();

                //Flatten the list
                List<ManifestEntry> workList = new List<ManifestEntry>();

                //The list is oldest first, this function work newest first
                fulls.Reverse();
                foreach (ManifestEntry f in fulls)
                {
                    f.Incrementals.Reverse();

                    workList.AddRange(f.Incrementals);
                    workList.Add(f);
                }

                bool warned_manifest_v1 = false;

                foreach (ManifestEntry mf in workList)
                {
                    List<Manifestfile.HashEntry> signatureHashes = null;
                    Manifestfile mfi;

                    using(Utility.TempFile tf = new Duplicati.Library.Utility.TempFile())
                    {
                        backend.Get(mf, null, tf, null);
                        mfi = new Manifestfile(tf, m_options.SkipFileHashChecks);
                        if (!m_options.SkipFileHashChecks)
                            signatureHashes = mfi.SignatureHashes;
                    }

                    //If there are no volumes, don't stop here
                    bool any_unmatched = true;

                    if (stats != null && !warned_manifest_v1 && (mfi.SourceDirs == null || mfi.SourceDirs.Length == 0))
                    {
                        warned_manifest_v1 = true;
                        stats.LogWarning(Strings.Interface.ManifestVersionRequiresRelativeNamesWarning, null);
                    }

                    foreach(KeyValuePair<SignatureEntry, ContentEntry> e in mf.Volumes)
                        using (Utility.TempFile tf = new Duplicati.Library.Utility.TempFile())
                        {
                            //Skip non-approved signature files
                            if (signatureHashes != null && e.Key.Volumenumber > signatureHashes.Count)
                            {
                                stats.LogWarning(string.Format(Strings.Interface.SkippedUnlistedSignatureFileWarning, e.Key.Filename), null);
                                continue;
                            }

                            backend.Get(e.Key, mfi, tf, signatureHashes == null ? null : signatureHashes[e.Key.Volumenumber - 1]);

                            any_unmatched = false;

                            RSync.RSyncDir.ContainsFile(mfi, filesToFind, DynamicLoader.CompressionLoader.GetModule(e.Key.Compression, tf, m_options.RawOptions));

                            for (int i = 0; i < filesToFind.Length; i++)
                            {
                                if (results[i].Value.Ticks == 0 && string.IsNullOrEmpty(filesToFind[i]))
                                    results[i] = new KeyValuePair<string,DateTime>(results[i].Key, mf.Time);
                                else
                                    any_unmatched = true;
                            }

                            if (!any_unmatched)
                                break;
                        }

                    if (!any_unmatched)
                        break;
                }

                return new List<KeyValuePair<string,DateTime>>(results);
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
                DynamicLoader.BackendLoader.GetSupportedCommands(m_backend), 
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

            //TODO: Based on the action, see if all options are relevant
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
                if (!string.IsNullOrEmpty(value) && Utility.Utility.ParseBool(value, true) != Utility.Utility.ParseBool(value, false))
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
                    Utility.Sizeparser.ParseSize(value);
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
                    Utility.Timeparser.ParseTimeSpan(value);
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
            foreach (string s in Utility.Utility.EnumerateFiles(folder))
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

        public static string DeleteAllButN(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.DeleteAllButN();
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

        public static void CreateFolder(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                i.CreateFolder();
        }

        public static List<KeyValuePair<string, DateTime>> FindLastFileVersion(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.FindLastFileVersion();
        }

        public static List<KeyValuePair<BackupEntryBase, Exception>> VerifyBackup(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.VerifyBackupChain();
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_options != null && m_options.LoadedModules != null)
            {
                foreach (KeyValuePair<bool, Library.Interface.IGenericModule> mx in m_options.LoadedModules)
                    if (mx.Key && mx.Value is IDisposable)
                        ((IDisposable)mx.Value).Dispose();

                m_options.LoadedModules.Clear();
                OperationRunning(false);
            }

            if (m_hasSetLogging && Logging.Log.CurrentLog is Logging.StreamLog)
            {
                Logging.StreamLog sl = (Logging.StreamLog)Logging.Log.CurrentLog;
                Logging.Log.CurrentLog = null;
                sl.Dispose();
                m_hasSetLogging = false;
            }
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

        public bool IsPauseRequested
        {
            get { return m_liveControl.IsPauseRequested; }
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
