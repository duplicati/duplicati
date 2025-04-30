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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class BackendToolTests : BasicSetupHelper
    {
        [Test]
        [Category("BackendTool")]
        public void Get()
        {
            // Files to create in MB.
            int[] fileSizes = {10, 20, 30};
            foreach (int size in fileSizes)
            {
                var data = new byte[size * 1024 * 1024];
                var rng = new Random();
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, size + "MB"), data);
            }

            // Run a backup.
            var options = new Dictionary<string, string>(TestOptions);
            var backendURL = "file://" + this.TARGETFOLDER;
            using (Controller c = new Controller(backendURL, options, null))
            {
                var backupResults = c.Backup(new[] {DATAFOLDER});
                foreach (var backupResultsWarning in backupResults.Warnings)
                {
                    TestContext.WriteLine("Backend result warning:" + backupResultsWarning);
                }
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            // Get the backend files using absolute paths
            var absoluteDownloadFolder = Path.Combine(RESTOREFOLDER, "target-files-absolute");
            Directory.CreateDirectory(absoluteDownloadFolder);
            foreach (var targetFile in Directory.GetFiles(TARGETFOLDER))
            {
                // Absolute path
                var downloadFileName = Path.Combine(absoluteDownloadFolder, Path.GetFileName(targetFile));
                var status = CommandLine.BackendTool.Program.Main(new[] { "GET", $"{backendURL}", $"{downloadFileName}" });
                Assert.AreEqual(0, status);
                Assert.IsTrue(File.Exists(downloadFileName));
                TestUtils.AssertFilesAreEqual(targetFile, downloadFileName, false, downloadFileName);
            }

            // Get the backend files using relative paths
            var relativeDownloadFolder = Path.Combine(RESTOREFOLDER, "target-files-relative");
            Directory.CreateDirectory(relativeDownloadFolder);
            var originalCurrentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(relativeDownloadFolder);
            try
            {
                foreach (var targetFile in Directory.GetFiles(TARGETFOLDER))
                {
                    // Relative path
                    var downloadFileName = Path.GetFileName(targetFile);
                    var status = CommandLine.BackendTool.Program.Main(new[] { "GET", $"{backendURL}", $"{downloadFileName}" });
                    Assert.AreEqual(0, status);
                    Assert.IsTrue(File.Exists(downloadFileName));
                    TestUtils.AssertFilesAreEqual(targetFile, downloadFileName, false, downloadFileName);
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCurrentDirectory);
            }
        }
    }
}
