// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.CommandLine.RecoveryTool
{
    public static class Index
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

            SortedSet<string> ix_sorted = new SortedSet<string>(StringComparer.Ordinal);
            string ixfile;
            options.TryGetValue("indexfile", out ixfile);
            if (string.IsNullOrWhiteSpace(ixfile))
                ixfile = "index.txt";

            ixfile = Path.GetFullPath(ixfile);

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
                    var p = Library.Main.Volumes.VolumeBase.ParseFilename(Path.GetFileName(file));
                    if (p == null)
                    {
                        Console.WriteLine(" - Not a Duplicati file, ignoring");
                        continue;
                    }

                    if (p.FileType != Library.Main.RemoteVolumeType.Blocks)
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
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var cp = Library.DynamicLoader.CompressionLoader.GetModule(p.CompressionModule, stream, Library.Interface.ArchiveMode.Read, options))
                    {
                        foreach (var f in cp.ListFiles(null))
                        {
                            ix_sorted.Add($"{Library.Utility.Utility.Base64UrlToBase64Plain(f)}, {filekey}");
                            blocks++;
                        }

                        files++;
                        totalblocks += blocks;

                        Console.Write(" {0} hashes found, sorting ...", blocks);

                        Console.WriteLine(" done!");

                        Console.Write("Merging {0} hashes ...", totalblocks);

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

            WriteIndex(ref ix_sorted, ixfile);
            Console.WriteLine("Processed {0} files and found {1} hashes", files, totalblocks);
            if (errors > 0)
                Console.WriteLine("Experienced {0} errors", errors);

            return 0;
        }

        private static void WriteIndex(ref SortedSet<string> sorted_set, string file1)
        {
            using (var sw = new StreamWriter(file1))
            {
                foreach (string line in sorted_set)
                {
                    sw.WriteLine(line);
                }
            }
        }
    }
}