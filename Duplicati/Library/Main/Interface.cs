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

    public delegate void OperationProgressEvent(Interface caller, DuplicatiOperation operation, int progress, int subprogress, string message);

    public class Interface : IDisposable
    {
        private string m_backend;
        private Dictionary<string, string> m_options;

        //The amount of progressbar allocated for reading incremental data
        private double m_incrementalFraction = 0.15;
        private double m_progress = 0.0;

        public event OperationProgressEvent OperationStarted;
        public event OperationProgressEvent OperationCompleted;
        public event OperationProgressEvent OperationProgress;
        public event OperationProgressEvent OperationError;

        public Interface(string backend, Dictionary<string, string> options)
        {
            m_backend = backend;
            m_options = options;
        }

        public string Backup(string source)
        {
            BackupStatistics bs = new BackupStatistics();

            SetupCommonOptions(m_options);
            Backend.IBackend backend = null;

            using (new Logging.Timer("Backup from " + source + " to " + m_backend))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Backup, -1, -1, "Loading remote filelist");

                    if (OperationProgress != null)
                        OperationProgress(this, DuplicatiOperation.Backup, -1, -1, "Loading remote filelist");

                    FilenameStrategy fns = new FilenameStrategy(m_options);
                    FilenameStrategy cacheFilenameStrategy = new FilenameStrategy("dpl", "_", true);
                    
                    Core.FilenameFilter filter = new Duplicati.Library.Core.FilenameFilter(m_options);
                    bool full = m_options.ContainsKey("full");

                    int volumesize = (int)Core.Sizeparser.ParseSize(m_options.ContainsKey("volsize") ? m_options["volsize"] : "5", "mb");
                    volumesize = Math.Max(1024 * 1024, volumesize);

                    long totalmax = m_options.ContainsKey("totalsize") ? Core.Sizeparser.ParseSize(m_options["totalsize"], "mb") : long.MaxValue;
                    totalmax = Math.Max(volumesize, totalmax);

                    backend = Backend.BackendLoader.GetBackend(m_backend, m_options);
                    if (backend == null)
                        throw new Exception("Unable to find backend for m_backend: " + m_backend);
                    backend = new BackendWrapper(bs, backend, m_options);
                    ((BackendWrapper)backend).ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupTransfer_ProgressEvent);
                    backend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, m_options);

                    List<BackupEntry> prev = ParseFileList(backend);

                    m_progress = 0.0;

                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading incremental data");

                    if (prev.Count == 0)
                        full = true;

                    if (!full && m_options.ContainsKey("full-if-older-than"))
                        full = DateTime.Now > Core.Timeparser.ParseTimeInterval(m_options["full-if-older-than"], prev[prev.Count - 1].Time);

                    List<string> controlfiles = new List<string>();
                    if (m_options.ContainsKey("signature-control-files"))
                        controlfiles.AddRange(m_options["signature-control-files"].Split(System.IO.Path.PathSeparator));

                    if (m_options.ContainsKey("signature-cache-path") && !System.IO.Directory.Exists(m_options["signature-cache-path"]))
                        System.IO.Directory.CreateDirectory(m_options["signature-cache-path"]);

                    using (Core.TempFolder tempfolder = new Duplicati.Library.Core.TempFolder())
                    {
                        List<Core.IFileArchive> patches = new List<Core.IFileArchive>();
                        if (!full)
                        {
                            using (new Logging.Timer("Reading incremental data"))
                            {
                                if (OperationProgress != null)
                                    OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading incremental data ...");

                                List<BackupEntry> entries = new List<BackupEntry>();
                                entries.Add(prev[prev.Count - 1]);
                                entries.AddRange(prev[prev.Count - 1].Incrementals);

                                int incCount = 0;
                                foreach (BackupEntry be in entries)
                                {
                                    incCount++;
                                    foreach (BackupEntry bes in be.SignatureFile)
                                        incCount++;
                                }

                                //The incremental part accounts for 15%, so each file has a fixed cost
                                double unitCost = m_incrementalFraction / incCount;

                                foreach (BackupEntry be in entries)
                                {
                                    m_progress += unitCost;

                                    List<string> signatureHashes = null;
                                    if (!m_options.ContainsKey("skip-file-hash-checks"))
                                    {
                                        if (OperationProgress != null)
                                            OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading manifest file: " + be.Time.ToShortDateString() + " " + be.Time.ToShortTimeString());

                                        signatureHashes = new List<string>();

                                        using (new Logging.Timer("Get " + be.Filename))
                                        using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
                                        {
                                            backend.Get(be.Filename, tf);
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
                                            continue; //TODO: Report this

                                        if (OperationProgress != null)
                                            OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Reading signatures: " + bes.Time.ToShortDateString() + " " + bes.Time.ToShortTimeString() + ", vol " + bes.VolumeNumber.ToString());

                                        string filename = System.IO.Path.Combine(tempfolder, "patch-" + patches.Count.ToString() + ".zip");

                                        if (System.IO.File.Exists(filename))
                                            System.IO.File.Delete(filename);

                                        string cachefilename = null;

                                        if (m_options.ContainsKey("signature-cache-path"))
                                            cachefilename = System.IO.Path.Combine(m_options["signature-cache-path"], cacheFilenameStrategy.GenerateFilename(bes.Type, bes.IsFull, bes.Time, bes.VolumeNumber));

                                        if (cachefilename != null && System.IO.File.Exists(cachefilename))
                                            if (signatureHashes != null && Core.Utility.CalculateHash(cachefilename) == signatureHashes[bes.VolumeNumber - 1])
                                                System.IO.File.Copy(cachefilename, filename); //TODO: Warn on hash mismatch?

                                        if (!System.IO.File.Exists(filename))
                                            using (new Logging.Timer("Get " + bes.Filename))
                                            {
                                                backend.Get(bes.Filename, filename);
                                                string hash = Core.Utility.CalculateHash(filename);
                                                if (signatureHashes != null && hash != signatureHashes[bes.VolumeNumber - 1])
                                                    throw new Exception("Hash mismatch on file " + bes.Filename + " recorded hash: " + signatureHashes[patches.Count] + ", actual hash: " + hash);

                                                //In case the cache was erased, or corrupt, keep the fresh copy
                                                if (cachefilename != null)
                                                    System.IO.File.Copy(filename, cachefilename);
                                            }

                                        patches.Add(new Compression.FileArchiveZip(filename));
                                    }
                                }
                            }
                        }
                        
                        if (full)
                            m_incrementalFraction = 0.0;

                        DateTime backuptime = DateTime.Now;

                        using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(source, bs, filter, patches))
                        {
                            if (OperationProgress != null)
                            {
                                OperationProgress(this, DuplicatiOperation.Backup, -1, -1, "Building filelist ...");
                                dir.ProgressEvent += new Duplicati.Library.Main.RSync.RSyncDir.ProgressEventDelegate(BackupRSyncDir_ProgressEvent);
                            }

                            dir.DisableFiletimeCheck = m_options.ContainsKey("disable-filetime-check");
                            using (new Logging.Timer("Initiating multipass"))
                                dir.InitiateMultiPassDiff(full);

                            int vol = 0;
                            long totalsize = 0;

                            List<string> contenthashes = new List<string>();
                            List<string> signaturehashes = new List<string>();

                            bool done = false;
                            while (!done && totalsize < totalmax)
                            {
                                using (new Logging.Timer("Multipass " + (vol + 1).ToString()))
                                {
                                    if (OperationProgress != null)
                                        OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Creating volume " + (vol + 1).ToString());

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

                                        done = dir.MakeMultiPassDiff(signaturearchive, contentarchive, Math.Min(volumesize, totalmax - totalsize));
                                        totalsize += new System.IO.FileInfo(contentfile).Length;

                                        totalsize += signaturearchive.Size;

                                        if (totalsize >= totalmax)
                                            dir.FinalizeMultiPass(signaturearchive, contentarchive);
                                    }

                                    if (OperationProgress != null)
                                        OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Uploading content, volume " + (vol + 1).ToString());

                                    contenthashes.Add(Core.Utility.CalculateHash(contentfile));
                                    using (new Logging.Timer("Writing delta file " + (vol + 1).ToString()))
                                        backend.Put(fns.GenerateFilename(BackupEntry.EntryType.Content, full, backuptime, vol + 1) + ".zip", contentfile);

                                    if (OperationProgress != null)
                                        OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Uploading signatures, volume " + (vol + 1).ToString());

                                    signaturehashes.Add(Core.Utility.CalculateHash(signaturefile));
                                    using (new Logging.Timer("Writing remote signatures"))
                                    {
                                        //TODO: put this in the backend wrapper, so it can be delayed properly
                                        //For now it works, because the manifest does not contain this entry anyway
                                        //Any problems would only occur when using disabled signature checks
                                        if (m_options.ContainsKey("signature-cache-path"))
                                            System.IO.File.Copy(signaturefile, System.IO.Path.Combine(m_options["signature-cache-path"], cacheFilenameStrategy.GenerateFilename(BackupEntry.EntryType.Signature, full, backuptime, vol + 1)));

                                        backend.Put(fns.GenerateFilename(BackupEntry.EntryType.Signature, full, backuptime, vol + 1) + ".zip", signaturefile);

                                    }
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

                                    if (OperationProgress != null)
                                        OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Uploading manifest, volume " + (vol + 1).ToString());

                                    backend.Put(fns.GenerateFilename(BackupEntry.EntryType.Manifest, full, backuptime) + ".manifest", mf);
                                }

                                vol++;

                            }
                        }
                    }


                }
                finally
                {
                    m_progress = 100.0;
                    if (backend != null)
                        backend.Dispose();

                    if (OperationCompleted != null)
                        OperationCompleted(this, DuplicatiOperation.Backup, 100, -1, "Completed");
                    if (OperationProgress != null)
                        OperationProgress(this, DuplicatiOperation.Backup, 100, -1, "Completed");
                }
            }

            bs.EndTime = DateTime.Now;
            return bs.ToString();
        }

        private void BackupTransfer_ProgressEvent(int progress, string filename)
        {
            if (OperationProgress != null)
                OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), progress, filename);
        }

        private void BackupRSyncDir_ProgressEvent(int progress, string filename)
        {
            m_progress = ((1.0 - m_incrementalFraction) * (progress / (double)100.0)) + m_incrementalFraction;
            if (OperationProgress != null)
                OperationProgress(this, DuplicatiOperation.Backup, (int)(m_progress * 100), -1, "Processing: " + filename);
        }

        public string Restore(string target)
        {
            SetupCommonOptions(m_options);
            RestoreStatistics rs = new RestoreStatistics();

            Backend.IBackend backend = null;

            using (new Logging.Timer("Restore from " + m_backend + " to " + target))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, -1, -1, "Started");

                    string specificfile = m_options.ContainsKey("file-to-restore") ? m_options["file-to-restore"] : "";
                    string specifictime = m_options.ContainsKey("restore-time") ? m_options["restore-time"] : "now";
                    Core.FilenameFilter filter = new Duplicati.Library.Core.FilenameFilter(m_options);

                    //Filter is prefered, if both file and filter is specified
                    if (!m_options.ContainsKey("filter") && !string.IsNullOrEmpty(specificfile))
                    {
                        List<Core.IFilenameFilter> list = new List<Duplicati.Library.Core.IFilenameFilter>();
                        list.Add(new Core.FilelistFilter(true, specificfile.Split(System.IO.Path.PathSeparator)));
                        list.Add(new Core.RegularExpressionFilter(false, ".*"));

                        filter = new Duplicati.Library.Core.FilenameFilter(list);
                    }

                    backend = Backend.BackendLoader.GetBackend(m_backend, m_options);
                    if (backend == null)
                        throw new Exception("Unable to find backend for target: " + m_backend);
                    backend = new BackendWrapper(rs, backend, m_options);

                    BackupEntry bestFit = FindBestMatch(ParseFileList(backend), specifictime);
                    if (bestFit.EncryptionMode != null)
                        backend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, bestFit.EncryptionMode, m_options);

                    List<BackupEntry> entries = new List<BackupEntry>();
                    entries.Add(bestFit);
                    entries.AddRange(bestFit.Incrementals);
                    int patchno = 0;

                    using (RSync.RSyncDir sync = new Duplicati.Library.Main.RSync.RSyncDir(target, rs, filter))
                    {
                        foreach (BackupEntry be in entries)
                        {
                            List<string> contentHashes = null;
                            if (!m_options.ContainsKey("skip-file-hash-checks"))
                            {
                                if (OperationProgress != null)
                                    OperationProgress(this, DuplicatiOperation.Backup, 0, -1, "Reading manifest file: " + be.Filename);

                                contentHashes = new List<string>();

                                using (new Logging.Timer("Get " + be.Filename))
                                using (Core.TempFile tf = new Duplicati.Library.Core.TempFile())
                                {
                                    backend.Get(be.Filename, tf);
                                    System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                                    doc.Load(tf);
                                    foreach (System.Xml.XmlNode n in doc.SelectNodes("Manifest/ContentFiles/Hash"))
                                        contentHashes.Add(n.InnerText);
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
                                    if (OperationProgress != null)
                                        OperationProgress(this, DuplicatiOperation.Backup, 0, -1, "Patching restore with #" + (patchno + 1).ToString());

                                    using (new Logging.Timer("Get " + vol.Filename))
                                        backend.Get(vol.Filename, patchzip);

                                    if (contentHashes != null)
                                    {
                                        string hash = Core.Utility.CalculateHash(patchzip);
                                        if (hash != contentHashes[vol.VolumeNumber - 1])
                                            throw new Exception("Hash mismatch on file " + vol.Filename + " recorded hash: " + contentHashes[vol.VolumeNumber] + ", actual hash: " + hash);
                                    }

                                    using (new Logging.Timer((patchno == 0 ? "Full restore to: " : "Incremental restore " + patchno.ToString() + " to: ") + target))
                                        sync.Patch(target, new Compression.FileArchiveZip(patchzip));
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
                        OperationCompleted(this, DuplicatiOperation.Restore, 100, -1, "Completed");
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
            SetupCommonOptions(m_options);
            RestoreStatistics rs = new RestoreStatistics();

            Backend.IBackend backend = null;

            using (new Logging.Timer("Restore control files from " + m_backend + " to " + target))
            {
                try
                {
                    if (OperationStarted != null)
                        OperationStarted(this, DuplicatiOperation.Restore, 0, -1, "Started");

                    backend = Backend.BackendLoader.GetBackend(m_backend, m_options);
                    if (backend == null)
                        throw new Exception("Unable to find backend for target: " + m_backend);
                    backend = new BackendWrapper(rs, backend, m_options);

                    List<BackupEntry> attempts = ParseFileList(backend);

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
                        Backend.IBackend realbackend = backend;
                        if (be.EncryptionMode != null)
                            realbackend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, be.EncryptionMode, m_options);

                        if (be.SignatureFile.Count > 0)
                            using(Core.TempFile z = new Duplicati.Library.Core.TempFile())
                            {
                                if (OperationProgress != null)
                                    OperationProgress(this, DuplicatiOperation.Backup, 0, -1, "Reading incremental data from " + be.SignatureFile[0].Filename);

                                using (new Logging.Timer("Get " + be.SignatureFile[0].Filename))
                                    realbackend.Get(be.SignatureFile[0].Filename, z);
                                
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
                        OperationCompleted(this, DuplicatiOperation.Restore, 100, -1, "Completed");
                }
            }

            rs.EndTime = DateTime.Now;

            //TODO: The RS should have the number of restored files, and the size of those
            //but that is a little difficult, because some may be restored, and then removed

            return rs.ToString();
        }

        public string RemoveAllButNFull()
        {
            if (!m_options.ContainsKey("remove-all-but-n-full"))
                throw new Exception("No count given for \"Remove All But N Full\"");

            int x = int.Parse(m_options["remove-all-but-n-full"]);
            if (x < 0)
                throw new Exception("Invalid count for remove-all-but-n-full");
            Backend.IBackend backend = Backend.BackendLoader.GetBackend(m_backend, m_options);
            if (backend == null)
                throw new Exception("Unable to find backend for target: " + m_backend);

            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, 0, -1, "Started");

                List<BackupEntry> entries = ParseFileList(backend);


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
                if (backend != null)
                    backend.Dispose();
                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.Remove, 100, -1, "Completed");
            }
        }

        public string RemoveOlderThan()
        {
            StringBuilder sb = new StringBuilder();

            if (!m_options.ContainsKey("remove-older-than"))
                throw new Exception("No count given for \"Remove Older Than\"");

            Backend.IBackend backend = Backend.BackendLoader.GetBackend(m_backend, m_options);
            if (backend == null)
                throw new Exception("Unable to find backend for target: " + m_backend);

            try
            {
                if (OperationStarted != null)
                    OperationStarted(this, DuplicatiOperation.Remove, 0, -1, "Started");

                string duration = m_options["remove-older-than"];
                List<BackupEntry> entries = ParseFileList(backend);

                DateTime expires = Core.Timeparser.ParseTimeInterval(duration, DateTime.Now, true);

                List<BackupEntry> toremove = new List<BackupEntry>();

                while (entries.Count > 0 && entries[0].Time > expires)
                {
                    BackupEntry be = entries[0];
                    entries.RemoveAt(0);

                    bool hasNewer = false;
                    foreach (BackupEntry bex in be.Incrementals)
                        if (bex.Time < expires)
                        {
                            hasNewer = true;
                            break;
                        }

                    if (hasNewer)
                    {
                        List<BackupEntry> t = new List<BackupEntry>(be.Incrementals);
                        t.Insert(0, be);

                        for (int i = 0; i < t.Count; i++)
                            if (t[i].Time > expires)
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
                if (backend != null)
                    backend.Dispose();
                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.Remove, 100, -1, "Completed");
            }

            return sb.ToString();
        }

        private string RemoveBackupSets(Backend.IBackend backend, List<BackupEntry> entries)
        {
            StringBuilder sb = new StringBuilder();

            foreach (BackupEntry be in entries)
            {
                sb.AppendLine("Deleting backup at " + be.Time.ToString(System.Globalization.CultureInfo.InvariantCulture));

                if (m_options.ContainsKey("force"))
                {
                    //Delete manifest
                    backend.Delete(be.Filename);

                    foreach (BackupEntry bex in be.ContentVolumes)
                        backend.Delete(bex.Filename);

                    foreach (BackupEntry bex in be.SignatureFile)
                        backend.Delete(bex.Filename);
                }
            }

            if (!m_options.ContainsKey("force") && entries.Count > 0)
                sb.AppendLine("Files are not deleted, use the --force command to actually remove files");

            return sb.ToString();
        }


        public string[] List()
        {
            SetupCommonOptions(m_options);

            List<string> res = new List<string>();
            Duplicati.Library.Backend.IBackend i = new Duplicati.Library.Backend.BackendLoader(m_backend, m_options);

            if (i == null)
                throw new Exception("Unable to find backend for target: " + m_backend);

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, "Started");

            using(i)
                foreach (Duplicati.Library.Backend.FileEntry fe in i.List())
                    res.Add(fe.Name);

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, 100, -1, "Completed");

            return res.ToArray();
        }

        public List<BackupEntry> ParseFileList()
        {
            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, "Started");

            Backend.IBackend obackend = Backend.BackendLoader.GetBackend(m_backend, m_options);
            if (obackend == null)
                throw new Exception("Unable to find backend for target: " + m_backend);
            using (obackend)
            {
                List<BackupEntry> data = ParseFileList(obackend);

                if (OperationCompleted != null)
                    OperationCompleted(this, DuplicatiOperation.List, 100, -1, "Completed");

                return data;
            }
        }

        private List<BackupEntry> ParseFileList(Duplicati.Library.Backend.IBackend backend)
        {
            SetupCommonOptions(m_options);

            using (new Logging.Timer("Getting and sorting filelist from " + backend.DisplayName))
            {
                FilenameStrategy fns = new FilenameStrategy(m_options);

                List<BackupEntry> incrementals = new List<BackupEntry>();
                List<BackupEntry> fulls = new List<BackupEntry>();
                Dictionary<string, List<BackupEntry>> signatures = new Dictionary<string, List<BackupEntry>>();
                Dictionary<string, List<BackupEntry>> contents = new Dictionary<string, List<BackupEntry>>();

                foreach (Duplicati.Library.Backend.FileEntry fe in backend.List())
                {
                    BackupEntry be = fns.DecodeFilename(fe);
                    if (be == null)
                        continue;

                    if (be.Type == BackupEntry.EntryType.Content)
                    {
                        string content = fns.GenerateFilename(BackupEntry.EntryType.Manifest, be.IsFull, be.IsShortName, be.Time) + ".manifest";
                        if (be.EncryptionMode != null)
                            content += "." + be.EncryptionMode;
                        
                        if (!contents.ContainsKey(content))
                            contents[content] = new List<BackupEntry>();
                        contents[content].Add(be);
                    }
                    else if (be.Type == BackupEntry.EntryType.Signature)
                    {
                        string content = fns.GenerateFilename(BackupEntry.EntryType.Manifest, be.IsFull, be.IsShortName, be.Time) + ".manifest";
                        if (be.EncryptionMode != null)
                            content += "." + be.EncryptionMode;

                        if (!signatures.ContainsKey(content))
                            signatures[content] = new List<BackupEntry>();
                        signatures[content].Add(be);
                    }
                    else if (be.Type != BackupEntry.EntryType.Manifest)
                        throw new Exception("Invalid entry type");
                    else if (be.IsFull)
                        fulls.Add(be);
                    else
                        incrementals.Add(be);
                }

                fulls.Sort(new Sorter());
                incrementals.Sort(new Sorter());

                foreach (BackupEntry be in fulls)
                {
                    if (contents.ContainsKey(be.Filename))
                        be.ContentVolumes.AddRange(contents[be.Filename]);
                    if (signatures.ContainsKey(be.Filename))
                        be.SignatureFile.AddRange(signatures[be.Filename]);
                }


                int index = 0;
                foreach (BackupEntry be in incrementals)
                {
                    if (contents.ContainsKey(be.Filename))
                        be.ContentVolumes.AddRange(contents[be.Filename]);
                    if (signatures.ContainsKey(be.Filename))
                        be.SignatureFile.AddRange(signatures[be.Filename]);

                    if (index >= fulls.Count || be.Time <= fulls[index].Time)
                    {
                        Logging.Log.WriteMessage("Failed to match incremental package to a full: " + be.Filename, Duplicati.Library.Logging.LogMessageType.Warning);
                        continue;
                    }
                    else
                    {
                        while (index < fulls.Count - 1 && be.Time > fulls[index].Time)
                            index++;
                        fulls[index].Incrementals.Add(be);
                    }
                }

                return fulls;
            }
        }

        public IList<string> ListContent()
        {
            SetupCommonOptions(m_options);
            RestoreStatistics rs = new RestoreStatistics();

            SortedList<string, string> res = new SortedList<string, string>();
            Duplicati.Library.Backend.IBackend backend = new Duplicati.Library.Backend.BackendLoader(m_backend, m_options);

            if (backend == null)
                throw new Exception("Unable to find backend for target: " + m_backend);
            backend = new BackendWrapper(rs, backend, m_options);

            if (OperationStarted != null)
                OperationStarted(this, DuplicatiOperation.List, 0, -1, "Started");

            string specifictime = m_options.ContainsKey("restore-time") ? m_options["restore-time"] : "now";
            BackupEntry bestFit = FindBestMatch(ParseFileList(backend), specifictime);
            if (bestFit.EncryptionMode != null)
                backend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, bestFit.EncryptionMode, m_options);

            using (backend)
            using (Core.TempFolder basefolder = new Duplicati.Library.Core.TempFolder())
            {
                List<BackupEntry> entries = new List<BackupEntry>();
                entries.Add(bestFit);
                entries.AddRange(bestFit.Incrementals);

                foreach (BackupEntry p in entries)
                {
                    if (p.SignatureFile.Count == 0)
                        throw new Exception("Unable to parse filenames for the volume: " + p.Filename);

                    using (Core.TempFolder t = new Duplicati.Library.Core.TempFolder())
                    {
                        foreach (BackupEntry be in p.SignatureFile)
                            using (Core.TempFile patchzip = new Duplicati.Library.Core.TempFile())
                            {
                                if (be.CompressionMode != "zip")
                                    throw new Exception("Unexpected compression mode");

                                if (OperationProgress != null)
                                    OperationProgress(this, DuplicatiOperation.Backup, 0, -1, "Reading incremental data: " + be.Filename);

                                using (new Logging.Timer("Get " + be.Filename))
                                    backend.Get(be.Filename, patchzip);

                                Core.IFileArchive archive = new Compression.FileArchiveZip(patchzip);

                                if (archive.FileExists(RSync.RSyncDir.DELETED_FOLDERS))
                                    foreach (string s in RSync.RSyncDir.FilenamesFromPlatformIndependant(archive.ReadAllLines(RSync.RSyncDir.DELETED_FOLDERS)))
                                        if (res.ContainsKey(s))
                                            res.Remove(s);

                                if (archive.FileExists(RSync.RSyncDir.DELETED_FILES))
                                    foreach (string s in RSync.RSyncDir.FilenamesFromPlatformIndependant(archive.ReadAllLines(RSync.RSyncDir.DELETED_FILES)))
                                        if (res.ContainsKey(s))
                                            res.Remove(s);

                                foreach (string s in archive.ListFiles(RSync.RSyncDir.SIGNATURE_ROOT))
                                    res[s.Substring(RSync.RSyncDir.SIGNATURE_ROOT.Length + 1)] = null;

                                if (archive.FileExists(RSync.RSyncDir.ADDED_FOLDERS))
                                    foreach (string s in RSync.RSyncDir.FilenamesFromPlatformIndependant(archive.ReadAllLines(RSync.RSyncDir.ADDED_FOLDERS)))
                                        res[s] = null;
                            }
                    }
                }
            }

            if (OperationCompleted != null)
                OperationCompleted(this, DuplicatiOperation.List, 100, -1, "Completed");

            return res.Keys;
        }

        private BackupEntry FindBestMatch(List<BackupEntry> backups, string specifictime)
        {
            if (string.IsNullOrEmpty(specifictime))
                specifictime = "now";

            if (backups.Count == 0)
                throw new Exception("No backups found at remote location");

            DateTime timelimit = Core.Timeparser.ParseTimeInterval(specifictime, DateTime.Now);

            BackupEntry bestFit = backups[0];
            List<BackupEntry> additions = new List<BackupEntry>();
            foreach (BackupEntry be in backups)
                if (be.Time < timelimit)
                {
                    bestFit = be;
                    foreach (BackupEntry bex in be.Incrementals)
                        if (bex.Time <= timelimit)
                            additions.Add(bex);

                }

            if (bestFit.SignatureFile.Count == 0 || bestFit.ContentVolumes.Count == 0)
                throw new Exception("Unable to parse filenames for the desired volumes");

            bestFit.Incrementals = additions;
            return bestFit;
        }

        private void SetupCommonOptions(Dictionary<string, string> options)
        {
            if (options.ContainsKey("tempdir"))
                Core.TempFolder.SystemTempPath = options["tempdir"];
            if (options.ContainsKey("thread-priority"))
                System.Threading.Thread.CurrentThread.Priority = Core.Utility.ParsePriority(options["thread-priority"]);
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
                return i.ParseFileList();
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

        public static string RestoreControlFiles(string source, string target, Dictionary<string, string> options)
        {
            using (Interface i = new Interface(source, options))
                return i.RestoreControlFiles(target);
        }


        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion
    }
}
