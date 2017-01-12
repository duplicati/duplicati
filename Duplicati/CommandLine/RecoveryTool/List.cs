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
                foreach(var v in times.Zip(Enumerable.Range(0, times.Length), (a,b) => new KeyValuePair<int, DateTime>(b, a.Key)))
                    Console.WriteLine("{0}: {1}", v.Key, v.Value.ToLocalTime());
            }
            else
            {
                var file = SelectListFile(args[2], folder);

                var p = Library.Main.Volumes.VolumeBase.ParseFilename(file);

                Library.Main.Volumes.VolumeReaderBase.UpdateOptionsFromManifest(p.CompressionModule, file, new Duplicati.Library.Main.Options(options));

                foreach(var f in EnumerateFilesInDList(file, filter, options))
                    Console.WriteLine("{0} ({1})", f.Path, Library.Utility.Utility.FormatSizeString(f.Size));
            }

            return 0;
        }

        public static IEnumerable<Duplicati.Library.Main.Volumes.IFileEntry> EnumerateFilesInDList(string file, Duplicati.Library.Utility.IFilter filter, Dictionary<string, string> options)
        {
            var p = Library.Main.Volumes.VolumeBase.ParseFilename(file);
            using(var cm = Library.DynamicLoader.CompressionLoader.GetModule(p.CompressionModule, file, options))
            using(var filesetreader = new Library.Main.Volumes.FilesetVolumeReader(cm, new Duplicati.Library.Main.Options(options)))
                foreach(var f in filesetreader.Files)
                {
                    if (f.Type != Duplicati.Library.Main.FilelistEntryType.File)
                        continue;

                    bool result;
                    Library.Utility.IFilter evfilter;
                    bool match = filter.Matches(f.Path, out result, out evfilter);
                    if (!match || (match && result))
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
                    throw new Duplicati.Library.Interface.UserInformationException(string.Format("Valid range for version is 0 to {1}", items.Length - 1));

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
                let p = Library.Main.Volumes.VolumeBase.ParseFilename(v)
                where p != null && p.FileType == Duplicati.Library.Main.RemoteVolumeType.Files
                orderby p.Time descending
                select new KeyValuePair<DateTime, string>(p.Time, v)).ToArray();

        }
    }
}

