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
    public enum DuplicatiOperation
    {
        Backup,
        Restore,
        List,
        Remove
    };

    public delegate void OperationProgressEvent(Interface caller, DuplicatiOperation operation, int progress, int subprogress, string message, string submessage);

    public class Interface : IDisposable, LiveControl.ILiveControl
    {
        //The amount of progressbar allocated for reading incremental data
        private const double INCREMENAL_COST = 0.15;

        private string m_backend;
        private Options m_options;

        private double m_incrementalFraction = INCREMENAL_COST;
        private double m_progress = 0.0;

        private string m_lastProgressMessage = "";

        public event OperationProgressEvent OperationStarted;
        public event OperationProgressEvent OperationCompleted;
        public event OperationProgressEvent OperationProgress;
        public event OperationProgressEvent OperationError;

        /// <summary>
        /// The live control interface
        /// </summary>
        private LiveControl.LiveControl m_liveControl;

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
        private void Interface_OperationProgress(Interface caller, DuplicatiOperation operation, int progress, int subprogress, string message, string submessage)
        {
            m_lastProgressMessage = message;
        }

        private void CheckLiveControl()
        {
            m_liveControl.PauseIfRequested();

            if (m_liveControl.IsStopRequested)
                throw new LiveControl.ExecutionStoppedException();
        }

        public string Backup(string[] sources)
        {
            BackupStatistics bs = new BackupStatistics();

            SetupCommonOptions();
            BackendWrapper backend = null;
            long volumesUploaded = 0;

            if (m_options.DontReadManifests)
                throw new Exception(Strings.Interface.ManifestsMustBeReadOnBackups);

            if (sources == null || sources.Length == 0)
                throw new Exception(Strings.Interface.NoSourceFoldersError);

            //Make sure they all have the same format
            for (int i = 0; i < sources.Length; i++)
                sources[i] = Core.Utility.AppendDirSeperator(sources[i]);

            //Sanity check for duplicate folders and multiple inclusions of the same folder
            for (int i = 0; i < sources.Length - 1; i++)
                for (int j = i + 1; j < sources.Length; j++)
                    if (sources[i].Equals(sources[j], Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                        throw new Exception(string.Format(Strings.Interface.SourceDirIsIncludedMultipleTimesError, sources[i]));
                    else if (sources[i].StartsWith(sources[j], Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                        throw new Exception(string.Format(Strings.Interface.SourceDirsAreRelatedError, sources[i], sources[j]));


            using (new Logging.Timer("Backup from " + string.Join(";", sources) + " to " + m_backend))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Backup, -1, -1, Strings.Interface.StatusLoadingFilelist, "");
                    OperationProgress(this, DuplicatiOperation.Backup, -1, -1, Strings.Interface.StatusLoadingFilelist, "");

                    CheckLiveControl();

                    bool full = m_options.Full;

                    backend = new BackendWrapper(bs, m_backend, m_options);
                    backend.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupTransfer_ProgressEvent);

                    m_progress = 0.0;

                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");
                    
                    CheckLiveControl();

                    List<ManifestEntry> backupsets = backend.GetBackupSets();

                    if (backupsets.Count == 0)
                        full = true;

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

                            patches = FindPatches(backend, entries, tempfolder);

                            Manifestfile latest = GetManifest(backend, backupsets[0]);

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

                        using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(manifest.SourceDirs, bs, m_options.Filter, patches))
                        {
                            OperationProgress(this, DuplicatiOperation.Backup, -1, -1, Strings.Interface.StatusBuildingFilelist, "");

                            CheckLiveControl();

                            dir.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupRSyncDir_ProgressEvent);

                            dir.DisableFiletimeCheck = m_options.DisableFiletimeCheck;
                            dir.MaxFileSize = m_options.SkipFilesLargerThan;
                            using (new Logging.Timer("Initiating multipass"))
                                dir.InitiateMultiPassDiff(full);

                            bool done = false;
                            while (!done && totalsize < m_options.MaxSize)
                            {
                                using (new Logging.Timer("Multipass " + (vol + 1).ToString()))
                                using (Core.TempFile signaturefile = new Duplicati.Library.Core.TempFile())
                                using (Core.TempFile contentfile = new Duplicati.Library.Core.TempFile())
                                {
                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusCreatingVolume, vol + 1), "");

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

                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingContentVolume, vol + 1), "");

                                    //Last check before we upload, we do not interrupt transfers
                                    CheckLiveControl();

                                    //The backendwrapper will remove these
                                    signaturefile.Protected = true;
                                    contentfile.Protected = true;

                                    manifest.ContentHashes.Add(Core.Utility.CalculateHash(contentfile));
                                    using (new Logging.Timer("Writing delta file " + (vol + 1).ToString()))
                                        backend.Put(new ContentEntry(backuptime, full, vol + 1), contentfile);

                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingSignatureVolume, vol + 1), "");
                                    
                                    manifest.SignatureHashes.Add(Core.Utility.CalculateHash(signaturefile));
                                    using (new Logging.Timer("Writing remote signatures"))
                                        backend.Put(new SignatureEntry(backuptime, full, vol + 1), signaturefile);
                                }

                                //The backend wrapper will remove these
                                Core.TempFile mf = new Duplicati.Library.Core.TempFile();
                                mf.Protected = true;

                                using (new Logging.Timer("Writing manifest"))
                                {
                                    manifest.Save(mf);

                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusUploadingManifestVolume, vol + 1), "");

                                    //Alternate primary/secondary
                                    backend.Put(new ManifestEntry(backuptime, full, manifest.SignatureHashes.Count % 2 != 0), mf);
                                }

                                //A control for partial uploads
                                volumesUploaded++;

                                //The file volume counter
                                vol++;
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    if (volumesUploaded == 0)
                        throw; //This also activates "finally", unlike in other languages...

                    bs.LogError(string.Format(Strings.Interface.PartialUploadMessage, volumesUploaded, ex.Message));
                }
                finally
                {
                    m_progress = 100.0;
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Backup, 100, -1, Strings.Interface.StatusCompleted, "");
                    
                    OperationProgress(this, DuplicatiOperation.Backup, 100, -1, Strings.Interface.StatusCompleted, "");
                }
            }

            bs.EndTime = DateTime.Now;
            return bs.ToString();
        }

        /// <summary>
        /// Event handler for reporting upload/download progress
        /// </summary>
        /// <param name="progress">The upload/download progress in percent</param>
        /// <param name="filename">The name of the file being transfered</param>
        private void BackupTransfer_ProgressEvent(int progress, string filename)
        {
            OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), progress, m_lastProgressMessage, filename);
        }

        /// <summary>
        /// Event handler for reporting backup/restore progress
        /// </summary>
        /// <param name="progress">The total progress in percent</param>
        /// <param name="filename">The file currently being examined</param>
        private void BackupRSyncDir_ProgressEvent(int progress, string filename)
        {
            m_progress = ((1.0 - m_incrementalFraction) * (progress / (double)100.0)) + m_incrementalFraction;
            
            OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusProcessing, filename), "");

            CheckLiveControl();
        }

        /// <summary>
        /// Will attempt to read the manifest file, optinally revering to the secondary manifest if reading one fails.
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
            SetupCommonOptions();
            RestoreStatistics rs = new RestoreStatistics();

            m_progress = 0;
            BackendWrapper backend = null;

            using (new Logging.Timer("Restore from " + m_backend + " to " + target))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, -1, -1, Strings.Interface.StatusStarted, "");
                    OperationProgress(this, DuplicatiOperation.Restore, -1, -1, Strings.Interface.StatusStarted, "");

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

                    OperationProgress(this, DuplicatiOperation.Restore, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementals, "");

                    ManifestEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);

                    m_progress = INCREMENAL_COST;

                    List<ManifestEntry> entries = new List<ManifestEntry>();
                    entries.Add(bestFit);
                    entries.AddRange(bestFit.Incrementals);
                    int patchno = 0;

                    int patchCount = 0;
                    foreach (ManifestEntry be in entries)
                        patchCount += be.Volumes.Count;

                    using (RSync.RSyncDir sync = new Duplicati.Library.Main.RSync.RSyncDir(target, rs, filter))
                    {
                        foreach (ManifestEntry be in entries)
                        {
                            m_progress = ((1.0 - INCREMENAL_COST) * (patchno / (double)patchCount)) + INCREMENAL_COST;

                            OperationProgress(this, DuplicatiOperation.Restore, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusReadingManifest, be.Filename), "");
                            
                            CheckLiveControl();

                            Manifestfile manifest = GetManifest(backend, be);

                            CheckLiveControl();

                            foreach (KeyValuePair<SignatureEntry, ContentEntry> vol in be.Volumes)
                            {
                                ContentEntry contentVol = vol.Value;
                                SignatureEntry signatureVol = vol.Key;

                                m_progress = ((1.0 - INCREMENAL_COST) * (patchno / (double)patchCount)) + INCREMENAL_COST;

                                //Skip nonlisted
                                if (manifest.ContentHashes != null && contentVol.Volumenumber > manifest.ContentHashes.Count)
                                {
                                    Logging.Log.WriteMessage(string.Format(Strings.Interface.SkippedContentVolumeLogMessage, contentVol.Volumenumber), Duplicati.Library.Logging.LogMessageType.Warning);
                                    patchno++;
                                    continue; //TODO: Report this
                                }

                                if (vol.Value.Compression != "zip")
                                    throw new Exception(string.Format(Strings.Interface.UnexpectedCompressionError, contentVol.Compression));

                                using (Core.TempFile patchzip = new Duplicati.Library.Core.TempFile())
                                {
                                    OperationProgress(this, DuplicatiOperation.Restore, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusPatching, patchno + 1), "");

                                    CheckLiveControl();

                                     if (m_options.HasFilter || !string.IsNullOrEmpty(m_options.FileToRestore))
                                     {
                                         bool hasFiles = false;

                                         using (Core.TempFile sigFile = new Duplicati.Library.Core.TempFile())
                                         {
                                             OperationProgress(this, DuplicatiOperation.Restore, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusDownloadingSignatureVolume, patchno + 1), "");

                                             using (new Logging.Timer("Get " + signatureVol.Filename))
                                                 backend.Get(signatureVol, sigFile, manifest.SignatureHashes == null ? null : manifest.SignatureHashes[signatureVol.Volumenumber - 1]);

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

                                     OperationProgress(this, DuplicatiOperation.Restore, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusDownloadingContentVolume, patchno + 1), "");

                                    using (new Logging.Timer("Get " + contentVol.Filename))
                                        backend.Get(contentVol, patchzip, manifest.ContentHashes == null ? null : manifest.ContentHashes[contentVol.Volumenumber - 1]);

                                    OperationProgress(this, DuplicatiOperation.Restore, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusPatching, patchno + 1), "");
                                    
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
                        OperationCompleted(this, DuplicatiOperation.Restore, 100, -1, Strings.Interface.StatusCompleted, "");

                    OperationProgress(this, DuplicatiOperation.Restore, 100, -1, Strings.Interface.StatusCompleted, "");
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
            SetupCommonOptions();
            RestoreStatistics rs = new RestoreStatistics();

            BackendWrapper backend = null;

            using (new Logging.Timer("Restore control files from " + m_backend + " to " + target))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, 0, -1, Strings.Interface.StatusStarted, "");

                    backend = new BackendWrapper(rs, m_backend, m_options);

                    List<ManifestEntry> attempts = backend.GetBackupSets();

                    List<ManifestEntry> flatlist = new List<ManifestEntry>();
                    foreach (ManifestEntry be in attempts)
                    {
                        flatlist.Add(be);
                        flatlist.AddRange(be.Incrementals);
                    }

                    flatlist.Reverse();

                    string prefix = Core.Utility.AppendDirSeperator(RSync.RSyncDir.CONTROL_ROOT);

                    foreach (ManifestEntry be in flatlist)
                    {
                        if (be.Volumes.Count > 0)
                            using(Core.TempFile z = new Duplicati.Library.Core.TempFile())
                            {
                                OperationProgress(this, DuplicatiOperation.Backup, 0, -1, string.Format(Strings.Interface.StatusReadingIncrementalFile, be.Volumes[0].Key.Filename), "");

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

                                    rs.LogError(string.Format(Strings.Interface.FailedToFindControlFilesMessage, be.Volumes[0].Key.Filename));
                                }
                            }
                    }

                }
                finally
                {
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Restore, 100, -1, Strings.Interface.StatusCompleted, "");
                }
            }

            rs.EndTime = DateTime.Now;

            return rs.ToString();
        }

        public string DeleteAllButNFull()
        {
            int x = Math.Max(0, m_options.RemoveAllButNFull);

            StringBuilder sb = new StringBuilder();

            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, 0, -1, Strings.Interface.StatusStarted, "");

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
                    OperationCompleted(this, DuplicatiOperation.Remove, 100, -1, Strings.Interface.StatusCompleted, "");
            }

            return sb.ToString();
        }

        public string DeleteOlderThan()
        {
            StringBuilder sb = new StringBuilder();

            DateTime expires = m_options.RemoveOlderThan;

            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, 0, -1, Strings.Interface.StatusStarted, "");

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
                    OperationCompleted(this, DuplicatiOperation.Remove, 100, -1, Strings.Interface.StatusCompleted, "");
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
            SetupCommonOptions();

            List<string> res = new List<string>();
            Duplicati.Library.Interface.IBackend i = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(m_backend, m_options.RawOptions);

            if (i == null)
                throw new Exception(string.Format(Strings.Interface.BackendNotFoundError, m_backend));

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, Strings.Interface.StatusStarted, "");

            using(i)
                foreach (Duplicati.Library.Interface.IFileEntry fe in i.List())
                    res.Add(fe.Name);

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, 100, -1, Strings.Interface.StatusCompleted, "");

            return res.ToArray();
        }

        public string Cleanup()
        {
            bool anyRemoved = false;
            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
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

        public IList<string> ListContent()
        {
            SetupCommonOptions();
            RestoreStatistics rs = new RestoreStatistics();

            Core.FilenameFilter filter = m_options.Filter;
            DateTime timelimit = m_options.RestoreTime;

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, Strings.Interface.StatusStarted, "");

            List<string> res;

            using (BackendWrapper backend = new BackendWrapper(rs, m_backend, m_options))
            using (Core.TempFolder basefolder = new Duplicati.Library.Core.TempFolder())
            {
                ManifestEntry bestFit = backend.GetBackupSet(timelimit);

                List<ManifestEntry> entries = new List<ManifestEntry>();
                entries.Add(bestFit);
                entries.AddRange(bestFit.Incrementals);

                List<Library.Interface.ICompression> patches = FindPatches(backend, entries, basefolder);

                using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(new string[] { basefolder }, rs, filter, patches))
                    res = dir.UnmatchedFiles();
            }

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, 100, -1, Strings.Interface.StatusCompleted, "");

            return res;
        }

        public string[] ListSourceFolders()
        {
            SetupCommonOptions();
            RestoreStatistics rs = new RestoreStatistics();

            Core.FilenameFilter filter = m_options.Filter;
            DateTime timelimit = m_options.RestoreTime;

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, Strings.Interface.StatusStarted, "");

            string[] res;

            using (BackendWrapper backend = new BackendWrapper(rs, m_backend, m_options))
            using (Core.TempFile mfile = new Duplicati.Library.Core.TempFile())
            {
                ManifestEntry bestFit = backend.GetBackupSet(timelimit);

                backend.Get(bestFit, mfile, null);
                res = new Manifestfile(mfile).SourceDirs;
            }

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, 100, -1, Strings.Interface.StatusCompleted, "");

            return res;
        }


        private void SetupCommonOptions()
        {
            if (!string.IsNullOrEmpty(m_options.TempDir))
                Core.TempFolder.SystemTempPath = m_options.TempDir;

            if (!string.IsNullOrEmpty(m_options.ThreadPriority))
                System.Threading.Thread.CurrentThread.Priority = Core.Utility.ParsePriority(m_options.ThreadPriority);

            //Load all generic modules
            m_options.LoadedModules.Clear();

            foreach (Library.Interface.IGenericModule m in DynamicLoader.GenericLoader.Modules)
                m_options.LoadedModules.Add(new KeyValuePair<bool, Library.Interface.IGenericModule>(Array.IndexOf<string>(m_options.DisableModules, m.Key.ToLower()) < 0 && (m.LoadAsDefault || Array.IndexOf<string>(m_options.EnableModules, m.Key.ToLower()) >= 0), m));

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
        /// <returns>A list of file archives</returns>
        private List<Library.Interface.ICompression> FindPatches(BackendWrapper backend, List<ManifestEntry> entries, string tempfolder)
        {
            List<Library.Interface.ICompression> patches = new List<Library.Interface.ICompression>();

            using (new Logging.Timer("Reading incremental data"))
            {
                OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, Strings.Interface.StatusReadingIncrementalData, "");

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


                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusReadingManifest, be.Time.ToShortDateString() + " " + be.Time.ToShortTimeString()), "");

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

                        OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, string.Format(Strings.Interface.StatusReadingSignatureFile, be.Time.ToShortDateString() + " " + be.Time.ToShortTimeString(), bes.Key.Volumenumber), "");

                        string filename = System.IO.Path.Combine(tempfolder, "patch-" + patches.Count.ToString() + ".zip");

                        using (new Logging.Timer("Get " + bes.Key.Filename))
                            backend.Get(bes.Key, filename, manifest.SignatureHashes == null ? null : manifest.SignatureHashes[bes.Key.Volumenumber - 1]);

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
            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
                return backend.GetBackupSets();
        }

        public List<KeyValuePair<RSync.RSyncDir.PatchFileType, string>> ListActualSignatureFiles()
        {
            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
            {
                ManifestEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);
                if (bestFit.Incrementals.Count > 0) //Get the most recent incremental
                    bestFit = bestFit.Incrementals[bestFit.Incrementals.Count - 1];

                using (Core.TempFolder folder = new Duplicati.Library.Core.TempFolder())
                {
                    List<Library.Interface.ICompression> patches = FindPatches(backend, new List<ManifestEntry>(new ManifestEntry[] { bestFit }), folder);
                    using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(new string[] { folder }, new CommunicationStatistics(), null))
                        return dir.ListPatchFiles(patches);
                }
            }

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

        public static IList<string> ListContent(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.ListContent();
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
