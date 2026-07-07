// Copyright (C) 2026, The Duplicati Team
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
using System.IO.Compression;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class RecoveryToolPathTraversalTests : BasicSetupHelper
    {
        private string originalCurrentDirectory;

        [SetUp]
        public void SetUp()
        {
            this.originalCurrentDirectory = Directory.GetCurrentDirectory();
            if (File.Exists("/tmp/evil.txt"))
                File.Delete("/tmp/evil.txt");
        }

        [TearDown]
        public void TearDown()
        {
            if (this.originalCurrentDirectory != null)
            {
                Directory.SetCurrentDirectory(this.originalCurrentDirectory);
            }
            if (File.Exists("/tmp/evil.txt"))
                File.Delete("/tmp/evil.txt");
        }

        [Test]
        [Category("RecoveryTool")]
        public async Task RecoveryToolRestoreRelativePathInDlistAsync()
        {
            // 1. Setup source data
            var sourceFolder = Path.Combine(this.DATAFOLDER, "source");
            Directory.CreateDirectory(sourceFolder);
            var subDir = Path.Combine(sourceFolder, "subdir");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "file1.txt"), "data1");
            File.WriteAllText(Path.Combine(subDir, "file2.txt"), "data2");

            var options = new Dictionary<string, string>(this.TestOptions);
            options["no-encryption"] = "true";
            options.Remove("passphrase");

            // 2. Backup
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                IBackupResults backupResults = c.Backup(new[] { sourceFolder });
                Assert.AreEqual(0, backupResults.Errors.Count());
            }

            // 3. Manipulate dlist
            var dlistFiles = Directory.GetFiles(this.TARGETFOLDER, "*dlist*");
            var dlistPath = dlistFiles[0];

            var tempDir = Path.Combine(this.DATAFOLDER, "temp_dlist");
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(dlistPath, tempDir);
            File.Delete(dlistPath);

            var filelistPath = Directory.GetFiles(tempDir, "*filelist*").FirstOrDefault();
            var json = File.ReadAllText(filelistPath);
            // Replace ONLY ONE file with traversal, keep the other
            json = json.Replace("subdir/file2.txt", "subdir/../../../../../../../../../../../tmp/evil.txt");
            File.WriteAllText(filelistPath, json);

            ZipFile.CreateFromDirectory(tempDir, dlistPath);

            // 4. Download and Index
            var downloadFolder = Path.Combine(this.RESTOREFOLDER, "downloadedFiles");
            Directory.CreateDirectory(downloadFolder);
            CommandLine.RecoveryTool.Program.Main(new[] { "download", "file://" + this.TARGETFOLDER, downloadFolder });
            CommandLine.RecoveryTool.Program.Main(new[] { "index", downloadFolder });

            // 5. Restore with RecoveryTool
            var restoreFolder = Path.Combine(this.RESTOREFOLDER, "restoredFiles");
            Directory.CreateDirectory(restoreFolder);

            Console.WriteLine("====== BEGIN RESTORE ======");
            var status = CommandLine.RecoveryTool.Program.Main(new[] { "restore", downloadFolder, $"--targetpath={restoreFolder}" });
            Console.WriteLine("====== END RESTORE ======");

            var evilPath = "/tmp/evil.txt";
            var exists = File.Exists(evilPath);
            Console.WriteLine("Evil path exists: " + exists);

            Assert.AreNotEqual(0, status, "Expected restore to fail due to path traversal");
        }
    }
}
