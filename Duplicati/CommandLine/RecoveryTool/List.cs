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
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace Duplicati.CommandLine.RecoveryTool
{
    public static class List
    {
        public static int Run(List<string> args, Dictionary<string, string> options, Library.Utility.IFilter filter)
        {
            if (args.Count != 2 && args.Count != 3)
            {
                Console.WriteLine("Invalid argument count ({0} expected 2 or 3): {1}{2}", args.Count, Environment.NewLine, string.Join(Environment.NewLine, args));
                return 100;
            }

            var folder = Path.GetFullPath(args[1]);

            if (!Directory.Exists(folder))
            {
                Console.WriteLine("Folder not found: {0}", folder);
                return 100;
            }

            Directory.SetCurrentDirectory(folder);

            if (args.Count == 2)
            {
                var times = ParseListFiles(folder);
                foreach (var v in times.Zip(Enumerable.Range(0, times.Length), (a, b) => new KeyValuePair<int, DateTime>(b, a.Key)))
                    Console.WriteLine("{0}: {1}", v.Key, v.Value.ToLocalTime());
            }
            else
            {
                var file = SelectListFile(args[2], folder);

                var p = Library.Main.Volumes.VolumeBase.ParseFilename(Path.GetFileName(file));

                Library.Main.Volumes.VolumeReaderBase.UpdateOptionsFromManifest(p.CompressionModule, file, new Duplicati.Library.Main.Options(options));

                foreach (var f in EnumerateFilesInDList(file, filter, options))
                    Console.WriteLine("{0} ({1})", f.Path, Library.Utility.Utility.FormatSizeString(f.Size));
            }

            return 0;
        }

        public static IEnumerable<Duplicati.Library.Main.Volumes.IFileEntry> EnumerateFilesInDList(string file, Duplicati.Library.Utility.IFilter filter, Dictionary<string, string> options)
        {
            var p = Library.Main.Volumes.VolumeBase.ParseFilename(Path.GetFileName(file));
            using (var fs = new System.IO.FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var cm = Library.DynamicLoader.CompressionLoader.GetModule(p.CompressionModule, fs, Library.Interface.ArchiveMode.Read, options))
            using (var filesetreader = new Library.Main.Volumes.FilesetVolumeReader(cm, new Duplicati.Library.Main.Options(options)))
                foreach (var f in filesetreader.Files)
                {
                    if (f.Type != Duplicati.Library.Main.FilelistEntryType.File)
                        continue;

                    bool result;
                    bool match = filter.Matches(f.Path, out result, out _);
                    if (!match || result)
                        yield return f;
                }
        }

        public static string SelectListFile(string time, string folder)
        {
            if (File.Exists(Path.Combine(folder, time)))
                return Path.Combine(folder, time);

            int index;
            if (int.TryParse(time, out index))
            {
                var items = ParseListFiles(folder);
                if (index < 0 || index >= items.Length)
                    throw new Duplicati.Library.Interface.UserInformationException(string.Format("Valid range for version is 0 to {0}", items.Length - 1), "RecoveryToolInvalidVersion");

                return items[index].Value;
            }

            var parsedtime = Library.Utility.Timeparser.ParseTimeInterval(time, DateTime.Now, true);
            var el = (
                from n in ParseListFiles(folder)
                let diff = Math.Abs((n.Key - parsedtime).TotalSeconds)
                orderby diff
                select n).First();

            Console.WriteLine("Selected time {0}", el.Key);

            return el.Value;

        }

        public static KeyValuePair<DateTime, string>[] ParseListFiles(string folder)
        {
            return (
                from v in Directory.EnumerateFiles(folder)
                let p = Library.Main.Volumes.VolumeBase.ParseFilename(Path.GetFileName(v))
                where p != null && p.FileType == Duplicati.Library.Main.RemoteVolumeType.Files
                orderby p.Time descending
                select new KeyValuePair<DateTime, string>(p.Time, v)).ToArray();

        }
    }
}

