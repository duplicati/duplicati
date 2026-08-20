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
using Duplicati.Library.Logging;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Reproduction test for https://github.com/duplicati/duplicati/issues/7189
    /// When the source list contains a file and a folder whose path starts with
    /// the file's path (e.g. "main.lrcat" and "main.lrcat-data/"), the folder
    /// is incorrectly removed as a "subfolder" of the file and is quietly
    /// skipped by the backup.
    /// </summary>
    [TestFixture]
    public class Issue7189 : BasicSetupHelper
    {
        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];

            public void WriteMessage(LogEntry entry)
            {
                Entries.Add(entry);
            }
        }

        /// <summary>
        /// Verifies that the fix does not break the intended deduplication:
        /// a folder that is a genuine subfolder of another source folder is
        /// still pruned from the source list, and its contents are still
        /// backed up via the parent folder.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public async Task RealSubfolderIsStillPrunedAndBackedUpAsync()
        {
            var parentFolder = Path.Combine(DATAFOLDER, "parent");
            var subFolder = Path.Combine(parentFolder, "sub");
            Directory.CreateDirectory(subFolder);
            File.WriteAllText(Path.Combine(parentFolder, "root.txt"), "root contents");
            var subFile = Path.Combine(subFolder, "inner.txt");
            File.WriteAllText(subFile, "inner contents");

            var logSink = new LogSink();
            using var isolatingScope = Log.StartIsolatingScope(true);
            using var log = Log.StartScope(logSink, LogMessageType.Verbose);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var results = await c.BackupAsync([parentFolder, subFolder]);
                Assert.AreEqual(0, results.Errors.Count(), "Backup should succeed");
            }

            // The subfolder should still be pruned as a duplicate of the parent folder
            Assert.That(logSink.Entries.Any(x => x.Id == "RemovingSubfolderSource"), Is.True,
                "A genuine subfolder source should still be pruned from the source list");

            // Restore everything and verify the subfolder contents were backed up via the parent
            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER
            };

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, restoreOptions, null))
            {
                var results = await c.RestoreAsync(null);
                Assert.AreEqual(0, results.Errors.Count(), "Restore should succeed");
            }

            var restoredFiles = Directory.GetFiles(RESTOREFOLDER, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(RESTOREFOLDER, x))
                .ToList();

            Assert.That(restoredFiles.Any(x => x.EndsWith(Path.Combine("sub", "inner.txt"))), Is.True,
                "The subfolder contents should be in the backup via the parent folder");
        }

        /// <summary>
        /// Covers the case where a source file sits inside another source folder
        /// (e.g. "parent/" and "parent/sub/inner.txt"). Since files are never
        /// pruned from the source list, the file is enumerated both via the
        /// folder scan and as an explicit file source. This test documents the
        /// resulting behavior.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public async Task FileInsideIncludedFolderIsKeptAsSourceAsync()
        {
            var parentFolder = Path.Combine(DATAFOLDER, "parent");
            var subFolder = Path.Combine(parentFolder, "sub");
            Directory.CreateDirectory(subFolder);
            File.WriteAllText(Path.Combine(parentFolder, "root.txt"), "root contents");
            var innerFile = Path.Combine(subFolder, "inner.txt");
            File.WriteAllText(innerFile, "inner contents");

            var logSink = new LogSink();
            using var isolatingScope = Log.StartIsolatingScope(true);
            using var log = Log.StartScope(logSink, LogMessageType.Verbose);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var results = await c.BackupAsync([parentFolder, innerFile]);
                Assert.AreEqual(0, results.Errors.Count(), "Backup should succeed");
            }

            // The file source must NOT be pruned, even though it is inside the folder source
            Assert.That(logSink.Entries.Any(x => x.Id == "RemovingSubfolderSource"), Is.False,
                "A file source should not be pruned, even if it sits inside another source folder");

            // Because the file is enumerated twice (folder scan + explicit file source),
            // the second processing fails to insert into the fileset with a duplicate-path
            // constraint violation, surfaced as a FileProcessingFailed warning.
            // This documents the current behavior; ideally the duplicate would be
            // detected with a better message.
            var duplicateWarnings = logSink.Entries
                .Where(x => x.Id == "FileProcessingFailed" && x.FormattedMessage.Contains(innerFile))
                .ToList();
            Assert.That(duplicateWarnings, Has.Count.EqualTo(1),
                "The double enumeration of the file source should surface as a FileProcessingFailed warning");
            Assert.That(duplicateWarnings[0].Exception, Is.Not.Null
                .And.Message.Contains("FilesetEntry"),
                "The warning should be caused by a duplicate FilesetEntry");

            // Restore everything and verify both files are present exactly once
            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER
            };

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, restoreOptions, null))
            {
                var results = await c.RestoreAsync(null);
                Assert.AreEqual(0, results.Errors.Count(), "Restore should succeed");
            }

            var restoredFiles = Directory.GetFiles(RESTOREFOLDER, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(RESTOREFOLDER, x))
                .ToList();

            Assert.That(restoredFiles.Any(x => x.EndsWith("root.txt")), Is.True,
                "The folder contents should be in the backup");
            Assert.That(restoredFiles.Count(x => x.EndsWith(Path.Combine("sub", "inner.txt"))), Is.EqualTo(1),
                "The inner file should be restored exactly once, even though it was enumerated via two sources");
        }

        /// <summary>
        /// Backs up a file together with a folder that shares the file's path
        /// as a prefix, then verifies that the folder contents were backed up.
        /// </summary>
        /// <param name="folderFirst">If true, the folder is listed before the file in the source list.</param>
        [Test]
        [Category("Targeted")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task FolderWithFilePrefixIsBackedUpAsync(bool folderFirst)
        {
            // Source file, e.g. "main.lrcat" (or "sshd_config")
            var sourceFile = Path.Combine(DATAFOLDER, "main.lrcat");
            File.WriteAllText(sourceFile, "file contents");

            // Source folder that starts with the file's path, e.g. "main.lrcat-data/"
            var sourceFolder = Path.Combine(DATAFOLDER, "main.lrcat-data");
            Directory.CreateDirectory(sourceFolder);
            var folderFile = Path.Combine(sourceFolder, "inside.txt");
            File.WriteAllText(folderFile, "folder contents");

            var sources = folderFirst
                ? new[] { sourceFolder, sourceFile }
                : new[] { sourceFile, sourceFolder };

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, TestOptions, null))
            {
                var results = await c.BackupAsync(sources);
                Assert.AreEqual(0, results.Errors.Count(), "Backup should succeed");
                Assert.AreEqual(0, results.Warnings.Count(), "Backup should not emit warnings");
            }

            // Restore everything and verify both the file and the folder contents are present
            var restoreOptions = new Dictionary<string, string>(TestOptions)
            {
                ["restore-path"] = RESTOREFOLDER
            };

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, restoreOptions, null))
            {
                var results = await c.RestoreAsync(null);
                Assert.AreEqual(0, results.Errors.Count(), "Restore should succeed");
            }

            var restoredFiles = Directory.GetFiles(RESTOREFOLDER, "*", SearchOption.AllDirectories)
                .Select(x => Path.GetRelativePath(RESTOREFOLDER, x))
                .ToList();

            Assert.That(restoredFiles.Any(x => x.EndsWith("main.lrcat")), Is.True,
                "The source file should be in the backup");
            Assert.That(restoredFiles.Any(x => x.EndsWith(Path.Combine("main.lrcat-data", "inside.txt"))), Is.True,
                "The folder contents should be in the backup, even though the folder path starts with the file path");
        }
    }
}
