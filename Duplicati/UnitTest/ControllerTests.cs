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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class ControllerTests : BasicSetupHelper
    {
        [Test]
        [Category("Controller")]
        public void DeleteAllRemoteFiles()
        {
            string filePath = Path.Combine(this.DATAFOLDER, "file");
            File.WriteAllBytes(filePath, new byte[] {0, 1, 2});

            Dictionary<string, string> firstOptions = new Dictionary<string, string>(this.TestOptions);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, firstOptions, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Keep track of the backend files from the first backup configuration so that we can
            // check that they remain after we remove the backend files from the second backup
            // configuration.
            string[] firstBackupFiles = Directory.GetFiles(this.TARGETFOLDER);
            Assert.Greater(firstBackupFiles.Length, 0);

            Dictionary<string, string> secondOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["dbpath"] = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
            };
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, secondOptions, null))
            {
                // An exception should be thrown due to unrecognized files in the target folder.
                // ReSharper disable once AccessToDisposedClosure
                Assert.That(() => c.Backup(new[] {this.DATAFOLDER}), Throws.Exception);
            }

            // We should be able to safely remove backend files from the second backup by referring
            // to the local database.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, secondOptions, null))
            {
                IListRemoteResults listResults = c.DeleteAllRemoteFiles();
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
            }

            // After we delete backend files from the second backup configuration, those from the first
            // configuration should remain (see issues #3845, and #4244).
            foreach (string file in firstBackupFiles)
            {
                Assert.IsTrue(File.Exists(file));
            }

            // Configure and run a second backup with a different prefix. This should run without error.
            secondOptions["prefix"] = new Options(firstOptions).Prefix + "2";
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, secondOptions, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Even without a local database, we should be able to safely remove backend files from
            // the second backup due to the prefix.
            File.Delete(secondOptions["dbpath"]);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, secondOptions, null))
            {
                IListRemoteResults listResults = c.DeleteAllRemoteFiles();
                Assert.AreEqual(0, listResults.Errors.Count());
                Assert.AreEqual(0, listResults.Warnings.Count());
            }

            // After we delete backend files from the second backup configuration, those from the first
            // configuration should remain (see issue #2678).
            foreach (string file in firstBackupFiles)
            {
                Assert.IsTrue(File.Exists(file));
            }

            // The first backup configuration should still run normally.
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, firstOptions, null))
            {
                IBackupResults backupResults = c.Backup(new[] {this.DATAFOLDER});
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }
        }
    }
}