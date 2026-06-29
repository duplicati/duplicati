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
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the <c>--restore-all-files</c> restore option, which restores every
    /// targeted backup version into its own timestamp-named subfolder below the restore
    /// target. See <see cref="RestoreAllFilesMode"/> for the supported modes.
    /// </summary>
    public class RestoreAllFilesTests : BasicSetupHelper
    {
        /// <summary>
        /// The timestamp format used for per-version restore subfolders.
        /// </summary>
        private static readonly string VersionFolderFormat = "yyyyMMdd-HHmmss";

        /// <summary>
        /// Creates three backup versions with distinct, predictable contents and returns the
        /// expected per-version contents. Versions are spaced at least one second apart so that
        /// their fileset timestamps (and thus the per-version subfolder names) are distinct.
        /// </summary>
        private async Task<List<(DateTime Time, string File1Content, string File2Content)>> CreateThreeVersionsAsync()
        {
            var file1Path = Path.Combine(this.DATAFOLDER, "file1.txt");
            var file2Path = Path.Combine(this.DATAFOLDER, "file2.txt");

            var versions = new List<(DateTime, string, string)>();

            // Version 2 (oldest): file1 and file2 with initial content.
            File.WriteAllText(file1Path, "v0-file1");
            File.WriteAllText(file2Path, "v0-file2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));
            versions.Add((DateTime.Now, "v0-file1", "v0-file2"));
            await Task.Delay(1100);

            // Version 1: only file1 changes (file2 content unchanged => same hash as v0).
            File.WriteAllText(file1Path, "v1-file1");
            using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));
            versions.Add((DateTime.Now, "v1-file1", "v0-file2"));
            await Task.Delay(1100);

            // Version 0 (newest): both files change.
            File.WriteAllText(file1Path, "v2-file1");
            File.WriteAllText(file2Path, "v2-file2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));
            versions.Add((DateTime.Now, "v2-file1", "v2-file2"));

            // versions[0] = oldest, versions[2] = newest. Return newest-first to match
            // Duplicati's version indexing (version 0 = newest).
            versions.Reverse();
            return versions;
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesFalseBehavesLikeNormalRestoreAsync()
        {
            await CreateThreeVersionsAsync();

            // --restore-all-files=false must behave like the option not being set. To prove
            // equivalence, run the same restore twice into two separate folders (once with the
            // option set to "false", once without the option) and assert the output trees are
            // identical.
            var withOptionFolder = Path.Combine(this.RESTOREFOLDER, "with-option");
            var withoutOptionFolder = Path.Combine(this.RESTOREFOLDER, "without-option");
            Directory.CreateDirectory(withOptionFolder);
            Directory.CreateDirectory(withoutOptionFolder);

            var withOption = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = withOptionFolder,
                ["restore-all-files"] = "false"
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, withOption, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var withoutOption = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = withoutOptionFolder
            };
            using (var c = new Controller("file://" + this.TARGETFOLDER, withoutOption, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            // No timestamp subfolders should exist in the with-option folder; files are
            // restored directly.
            var subFolders = Directory.GetDirectories(withOptionFolder)
                .Where(d => DateTime.TryParseExact(Path.GetFileName(d), VersionFolderFormat, null, System.Globalization.DateTimeStyles.None, out _))
                .ToArray();
            Assert.AreEqual(0, subFolders.Length, "No timestamp subfolders expected for restore-all-files=false");

            // The two output trees must be identical.
            AssertTreesEqual(withOptionFolder, withoutOptionFolder);

            var restoredFile1 = Path.Combine(withOptionFolder, "file1.txt");
            var restoredFile2 = Path.Combine(withOptionFolder, "file2.txt");
            Assert.IsTrue(File.Exists(restoredFile1), "file1.txt should be restored");
            Assert.IsTrue(File.Exists(restoredFile2), "file2.txt should be restored");
            Assert.AreEqual("v2-file1", File.ReadAllText(restoredFile1), "Newest file1 content expected");
            Assert.AreEqual("v2-file2", File.ReadAllText(restoredFile2), "Newest file2 content expected");
        }

        /// <summary>
        /// Recursively asserts that two directory trees contain the same files with the same
        /// contents. Used to prove that two restores produced identical output.
        /// </summary>
        private static void AssertTreesEqual(string expectedRoot, string actualRoot)
        {
            var expectedFiles = Directory.GetFiles(expectedRoot, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(expectedRoot, p))
                .OrderBy(p => p)
                .ToArray();
            var actualFiles = Directory.GetFiles(actualRoot, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(actualRoot, p))
                .OrderBy(p => p)
                .ToArray();
            Assert.AreEqual(expectedFiles.Length, actualFiles.Length,
                $"File count mismatch between '{expectedRoot}' and '{actualRoot}'");
            for (var i = 0; i < expectedFiles.Length; i++)
            {
                Assert.AreEqual(expectedFiles[i], actualFiles[i], $"File path mismatch at index {i}");
                Assert.AreEqual(
                    File.ReadAllText(Path.Combine(expectedRoot, expectedFiles[i])),
                    File.ReadAllText(Path.Combine(actualRoot, actualFiles[i])),
                    $"Content mismatch for '{expectedFiles[i]}'");
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesTrueRestoresEveryVersionIntoTimestampFoldersAsync()
        {
            var versions = await CreateThreeVersionsAsync();

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-all-files"] = "true"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var subFolders = Directory.GetDirectories(this.RESTOREFOLDER)
                .Where(d => DateTime.TryParseExact(Path.GetFileName(d), VersionFolderFormat, null, System.Globalization.DateTimeStyles.None, out _))
                .OrderBy(d => Path.GetFileName(d))
                .ToArray();
            Assert.AreEqual(3, subFolders.Length, "Expected three timestamp subfolders (one per version)");

            // Each subfolder contains both files from its version (true mode => no de-dup).
            for (var i = 0; i < subFolders.Length; i++)
            {
                var folder = subFolders[i];
                // subFolders are ordered oldest -> newest; versions list is newest-first,
                // so the oldest folder (index 0) corresponds to versions[2] (oldest).
                var versionInfo = versions[versions.Count - 1 - i];
                var file1 = Path.Combine(folder, "file1.txt");
                var file2 = Path.Combine(folder, "file2.txt");
                Assert.IsTrue(File.Exists(file1), $"file1.txt missing in {Path.GetFileName(folder)}");
                Assert.IsTrue(File.Exists(file2), $"file2.txt missing in {Path.GetFileName(folder)}");
                Assert.AreEqual(versionInfo.File1Content, File.ReadAllText(file1), $"file1.txt content mismatch in {Path.GetFileName(folder)}");
                Assert.AreEqual(versionInfo.File2Content, File.ReadAllText(file2), $"file2.txt content mismatch in {Path.GetFileName(folder)}");
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesUniqueSkipsAlreadyRestoredContentAsync()
        {
            var versions = await CreateThreeVersionsAsync();

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-all-files"] = "unique"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var subFolders = Directory.GetDirectories(this.RESTOREFOLDER)
                .Where(d => DateTime.TryParseExact(Path.GetFileName(d), VersionFolderFormat, null, System.Globalization.DateTimeStyles.None, out _))
                .OrderBy(d => Path.GetFileName(d))
                .ToArray();
            Assert.AreEqual(3, subFolders.Length, "Expected three timestamp subfolders (one per version)");

            // subFolders[0] = oldest, subFolders[2] = newest. The first version restored is
            // the newest (versions[0]); it contains both files. Each subsequent (older) version
            // restores only files whose content hash was not restored in a previous version.
            //
            // Content timeline (version -> file -> content):
            //   newest (v0): file1="v2-file1", file2="v2-file2"
            //   middle (v1): file1="v1-file1", file2="v0-file2"
            //   oldest (v2): file1="v0-file1", file2="v0-file2"
            //
            // Unique de-dup, processed newest-first, with the set of hashes already restored:
            //   | Version | file1 content | file2 content | file1 restored? | file2 restored? |
            //   |---------|---------------|---------------|-----------------|-----------------|
            //   | newest  | v2-file1      | v2-file2      | yes (new)       | yes (new)       |
            //   | middle  | v1-file1      | v0-file2      | yes (new)      | yes (new)       |
            //   | oldest  | v0-file1      | v0-file2      | yes (new)      | no (dup of middle's file2) |
            //
            // After the middle version, {v2-file1, v2-file2, v1-file1, v0-file2} are recorded.
            // The oldest version's file2 ("v0-file2") is already in that set, so it is skipped.

            // Map folder -> version content. Folders oldest->newest map to versions newest-first reversed:
            var folderVersionContent = new List<(string Folder, string File1Content, string File2Content, string VersionLabel)>
            {
                (subFolders[2], versions[0].File1Content, versions[0].File2Content, "newest"),
                (subFolders[1], versions[1].File1Content, versions[1].File2Content, "middle"),
                (subFolders[0], versions[2].File1Content, versions[2].File2Content, "oldest"),
            };

            // Newest folder: both files restored (first version, nothing de-duped yet).
            var newest = folderVersionContent[0];
            Assert.IsTrue(File.Exists(Path.Combine(newest.Folder, "file1.txt")), "newest: file1 should be restored");
            Assert.IsTrue(File.Exists(Path.Combine(newest.Folder, "file2.txt")), "newest: file2 should be restored");
            Assert.AreEqual(newest.File1Content, File.ReadAllText(Path.Combine(newest.Folder, "file1.txt")));
            Assert.AreEqual(newest.File2Content, File.ReadAllText(Path.Combine(newest.Folder, "file2.txt")));

            // Middle folder: file1 has a new hash (v1-file1) => restored. file2 has content
            // v0-file2, whose hash was not restored in the newest version (which had v2-file2),
            // so it is new => restored.
            var middle = folderVersionContent[1];
            Assert.IsTrue(File.Exists(Path.Combine(middle.Folder, "file1.txt")), "middle: file1 should be restored (new content)");
            Assert.IsTrue(File.Exists(Path.Combine(middle.Folder, "file2.txt")), "middle: file2 should be restored (content not seen before)");
            Assert.AreEqual(middle.File1Content, File.ReadAllText(Path.Combine(middle.Folder, "file1.txt")));
            Assert.AreEqual(middle.File2Content, File.ReadAllText(Path.Combine(middle.Folder, "file2.txt")));

            // Oldest folder: file1 has a new hash (v0-file1) => restored. file2 has content
            // v0-file2, whose hash was already restored in the middle folder => SKIPPED.
            var oldest = folderVersionContent[2];
            Assert.IsTrue(File.Exists(Path.Combine(oldest.Folder, "file1.txt")), "oldest: file1 should be restored (new content)");
            Assert.AreEqual(oldest.File1Content, File.ReadAllText(Path.Combine(oldest.Folder, "file1.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(oldest.Folder, "file2.txt")), "oldest: file2 should be skipped (content already restored in middle folder)");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesRequiresRestorePathAsync()
        {
            await CreateThreeVersionsAsync();

            // No --restore-path set. The restore should fail because --restore-all-files
            // requires a definitive target. In unittest mode the UserInformationException is
            // propagated, so assert that the expected exception is thrown.
            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-all-files"] = "true"
            };
            // Remove any default restore-path if present.
            restoreOptions.Remove("restore-path");

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            {
                Assert.ThrowsAsync<Duplicati.Library.Interface.UserInformationException>(
                    async () => await c.RestoreAsync(new[] { "*" }),
                    "Expected --restore-all-files without --restore-path to throw");
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesRespectsVersionSelectionAsync()
        {
            var versions = await CreateThreeVersionsAsync();

            // Target only versions 0 and 2 (newest and oldest) via --version=0,2.
            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-all-files"] = "true",
                ["version"] = "0,2"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var subFolders = Directory.GetDirectories(this.RESTOREFOLDER)
                .Where(d => DateTime.TryParseExact(Path.GetFileName(d), VersionFolderFormat, null, System.Globalization.DateTimeStyles.None, out _))
                .ToArray();
            Assert.AreEqual(2, subFolders.Length, "Expected two timestamp subfolders for the two selected versions");

            // The two folders correspond to the newest (version 0) and oldest (version 2) versions.
            var orderedFolders = subFolders.OrderBy(d => Path.GetFileName(d)).ToArray();
            var oldestFolder = orderedFolders[0];
            var newestFolder = orderedFolders[1];

            Assert.AreEqual(versions[0].File1Content, File.ReadAllText(Path.Combine(newestFolder, "file1.txt")), "newest folder file1");
            Assert.AreEqual(versions[0].File2Content, File.ReadAllText(Path.Combine(newestFolder, "file2.txt")), "newest folder file2");
            Assert.AreEqual(versions[2].File1Content, File.ReadAllText(Path.Combine(oldestFolder, "file1.txt")), "oldest folder file1");
            Assert.AreEqual(versions[2].File2Content, File.ReadAllText(Path.Combine(oldestFolder, "file2.txt")), "oldest folder file2");
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesWithTimeDoesNotPollutePerVersionFoldersAsync()
        {
            // Regression test: when --time and --restore-all-files are combined, each
            // per-version iteration must restore exactly that single version's files into its
            // own subfolder. Previously the --time option was not cleared between iterations,
            // so the underlying file-list query (which combines --time and --version with OR)
            // pulled in all filesets at or before --time on every iteration, polluting each
            // subfolder with files from multiple versions.
            var versions = await CreateThreeVersionsAsync();

            // Use --time set to the middle version's post-backup timestamp. This selects the
            // middle and oldest versions (both at or before --time); the newest version is after
            // the cutoff and excluded. Each targeted version is restored into its own subfolder
            // with exactly its own files. Use the invariant sortable format (yyyy-MM-ddTHH:mm:ss)
            // which Timeparser.ParseTimeInterval accepts via DateTime.TryParse with AssumeLocal.
            var middleTime = versions[1].Time;
            var timeSpec = middleTime.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-all-files"] = "true",
                ["time"] = timeSpec
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var subFolders = Directory.GetDirectories(this.RESTOREFOLDER)
                .Where(d => DateTime.TryParseExact(Path.GetFileName(d), VersionFolderFormat, null, System.Globalization.DateTimeStyles.None, out _))
                .OrderBy(d => Path.GetFileName(d))
                .ToArray();
            Assert.AreEqual(2, subFolders.Length, "Expected two timestamp subfolders (middle and oldest, both at or before --time)");

            // Each subfolder must contain exactly its own version's two files -- no cross-version
            // pollution. The older folder maps to the oldest version, the newer folder to the
            // middle version (both at or before --time).
            var orderedFolders = subFolders.OrderBy(d => Path.GetFileName(d)).ToArray();
            var oldestFolder = orderedFolders[0];
            var middleFolder = orderedFolders[1];

            Assert.AreEqual(versions[2].File1Content, File.ReadAllText(Path.Combine(oldestFolder, "file1.txt")), "oldest folder file1");
            Assert.AreEqual(versions[2].File2Content, File.ReadAllText(Path.Combine(oldestFolder, "file2.txt")), "oldest folder file2");
            Assert.AreEqual(versions[1].File1Content, File.ReadAllText(Path.Combine(middleFolder, "file1.txt")), "middle folder file1");
            Assert.AreEqual(versions[1].File2Content, File.ReadAllText(Path.Combine(middleFolder, "file2.txt")), "middle folder file2");

            // Each folder must contain exactly two files (file1.txt and file2.txt) -- if the
            // --time bug were present, a folder could contain duplicate/extra files from the
            // other version. Assert no extra files leaked in.
            foreach (var folder in subFolders)
            {
                var files = Directory.GetFiles(folder);
                Assert.AreEqual(2, files.Length, $"Folder {Path.GetFileName(folder)} should contain exactly 2 files, found {files.Length}: {string.Join(", ", files.Select(Path.GetFileName))}");
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesTrueLegacyRestoresEveryVersionIntoTimestampFoldersAsync()
        {
            // --restore-legacy exercises the DoRunAsync path, which has its own
            // HarvestRestoredHashesAsync wiring. Ensure the multi-version restore works there too.
            var versions = await CreateThreeVersionsAsync();

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-all-files"] = "true",
                ["restore-legacy"] = "true"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            var subFolders = Directory.GetDirectories(this.RESTOREFOLDER)
                .Where(d => DateTime.TryParseExact(Path.GetFileName(d), VersionFolderFormat, null, System.Globalization.DateTimeStyles.None, out _))
                .OrderBy(d => Path.GetFileName(d))
                .ToArray();
            Assert.AreEqual(3, subFolders.Length, "Expected three timestamp subfolders (one per version) with --restore-legacy");

            for (var i = 0; i < subFolders.Length; i++)
            {
                var folder = subFolders[i];
                var versionInfo = versions[versions.Count - 1 - i];
                var file1 = Path.Combine(folder, "file1.txt");
                var file2 = Path.Combine(folder, "file2.txt");
                Assert.IsTrue(File.Exists(file1), $"file1.txt missing in {Path.GetFileName(folder)} (legacy)");
                Assert.IsTrue(File.Exists(file2), $"file2.txt missing in {Path.GetFileName(folder)} (legacy)");
                Assert.AreEqual(versionInfo.File1Content, File.ReadAllText(file1), $"file1.txt content mismatch in {Path.GetFileName(folder)} (legacy)");
                Assert.AreEqual(versionInfo.File2Content, File.ReadAllText(file2), $"file2.txt content mismatch in {Path.GetFileName(folder)} (legacy)");
            }
        }

        [Test]
        [Category("RestoreHandler")]
        public async Task RestoreAllFilesUniqueCleansUpScratchTableAsync()
        {
            // The unique mode tracks restored hashes in a persistent scratch table in the local
            // database (not in memory). Verify the table is dropped after the run so it does not
            // leak into the database file.
            await CreateThreeVersionsAsync();

            var restoreOptions = new Dictionary<string, string>(this.TestOptions)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-all-files"] = "unique"
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }));

            // Inspect the local database for any leftover "RestoredHashes-*" scratch tables.
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={this.DBFILE}");
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'RestoredHashes-%'";
            await using var reader = await cmd.ExecuteReaderAsync();
            var leftoverTables = new List<string>();
            while (await reader.ReadAsync())
                leftoverTables.Add(reader.GetString(0));

            Assert.AreEqual(0, leftoverTables.Count,
                $"The restored-hashes scratch table should be dropped after the run, but found: {string.Join(", ", leftoverTables)}");
        }
    }
}
