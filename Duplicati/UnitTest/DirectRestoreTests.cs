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
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A restore without a local database builds its temporary database from one filelist and the
    /// index files, with only the blocks of the files being restored, instead of running a recreate.
    /// The recreate is used with --disable-fast-direct-restore, and for the legacy restore.
    /// </summary>
    [TestFixture]
    public class DirectRestoreTests : BasicSetupHelper
    {
        /// <summary>
        /// The log message written when the direct restore database was built and is used
        /// </summary>
        private const string Completed = "DirectRestoreCompleted";
        /// <summary>
        /// The log message written when the filelist is selected, before the index files are read
        /// </summary>
        private const string Started = "DirectRestoreStarted";
        /// <summary>
        /// The log message written when a remote file could not be read and a recreate is used
        /// </summary>
        private const string UsingRecreateAfterFailure = "DirectRestoreIndexFailed";
        /// <summary>
        /// The log message written when the remote files contradict each other and a recreate is used
        /// </summary>
        private const string Inconsistent = "DirectRestoreInconsistent";

        /// <summary>
        /// The options for a backup with several block volumes, and therefore several index files
        /// </summary>
        private Dictionary<string, string> BackupOptions(bool encrypted)
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["dblock-size"] = "1mb"
            };
            if (!encrypted)
            {
                options.Remove("passphrase");
                options["no-encryption"] = "true";
            }

            return options;
        }

        /// <summary>
        /// Writes files of each shape the restore handles differently. The block size is 10kb, so a
        /// blocklist holds 320 hashes and a file over 3.2mb needs more than one blocklist.
        /// </summary>
        private void WriteSourceFiles()
        {
            var sub = Path.Combine(this.DATAFOLDER, "sub", "folder");
            Directory.CreateDirectory(sub);
            Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "empty-folder"));

            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "empty.bin"), []);
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "small.txt"), "small");
            TestUtils.WriteTestFile(Path.Combine(this.DATAFOLDER, "one-block.bin"), 10 * 1024);
            TestUtils.WriteTestFile(Path.Combine(sub, "few-blocks.bin"), 25 * 1024);
            TestUtils.WriteTestFile(Path.Combine(sub, "two-blocklists.bin"), 4 * 1024 * 1024);
            File.Copy(Path.Combine(sub, "few-blocks.bin"), Path.Combine(this.DATAFOLDER, "copy-of-few-blocks.bin"));
        }

        /// <summary>
        /// Restores without a local database, and returns the IDs of the direct restore log messages.
        /// </summary>
        private Task<(IRestoreResults Results, List<string> Messages)> RestoreAsync(Dictionary<string, string> options, string[] paths)
            => RestoreAsync(options, paths, [], (_, _) => { });

        /// <summary>
        /// Restores without a local database, and returns the IDs of the direct restore log messages.
        /// </summary>
        /// <param name="options">The options of the backup, and any to add for the restore.</param>
        /// <param name="paths">The paths to restore.</param>
        /// <param name="otherMessages">The IDs of the log messages to return, besides those of the outcome.</param>
        /// <param name="onMessage">Called with the controller and the ID for each returned message, as it is logged.</param>
        private async Task<(IRestoreResults Results, List<string> Messages)> RestoreAsync(Dictionary<string, string> options, string[] paths, string[] otherMessages, Action<Controller, string> onMessage)
        {
            var restoreOptions = new Dictionary<string, string>(options)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["no-local-db"] = "true"
            };

            var messages = new List<string>();
            using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
            using (Log.StartScope(e =>
            {
                lock (messages)
                    messages.Add(e.Id);
                onMessage(c, e.Id);
            }, e => e.Id is Completed or Started || otherMessages.Contains(e.Id)))
                return (await c.RestoreAsync(paths), messages);
        }

        /// <summary>
        /// Backs up the source files without encryption, so the remote files can be changed.
        /// </summary>
        private async Task<Dictionary<string, string>> BackupUnencryptedAsync()
        {
            var options = BackupOptions(false);
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            return options;
        }

        /// <summary>
        /// Replaces the content of an entry in a remote volume.
        /// </summary>
        private static void ReplaceEntry(string volume, string name, byte[] content)
        {
            using var zip = ZipFile.Open(volume, ZipArchiveMode.Update);
            zip.GetEntry(name).Delete();
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(content);
        }

        /// <summary>
        /// Reads an entry in a remote volume.
        /// </summary>
        private static byte[] ReadEntry(string volume, string name)
        {
            using var zip = ZipFile.OpenRead(volume);
            using var stream = zip.GetEntry(name).Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Lists the names of the entries in a remote volume.
        /// </summary>
        private static string[] ListEntries(string volume)
        {
            using var zip = ZipFile.OpenRead(volume);
            return zip.Entries.Select(x => x.FullName).ToArray();
        }

        [Test]
        [Category("Restore")]
        [TestCase(true)]
        [TestCase(false)]
        public async Task AllFilesAreRestoredFromTheIndexFilesAsync(bool encrypted)
        {
            var options = BackupOptions(encrypted);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));
            Assert.That(Directory.GetFiles(this.TARGETFOLDER, "*.dindex.*").Length, Is.GreaterThan(1), "The backup should have several index files");

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore");
        }

        [Test]
        [Category("Restore")]
        public async Task ASingleFileIsRestoredFromAnOlderVersionAsync()
        {
            var options = BackupOptions(true);
            WriteSourceFiles();
            var target = Path.Combine(this.DATAFOLDER, "sub", "folder", "few-blocks.bin");
            var original = File.ReadAllBytes(target);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // The filelists are told apart by a time with a resolution of one second
            await Task.Delay(1500);
            TestUtils.WriteTestFile(target, 60 * 1024);
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "added.txt"), "added");
            Assert.That(File.ReadAllBytes(target), Is.Not.EqualTo(original));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(new Dictionary<string, string>(options) { ["version"] = "1" }, new[] { target });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            var restored = Directory.GetFiles(this.RESTOREFOLDER, "*", SearchOption.AllDirectories);
            Assert.That(restored.Select(Path.GetFileName), Is.EqualTo(new[] { "few-blocks.bin" }));
            Assert.That(File.ReadAllBytes(restored[0]), Is.EqualTo(original));
        }

        [Test]
        [Category("Restore")]
        public async Task TheNewestVersionIsRestoredWithoutAVersionAsync()
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            await Task.Delay(1500);
            TestUtils.WriteTestFile(Path.Combine(this.DATAFOLDER, "one-block.bin"), 7 * 1024);
            File.Delete(Path.Combine(this.DATAFOLDER, "small.txt"));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore of the newest version");
        }

        [Test]
        [Category("Restore")]
        [TestCase("Lookup")]
        [TestCase("None")]
        public async Task TheBlockVolumesAreReadForWhatTheIndexFilesDoNotHaveAsync(string indexFilePolicy)
        {
            var options = BackupOptions(true);
            options["index-file-policy"] = indexFilePolicy;
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore with index file policy " + indexFilePolicy);
        }

        [Test]
        [Category("Restore")]
        [TestCase(true)]
        [TestCase(false)]
        public async Task TheBlockVolumesAreReadWhenIndexFilesAreMissingAsync(bool all)
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            var indexfiles = Directory.GetFiles(this.TARGETFOLDER, "*.dindex.*").OrderBy(x => x).ToArray();
            foreach (var f in all ? indexfiles : indexfiles.Take(2))
                File.Delete(f);

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore with missing index files");
        }

        [Test]
        [Category("Restore")]
        public async Task TheOtherFilesAreRestoredWhenABlockVolumeIsMissingAsync()
        {
            var options = BackupOptions(true);
            // Each file ends up in a block volume of its own, apart from the large one
            WriteSourceFiles();
            foreach (var i in Enumerable.Range(0, 4))
                TestUtils.WriteTestFile(Path.Combine(this.DATAFOLDER, $"filler-{i}.bin"), 900 * 1024);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // The largest block volume holds file data only
            var missing = Directory.GetFiles(this.TARGETFOLDER, "*.dblock.*").OrderByDescending(x => new FileInfo(x).Length).ThenBy(x => x).First();
            File.Delete(missing);

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            Assert.That(results.Errors, Is.Not.Empty, "The files that cannot be restored are an error");

            var source = Directory.GetFiles(this.DATAFOLDER, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(this.DATAFOLDER, x)).OrderBy(x => x).ToArray();
            var restored = Directory.GetFiles(this.RESTOREFOLDER, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(this.RESTOREFOLDER, x)).OrderBy(x => x).ToArray();
            Assert.That(restored, Is.SubsetOf(source));
            Assert.That(restored.Length, Is.GreaterThan(0).And.LessThan(source.Length), "Some files are restored, and some are not");
            foreach (var f in restored)
                Assert.That(File.ReadAllBytes(Path.Combine(this.RESTOREFOLDER, f)), Is.EqualTo(File.ReadAllBytes(Path.Combine(this.DATAFOLDER, f))), f);
        }

        [Test]
        [Category("Restore")]
        public async Task TheIndexFilesAreDownloadedAgainWhenTheyAreNotKeptAsync()
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // The index files are kept between the two passes within the size of the volume cache
            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(new Dictionary<string, string>(options) { ["restore-volume-cache-hint"] = "0b" }, new[] { "*" });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore without keeping the index files");
        }

        [Test]
        [Category("Restore")]
        public async Task NothingIsRestoredWhenStoppedWhileTheDatabaseIsBuiltAsync()
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            File.Delete(this.DBFILE);
            // The filelist is selected, and the index files are not yet read
            var (results, messages) = await RestoreAsync(options, new[] { "*" }, ["RestoreStopped"], (c, id) =>
            {
                if (id == Started)
                    c.StopAsync().Wait();
            });

            Assert.That(messages, Is.EqualTo(new[] { Started, "RestoreStopped" }));
            Assert.That(results.Errors, Is.Empty);
            Assert.That(results.Warnings, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(this.RESTOREFOLDER), Is.Empty);
        }

        [Test]
        [Category("Restore")]
        public async Task TheRestoreCarriesOnAfterAPauseWhileTheDatabaseIsBuiltAsync()
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            File.Delete(this.DBFILE);
            var paused = TimeSpan.FromMilliseconds(1500);
            var timer = Stopwatch.StartNew();
            var (results, messages) = await RestoreAsync(options, new[] { "*" }, [], (c, id) =>
            {
                if (id != Started)
                    return;

                c.PauseAsync(false).Wait();
                _ = Task.Delay(paused).ContinueWith(_ => c.ResumeAsync());
            });
            timer.Stop();
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            Assert.That(timer.Elapsed, Is.GreaterThanOrEqualTo(paused - TimeSpan.FromMilliseconds(100)), "The database is not built while paused");
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore after a pause");
        }

        [Test]
        [Category("Restore")]
        public async Task NothingIsRestoredWhenAbortedWhileTheDatabaseIsBuiltAsync()
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            File.Delete(this.DBFILE);
            var messages = new List<string>();
            Exception failure = null;
            try
            {
                await RestoreAsync(options, new[] { "*" }, ["RestoreStopped", UsingRecreateAfterFailure], (c, id) =>
                {
                    lock (messages)
                        messages.Add(id);
                    if (id == Started)
                        c.AbortAsync().Wait();
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // An abort is not a reason to try the recreate
            Assert.That(messages, Is.EqualTo(new[] { Started }));
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.GetFileSystemEntries(this.RESTOREFOLDER), Is.Empty);
        }

        [Test]
        [Category("Restore")]
        public async Task ABlocklistIsReadFromItsBlockVolumeWhenTheCopyInTheIndexFileIsDamagedAsync()
        {
            WriteSourceFiles();
            var options = await BackupUnencryptedAsync();

            // Each index file has a copy of the blocklists in the block volume it describes
            var damaged = 0;
            foreach (var indexfile in Directory.GetFiles(this.TARGETFOLDER, "*.dindex.*"))
                foreach (var entry in ListEntries(indexfile).Where(x => x.StartsWith("list/")))
                {
                    ReplaceEntry(indexfile, entry, new byte[ReadEntry(indexfile, entry).Length]);
                    damaged++;
                }
            Assert.That(damaged, Is.GreaterThan(1), "The backup should have several blocklists");

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore with damaged blocklists in the index files");
        }

        [Test]
        [Category("Restore")]
        public async Task ABlockVolumeIsReadWhenItsIndexFileIsDamagedAsync()
        {
            WriteSourceFiles();
            var options = await BackupUnencryptedAsync();

            var indexfile = Directory.GetFiles(this.TARGETFOLDER, "*.dindex.*").OrderBy(x => x).First();
            File.WriteAllBytes(indexfile, Enumerable.Range(0, 1000).Select(x => (byte)x).ToArray());

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            Assert.That(results.Errors, Is.Empty);
            // The compression module warns about the file as well
            Assert.That(results.Warnings.Count(x => x.Contains("DirectRestoreIndexFileFailed") && x.Contains(Path.GetFileName(indexfile))), Is.EqualTo(1));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore with a damaged index file");
        }

        [Test]
        [Category("Restore")]
        public async Task ABlockVolumeIsReadWhenItsIndexFileCannotBeDownloadedAsync()
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            // The file cannot be decrypted, which fails the download
            var indexfile = Directory.GetFiles(this.TARGETFOLDER, "*.dindex.*").OrderBy(x => x).First();
            File.WriteAllBytes(indexfile, Enumerable.Range(0, 1000).Select(x => (byte)x).ToArray());

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(new Dictionary<string, string>(options) { ["number-of-retries"] = "0" }, new[] { "*" }, [UsingRecreateAfterFailure], (_, _) => { });

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            Assert.That(results.Errors, Is.Empty);
            Assert.That(results.Warnings.Count(x => x.Contains("DirectRestoreDownloadFailed") && x.Contains(Path.GetFileName(indexfile))), Is.EqualTo(1));
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Direct restore with an index file that cannot be downloaded");
        }

        [Test]
        [Category("Restore")]
        public async Task ARecreateIsUsedWhenTheRemoteFilesDoNotAgreeAsync()
        {
            WriteSourceFiles();
            var options = await BackupUnencryptedAsync();
            options.Remove("unittest-mode");

            // The file of 4mb has two blocklists. Recorded as a smaller file, it has one, with more
            // hashes than the file has blocks
            var filelist = Directory.GetFiles(this.TARGETFOLDER, "*.dlist.*").Single();
            var json = System.Text.Encoding.UTF8.GetString(ReadEntry(filelist, "filelist.json"));
            var changed = Regex.Replace(json, "\"size\"\\s*:\\s*4194304", "\"size\":3000000");
            Assert.That(changed, Is.Not.EqualTo(json), "The filelist should record the size of the large file");
            ReplaceEntry(filelist, "filelist.json", System.Text.Encoding.UTF8.GetBytes(changed));

            File.Delete(this.DBFILE);
            var messages = new List<string>();
            try
            {
                await RestoreAsync(options, new[] { "*" }, [Inconsistent], (_, id) =>
                {
                    lock (messages)
                        messages.Add(id);
                });
            }
            catch (Exception ex)
            {
                // What the recreate makes of such a filelist is not what is tested
                TestContext.Out.WriteLine("Recreate outcome: {0}", ex);
            }

            Assert.That(messages, Is.EqualTo(new[] { Started, Inconsistent }));
        }

        [Test]
        [Category("Restore")]
        public async Task AFileIsRestoredWithoutMetadataWhenTheMetadataIsMissingAsync()
        {
            WriteSourceFiles();
            var withoutMetadata = Path.Combine(this.DATAFOLDER, "small.txt");
            var withMetadata = Path.Combine(this.DATAFOLDER, "one-block.bin");
            // The times make the metadata of each file different from that of any other
            File.SetLastWriteTimeUtc(withoutMetadata, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(withMetadata, new DateTime(2002, 3, 4, 5, 6, 7, DateTimeKind.Utc));
            var options = await BackupUnencryptedAsync();

            string hash, volume;
            using (var db = new SqliteConnection($"Data Source={this.DBFILE};Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"
                    SELECT ""B"".""Hash"", ""RV"".""Name""
                    FROM ""File"" ""F""
                    JOIN ""Metadataset"" ""M"" ON ""M"".""ID"" = ""F"".""MetadataID""
                    JOIN ""BlocksetEntry"" ""BE"" ON ""BE"".""BlocksetID"" = ""M"".""BlocksetID""
                    JOIN ""Block"" ""B"" ON ""B"".""ID"" = ""BE"".""BlockID""
                    JOIN ""Remotevolume"" ""RV"" ON ""RV"".""ID"" = ""B"".""VolumeID""
                    WHERE ""F"".""Path"" = @Path";
                cmd.Parameters.AddWithValue("@Path", withoutMetadata);
                using var reader = cmd.ExecuteReader();
                Assert.That(reader.Read(), Is.True, "The metadata of the file should be a single block");
                (hash, volume) = (reader.GetString(0), reader.GetString(1));
                Assert.That(reader.Read(), Is.False, "The metadata of the file should be a single block");
            }

            // The block is taken out of its volume, and the index file that lists it there is removed,
            // so the volume is read for what it holds
            using (var zip = ZipFile.Open(Path.Combine(this.TARGETFOLDER, volume), ZipArchiveMode.Update))
                zip.GetEntry(Library.Utility.Utility.Base64PlainToBase64Url(hash)).Delete();
            var indexfile = Directory.GetFiles(this.TARGETFOLDER, "*.dindex.*").Single(x => ListEntries(x).Contains("vol/" + volume));
            File.Delete(indexfile);

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(options, new[] { "*" });

            Assert.That(messages, Is.EqualTo(new[] { Started, Completed }));
            Assert.That(results.Errors, Is.Empty);
            Assert.That(results.Warnings.Count(), Is.EqualTo(1));
            Assert.That(results.Warnings.Single(), Does.Contain("DirectRestoreMissingMetadata").And.Contain(withoutMetadata));

            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, false, "Direct restore with missing metadata");
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(this.RESTOREFOLDER, "one-block.bin")), Is.EqualTo(File.GetLastWriteTimeUtc(withMetadata)));
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(this.RESTOREFOLDER, "small.txt")), Is.Not.EqualTo(File.GetLastWriteTimeUtc(withoutMetadata)));
        }

        [Test]
        [Category("Restore")]
        [TestCase("disable-fast-direct-restore")]
        [TestCase("restore-legacy")]
        public async Task ARecreateIsUsedWhenTurnedOffOrForTheLegacyRestoreAsync(string option)
        {
            var options = BackupOptions(true);
            WriteSourceFiles();

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));

            File.Delete(this.DBFILE);
            var (results, messages) = await RestoreAsync(new Dictionary<string, string>(options) { [option] = "true" }, new[] { "*" });
            TestUtils.AssertResults(results);

            Assert.That(messages, Is.Empty);
            TestUtils.AssertDirectoryTreesAreEquivalent(this.DATAFOLDER, this.RESTOREFOLDER, true, "Restore with " + option);
        }
    }
}
