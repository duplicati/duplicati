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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class FilterTest : BasicSetupHelper
    {
        [Test]
        [Category("Filter")]
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
            // Folder with excludefile
            Directory.CreateDirectory(Path.Combine(source, "toplevel", "excludefile"));

            // Write a file that we will use for exclude target
            File.WriteAllLines(Path.Combine(source, "toplevel", "excludefile", "exclude.me"), new string[] { });
            File.WriteAllLines(Path.Combine(source, "toplevel", "excludefile", "anyfile.txt"), new string[] { "data" });

            // Write a file that we will filter
            File.WriteAllLines(Path.Combine(source, "toplevel", "filteredempty", "myfile.txt"), new string[] { "data" });

            // Write a file that we will not filter
            File.WriteAllLines(Path.Combine(source, "toplevel", "normal", "standard.txt"), new string[] { "data" });

            // Get the default options
            var testopts = TestOptions;

            // Create a fileset with all data present
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] {DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Check that we have 4 files and 7 folders
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                var folders = r.Files.Count(x => x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));

                if (folders != 7)
                    throw new Exception($"Initial condition not satisfied, found {folders} folders, but expected 7");
                if (files != 4)
                    throw new Exception($"Initial condition not satisfied, found {files} files, but expected 4");
            }

            // Toggle the exclude file, and build a new fileset
            System.Threading.Thread.Sleep(5000);
            testopts["ignore-filenames"] = "exclude.me";
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] {DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Check that we have 2 files and 6 folders after excluding the "excludefile" folder
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                var folders = r.Files.Count(x => x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));

                if (folders != 6)
                    throw new Exception($"Initial condition not satisfied, found {folders} folders, but expected 6");
                if (files != 2)
                    throw new Exception($"Initial condition not satisfied, found {files} files, but expected 2");
            }

            // Toggle empty folder excludes, and run a new backup to remove them
            System.Threading.Thread.Sleep(5000);
            testopts["exclude-empty-folders"] = "true";
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] {DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Check that the two empty folders are now removed
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                var folders = r.Files.Count(x => x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));

                if (folders != 4)
                    throw new Exception($"Empty not satisfied, found {folders} folders, but expected 4");
                if (files != 2)
                    throw new Exception($"Empty not satisfied, found {files} files, but expected 2");
            }

            // Filter out one file and rerun the backup to exclude the folder
            System.Threading.Thread.Sleep(5000);
            var excludefilter = new Library.Utility.FilterExpression($"*{System.IO.Path.DirectorySeparatorChar}myfile.txt", false);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] {DATAFOLDER}, excludefilter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Check that the empty folder is now removed
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                var folders = r.Files.Count(x => x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));

                if (folders != 3)
                    throw new Exception($"Empty not satisfied, found {folders} folders, but expected 3");
                if (files != 1)
                    throw new Exception($"Empty not satisfied, found {files} files, but expected 1");
            }

            // Delete the one remaining file and check that we only have the top-level folder in the set
            System.Threading.Thread.Sleep(5000);
            File.Delete(Path.Combine(source, "toplevel", "normal", "standard.txt"));
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new string[] {DATAFOLDER}, excludefilter);
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Check we now have only one folder and no files
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = 0 }), null))
            {
                var r = c.List("*");
                Assert.AreEqual(0, r.Errors.Count());
                Assert.AreEqual(0, r.Warnings.Count());
                var folders = r.Files.Count(x => x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));
                var files = r.Files.Count(x => !x.Path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));

                if (folders != 1)
                    throw new Exception($"Empty not satisfied, found {folders} folders, but expected 1");
                if (files != 0)
                    throw new Exception($"Empty not satisfied, found {files} files, but expected 0");
            }
        }

        [Test]
        [Category("Filter")]
        public static void WildcardPatterns()
        {
            // These examples were taken from https://www.c-sharpcorner.com/uploadfile/b81385/efficient-string-matching-algorithm-with-use-of-wildcard-characters/.
            Dictionary<string, string> shouldMatch = new Dictionary<string, string>
            {
                { @"*", "Something" },
                { @"S*eth??g", "Something" },
                { @"A *?string*", "A very long long long stringggggggg" },
                { @"Performance issue when using *,Window server ???? R? and java *.*.*_*", "Performance issue when using WebSphere MQ 7.1 ,Window server 2008 R2 and java 1.6.0_21" },
                { @"Performance* and java 1.6.0_21", "Performance issue when using WebSphere MQ 7.1 ,Window server 2008 R2 and java 1.6.0_21" }
            };

            Dictionary<string, string> shouldNotMatch = new Dictionary<string, string>
            {
                { @"Performance issue when using *,Window server ???? R? and java *.*.*_", "Performance issue when using WebSphere MQ 7.1 ,Window server 2008 R2 and java 1.6.0_21" }
            };

            foreach (KeyValuePair<string, string> entry in shouldMatch)
            {
                IFilter filter = new FilterExpression(entry.Key);
                Assert.IsTrue(filter.Matches(entry.Value, out _, out _));
            }

            foreach (KeyValuePair<string, string> entry in shouldNotMatch)
            {
                IFilter filter = new FilterExpression(entry.Key);
                Assert.IsFalse(filter.Matches(entry.Value, out _, out _));
            }
        }
    }
}
