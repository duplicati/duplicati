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
    public static class Interface
    {
        public static string Backup(string source, string target, Dictionary<string, string> options)
        {
            BackupStatistics bs = new BackupStatistics();

            SetupCommonOptions(options);
            Backend.IBackend backend = null;

            using (new Logging.Timer("Backup from " + source + " to " + target))
            {
                try
                {
                    FilenameStrategy fns = new FilenameStrategy(options);
                    Core.FilenameFilter filter = new Duplicati.Library.Core.FilenameFilter(options);
                    bool full = options.ContainsKey("full");

                    int volumesize = (int)Core.Sizeparser.ParseSize(options.ContainsKey("volsize") ? options["volsize"] : "5", "mb");
                    volumesize = Math.Max(1024 * 1024, volumesize);

                    long totalmax = options.ContainsKey("totalsize") ? Core.Sizeparser.ParseSize(options["totalsize"], "mb") : long.MaxValue;
                    totalmax = Math.Max(volumesize, totalmax);

                    backend = Backend.BackendLoader.GetBackend(target, options);
                    if (backend == null)
                        throw new Exception("Unable to find backend for target: " + target);
                    backend = new BackendWrapper(bs, backend, options);
                    backend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, options);

                    List<BackupEntry> prev = ParseFileList(backend, options);

                    if (prev.Count == 0)
                        full = true;

                    if (!full && options.ContainsKey("full-if-older-than"))
                        full = DateTime.Now > Core.Timeparser.ParseTimeInterval(options["full-if-older-than"], prev[prev.Count - 1].Time);

                    List<string> controlfiles = new List<string>();
                    if (options.ContainsKey("signature-control-files"))
                        controlfiles.AddRange(options["signature-control-files"].Split(System.IO.Path.PathSeparator));

                    using (Core.TempFolder tempfolder = new Duplicati.Library.Core.TempFolder())
                    {
                        List<Core.IFileArchive> patches = new List<Core.IFileArchive>();
                        if (!full)
                        {
                            using (new Logging.Timer("Reading incremental data"))
                            {
                                List<BackupEntry> entries = new List<BackupEntry>();
                                entries.Add(prev[prev.Count - 1]);
                                entries.AddRange(prev[prev.Count - 1].Incrementals);

                                foreach (BackupEntry be in entries)
                                    foreach (BackupEntry bes in be.SignatureFile)
                                    {
                                        string filename = System.IO.Path.Combine(tempfolder, "patch-" + patches.Count.ToString() + ".zip");
                                        using (new Logging.Timer("Get " + bes.Filename))
                                            backend.Get(bes.Filename, filename);
                                        patches.Add(new Compression.FileArchiveZip(filename));
                                    }
                            }
                        }
                        DateTime backuptime = DateTime.Now;

                        using (RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(source, bs, filter, patches))
                        {
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
                                                using (System.IO.Stream cs = signaturearchive.CreateFile(System.IO.Path.GetFileName(s)))
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
                                    

                                    contenthashes.Add(Core.Utility.CalculateHash(contentfile));
                                    using (new Logging.Timer("Writing delta file " + (vol + 1).ToString()))
                                        backend.Put(fns.GenerateFilename(BackupEntry.EntryType.Content, full, backuptime, vol + 1) + ".zip", contentfile);

                                    signaturehashes.Add(Core.Utility.CalculateHash(signaturefile));
                                    using (new Logging.Timer("Writing remote signatures"))
                                        backend.Put(fns.GenerateFilename(BackupEntry.EntryType.Signature, full, backuptime, vol + 1) + ".zip", signaturefile);
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

                                    //TODO: Actually read the manifest on restore
                                    doc.Save(mf);

                                    backend.Put(fns.GenerateFilename(BackupEntry.EntryType.Manifest, full, backuptime) + ".manifest", mf);
                                }

                                vol++;

                            }
                        }
                    }


                }
                finally
                {
                    if (backend != null)
                        backend.Dispose();
                }
            }

            bs.EndTime = DateTime.Now;
            return bs.ToString();
        }

        public static string Restore(string source, string target, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);
            RestoreStatistics rs = new RestoreStatistics();

            Backend.IBackend backend = null;

            using (new Logging.Timer("Restore from " + source + " to " + target))
            {
                try
                {
                    string specificfile = options.ContainsKey("file-to-restore") ? options["file-to-restore"] : "";
                    string specifictime = options.ContainsKey("restore-time") ? options["restore-time"] : "now";
                    Core.FilenameFilter filter = new Duplicati.Library.Core.FilenameFilter(options);

                    //Filter is prefered, if both file and filter is specified
                    if (!options.ContainsKey("filter") && !string.IsNullOrEmpty(specificfile))
                    {
                        List<Core.IFilenameFilter> list = new List<Duplicati.Library.Core.IFilenameFilter>();
                        list.Add(new Core.FilelistFilter(true, specificfile.Split(System.IO.Path.PathSeparator)));
                        list.Add(new Core.RegularExpressionFilter(false, ".*"));

                        filter = new Duplicati.Library.Core.FilenameFilter(list);
                    }

                    backend = Backend.BackendLoader.GetBackend(source, options);
                    if (backend == null)
                        throw new Exception("Unable to find backend for target: " + source);
                    backend = new BackendWrapper(rs, backend, options);

                    BackupEntry bestFit = FindBestMatch(ParseFileList(backend, options), specifictime);
                    if (bestFit.EncryptionMode != null)
                        backend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, bestFit.EncryptionMode, options);

                    List<BackupEntry> entries = new List<BackupEntry>();
                    entries.Add(bestFit);
                    entries.AddRange(bestFit.Incrementals);
                    int patchno = 0;

                    using (RSync.RSyncDir sync = new Duplicati.Library.Main.RSync.RSyncDir(target, rs, filter))
                    {
                        foreach (BackupEntry be in entries)
                            foreach (BackupEntry vol in be.ContentVolumes)
                            {
                                if (vol.CompressionMode != "zip")
                                    throw new Exception("Unexpected compression mode");

                                using (Core.TempFile patchzip = new Duplicati.Library.Core.TempFile())
                                {
                                    using (new Logging.Timer("Get " + vol.Filename))
                                        backend.Get(vol.Filename, patchzip);

                                    using (new Logging.Timer((patchno == 0 ? "Full restore to: " : "Incremental restore " + patchno.ToString() + " to: ") + target))
                                        sync.Patch(target, new Compression.FileArchiveZip(patchzip));
                                }
                                patchno++;
                            }
                    }
                }
                finally
                {
                    if (backend != null)
                        backend.Dispose();
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
        /// <param name="source">The backend to retrieve the control files from</param>
        /// <param name="target">The folder into which to restore the files</param>
        /// <param name="options">Options that affect how the call is performed</param>
        /// <returns>A restore report</returns>
        public static string RestoreControlFiles(string source, string target, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);
            RestoreStatistics rs = new RestoreStatistics();

            Backend.IBackend backend = null;

            using (new Logging.Timer("Restore control files from " + source + " to " + target))
            {
                try
                {
                    backend = Backend.BackendLoader.GetBackend(source, options);
                    if (backend == null)
                        throw new Exception("Unable to find backend for target: " + source);
                    backend = new BackendWrapper(rs, backend, options);

                    List<BackupEntry> attempts = ParseFileList(backend, options);

                    Core.FilenameFilter filter;

                    if (!options.ContainsKey("file-to-restore"))
                    {
                        filter = new Duplicati.Library.Core.FilenameFilter(
                            new List<Core.IFilenameFilter>(new Core.IFilenameFilter[] {
                                //Exclude everything with a path
                                new Duplicati.Library.Core.RegularExpressionFilter(false, "*." + System.IO.Path.DirectorySeparatorChar.ToString() + "*."),
                                //Exclude known files
                                new Duplicati.Library.Core.FilelistFilter(false, new string[] {
                                    RSync.RSyncDir.ADDED_FOLDERS,
                                    RSync.RSyncDir.DELETED_FILES,
                                    RSync.RSyncDir.DELETED_FOLDERS
                                })
                            })
                        );
                    }
                    else
                    {
                        filter = new Duplicati.Library.Core.FilenameFilter(
                            new List<Core.IFilenameFilter>(new Core.IFilenameFilter[] {
                                //Include requested items
                                new Duplicati.Library.Core.FilelistFilter(true, options["file-to-restore"].Split(System.IO.Path.PathSeparator)),
                                //Exclude everything else
                                new Duplicati.Library.Core.RegularExpressionFilter(false, "*.")
                            })
                        );
                    }



                    List<BackupEntry> flatlist = new List<BackupEntry>();
                    foreach (BackupEntry be in attempts)
                    {
                        flatlist.Add(be);
                        flatlist.AddRange(be.Incrementals);
                    }

                    flatlist.Reverse();
                    foreach (BackupEntry be in flatlist)
                    {
                        Backend.IBackend realbackend = backend;
                        if (be.EncryptionMode != null)
                            realbackend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, be.EncryptionMode, options);

                        if (be.SignatureFile.Count > 0)
                            using(Core.TempFile z = new Duplicati.Library.Core.TempFile())
                            {
                                using (new Logging.Timer("Get " + be.SignatureFile[0].Filename))
                                    realbackend.Get(be.SignatureFile[0].Filename, z);
                                
                                using(Compression.FileArchiveZip fz = new Duplicati.Library.Compression.FileArchiveZip(z))
                                {
                                    bool any = false;
                                    foreach (string f in filter.FilterList("", fz.ListFiles(null)))
                                    {
                                        any = true;
                                        using (System.IO.Stream s1 = fz.OpenRead(f))
                                        using (System.IO.Stream s2 = System.IO.File.Create(System.IO.Path.Combine(target, f)))
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
                }
            }

            rs.EndTime = DateTime.Now;

            //TODO: The RS should have the number of restored files, and the size of those
            //but that is a little difficult, because some may be restored, and then removed

            return rs.ToString();
        }

        public static string RemoveAllButNFull(string source, Dictionary<string, string> options)
        {
            if (!options.ContainsKey("remove-all-but-n-full"))
                throw new Exception("No count given for \"Remove All But N Full\"");

            int x = int.Parse(options["remove-all-but-n-full"]);
            if (x < 0)
                throw new Exception("Invalid count for remove-all-but-n-full");
            Backend.IBackend backend = Backend.BackendLoader.GetBackend(source, options);
            if (backend == null)
                throw new Exception("Unable to find backend for target: " + source);

            try
            {
                List<BackupEntry> entries = ParseFileList(backend, options);


                List<BackupEntry> toremove = new List<BackupEntry>();

                while (entries.Count > x)
                {
                    BackupEntry be = entries[0];
                    entries.RemoveAt(0);

                    be.Incrementals.Reverse();
                    toremove.AddRange(be.Incrementals);
                    toremove.Add(be);
                }

                return RemoveBackupSets(backend, options, toremove);
            }
            finally
            {
                if (backend != null)
                    backend.Dispose();
            }
        }

        public static string RemoveOlderThan(string source, Dictionary<string, string> options)
        {
            StringBuilder sb = new StringBuilder();

            if (!options.ContainsKey("remove-older-than"))
                throw new Exception("No count given for \"Remove Older Than\"");

            Backend.IBackend backend = Backend.BackendLoader.GetBackend(source, options);
            if (backend == null)
                throw new Exception("Unable to find backend for target: " + source);

            try
            {
                string duration = options["remove-older-than"];
                List<BackupEntry> entries = ParseFileList(backend, options);

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


                sb.Append(RemoveBackupSets(backend, options, toremove));
            }
            finally
            {
                if (backend != null)
                    backend.Dispose();
            }

            return sb.ToString();
        }

        private static string RemoveBackupSets(Backend.IBackend backend, Dictionary<string, string> options, List<BackupEntry> entries)
        {
            StringBuilder sb = new StringBuilder();

            foreach (BackupEntry be in entries)
            {
                sb.AppendLine("Deleting backup at " + be.Time.ToString(System.Globalization.CultureInfo.InvariantCulture));

                if (options.ContainsKey("force"))
                {
                    //Delete manifest
                    backend.Delete(be.Filename);

                    foreach (BackupEntry bex in be.ContentVolumes)
                        backend.Delete(bex.Filename);

                    foreach (BackupEntry bex in be.SignatureFile)
                        backend.Delete(bex.Filename);
                }
            }

            if (!options.ContainsKey("force") && entries.Count > 0)
                sb.AppendLine("Files are not deleted, use the --force command to actually remove files");

            return sb.ToString();
        }


        public static string[] List(string source, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);

            List<string> res = new List<string>();
            Duplicati.Library.Backend.IBackend i = new Duplicati.Library.Backend.BackendLoader(source, options);

            if (i == null)
                throw new Exception("Unable to find backend for target: " + source);

            using(i)
                foreach (Duplicati.Library.Backend.FileEntry fe in i.List())
                    res.Add(fe.Name);

            return res.ToArray();
        }

        public static List<BackupEntry> ParseFileList(string backend, Dictionary<string, string> options)
        {
            Backend.IBackend obackend = Backend.BackendLoader.GetBackend(backend, options);
            if (obackend == null)
                throw new Exception("Unable to find backend for target: " + backend);
            using(obackend)
                return ParseFileList(obackend, options);
        }

        public static List<BackupEntry> ParseFileList(Duplicati.Library.Backend.IBackend backend, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);

            using (new Logging.Timer("Getting and sorting filelist from " + backend.DisplayName))
            {
                FilenameStrategy fns = new FilenameStrategy(options);

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

        public static IList<string> ListContent(string source, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);
            RestoreStatistics rs = new RestoreStatistics();

            SortedList<string, string> res = new SortedList<string, string>();
            Duplicati.Library.Backend.IBackend backend = new Duplicati.Library.Backend.BackendLoader(source, options);

            if (backend == null)
                throw new Exception("Unable to find backend for target: " + source);
            backend = new BackendWrapper(rs, backend, options);

            string specifictime = options.ContainsKey("restore-time") ? options["restore-time"] : "now";
            BackupEntry bestFit = FindBestMatch(ParseFileList(backend, options), specifictime);
            if (bestFit.EncryptionMode != null)
                backend = Encryption.EncryptedBackendWrapper.WrapWithEncryption(backend, bestFit.EncryptionMode, options);

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

            return res.Keys;
        }

        private static string NormalizeZipFileName(string name)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                name = name.Replace(System.IO.Path.DirectorySeparatorChar, '/');
            return name;
        }

        private static string NormalizeZipDirName(string name)
        {
            name = NormalizeZipFileName(name);
            if (!name.EndsWith("/"))
                name += "/";
            return name;
        }

        private static BackupEntry FindBestMatch(List<BackupEntry> backups, string specifictime)
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

        private static void SetupCommonOptions(Dictionary<string, string> options)
        {
            if (options.ContainsKey("tempdir"))
                Core.TempFolder.SystemTempPath = options["tempdir"];
            if (options.ContainsKey("thread-priority"))
                System.Threading.Thread.CurrentThread.Priority = Core.Utility.ParsePriority(options["thread-priority"]);
        }
    }
}
