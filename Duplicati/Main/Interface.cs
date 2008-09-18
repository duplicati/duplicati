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

namespace Duplicati.Main
{
    public static class Interface
    {
        public static void Backup(string source, string target, Dictionary<string, string> options)
        {
            FilenameStrategy fns = new FilenameStrategy(options);
            bool full = options.ContainsKey("full");

            Backend.IBackendInterface backend = new Backend.BackendLoader(target, options);
            List<BackupEntry> prev = ParseFileList(target, options);

            if (!full && options.ContainsKey("full-if-older-than"))
            {
                if (prev.Count == 0)
                    full = true;
                else
                    full = DateTime.Now > Core.Timeparser.ParseTimeInterval(options["full-if-older-than"], prev[prev.Count - 1].Time);
            }

            using (Core.TempFolder basefolder = new Duplicati.Core.TempFolder())
            {
                if (!full)
                {
                    using (Core.TempFile t = new Duplicati.Core.TempFile())
                    {
                        backend.Get(prev[prev.Count - 1].Filename, t);
                        Compression.Compression.Decompress(t, basefolder);
                    }

                    foreach(BackupEntry be in prev[prev.Count - 1].Incrementals)
                        using (Core.TempFile t = new Duplicati.Core.TempFile())
                        {
                            backend.Get(be.Filename, t);

                            using (Core.TempFolder tf = new Duplicati.Core.TempFolder())
                            {
                                Compression.Compression.Decompress(t, tf);
                                Main.RSync.RSyncDir.MergeSignatures(basefolder, tf);
                            }
                        }
                }

                DateTime backuptime = DateTime.Now;

                RSync.RSyncDir dir = new Duplicati.Main.RSync.RSyncDir(source, basefolder);
                dir.CalculateDiffFilelist(full);

                int volumesize = options.ContainsKey("volsize") ? int.Parse(options["volsize"]) : 5;
                dir.CreateDeltas();

                using(Core.TempFile zf = new Duplicati.Core.TempFile())
                {
                    Compression.Compression.Compress(dir.NewSignatures, zf, dir.m_targetfolder);
                    backend.Put(fns.GenerateFilename("duplicati", true, full, backuptime) + ".zip", zf);
                }

                using (Core.TempFile zf = new Duplicati.Core.TempFile())
                {
                    Compression.Compression.Compress(dir.NewDeltas, zf, dir.m_targetfolder);
                    backend.Put(fns.GenerateFilename("duplicati", false, full, backuptime) + ".zip", zf);
                }
            }
        }

        public static void Restore(string source, string taget, Dictionary<string, string> options)
        {
            string specificfile = options.ContainsKey("file-to-restore") ? options["file-to-restore"] : "";
            string specifictime = options.ContainsKey("restore-time") ? options["restore-time"] : "now";
            Backend.IBackendInterface backend = new Backend.BackendLoader(source, options);

            if (string.IsNullOrEmpty(specifictime))
                specifictime = "now";

            List<BackupEntry> backups = ParseFileList(source, options);
            if (backups.Count == 0)
                throw new Exception("No backups found at remote location");

            DateTime timelimit = Core.Timeparser.ParseTimeInterval(specifictime, DateTime.Now);

            BackupEntry bestFit = backups[0];
            List<BackupEntry> additions = new List<BackupEntry>();
            foreach(BackupEntry be in backups)
                if (be.Time < timelimit)
                {
                    bestFit = be;
                    foreach (BackupEntry bex in be.Incrementals)
                        if (bex.Time < timelimit)
                            additions.Add(bex);

                }

            using (Core.TempFolder basefolder = new Duplicati.Core.TempFolder())
            {
                using (Core.TempFile basezip = new Duplicati.Core.TempFile())
                {
                    backend.Get(bestFit.Filename, basezip);
                    Compression.Compression.Decompress(basezip, basefolder);
                }

                RSync.RSyncDir sync = new Duplicati.Main.RSync.RSyncDir(taget, basefolder);
                sync.Restore(taget, new List<string>());

                foreach (BackupEntry p in additions)
                {
                    using (Core.TempFolder t = new Duplicati.Core.TempFolder())
                    {
                        using (Core.TempFile patchzip = new Duplicati.Core.TempFile())
                        {
                            backend.Get(p.Filename, patchzip);
                            Compression.Compression.Decompress(patchzip, t);
                            sync.Patch(taget, t);
                        }
                    }
                }
            }
        }

        public static string[] List(string source, Dictionary<string, string> options)
        {
            List<string> res = new List<string>();
            Duplicati.Backend.IBackendInterface i = new Duplicati.Backend.BackendLoader(source, options);
            foreach (Duplicati.Backend.FileEntry fe in i.List())
                res.Add(fe.Name);

            return res.ToArray();
        }

        private static List<BackupEntry> ParseFileList(string source, Dictionary<string, string> options)
        {
            FilenameStrategy fns = new FilenameStrategy(options);
            List<BackupEntry> incrementals = new List<BackupEntry>();
            List<BackupEntry> fulls = new List<BackupEntry>();
            string filename = "duplicati";

            Duplicati.Backend.IBackendInterface i = new Duplicati.Backend.BackendLoader(source, options);

            foreach (Duplicati.Backend.FileEntry fe in i.List())
            {
                BackupEntry be = fns.DecodeFilename(filename, fe);
                if (be == null)
                    continue;

                if (be.IsFull && !be.IsContent)
                    fulls.Add(be);
                else if (!be.IsFull && !be.IsContent)
                    incrementals.Add(be);
            }

            fulls.Sort(new Sorter());
            incrementals.Sort(new Sorter());

            int index = 0;
            foreach (BackupEntry be in incrementals)
                if (be.Time <= fulls[index].Time)
                    continue; //TODO: Log this!
                else
                {
                    while (index < fulls.Count - 1 && be.Time > fulls[index].Time)
                        index++;
                    fulls[index].Incrementals.Add(be);
                }

            return fulls;
        }

    }
}
