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

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class BasicSetupHelperTests
    {
        private string testFolder;
        private string logFile;

        [SetUp]
        public void SetUp()
        {
            testFolder = Path.Combine(Path.GetTempPath(), $"duplicati-log-cleanup-{Path.GetRandomFileName()}");
            Directory.CreateDirectory(testFolder);
            logFile = Path.Combine(testFolder, "logfile.log");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testFolder))
                Directory.Delete(testFolder, true);
        }

        [Test]
        public void SuccessfulTestLogIsDeleted()
        {
            File.WriteAllText(logFile, "successful test log");

            var archivePath = BasicSetupHelper.CleanupLogFile(logFile, false, 10);

            Assert.IsNull(archivePath);
            Assert.IsFalse(File.Exists(logFile));
            Assert.IsEmpty(Directory.GetFiles(testFolder));
        }

        [Test]
        public void FailedTestLogIsCompressed()
        {
            const string contents = "failed test log";
            File.WriteAllText(logFile, contents);

            var archivePath = BasicSetupHelper.CleanupLogFile(logFile, true, 10);

            Assert.IsFalse(File.Exists(logFile));
            Assert.IsTrue(File.Exists(archivePath));
            using var source = File.OpenRead(archivePath);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            Assert.AreEqual(contents, reader.ReadToEnd());
        }

        [Test]
        public void FailedTestLogsAreLimitedToMostRecentTen()
        {
            var archivePaths = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                File.WriteAllText(logFile, $"failed test log {i}");
                archivePaths.Add(BasicSetupHelper.CleanupLogFile(logFile, true, 10));
            }

            Assert.AreEqual(10, Directory.GetFiles(testFolder, "logfile.log.*.gz").Length);
            Assert.IsFalse(File.Exists(archivePaths[0]));
            Assert.IsFalse(File.Exists(archivePaths[1]));
            Assert.IsTrue(archivePaths.Skip(2).All(File.Exists));
        }
    }
}
