//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
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
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.CommandLine.RecoveryTool
{
    public static class FileIndex
    {
        public static int Run(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 2)
            {
                Console.WriteLine("Invalid argument count ({0} expected 2): {1}{2}", args.Count, Environment.NewLine, string.Join(Environment.NewLine, args));
                return 100;
            }

            var folder = Path.GetFullPath(args[1]);

            if (!Directory.Exists(folder))
            {
                Console.WriteLine("Folder not found: {0}", folder);
                return 100;
            }

            Directory.SetCurrentDirectory(folder);

            string ixfile;
            options.TryGetValue("indexfile", out ixfile);
            if (string.IsNullOrWhiteSpace(ixfile))
                ixfile = "index.txt";

            ixfile = Path.GetFullPath(ixfile);
            if (!File.Exists(ixfile))
            {
                using (File.Create(ixfile))
                {
                }
            }
            else
            {
                Console.WriteLine("Sorting existing index file");
                SortFile(ixfile, ixfile);
            }

            var filecount = Directory.EnumerateFiles(folder).Count();

            Console.WriteLine("Processing {0} files", filecount);

            var i = 0;
            var errors = 0;
            var totalblocks = 0L;
            var files = 0;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                Console.Write("{0}: {1}", i, file);

                try
                {
                    var p = Duplicati.Library.Main.Volumes.VolumeBase.ParseFilename(file);
                    if (p == null)
                    {
                        Console.WriteLine(" - Not a Duplicati file, ignoring");
                        continue;
                    }

                    if (p.FileType != Duplicati.Library.Main.RemoteVolumeType.Blocks)
                    {
                        Console.WriteLine(" - Filetype {0}, skipping", p.FileType);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(p.EncryptionModule))
                    {
                        Console.WriteLine(" - Encrypted, skipping");
                        continue;
                    }

                    var filekey = Path.GetFileName(file);

                    var blocks = 0;
                    using (var stream = new System.IO.FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var cp = Library.DynamicLoader.CompressionLoader.GetModule(p.CompressionModule, stream, Library.Interface.ArchiveMode.Read, options))
                    using (var tf = new Library.Utility.TempFile())
                    {
                        using (var sw = new StreamWriter(tf))
                            foreach (var f in cp.ListFiles(null))
                            {
                                sw.WriteLine("{0}, {1}", Library.Utility.Utility.Base64UrlToBase64Plain(f), filekey);
                                blocks++;
                            }

                        files++;
                        totalblocks += blocks;

                        Console.Write(" {0} hashes found, sorting ...", blocks);

                        SortFile(tf, tf);

                        Console.WriteLine(" done!");

                        Console.Write("Merging {0} hashes ...", totalblocks);

                        MergeFiles(ixfile, tf, ixfile);

                        Console.WriteLine(" done!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(" error: {0}", ex);
                    errors++;
                }

                i++;
            }

            Console.Write("Sorting index file ...");
            SortFile(ixfile, ixfile);
            Console.WriteLine(" done!");

            Console.WriteLine("Processed {0} files and found {1} hashes", files, totalblocks);
            if (errors > 0)
                Console.WriteLine("Experienced {0} errors", errors);

            return 0;
        }

        public static void SortFile(string filein, string fileout)
        {
            try
            {
                // If the file can fit into memory, this is MUCH faster
                using (var tfout = new Library.Utility.TempFile())
                {
                    var data = File.ReadAllLines(filein);
                    Array.Sort(data, StringComparer.Ordinal);
                    File.WriteAllLines(tfout, data);

                    File.Copy(tfout, fileout, true);
                    return;
                }
            }
            catch
            {
            }

            using (var tfin = new Library.Utility.TempFile())
            using (var tfout = new Library.Utility.TempFile())
            {
                long swaps;

                File.Copy(filein, tfin, true);

                do
                {
                    swaps = 0L;

                    using (var sw = new System.IO.StreamWriter(tfout))
                    using (var sr = new System.IO.StreamReader(tfin))
                    {
                        var c1 = sr.ReadLine();
                        var c2 = sr.ReadLine();

                        while (c1 != null || c2 != null)
                        {
                            if (c1 != null && c1.StartsWith("a", StringComparison.Ordinal))
                                Console.Write("");
                            var cmp = StringComparer.Ordinal.Compare(c1, c2);

                            if (c2 == null || (c1 != null && cmp < 0))
                            {
                                sw.WriteLine(c1);

                                c1 = c2;
                                c2 = sr.ReadLine();
                            }
                            else
                            {
                                if (cmp != 0)
                                    sw.WriteLine(c2);
                                c2 = sr.ReadLine();
                                swaps++;
                            }
                        }
                    }

                    File.Copy(tfout, tfin, true);
                } while (swaps > 0);

                File.Copy(tfout, fileout, true);
            }
        }

        private static void MergeFiles(string file1, string file2, string fileout)
        {
            using (var tf = new Library.Utility.TempFile())
            {
                using (var sw = new System.IO.StreamWriter(tf))
                using (var sr1 = new System.IO.StreamReader(file1))
                using (var sr2 = new System.IO.StreamReader(file2))
                {
                    var c1 = sr1.ReadLine();
                    var c2 = sr2.ReadLine();

                    while (c1 != null || c2 != null)
                    {
                        var cmp = StringComparer.Ordinal.Compare(c1, c2);

                        if (c2 == null || (c1 != null && cmp < 0))
                        {
                            sw.WriteLine(c1);
                            c1 = sr1.ReadLine();
                        }
                        else
                        {
                            if (cmp != 0)
                                sw.WriteLine(c2);

                            c2 = sr2.ReadLine();
                        }
                    }
                }

                File.Copy(tf, fileout, true);
            }
        }
    }
}