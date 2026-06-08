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
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [Category("Windows")]
    public class AlternateDataStreamTests : BasicSetupHelper
    {
        /// <summary>
        /// Creates an alternate data stream on Windows by opening a file stream with the ADS path syntax.
        /// </summary>
        private static void CreateAlternateDataStream(string filePath, string streamName, string content)
        {
            var adsPath = filePath + ":" + streamName;
            File.WriteAllText(adsPath, content);
        }

        /// <summary>
        /// Reads the content of an alternate data stream.
        /// </summary>
        private static string ReadAlternateDataStream(string filePath, string streamName)
        {
            var adsPath = filePath + ":" + streamName;
            return File.ReadAllText(adsPath);
        }

        /// <summary>
        /// Checks whether an alternate data stream exists.
        /// </summary>
        private static bool AlternateDataStreamExists(string filePath, string streamName)
        {
            var adsPath = filePath + ":" + streamName;
            return File.Exists(adsPath);
        }

        [Test]
        [Category("Windows")]
        public void EnumerateAlternateDataStreams()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var filePath = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(filePath, "main content");
            CreateAlternateDataStream(filePath, "stream1", "stream1 content");
            CreateAlternateDataStream(filePath, "stream2", "stream2 content");

            var streams = SystemIO.IO_OS.EnumerateAlternateDataStreams(filePath).ToList();
            Assert.AreEqual(2, streams.Count);
            Assert.IsTrue(streams.Contains(":stream1"));
            Assert.IsTrue(streams.Contains(":stream2"));
        }

        [Test]
        [Category("Windows")]
        public async Task BackupAndRestoreAlternateDataStreamsAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var filePath = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(filePath, "main content");
            CreateAlternateDataStream(filePath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions)
            {
                ["enable-ads-backup"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Restore without disabling ADS restore
            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { filePath }));

            var restoredFilePath = Path.Combine(RESTOREFOLDER, "testfile.txt");
            Assert.IsTrue(File.Exists(restoredFilePath));
            Assert.AreEqual("main content", File.ReadAllText(restoredFilePath));
            Assert.IsTrue(AlternateDataStreamExists(restoredFilePath, "hiddenstream"));
            Assert.AreEqual("hidden content", ReadAlternateDataStream(restoredFilePath, "hiddenstream"));
        }

        [Test]
        [Category("Windows")]
        public async Task DisableAdsRestoreSkipsAlternateDataStreamsAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var filePath = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(filePath, "main content");
            CreateAlternateDataStream(filePath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions)
            {
                ["enable-ads-backup"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // Restore with ADS restore disabled
            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER,
                ["disable-ads-restore"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { filePath }));

            var restoredFilePath = Path.Combine(RESTOREFOLDER, "testfile.txt");
            Assert.IsTrue(File.Exists(restoredFilePath));
            Assert.AreEqual("main content", File.ReadAllText(restoredFilePath));
            Assert.IsFalse(AlternateDataStreamExists(restoredFilePath, "hiddenstream"));
        }

        [Test]
        [Category("Windows")]
        public void IsAlternateDataStream()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var filePath = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(filePath, "main content");

            Assert.IsFalse(SystemIO.IO_OS.IsAlternateDataStream(filePath));
            Assert.IsTrue(SystemIO.IO_OS.IsAlternateDataStream(filePath + ":stream"));
            Assert.IsTrue(SystemIO.IO_OS.IsAlternateDataStream("C:\\file.txt:ads"));
            Assert.IsFalse(SystemIO.IO_OS.IsAlternateDataStream("C:\\file.txt"));

            var dirPath = Path.Combine(DATAFOLDER, "testdir");
            Directory.CreateDirectory(dirPath);

            Assert.IsFalse(SystemIO.IO_OS.IsAlternateDataStream(dirPath));
            Assert.IsTrue(SystemIO.IO_OS.IsAlternateDataStream(dirPath + ":stream"));
            Assert.IsTrue(SystemIO.IO_OS.IsAlternateDataStream("C:\\folder:ads"));
            Assert.IsFalse(SystemIO.IO_OS.IsAlternateDataStream("C:\\folder"));
        }

        [Test]
        [Category("Windows")]
        public void EnumerateAlternateDataStreamsOnDirectory()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var dirPath = Path.Combine(DATAFOLDER, "testdir");
            Directory.CreateDirectory(dirPath);
            CreateAlternateDataStream(dirPath, "stream1", "stream1 content");
            CreateAlternateDataStream(dirPath, "stream2", "stream2 content");

            var streams = SystemIO.IO_OS.EnumerateAlternateDataStreams(dirPath).ToList();
            Assert.AreEqual(2, streams.Count);
            Assert.IsTrue(streams.Contains(":stream1"));
            Assert.IsTrue(streams.Contains(":stream2"));
        }

        [Test]
        [Category("Windows")]
        public async Task BackupWithoutEnableAdsBackupIgnoresStreamsAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var filePath = Path.Combine(DATAFOLDER, "testfile.txt");
            File.WriteAllText(filePath, "main content");
            CreateAlternateDataStream(filePath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions);
            // enable-ads-backup is not set (defaults to false)

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { filePath }));

            var restoredFilePath = Path.Combine(RESTOREFOLDER, "testfile.txt");
            Assert.IsTrue(File.Exists(restoredFilePath));
            Assert.AreEqual("main content", File.ReadAllText(restoredFilePath));
            Assert.IsFalse(AlternateDataStreamExists(restoredFilePath, "hiddenstream"));
        }

        [Test]
        [Category("Windows")]
        public async Task BackupAndRestoreAlternateDataStreamsOnDirectoryAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var dirPath = Path.Combine(DATAFOLDER, "testdir");
            Directory.CreateDirectory(dirPath);
            CreateAlternateDataStream(dirPath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions)
            {
                ["enable-ads-backup"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { dirPath }));

            var restoredDirPath = Path.Combine(RESTOREFOLDER, "testdir");
            Assert.IsTrue(Directory.Exists(restoredDirPath));
            Assert.IsTrue(AlternateDataStreamExists(restoredDirPath, "hiddenstream"));
            Assert.AreEqual("hidden content", ReadAlternateDataStream(restoredDirPath, "hiddenstream"));
        }

        [Test]
        [Category("Windows")]
        public async Task DisableAdsRestoreSkipsAlternateDataStreamsOnDirectoryAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var dirPath = Path.Combine(DATAFOLDER, "testdir");
            Directory.CreateDirectory(dirPath);
            CreateAlternateDataStream(dirPath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions)
            {
                ["enable-ads-backup"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER,
                ["disable-ads-restore"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { dirPath }));

            var restoredDirPath = Path.Combine(RESTOREFOLDER, "testdir");
            Assert.IsTrue(Directory.Exists(restoredDirPath));
            Assert.IsFalse(AlternateDataStreamExists(restoredDirPath, "hiddenstream"));
        }

        [Test]
        [Category("Windows")]
        public async Task BackupAndRestoreAlternateDataStreamsOnDirectoryLegacyAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var dirPath = Path.Combine(DATAFOLDER, "testdir");
            Directory.CreateDirectory(dirPath);
            CreateAlternateDataStream(dirPath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions)
            {
                ["enable-ads-backup"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER,
                ["restore-legacy"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { dirPath }));

            var restoredDirPath = Path.Combine(RESTOREFOLDER, "testdir");
            Assert.IsTrue(Directory.Exists(restoredDirPath));
            Assert.IsTrue(AlternateDataStreamExists(restoredDirPath, "hiddenstream"));
            Assert.AreEqual("hidden content", ReadAlternateDataStream(restoredDirPath, "hiddenstream"));
        }

        [Test]
        [Category("Windows")]
        public async Task DisableAdsRestoreSkipsAlternateDataStreamsOnDirectoryLegacyAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var dirPath = Path.Combine(DATAFOLDER, "testdir");
            Directory.CreateDirectory(dirPath);
            CreateAlternateDataStream(dirPath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions)
            {
                ["enable-ads-backup"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER,
                ["disable-ads-restore"] = "true",
                ["restore-legacy"] = "true"
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { dirPath }));

            var restoredDirPath = Path.Combine(RESTOREFOLDER, "testdir");
            Assert.IsTrue(Directory.Exists(restoredDirPath));
            Assert.IsFalse(AlternateDataStreamExists(restoredDirPath, "hiddenstream"));
        }

        [Test]
        [Category("Windows")]
        public async Task BackupWithoutEnableAdsBackupIgnoresStreamsOnDirectoryAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("This test is Windows-only.");
                return;
            }

            var dirPath = Path.Combine(DATAFOLDER, "testdir");
            Directory.CreateDirectory(dirPath);
            CreateAlternateDataStream(dirPath, "hiddenstream", "hidden content");

            var backupOptions = new Dictionary<string, string>(TestOptions);
            // enable-ads-backup is not set (defaults to false)

            using (var c = new Controller("file://" + TARGETFOLDER, backupOptions, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER
            };

            using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { dirPath }));

            var restoredDirPath = Path.Combine(RESTOREFOLDER, "testdir");
            Assert.IsTrue(Directory.Exists(restoredDirPath));
            Assert.IsFalse(AlternateDataStreamExists(restoredDirPath, "hiddenstream"));
        }
    }
}
