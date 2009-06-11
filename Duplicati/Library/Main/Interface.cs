#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

    public class Interface : IDisposable
    {
        private string m_backend;
        //private Dictionary<string, string> m_options;
        private Options m_options;

        //The amount of progressbar allocated for reading incremental data
        private double m_incrementalFraction = 0.15;
        private double m_progress = 0.0;
        private string m_progress_message = "";

        /// <summary>
        /// The filenamestrategy for the signature cache
        /// </summary>
        FilenameStrategy m_cacheFilenameStrategy = new FilenameStrategy("dpl", "_", true);

        public event OperationProgressEvent OperationStarted;
        public event OperationProgressEvent OperationCompleted;
        public event OperationProgressEvent OperationProgress;
        public event OperationProgressEvent OperationError;

        public Interface(string backend, Dictionary<string, string> options)
        {
            m_backend = backend;
            m_options = new Options(options);
            OperationProgress += new OperationProgressEvent(Interface_OperationProgress);
        }

        void Interface_OperationProgress(Interface caller, DuplicatiOperation operation, int progress, int subprogress, string message, string submessage)
        {
            m_progress_message = message;
        }

        public string Backup(string source)
        {
            BackupStatistics bs = new BackupStatistics();

            SetupCommonOptions();
            BackendWrapper backend = null;
            long volumesUploaded = 0;


            using (new Logging.Timer("Backup from " + source + " to " + m_backend))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Backup, -1, -1, "Loading remote filelist", "");

                    OperationProgress(this, DuplicatiOperation.Backup, -1, -1, "Loading remote filelist", "");

                    bool full = m_options.Full;

                    backend = new BackendWrapper(bs, m_backend, m_options);
                    backend.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupTransfer_ProgressEvent);

                    m_progress = 0.0;

                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading incremental data", "");

                    List<BackupEntry> backupsets = backend.GetBackupSets();

                    if (backupsets.Count == 0)
                        full = true;

                    if (!full)
                        full = DateTime.Now > m_options.FullIfOlderThan(backupsets[backupsets.Count - 1].Time);

                    List<string> controlfiles = new List<string>();
                    if (!string.IsNullOrEmpty(m_options.SignatureControlFiles))
                        controlfiles.AddRange(m_options.SignatureControlFiles.Split(System.IO.Path.PathSeparator));

                    using (Core.TempFolder tempfolder = new Duplicati.Library.Core.TempFolder())
                    {
                        List<Core.IFileArchive> patches = new List<Duplicati.Library.Core.IFileArchive>();
                        if (!full)
                        {
                            List<BackupEntry> entries = new List<BackupEntry>();
                            entries.Add(backupsets[backupsets.Count - 1]);
                            entries.AddRange(backupsets[backupsets.Count - 1].Incrementals);

                            patches = FindPatches(backend, entries, tempfolder);
                        }
                        else
                            m_incrementalFraction = 0.0;

                        DateTime backuptime = DateTime.Now;

                        using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(source, bs, m_options.Filter, patches))
                        {
                            OperationProgress(this, DuplicatiOperation.Backup, -1, -1, "Building filelist ...", "");
                            dir.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupRSyncDir_ProgressEvent);

                            dir.DisableFiletimeCheck = m_options.DisableFiletimeCheck;
                            dir.MaxFileSize = m_options.SkipFilesLargerThan;
                            using (new Logging.Timer("Initiating multipass"))
                                dir.InitiateMultiPassDiff(full);

                            int vol = 0;
                            long totalsize = 0;

                            List<string> contenthashes = new List<string>();
                            List<string> signaturehashes = new List<string>();

                            bool done = false;
                            while (!done && totalsize < m_options.MaxSize)
                            {
                                using (new Logging.Timer("Multipass " + (vol + 1).ToString()))
                                {
                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Creating volume " + (vol + 1).ToString(), "");

                                    //The backendwrapper will remove these
                                    Core.TempFile signaturefile = new Duplicati.Library.Core.TempFile();
                                    Core.TempFile contentfile = new Duplicati.Library.Core.TempFile();

                                    signaturefile.Protected = true;
                                    contentfile.Protected = true;

                                    using (Compression.FileArchiveZip signaturearchive = Duplicati.Library.Compression.FileArchiveZip.CreateArchive(signaturefile))
                                    using (Compression.FileArchiveZip contentarchive = Duplicati.Library.Compression.FileArchiveZip.CreateArchive(contentfile))
                                    {
                                        //Add signature files to archive
                                        foreach (string s in controlfiles)
                                            if (!string.IsNullOrEmpty(s))
                                                using (System.IO.Stream cs = signaturearchive.CreateFile(System.IO.Path.Combine(RSync.RSyncDir.CONTROL_ROOT, System.IO.Path.GetFileName(s))))
                                                using (System.IO.FileStream fs = System.IO.File.OpenRead(s))
                                                    Core.Utility.CopyStream(fs, cs);

                                        //Only add control files to the very first volume
                                        controlfiles.Clear();

                                        done = dir.MakeMultiPassDiff(signaturearchive, contentarchive, Math.Min(m_options.VolumeSize, m_options.MaxSize - totalsize));
                                        totalsize += new System.IO.FileInfo(contentfile).Length;

                                        totalsize += signaturearchive.Size;

                                        if (totalsize >= m_options.MaxSize)
                                            dir.FinalizeMultiPass(signaturearchive, contentarchive);
                                    }

                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Uploading content, volume " + (vol + 1).ToString(), "");

                                    contenthashes.Add(Core.Utility.CalculateHash(contentfile));
                                    using (new Logging.Timer("Writing delta file " + (vol + 1).ToString()))
                                        backend.Put(new BackupEntry(BackupEntry.EntryType.Content, full, backuptime, vol + 1), contentfile);

                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Uploading signatures, volume " + (vol + 1).ToString(), "");

                                    signaturehashes.Add(Core.Utility.CalculateHash(signaturefile));
                                    using (new Logging.Timer("Writing remote signatures"))
                                        backend.Put(new BackupEntry(BackupEntry.EntryType.Signature, full, backuptime, vol + 1), signaturefile);

                                }

                                //The backend wrapper will remove this
                                Core.TempFile mf = new Duplicati.Library.Core.TempFile();
                                mf.Protected = true;

                                using (new Logging.Timer("Writing manifest"))
                                {
                                    System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                                    System.Xml.XmlNode root = doc.AppendChild(doc.CreateElement("Manifest"));
                                    root.Attributes.Append(doc.CreateAttribute("version")).Value = "1";
                                    root.AppendChild(doc.CreateElement("VolumeCount")).InnerText = (vol + 1).ToString();
                                    System.Xml.XmlNode contentroot = root.AppendChild(doc.CreateElement("ContentFiles"));
                                    System.Xml.XmlNode signatureroot = root.AppendChild(doc.CreateElement("SignatureFiles"));

                                    foreach (string s in contenthashes)
                                        contentroot.AppendChild(doc.CreateElement("Hash")).InnerText = s;
                                    foreach (string s in signaturehashes)
                                        signatureroot.AppendChild(doc.CreateElement("Hash")).InnerText = s;

                                    doc.Save(mf);

                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Uploading manifest, volume " + (vol + 1).ToString(), "");

                                    backend.Put(new BackupEntry(BackupEntry.EntryType.Manifest, full, backuptime, 0), mf);
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

                    bs.LogError("Failed after uploading " + volumesUploaded.ToString() + " volumes. Message: " + ex.Message);
                }
                finally
                {
                    m_progress = 100.0;
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Backup, 100, -1, "Completed", "");
                    
                    OperationProgress(this, DuplicatiOperation.Backup, 100, -1, "Completed", "");
                }
            }

            bs.EndTime = DateTime.Now;
            return bs.ToString();
        }

        private void BackupTransfer_ProgressEvent(int progress, string filename)
        {
            if (OperationProgress != null)
                OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), progress, m_progress_message, filename);
        }

        private void BackupRSyncDir_ProgressEvent(int progress, string filename)
        {
            m_progress = ((1.0 - m_incrementalFraction) * (progress / (double)100.0)) + m_incrementalFraction;
            OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Processing: " + filename, "");
        }

        public string Restore(string target)
        {
            SetupCommonOptions();
            RestoreStatistics rs = new RestoreStatistics();

            BackendWrapper backend = null;

            using (new Logging.Timer("Restore from " + m_backend + " to " + target))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, -1, -1, "Started", "");

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

                    BackupEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);

                    List<BackupEntry> entries = new List<BackupEntry>();
                    entries.Add(bestFit);
                    entries.AddRange(bestFit.Incrementals);
                    int patchno = 0;

                    using (RSync.RSyncDir sync = new Duplicati.Library.Main.RSync.RSyncDir(target, rs, filter))
                    {
                        foreach (BackupEntry be in entries)
                        {
                            List<string> contentHashes = null;
                            List<string> signatureHashes = null;
                            if (!m_options.SkipFileHashChecks)
                            {
                                OperationProgress(this, DuplicatiOperation.Backup, 0, -1, "Reading manifest file: " + be.Filename, "");

                                contentHashes = new List<string>();
                                signatureHashes = new List<string>();

                                using (new Logging.Timer("Get " + be.Filename))
                                using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
                                {
                                    backend.Get(be, tf, null);
                                    System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                                    doc.Load(tf);
                                    foreach (System.Xml.XmlNode n in doc.SelectNodes("Manifest/ContentFiles/Hash"))
                                        contentHashes.Add(n.InnerText);
                                    foreach (System.Xml.XmlNode n in doc.SelectNodes("Manifest/SignatureFiles/Hash"))
                                        signatureHashes.Add(n.InnerText);
                                }
                            }

                            foreach (BackupEntry vol in be.ContentVolumes)
                            {
                                //Skip nonlisted
                                if (contentHashes != null && vol.VolumeNumber > contentHashes.Count)
                                    continue; //TODO: Report this

                                if (vol.CompressionMode != "zip")
                                    throw new Exception("Unexpected compression mode");

                                using (Core.TempFile patchzip = new Duplicati.Library.Core.TempFile())
                                {
                                    //TODO: Set better text messages
                                     OperationProgress(this, DuplicatiOperation.Backup, 0, -1, "Patching restore with #" + (patchno + 1).ToString(), "");

                                     if (m_options.HasFilter || !string.IsNullOrEmpty(m_options.FileToRestore))
                                     {
                                         bool hasFiles = false;

                                         using (Core.TempFile sigFile = new Duplicati.Library.Core.TempFile())
                                         {
                                             BackupEntry signatureVol = new BackupEntry(BackupEntry.EntryType.Signature, vol.IsFull, vol.Time, vol.VolumeNumber);
                                             using (new Logging.Timer("Get " + signatureVol))
                                                 backend.Get(signatureVol, sigFile, signatureHashes == null ? null : signatureHashes[signatureVol.VolumeNumber - 1]);

                                             using (Core.IFileArchive patch = new Compression.FileArchiveZip(sigFile))
                                             {
                                                 foreach(KeyValuePair<RSync.RSyncDir.PatchFileType, string> k in sync.ListPatchFiles(patch))
                                                     if (filter.ShouldInclude("", System.IO.Path.DirectorySeparatorChar.ToString() + k.Value))
                                                     {
                                                         hasFiles = true; 
                                                         break;
                                                     }
                                             }
                                         }

                                         if (!hasFiles)
                                             continue; //Avoid downloading the content file
                                     }

                                    using (new Logging.Timer("Get " + vol.Filename))
                                        backend.Get(vol, patchzip, contentHashes == null ? null : contentHashes[vol.VolumeNumber - 1]);

                                    using (new Logging.Timer((patchno == 0 ? "Full restore to: " : "Incremental restore " + patchno.ToString() + " to: ") + target))
                                    using(Core.IFileArchive patch = new Compression.FileArchiveZip(patchzip))
                                        sync.Patch(target, patch);
                                }
                                patchno++;
                            }
                        }
                    }
                }
                finally
                {
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Restore, 100, -1, "Completed", "");
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
                        OperationStarted(this, DuplicatiOperation.Restore, 0, -1, "Started", "");

                    backend = new BackendWrapper(rs, m_backend, m_options);

                    List<BackupEntry> attempts = backend.GetBackupSets();

                    List<BackupEntry> flatlist = new List<BackupEntry>();
                    foreach (BackupEntry be in attempts)
                    {
                        flatlist.Add(be);
                        flatlist.AddRange(be.Incrementals);
                    }

                    flatlist.Reverse();

                    string prefix = Core.Utility.AppendDirSeperator(RSync.RSyncDir.CONTROL_ROOT);

                    foreach (BackupEntry be in flatlist)
                    {
                        if (be.SignatureFile.Count > 0)
                            using(Core.TempFile z = new Duplicati.Library.Core.TempFile())
                            {
                                OperationProgress(this, DuplicatiOperation.Backup, 0, -1, "Reading incremental data from " + be.SignatureFile[0].Filename, "");

                                //TODO: Verify file hashes
                                using (new Logging.Timer("Get " + be.SignatureFile[0].Filename))
                                    backend.Get(be.SignatureFile[0], z, null);
                                
                                using(Compression.FileArchiveZip fz = new Duplicati.Library.Compression.FileArchiveZip(z))
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

                                    rs.LogError("Failed to find control files in: " + be.SignatureFile[0].Filename);
                                }
                            }
                    }

                }
                finally
                {
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Restore, 100, -1, "Completed", "");
                }
            }

            rs.EndTime = DateTime.Now;

            //TODO: The RS should have the number of restored files, and the size of those
            //but that is a little difficult, because some may be restored, and then removed

            return rs.ToString();
        }

        public string RemoveAllButNFull()
        {
            int x = m_options.RemoveAllButNFull;

            using(BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, 0, -1, "Started", "");

                List<BackupEntry> entries = backend.GetBackupSets();
                List<BackupEntry> toremove = new List<BackupEntry>();

                while (entries.Count > x)
                {
                    BackupEntry be = entries[0];
                    entries.RemoveAt(0);

                    be.Incrementals.Reverse();
                    toremove.AddRange(be.Incrementals);
                    toremove.Add(be);
                }

                return RemoveBackupSets(backend, toremove);
            }
            finally
            {
                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.Remove, 100, -1, "Completed", "");
            }
        }

        public string RemoveOlderThan()
        {
            StringBuilder sb = new StringBuilder();

            DateTime expires = m_options.RemoveOlderThan;

            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, 0, -1, "Started", "");

                List<BackupEntry> entries = backend.GetBackupSets();
                List<BackupEntry> toremove = new List<BackupEntry>();

                while (entries.Count > 0 && entries[0].Time <= expires)
                {
                    BackupEntry be = entries[0];
                    entries.RemoveAt(0);

                    bool hasNewer = false;
                    foreach (BackupEntry bex in be.Incrementals)
                        if (bex.Time >= expires)
                        {
                            hasNewer = true;
                            break;
                        }

                    if (hasNewer)
                    {
                        List<BackupEntry> t = new List<BackupEntry>(be.Incrementals);
                        t.Insert(0, be);

                        for (int i = 0; i < t.Count; i++)
                            if (t[i].Time <= expires)
                                sb.AppendLine("Not deleting backup at time: " + t[i].Time.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", because later backups depend on it");

                        break;
                    }
                    else
                    {
                        be.Incrementals.Reverse();
                        toremove.AddRange(be.Incrementals);
                        toremove.Add(be);
                    }
                }


                sb.Append(RemoveBackupSets(backend, toremove));
            }
            finally
            {
                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.Remove, 100, -1, "Completed", "");
            }

            return sb.ToString();
        }

        private string RemoveBackupSets(BackendWrapper backend, List<BackupEntry> entries)
        {
            StringBuilder sb = new StringBuilder();

            foreach (BackupEntry be in entries)
            {
                sb.AppendLine("Deleting backup at " + be.Time.ToString(System.Globalization.CultureInfo.InvariantCulture));

                if (m_options.Force)
                {
                    //Delete manifest
                    backend.Delete(be);

                    foreach (BackupEntry bex in be.ContentVolumes)
                        backend.Delete(bex);

                    foreach (BackupEntry bex in be.SignatureFile)
                        backend.Delete(bex);
                }
            }

            if (!m_options.Force && entries.Count > 0)
                sb.AppendLine("Files are not deleted, use the --force command to actually remove files");

            return sb.ToString();
        }


        public string[] List()
        {
            SetupCommonOptions();

            List<string> res = new List<string>();
            Duplicati.Library.Backend.IBackend i = new Duplicati.Library.Backend.BackendLoader(m_backend, m_options.RawOptions);

            if (i == null)
                throw new Exception("Unable to find backend for target: " + m_backend);

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, "Started", "");

            using(i)
                foreach (Duplicati.Library.Backend.FileEntry fe in i.List())
                    res.Add(fe.Name);

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, 100, -1, "Completed", "");

            return res.ToArray();
        }

        public string Cleanup()
        {
            bool anyRemoved = false;
            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
            {
                List<BackupEntry> orphans = new List<BackupEntry>();
                List<BackupEntry> sorted = backend.GetBackupSets();

                List<BackupEntry> entries = new List<BackupEntry>();
                entries.AddRange(sorted);
                foreach (BackupEntry be in sorted)
                    entries.AddRange(be.Incrementals);

                backend.DeleteOrphans();

                //Now compare the actual filelist with the manifest
                foreach (BackupEntry be in entries)
                {
                    using (new Logging.Timer("Get " + be.Filename))
                    using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
                    {
                        backend.Get(be, tf, null);
                        System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                        doc.Load(tf);

                        int count = doc.SelectNodes("Manifest/SignatureFiles/Hash").Count;
                        if (doc.SelectNodes("Manifest/ContentFiles/Hash").Count != count)
                            throw new Exception("Invalid manifest, wrong count: " + be.Filename);
                        if (count == 0)
                            throw new Exception("Invalid manifest, no files: " + be.Filename);

                        for (int i = count - 1; i < be.SignatureFile.Count; i++)
                        {
                            anyRemoved = true;
                            Logging.Log.WriteMessage("Removing partial file: " + be.SignatureFile[i].Filename, Duplicati.Library.Logging.LogMessageType.Information);
                            if (m_options.Force)
                                backend.Delete(be.SignatureFile[i]);
                        }

                        for (int i = count - 1; i < be.ContentVolumes.Count; i++)
                        {
                            anyRemoved = true;
                            Logging.Log.WriteMessage("Removing partial file: " + be.SignatureFile[i].Filename, Duplicati.Library.Logging.LogMessageType.Information);
                            if (m_options.Force)
                                backend.Delete(be.ContentVolumes[i]);
                        }
                    }
                }
            }

            if (!m_options.Force && anyRemoved)
                Logging.Log.WriteMessage("No files removed, specify --force to remove files.", Duplicati.Library.Logging.LogMessageType.Information);

            return ""; //TODO: Write a message here?
        }

        public IList<string> ListContent()
        {
            SetupCommonOptions();
            RestoreStatistics rs = new RestoreStatistics();

            Core.FilenameFilter filter = m_options.Filter;
            DateTime timelimit = m_options.RestoreTime;

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, "Started", "");

            List<string> res;

            using (BackendWrapper backend = new BackendWrapper(rs, m_backend, m_options))
            using (Core.TempFolder basefolder = new Duplicati.Library.Core.TempFolder())
            {
                BackupEntry bestFit = backend.GetBackupSet(timelimit);

                List<BackupEntry> entries = new List<BackupEntry>();
                entries.Add(bestFit);
                entries.AddRange(bestFit.Incrementals);

                List<Core.IFileArchive> patches = FindPatches(backend, entries, basefolder);

                using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(basefolder, rs, filter, patches))
                    res = dir.UnmatchedFiles();
            }

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, 100, -1, "Completed", "");

            return res;
        }


        private void SetupCommonOptions()
        {
            if (!string.IsNullOrEmpty(m_options.TempDir))
                Core.TempFolder.SystemTempPath = m_options.TempDir;

            if (!string.IsNullOrEmpty(m_options.ThreadPriority))
                System.Threading.Thread.CurrentThread.Priority = Core.Utility.ParsePriority(m_options.ThreadPriority);
        }

        private List<Core.IFileArchive> FindPatches(BackendWrapper backend, List<BackupEntry> entries, string tempfolder)
        {
            List<Core.IFileArchive> patches = new List<Core.IFileArchive>();

            using (new Logging.Timer("Reading incremental data"))
            {
                OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading incremental data ...", "");

                //Calculate the total number of files to download
                //, and verify their order
                int incCount = 0;
                foreach (BackupEntry be in entries)
                {
                    int volNo = 0;
                    //Prevent order based bugs
                    if (entries.IndexOf(be) > 0)
                        if (entries[entries.IndexOf(be) - 1].Time >= be.Time)
                            throw new Exception("Bad sorting of backup times detected");

                    incCount++;
                    foreach (BackupEntry bes in be.SignatureFile)
                    {
                        incCount++;
                        if (volNo + 1 != bes.VolumeNumber)
                            throw new Exception("Bad sort order on volumes detected");

                        volNo++;
                    }
                }

                //The incremental part has a fixed cost, and each file has a fixed fraction of that
                double unitCost = m_incrementalFraction / incCount;

                foreach (BackupEntry be in entries)
                {
                    m_progress += unitCost;

                    List<string> signatureHashes = null;
                    if (!m_options.SkipFileHashChecks)
                    {
                        OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading manifest file: " + be.Time.ToShortDateString() + " " + be.Time.ToShortTimeString(), "");

                        signatureHashes = new List<string>();

                        using (new Logging.Timer("Get " + be.Filename))
                        using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
                        {
                            backend.Get(be, tf, null);
                            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                            doc.Load(tf);
                            foreach (System.Xml.XmlNode n in doc.SelectNodes("Manifest/SignatureFiles/Hash"))
                                signatureHashes.Add(n.InnerText);
                        }

                    }

                    foreach (BackupEntry bes in be.SignatureFile)
                    {
                        m_progress += unitCost;

                        //Skip non-listed incrementals
                        if (signatureHashes != null && bes.VolumeNumber > signatureHashes.Count)
                        {
                            foreach (BackupEntry bec in be.ContentVolumes)
                                if (bec.VolumeNumber == bes.VolumeNumber)
                                    backend.AddOrphan(bec);

                            backend.AddOrphan(bes);
                            continue;
                        }

                        OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading signatures: " + bes.Time.ToShortDateString() + " " + bes.Time.ToShortTimeString() + ", vol " + bes.VolumeNumber.ToString(), "");

                        string filename = System.IO.Path.Combine(tempfolder, "patch-" + patches.Count.ToString() + ".zip");

                        using (new Logging.Timer("Get " + bes.Filename))
                            backend.Get(bes, filename, signatureHashes == null ? null : signatureHashes[bes.VolumeNumber - 1]);

                        patches.Add(new Compression.FileArchiveZip(filename));
                    }
                }
            }

            backend.DeleteOrphans();

            return patches;
        }

        public void PurgeSignatureCache()
        {
            if (string.IsNullOrEmpty(m_options.SignatureCachePath))
                throw new Exception("Signature cache path was not given as an argument");
            else
                RemoveSignatureFiles(m_options.SignatureCachePath);
        }

        public List<BackupEntry> GetBackupSets()
        {
            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
                return backend.GetBackupSets();
        }

        public List<KeyValuePair<RSync.RSyncDir.PatchFileType, string>> ListActualSignatureFiles()
        {
            using (BackendWrapper backend = new BackendWrapper(new CommunicationStatistics(), m_backend, m_options))
            {
                BackupEntry bestFit = backend.GetBackupSet(m_options.RestoreTime);
                if (bestFit.Incrementals.Count > 0) //Get the most recent incremental
                    bestFit = bestFit.Incrementals[bestFit.Incrementals.Count - 1];

                using (Core.TempFolder folder = new Duplicati.Library.Core.TempFolder())
                {
                    List<Core.IFileArchive> patches = FindPatches(backend, new List<BackupEntry>(new BackupEntry[] { bestFit }), folder);
                    using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(folder, new CommunicationStatistics(), null))
                        return dir.ListPatchFiles(patches);
                }
            }

        }

        public static void RemoveSignatureFiles(string folder)
        {
            FilenameStrategy cachenames = BackendWrapper.CreateCacheFilenameStrategy();
            foreach (string s in Core.Utility.EnumerateFiles(folder))
            {
                BackupEntry e = cachenames.DecodeFilename(new Duplicati.Library.Backend.FileEntry(System.IO.Path.GetFileName(s)));
                if (e.IsShortName == cachenames.UseShortNames && e.Type == BackupEntry.EntryType.Signature)
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

        public static string Backup(string source, string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.Backup(source);
        }

        public static string Restore(string source, string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(source, options))
                return i.Restore(target);
        }

        public static List<BackupEntry> ParseFileList(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.GetBackupSets();
        }

        public static IList<string> ListContent(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.ListContent();
        }

        public static string RemoveAllButNFull(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.RemoveAllButNFull();
        }

        public static string RemoveOlderThan(string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(target, options))
                return i.RemoveOlderThan();
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
                throw new Exception("Signature cache path was not given as an argument");
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
    }
}
