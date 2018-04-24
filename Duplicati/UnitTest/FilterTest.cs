//  Copyright (C) 2018, The Duplicati Team
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
using System.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class FilterTest : BasicSetupHelper
    {
        public override void PrepareSourceData()
        {
            base.PrepareSourceData();

            Directory.CreateDirectory(DATAFOLDER);
            Directory.CreateDirectory(TARGETFOLDER);
        }

        [Test]
        [Category("Border")]
        public void TestEmptyFolderExclude()
        {
            var source = DATAFOLDER;
            // Top level folder with no contents
            Directory.CreateDirectory(Path.Combine(source, "empty-toplevel"));

            // Top level folder with contents in one leaf
            Directory.CreateDirectory(Path.Combine(source, "toplevel"));
            // Empty folder
            Directory.CreateDirectory(Path.Combine(source, "toplevel", "empty"));
            // Folder with an excluded file
            Directory.CreateDirectory(Path.Combine(source, "toplevel", "filteredempty"));
            // Folder with contents
            Directory.CreateDirectory(Path.Combine(source, "toplevel", "normal"));

            // Write a file that we will filter
            File.WriteAllLines(Path.Combine(source, "toplevel", "filteredempty", "myfile.txt"), new string[] { "data" });

            // Write a file that we will not filter
            File.WriteAllLines(Path.Combine(source, "toplevel", "normal", "standard.txt"), new string[] { "data" });

            // Get the default options
            var testopts = TestOptions;

            // Create a fileset with all data present
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });

            // Check that we have 2 files and 6 folders
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                var folders = r.Files.Count(x => x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));

                if (folders != 6)
                    throw new Exception($"Initial condition not satisfied, found {folders} folders, but expected 6");
                if (files != 2)
                    throw new Exception($"Initial condition not satisfied, found {files} files, but expected 2");
            }

            // Toggle empty folder excludes, and run a new backup to remove them
            System.Threading.Thread.Sleep(5000);
            testopts["exclude-empty-folders"] = "true";
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER });

            // Check that the two empty folders are now removed
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                var folders = r.Files.Count(x => x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));

                if (folders != 4)
                    throw new Exception($"Empty not satisfied, found {folders} folders, but expected 4");
                if (files != 2)
                    throw new Exception($"Empty not satisfied, found {files} files, but expected 2");
            }

            // Filter out one file and rerun the backup to exclude the folder
            System.Threading.Thread.Sleep(5000);
            var excludefilter = new Library.Utility.FilterExpression($"*{System.IO.Path.DirectorySeparatorChar}myfile.txt", false);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null ))
                c.Backup(new string[] { DATAFOLDER }, excludefilter);

            // Check that the empty folder is now removed
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                var folders = r.Files.Count(x => x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));

                if (folders != 3)
                    throw new Exception($"Empty not satisfied, found {folders} folders, but expected 3");
                if (files != 1)
                    throw new Exception($"Empty not satisfied, found {files} files, but expected 1");
            }

            // Delete the one remaining file and check that we only have the top-level folder in the set
            System.Threading.Thread.Sleep(5000);
            File.Delete(Path.Combine(source, "toplevel", "normal", "standard.txt"));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                c.Backup(new string[] { DATAFOLDER }, excludefilter);

            // Check we now have only one folder and no files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                var folders = r.Files.Count(x => x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal));

                if (folders != 1)
                    throw new Exception($"Empty not satisfied, found {folders} folders, but expected 1");
                if (files != 0)
                    throw new Exception($"Empty not satisfied, found {files} files, but expected 0");
            }

        }
    }
}
