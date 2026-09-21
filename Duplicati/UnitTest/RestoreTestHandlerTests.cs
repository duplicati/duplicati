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
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the restore-test operation: a sample of files is restored from the
    /// remote destination into a scratch folder and verified against the backup.
    /// </summary>
    public class RestoreTestHandlerTests : BasicSetupHelper
    {
        /// <summary>
        /// The folder the restore test uses for its scratch files
        /// </summary>
        private readonly string SCRATCHFOLDER = Path.Combine(BASEFOLDER, "restore-test-scratch");

        /// <summary>
        /// The prefix of the scratch folders created by the restore test
        /// </summary>
        private const string SCRATCH_PREFIX = "duplicati-restore-test-";

        /// <summary>
        /// The tables that hold the backup content; these must not change during a restore test
        /// </summary>
        private static readonly string[] ContentTables = ["Fileset", "FilesetEntry", "FileLookup", "PathPrefix", "Blockset", "BlocksetEntry", "Block", "Metadataset", "Remotevolume", "Configuration"];

        [SetUp]
        public void RestoreTestSetUp()
        {
            // Start from an empty folder, so scratch folders left by an aborted run do not fail the cleanup checks
            if (Directory.Exists(SCRATCHFOLDER))
                Directory.Delete(SCRATCHFOLDER, true);
            Directory.CreateDirectory(SCRATCHFOLDER);
        }

        [TearDown]
        public void RestoreTestTearDown()
        {
            if (Directory.Exists(SCRATCHFOLDER))
                Directory.Delete(SCRATCHFOLDER, true);
        }

        /// <summary>
        /// Creates a set of source files of varying sizes in nested folders
        /// </summary>
        /// <param name="count">The number of files to create</param>
        /// <param name="maxSize">The maximum file size</param>
        /// <returns>The full paths of the created files</returns>
        private List<string> CreateSourceFiles(int count, int maxSize = 60 * 1024)
        {
            var random = new Random(4242);
            var folders = new[] { DATAFOLDER, Path.Combine(DATAFOLDER, "sub"), Path.Combine(DATAFOLDER, "sub", "deeper") };
            foreach (var folder in folders)
                Directory.CreateDirectory(folder);

            var paths = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var path = Path.Combine(folders[i % folders.Length], $"file-{i}.bin");
                // The first file is empty, the rest span several blocks
                var data = new byte[i == 0 ? 0 : random.Next(1024, maxSize)];
                random.NextBytes(data);
                File.WriteAllBytes(path, data);
                paths.Add(path);
            }

            return paths;
        }

        /// <summary>
        /// The options used for the backup, using small volumes so the data spans several dblock files
        /// </summary>
        private Dictionary<string, string> BackupOptions => new Dictionary<string, string>(TestOptions)
        {
            ["dblock-size"] = "100kb"
        };

        /// <summary>
        /// The options used for the restore test
        /// </summary>
        /// <param name="mode">The sampling mode</param>
        private Dictionary<string, string> RestoreTestOptions(string mode = "Full") => new Dictionary<string, string>(BackupOptions)
        {
            ["restore-test-mode"] = mode,
            ["restore-test-temp-path"] = SCRATCHFOLDER,
            ["restore-test-seed"] = "7",
            // Broken volumes must not stall the tests on retries
            ["number-of-retries"] = "0",
            ["retry-delay"] = "0s"
        };

        private async Task RunBackupAsync()
        {
            using var c = new Controller("file://" + TARGETFOLDER, BackupOptions, null);
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }

        private async Task<IRestoreTestResults> RunRestoreTestAsync(Dictionary<string, string> options, Library.Utility.IFilter? filter = null)
        {
            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            return await c.RestoreTestAsync(filter);
        }

        /// <summary>
        /// Runs the restore-test command through the command-line entry point
        /// </summary>
        /// <param name="options">The options to pass</param>
        /// <param name="output">The captured console output</param>
        /// <returns>The exit code</returns>
        private int RunCli(Dictionary<string, string> options, out string output)
        {
            var args = new List<string> { "restore-test", "file://" + TARGETFOLDER };
            args.AddRange(options.Select(kv => $"--{kv.Key}={kv.Value}"));
            var writer = new StringWriter();
            var code = Duplicati.CommandLine.Program.RunCommandLine(writer, writer, c => { }, args.ToArray());
            output = writer.ToString();
            return code;
        }

        private IEnumerable<string> ScratchFolders()
            => Directory.Exists(SCRATCHFOLDER)
                ? Directory.GetDirectories(SCRATCHFOLDER).Where(x => Path.GetFileName(x).StartsWith(SCRATCH_PREFIX, StringComparison.Ordinal))
                : [];

        /// <summary>
        /// Builds a string that captures the content of the backup tables in the local database
        /// </summary>
        private static async Task<string> SnapshotContentTablesAsync(string dbpath)
        {
            var sb = new StringBuilder();
            using var connection = await SQLiteLoader.LoadConnectionAsync(dbpath);
            foreach (var table in ContentTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $@"SELECT * FROM ""{table}"" ORDER BY 1";
                using var rd = await cmd.ExecuteReaderAsync();
                sb.AppendLine(table);
                while (await rd.ReadAsync())
                {
                    for (var i = 0; i < rd.FieldCount; i++)
                        sb.Append(rd.IsDBNull(i) ? "<null>" : rd.GetValue(i).ToString()).Append('|');
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads the verification history rows from the local database
        /// </summary>
        private static async Task<List<(string Path, string Result)>> ReadHistoryAsync(string dbpath)
        {
            var result = new List<(string, string)>();
            using var connection = await SQLiteLoader.LoadConnectionAsync(dbpath);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT ""Path"", ""LastResult"" FROM ""RestoreTestHistory""";
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                result.Add((rd.GetString(0), rd.GetString(1)));
            return result;
        }

        /// <summary>
        /// Reads the paths of the files that have data blocks in the given remote volume
        /// </summary>
        private static async Task<List<string>> GetFilesInVolumeAsync(string dbpath, string volumeName)
        {
            var result = new List<string>();
            using var connection = await SQLiteLoader.LoadConnectionAsync(dbpath);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT ""F"".""Path""
                FROM ""File"" ""F""
                JOIN ""BlocksetEntry"" ""BE"" ON ""BE"".""BlocksetID"" = ""F"".""BlocksetID""
                JOIN ""Block"" ""B"" ON ""B"".""ID"" = ""BE"".""BlockID""
                JOIN ""Remotevolume"" ""RV"" ON ""RV"".""ID"" = ""B"".""VolumeID""
                WHERE ""RV"".""Name"" = @Name";
            cmd.Parameters.AddWithValue("@Name", volumeName);
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                result.Add(rd.GetString(0));
            return result;
        }

        /// <summary>
        /// Overwrites a stretch of bytes in the middle of the file, keeping the size
        /// </summary>
        private static void CorruptFileMiddle(string path, int count)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            var offset = Math.Max(0, (fs.Length / 2) - (count / 2));
            fs.Seek(offset, SeekOrigin.Begin);
            var junk = new byte[count];
            new Random(1234).NextBytes(junk);
            fs.Write(junk, 0, count);
        }

        [Test]
        [Category("RestoreTest")]
        public async Task AllModesPassOnHealthyBackupAsync([Values("RandomFiles", "RandomSize", "Full", "Rolling")] string mode)
        {
            var files = CreateSourceFiles(12);
            await RunBackupAsync();

            var options = RestoreTestOptions(mode);
            options["restore-test-recreate-database"] = "true";
            options["restore-test-sample-count"] = "5";
            options["restore-test-sample-percent"] = "30";

            var results = await RunRestoreTestAsync(options);

            Assert.AreEqual(0, results.Errors.Count(), string.Join(Environment.NewLine, results.Errors));
            Assert.AreEqual(0, results.Warnings.Count(), string.Join(Environment.NewLine, results.Warnings));
            Assert.AreEqual(ParsedResultType.Success, results.ParsedResult);
            Assert.IsTrue(results.FilesTested > 0, "Expected files to be tested");
            Assert.AreEqual(results.FilesTested, results.FilesPassed, "All tested files should pass");
            Assert.AreEqual(0, results.FilesFailed);
            Assert.AreEqual(0, results.FilesSkipped);
            Assert.IsFalse(results.Failures.Any());
            Assert.IsFalse(results.Budget.Exceeded);
            Assert.IsTrue(results.DatabaseRecreated, "The database should be recreated when requested");
            Assert.IsNotNull(results.RecreateDatabaseResults);
            Assert.IsNotNull(results.RestoreResults);
            Assert.IsTrue(results.BytesDownloaded > 0, "Expected remote data to be downloaded");
            Assert.IsTrue(results.RemoteVolumesDownloaded > 0, "Expected remote volumes to be downloaded");
            Assert.AreEqual(0, results.Version);
            Assert.AreEqual(7, results.Seed);

            switch (mode)
            {
                case "Full":
                    Assert.AreEqual(files.Count, results.FilesTested);
                    break;
                case "RandomFiles":
                case "Rolling":
                    Assert.AreEqual(5, results.FilesTested);
                    break;
                case "RandomSize":
                    Assert.IsTrue(results.FilesTested < files.Count, "The size based sample should not include every file");
                    break;
            }

            Assert.IsFalse(ScratchFolders().Any(), "The scratch folder should be removed after the run");
        }

        [Test]
        [Category("RestoreTest")]
        public async Task ExistingDatabaseIsUsedByDefaultAsync()
        {
            var files = CreateSourceFiles(6);
            await RunBackupAsync();

            var results = await RunRestoreTestAsync(RestoreTestOptions("Full"));

            Assert.AreEqual(ParsedResultType.Success, results.ParsedResult, string.Join(Environment.NewLine, results.Errors.Concat(results.Warnings)));
            Assert.IsFalse(results.DatabaseRecreated);
            Assert.IsNull(results.RecreateDatabaseResults);
            Assert.AreEqual(files.Count, results.FilesTested);
            Assert.AreEqual(files.Count, results.FilesPassed);
        }

        [Test]
        [Category("RestoreTest")]
        public async Task SeedMakesSampleReproducibleAsync()
        {
            CreateSourceFiles(12);
            await RunBackupAsync();

            var options = RestoreTestOptions("RandomFiles");
            options["restore-test-sample-count"] = "4";
            options["restore-test-seed"] = "99";

            // The history records which files were picked, so two runs with the same seed must record the same paths
            var first = await RunRestoreTestAsync(options);
            Assert.AreEqual(ParsedResultType.Success, first.ParsedResult);
            var firstPaths = (await ReadHistoryAsync(DBFILE)).Select(x => x.Path).OrderBy(x => x).ToList();
            Assert.AreEqual(4, firstPaths.Count);

            var second = await RunRestoreTestAsync(options);
            Assert.AreEqual(ParsedResultType.Success, second.ParsedResult);
            var secondPaths = (await ReadHistoryAsync(DBFILE)).Select(x => x.Path).OrderBy(x => x).ToList();

            CollectionAssert.AreEqual(firstPaths, secondPaths, "The same seed should select the same files");
        }

        [Test]
        [Category("RestoreTest")]
        public async Task CorruptedDblockIsReportedWithPathsAndErrorExitCodeAsync()
        {
            var files = CreateSourceFiles(12);
            await RunBackupAsync();

            var dblocks = Directory.GetFiles(TARGETFOLDER, "*.dblock*").OrderBy(x => x).ToList();
            Assert.IsTrue(dblocks.Count > 1, "The backup should span several dblock files");

            // Damage one volume in place, keeping the size so the remote listing still matches the database
            var corrupted = dblocks[0];
            CorruptFileMiddle(corrupted, 2000);

            var corruptedName = Path.GetFileName(corrupted);
            var affected = await GetFilesInVolumeAsync(DBFILE, corruptedName);
            Assert.IsTrue(affected.Count > 0, "The damaged volume should hold data for at least one file");
            Assert.IsTrue(affected.Count < files.Count, "The damaged volume should not hold every file");

            // The run must produce a structured result, not an exception
            var results = await RunRestoreTestAsync(RestoreTestOptions("Full"));

            Assert.AreEqual(ParsedResultType.Error, results.ParsedResult);
            Assert.AreEqual(files.Count, results.FilesTested);
            Assert.IsTrue(results.FilesFailed >= affected.Count, "Every file in the damaged volume must fail");
            Assert.AreEqual(files.Count, results.FilesPassed + results.FilesFailed);
            Assert.AreEqual(results.FilesFailed, results.Failures.Count());

            foreach (var failure in results.Failures)
            {
                Assert.IsTrue(files.Contains(failure.Path), $"Unexpected failure path: {failure.Path}");
                Assert.IsTrue(failure.Reason is RestoreTestFailureReason.MissingRemoteVolume or RestoreTestFailureReason.HashMismatch or RestoreTestFailureReason.RestoreError, $"Unexpected reason: {failure.Reason}");
            }

            // The files stored in the damaged volume are reported against that volume
            foreach (var path in affected)
            {
                var failure = results.Failures.FirstOrDefault(x => x.Path == path);
                Assert.IsNotNull(failure, $"Expected a failure for {path}");
                Assert.AreEqual(RestoreTestFailureReason.MissingRemoteVolume, failure!.Reason, $"Unexpected reason for {path}: {failure.Reason}");
                Assert.IsTrue(failure.Expected.Contains(corruptedName), $"Expected the failure to name the damaged volume, got: {failure.Expected}");
            }

            // The command line reports the failure with the error exit code
            var code = RunCli(RestoreTestOptions("Full"), out var output);
            Assert.AreEqual(3, code, output);
            Assert.IsTrue(output.Contains("failed verification"), output);
        }

        [Test]
        [Category("RestoreTest")]
        public async Task ScratchFilesAndTemporaryDatabaseAreRemovedAsync()
        {
            CreateSourceFiles(6);
            await RunBackupAsync();

            var options = RestoreTestOptions("Full");
            options["restore-test-recreate-database"] = "true";
            var results = await RunRestoreTestAsync(options);
            Assert.AreEqual(ParsedResultType.Success, results.ParsedResult);
            Assert.IsTrue(results.DatabaseRecreated);
            Assert.IsFalse(ScratchFolders().Any(), "The scratch folder with the restored files and the temporary database should be removed");
        }

        [Test]
        [Category("RestoreTest")]
        public async Task DownloadBudgetProducesBudgetExceededResultAsync()
        {
            CreateSourceFiles(12);
            await RunBackupAsync();

            var options = RestoreTestOptions("Full");
            options["restore-test-max-download-size"] = "50kb";

            var results = await RunRestoreTestAsync(options);

            Assert.IsTrue(results.Budget.Exceeded, "The download budget should be exceeded");
            Assert.AreEqual(RestoreTestBudgetReason.DownloadSize, results.Budget.Reason);
            Assert.AreEqual(ParsedResultType.Error, results.ParsedResult);
            Assert.AreEqual(0, results.FilesPassed);
            Assert.AreEqual(0, results.FilesFailed);
            Assert.AreEqual(results.FilesTested, results.FilesSkipped);
            Assert.IsFalse(ScratchFolders().Any(), "The scratch folder should be removed after the run");

            var code = RunCli(options, out var output);
            Assert.AreEqual(3, code, output);
            Assert.IsTrue(output.Contains("Budget exceeded"), output);
        }

        [Test]
        [Category("RestoreTest")]
        public async Task RollingModeCoversEveryFileAcrossRunsAsync()
        {
            var files = CreateSourceFiles(9);
            await RunBackupAsync();

            for (var run = 1; run <= 3; run++)
            {
                var options = RestoreTestOptions("Rolling");
                options["restore-test-recreate-database"] = "true";
                options["restore-test-sample-count"] = "3";
                options["restore-test-rolling-window"] = "1D";
                options["restore-test-seed"] = run.ToString();

                var results = await RunRestoreTestAsync(options);
                Assert.AreEqual(ParsedResultType.Success, results.ParsedResult, string.Join(Environment.NewLine, results.Errors.Concat(results.Warnings)));
                Assert.AreEqual(3, results.FilesPassed);

                // The history lives in the real database although the restore used the temporary database
                var history = await ReadHistoryAsync(DBFILE);
                Assert.AreEqual(3 * run, history.Count, $"Each run should verify new files (run {run})");
                Assert.IsTrue(history.All(x => x.Result == "Passed"));
            }

            var verified = (await ReadHistoryAsync(DBFILE)).Select(x => x.Path).OrderBy(x => x).ToList();
            CollectionAssert.AreEquivalent(files, verified, "Every file should be verified once within the window");
        }

        [Test]
        [Category("RestoreTest")]
        public async Task LocalDatabaseContentIsUnchangedByRecreateAsync()
        {
            CreateSourceFiles(8);
            await RunBackupAsync();

            var before = await SnapshotContentTablesAsync(DBFILE);

            var options = RestoreTestOptions("Full");
            options["restore-test-recreate-database"] = "true";
            var results = await RunRestoreTestAsync(options);
            Assert.AreEqual(ParsedResultType.Success, results.ParsedResult);
            Assert.IsTrue(results.DatabaseRecreated);

            var after = await SnapshotContentTablesAsync(DBFILE);
            Assert.AreEqual(before, after, "The backup content in the local database must not change");

            // The verification history is the only addition to the local database
            Assert.AreEqual(8, (await ReadHistoryAsync(DBFILE)).Count);
        }

        [Test]
        [Category("RestoreTest")]
        public async Task CompareSourceReportsSilentlyModifiedSourceAsWarningAsync()
        {
            var files = CreateSourceFiles(6);
            await RunBackupAsync();

            // Change the content of one source file without changing its size or timestamp
            var modified = files[3];
            var timestamp = File.GetLastWriteTimeUtc(modified);
            var data = File.ReadAllBytes(modified);
            data[data.Length / 2] ^= 0xFF;
            File.WriteAllBytes(modified, data);
            File.SetLastWriteTimeUtc(modified, timestamp);

            var options = RestoreTestOptions("Full");
            options["restore-test-compare-source"] = "true";

            var results = await RunRestoreTestAsync(options);

            Assert.AreEqual(0, results.FilesFailed, "The restored files must still verify against the backup");
            Assert.AreEqual(files.Count, results.FilesPassed);
            Assert.AreEqual(1, results.SourceDifferences.Count());
            Assert.AreEqual(modified, results.SourceDifferences.First().Path);
            Assert.AreEqual(ParsedResultType.Warning, results.ParsedResult);
            Assert.IsTrue(results.Warnings.Any());

            var code = RunCli(options, out var output);
            Assert.AreEqual(2, code, output);
        }

        [Test]
        [Category("RestoreTest")]
        public async Task CommandLineReportsSuccessAsync()
        {
            CreateSourceFiles(6);
            await RunBackupAsync();

            var code = RunCli(RestoreTestOptions("RandomFiles"), out var output);
            Assert.AreEqual(0, code, output);
            Assert.IsTrue(output.Contains("Files tested"), output);
            Assert.IsTrue(output.Contains("verified successfully"), output);
        }

        [Test]
        [Category("RestoreTest")]
        public async Task RestoreTestRunsAfterBackupAtTheConfiguredIntervalAsync()
        {
            var files = CreateSourceFiles(6);
            var options = RestoreTestOptions("Full");
            options["perform-restore-test-after"] = "1h";

            async Task<IBackupResults> BackupAsync(Action<Controller>? setup = null)
            {
                // Change a file so every backup creates a new version
                File.AppendAllText(files[1], "x");
                using var c = new Controller("file://" + TARGETFOLDER, options, null);
                setup?.Invoke(c);
                var results = await c.BackupAsync([DATAFOLDER]);
                TestUtils.AssertResults(results);
                return results;
            }

            // No restore test has run yet, so the first backup runs one
            var backup = await BackupAsync();
            Assert.IsNotNull(backup.RestoreTestResults, "The first backup should run a restore test");
            Assert.AreEqual(files.Count, backup.RestoreTestResults!.FilesTested);
            Assert.AreEqual(files.Count, backup.RestoreTestResults.FilesPassed);
            Assert.AreEqual(ParsedResultType.Success, backup.ParsedResult);

            // The local database records the restore test, so the next backup within the interval skips it
            backup = await BackupAsync();
            Assert.IsNull(backup.RestoreTestResults, "A backup within the interval should skip the restore test");
            Assert.IsTrue(backup.Messages.Any(x => x.Contains("Skipping restore test")), string.Join(Environment.NewLine, backup.Messages));

            // A last restore test time supplied by the caller, as the server does, is honoured as well
            backup = await BackupAsync(c => c.SetLastRestoreTestAsync(DateTime.UtcNow.AddHours(-2)).Wait());
            Assert.IsNotNull(backup.RestoreTestResults, "A last restore test older than the interval should run a restore test");

            backup = await BackupAsync(c => c.SetLastRestoreTestAsync(DateTime.UtcNow).Wait());
            Assert.IsNull(backup.RestoreTestResults, "A recent last restore test should skip the restore test");

            // Once the interval has passed, the restore test runs again
            options["perform-restore-test-after"] = "1s";
            await Task.Delay(1500);
            backup = await BackupAsync();
            Assert.IsNotNull(backup.RestoreTestResults, "A backup after the interval should run a restore test");

            // Without the option, no restore test is run
            options.Remove("perform-restore-test-after");
            backup = await BackupAsync();
            Assert.IsNull(backup.RestoreTestResults, "A backup without the option should not run a restore test");
        }

        [Test]
        [Category("RestoreTest")]
        public async Task FailingRestoreTestAfterBackupMakesTheBackupAnErrorAsync()
        {
            CreateSourceFiles(6);
            await RunBackupAsync();

            // Damage a volume of the first version, then back up a new file so the next backup completes on its own
            var dblocks = Directory.GetFiles(TARGETFOLDER, "*.dblock*").OrderBy(x => x).ToList();
            CorruptFileMiddle(dblocks[0], 2000);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "extra.bin"), new byte[] { 1, 2, 3 });

            var options = RestoreTestOptions("Full");
            options["perform-restore-test-after"] = "1h";

            using var c = new Controller("file://" + TARGETFOLDER, options, null);
            var backup = await c.BackupAsync([DATAFOLDER]);

            Assert.IsNotNull(backup.RestoreTestResults);
            Assert.IsTrue(backup.RestoreTestResults!.FilesFailed > 0, "The damaged volume should fail the restore test");
            Assert.AreEqual(ParsedResultType.Error, backup.RestoreTestResults.ParsedResult);
            Assert.AreEqual(ParsedResultType.Error, backup.ParsedResult, "A failed restore test should make the backup an error");
        }

        [Test]
        [Category("RestoreTest")]
        public async Task IncludeFilterLimitsCandidatesAsync()
        {
            var files = CreateSourceFiles(9);
            await RunBackupAsync();

            var options = RestoreTestOptions("Full");
            options["restore-test-include"] = "*" + Path.DirectorySeparatorChar + "deeper" + Path.DirectorySeparatorChar + "*";

            var results = await RunRestoreTestAsync(options);
            Assert.AreEqual(ParsedResultType.Success, results.ParsedResult, string.Join(Environment.NewLine, results.Errors.Concat(results.Warnings)));

            var expected = files.Count(x => x.Contains(Path.DirectorySeparatorChar + "deeper" + Path.DirectorySeparatorChar));
            Assert.IsTrue(expected > 0);
            Assert.AreEqual(expected, results.FilesTested);
            Assert.AreEqual(expected, results.FilesPassed);
        }
    }
}
