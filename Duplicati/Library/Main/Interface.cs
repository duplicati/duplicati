#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
        public static void Backup(string source, string target, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);

            using (new Logging.Timer("Backup from " + source + " to " + target))
            {
                FilenameStrategy fns = new FilenameStrategy(options);
                Core.FilenameFilter filter = new Duplicati.Library.Core.FilenameFilter(options);
                bool full = options.ContainsKey("full");

                Backend.IBackendInterface backend = new Encryption.EncryptedBackendWrapper(target, options);
                List<BackupEntry> prev = ParseFileList(target, options);

                if (prev.Count == 0)
                    full = true;

                if (!full && options.ContainsKey("full-if-older-than"))
                    full = DateTime.Now > Core.Timeparser.ParseTimeInterval(options["full-if-older-than"], prev[prev.Count - 1].Time);

                using (Core.TempFolder basefolder = new Duplicati.Library.Core.TempFolder())
                {
                    if (!full)
                    {
                        using (new Logging.Timer("Reading incremental data"))
                        {
                            foreach (BackupEntry be in prev[prev.Count - 1].SignatureFile) 
                                using (Core.TempFile t = new Duplicati.Library.Core.TempFile())
                                {
                                    using (new Logging.Timer("Get " + be.Filename))
                                        backend.Get(be.Filename, t);
                                    Compression.Compression.Decompress(t, basefolder);
                                }

                            foreach (BackupEntry be in prev[prev.Count - 1].Incrementals)
                                foreach (BackupEntry bes in be.SignatureFile) 
                                    using (Core.TempFile t = new Duplicati.Library.Core.TempFile())
                                    {
                                        using (new Logging.Timer("Get " + bes.Filename))
                                            backend.Get(bes.Filename, t);

                                        using (Core.TempFolder tf = new Duplicati.Library.Core.TempFolder())
                                        {
                                            Compression.Compression.Decompress(t, tf);
                                            using (new Logging.Timer("Full signature merge"))
                                                Main.RSync.RSyncDir.MergeSignatures(basefolder, tf);
                                        }
                                    }
                        }
                    }
                    DateTime backuptime = DateTime.Now;

                    RSync.RSyncDir dir = new Duplicati.Library.Main.RSync.RSyncDir(source, basefolder);

                    using (new Logging.Timer("Initiating multipass"))
                        dir.InitiateMultiPassDiff(full, filter);

                    int volumesize = (int)Core.Sizeparser.ParseSize(options.ContainsKey("volsize") ? options["volsize"] : "5", "mb");
                    volumesize = Math.Max(1024 * 1024, volumesize);

                    long totalmax = options.ContainsKey("totalsize") ? Core.Sizeparser.ParseSize(options["totalsize"], "mb") : long.MaxValue;
                    totalmax = Math.Max(volumesize, totalmax);

                    List<string> folders;
                    using (new Logging.Timer("Creating folders for signature file"))
                        folders = dir.CreateFolders();

                    int vol = 0;
                    long totalsize = 0;
                    
                    bool done = false;
                    while (!done && totalsize < totalmax)
                    {
                        using (new Logging.Timer("Multipass " + (vol + 1).ToString()))
                        {
                            using (Core.TempFile sigzip = new Duplicati.Library.Core.TempFile())
                            {
                                using (Compression.Compression signature = new Duplicati.Library.Compression.Compression(dir.m_targetfolder, sigzip))
                                {
                                    if (folders != null)
                                        foreach(string s in folders)
                                            signature.AddFolder(s);

                                    folders = null;

                                    if (System.IO.File.Exists(dir.DeletedFolders))
                                    {
                                        signature.AddFile(dir.DeletedFolders);
                                        System.IO.File.Delete(dir.DeletedFolders);
                                    }
                                    if (System.IO.File.Exists(dir.DeletedFiles))
                                    {
                                        signature.AddFile(dir.DeletedFiles);
                                        System.IO.File.Delete(dir.DeletedFiles);
                                    }

                                    using (Core.TempFile zf = new Duplicati.Library.Core.TempFile())
                                    {
                                        done = dir.MakeMultiPassDiff(signature, zf, volumesize);
                                        totalsize += new System.IO.FileInfo(zf).Length;
                                        using (new Logging.Timer("Writing delta file " + (vol + 1).ToString()))
                                            backend.Put(fns.GenerateFilename("duplicati", BackupEntry.EntryType.Content, full, backuptime, vol + 1) + ".zip", zf);
                                    }
                                }

                                totalsize += new System.IO.FileInfo(sigzip).Length;
                                using (new Logging.Timer("Writing remote signatures"))
                                    backend.Put(fns.GenerateFilename("duplicati", BackupEntry.EntryType.Signature, full, backuptime, vol + 1) + ".zip", sigzip);
                            }

                            using (Core.TempFile mf = new Duplicati.Library.Core.TempFile())
                            using (new Logging.Timer("Writing manifest"))
                            {
                                //TODO: Actually read the manifest on restore
                                System.IO.File.WriteAllLines(mf, new string[] {
                                    "Manifest: Dummy type",
                                    "Signature: Missing!",
                                    "Volumes: " + (vol + 1).ToString()
                                });
                                backend.Put(fns.GenerateFilename("duplicati", BackupEntry.EntryType.Manifest, full, backuptime) + ".manifest", mf);
                            }

                            vol++;
                        }
                    }


                }
            }
        }

        public static void Restore(string source, string target, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);

            using (new Logging.Timer("Restore from " + source + " to " + target))
            {
                string specificfile = options.ContainsKey("file-to-restore") ? options["file-to-restore"] : "";
                string specifictime = options.ContainsKey("restore-time") ? options["restore-time"] : "now";
                Backend.IBackendInterface backend = new Encryption.EncryptedBackendWrapper(source, options);

                if (string.IsNullOrEmpty(specifictime))
                    specifictime = "now";

                List<BackupEntry> backups = ParseFileList(source, options);
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

                using (Core.TempFolder basefolder = new Duplicati.Library.Core.TempFolder())
                {
                    foreach(BackupEntry be in bestFit.SignatureFile)
                        using (Core.TempFile basezip = new Duplicati.Library.Core.TempFile())
                        {
                            using (new Logging.Timer("Get " + be.Filename))
                                backend.Get(be.Filename, basezip);
                            Compression.Compression.Decompress(basezip, basefolder);
                        }

                    foreach (BackupEntry vol in bestFit.ContentVolumes)
                        using (Core.TempFile basezip = new Duplicati.Library.Core.TempFile())
                        {
                            using (new Logging.Timer("Get " + vol.Filename))
                                backend.Get(vol.Filename, basezip);
                            Compression.Compression.Decompress(basezip, basefolder);
                        }

                    RSync.RSyncDir sync;
                    using (new Logging.Timer("Parsing contents " + target))
                        sync = new Duplicati.Library.Main.RSync.RSyncDir(target, basefolder);

                    using (new Logging.Timer("Full restore to " + target))
                        sync.Restore(target, new List<string>());

                    foreach (BackupEntry p in additions)
                    {
                        using (Core.TempFolder t = new Duplicati.Library.Core.TempFolder())
                        {
                            foreach (BackupEntry be in p.SignatureFile) 
                                using (Core.TempFile patchzip = new Duplicati.Library.Core.TempFile())
                                {
                                    using (new Logging.Timer("Get " + be.Filename))
                                        backend.Get(be.Filename, patchzip);
                                    Compression.Compression.Decompress(patchzip, t);
                                }

                            foreach (BackupEntry vol in p.ContentVolumes)
                                using (Core.TempFile patchzip = new Duplicati.Library.Core.TempFile())
                                {
                                    using (new Logging.Timer("Get " + vol.Filename))
                                        backend.Get(vol.Filename, patchzip);
                                    Compression.Compression.Decompress(patchzip, t);
                                }

                            using (new Logging.Timer("Incremental patch " + p.Time.ToString()))
                                sync.Patch(target, t);

                        }
                    }


                }
            }
        }

        public static string[] List(string source, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);

            List<string> res = new List<string>();
            Duplicati.Library.Backend.IBackendInterface i = new Duplicati.Library.Backend.BackendLoader(source, options);
            foreach (Duplicati.Library.Backend.FileEntry fe in i.List())
                res.Add(fe.Name);

            return res.ToArray();
        }

        public static List<BackupEntry> ParseFileList(string source, Dictionary<string, string> options)
        {
            SetupCommonOptions(options);

            using (new Logging.Timer("Getting and sorting filelist from " + source))
            {
                FilenameStrategy fns = new FilenameStrategy(options);

                List<BackupEntry> incrementals = new List<BackupEntry>();
                List<BackupEntry> fulls = new List<BackupEntry>();
                Dictionary<string, List<BackupEntry>> signatures = new Dictionary<string, List<BackupEntry>>();
                Dictionary<string, List<BackupEntry>> contents = new Dictionary<string, List<BackupEntry>>();
                string filename = "duplicati";

                Duplicati.Library.Backend.IBackendInterface i = new Duplicati.Library.Backend.BackendLoader(source, options);

                foreach (Duplicati.Library.Backend.FileEntry fe in i.List())
                {
                    BackupEntry be = fns.DecodeFilename(filename, fe);
                    if (be == null)
                        continue;

                    if (be.Type == BackupEntry.EntryType.Content)
                    {
                        string content = fns.GenerateFilename(filename, BackupEntry.EntryType.Manifest, be.IsFull, be.Time) + ".manifest";
                        if (!contents.ContainsKey(content))
                            contents[content] = new List<BackupEntry>();
                        contents[content].Add(be);
                    }
                    else if (be.Type == BackupEntry.EntryType.Signature)
                    {
                        string content = fns.GenerateFilename(filename, BackupEntry.EntryType.Manifest, be.IsFull, be.Time) + ".manifest";
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

                    if (be.Time <= fulls[index].Time)
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


        private static void SetupCommonOptions(Dictionary<string, string> options)
        {
            if (options.ContainsKey("tempdir"))
                Core.TempFolder.SystemTempPath = options["tempdir"];
        }
    }
}
