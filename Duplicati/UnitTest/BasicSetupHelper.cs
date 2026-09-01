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
using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Duplicati.Library.Common.IO;
using NUnit.Framework.Interfaces;
using System.Timers;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Assembly-wide setup that runs once before any test in this assembly.
    /// Ensures a stable machine ID is present in the data folder, so the DEBUG guard
    /// in <see cref="Library.AutoUpdater.DataFolderManager"/> does not trip when a test
    /// reads the ID without going through the application startup sequence that
    /// normally initializes the data folder.
    /// </summary>
    [SetUpFixture]
    public class GlobalTestSetup
    {
        /// <summary>
        /// The machine ID used by the test suite
        /// </summary>
        internal const string TestMachineId = "unittestid";

        [OneTimeSetUp]
        public void RunBeforeAnyTests()
        {
            Library.AutoUpdater.DataFolderManager.SetMachineIDForTesting(TestMachineId);

            // Seed the machine id file in the data folder so processes spawned by tests
            // (e.g. the IPC controller process) inherit a valid machine ID via the
            // DUPLICATI_HOME environment; such processes cannot see the in-process
            // SetMachineIDForTesting value.
            try
            {
                var dataFolder = Library.AutoUpdater.DataFolderManager.GetDataFolder(Library.AutoUpdater.DataFolderManager.AccessMode.ProbeOnly);
                if (!Directory.Exists(dataFolder))
                {
                    // Create the folder through the regular startup path so it gets the
                    // canonical locked-down permissions and both id files; a plainly created
                    // folder would later be rejected by the secure-folder verification.
                    Library.AutoUpdater.DataFolderManager.GetDataFolder(Library.AutoUpdater.DataFolderManager.AccessMode.ReadWritePermissionSet);
                }
                else
                {
                    var machineIdPath = Path.Combine(dataFolder, "machineid.txt");
                    if (!File.Exists(machineIdPath))
                        File.WriteAllText(machineIdPath, TestMachineId);
                }
            }
            catch
            {
                // Best-effort: if the data folder cannot be written, the in-process
                // fallback above still protects the tests running in this process.
            }
        }
    }

    public abstract class BasicSetupHelper
    {
        /// <summary>
        /// The base folder where all data is trashed around
        /// </summary>
        protected static readonly string BASEFOLDER = Path.GetFullPath(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITTEST_BASEFOLDER"))
            ? "duplicati_testdata"
            : Environment.GetEnvironmentVariable("UNITTEST_BASEFOLDER")
            );

        /// <summary>
        /// The folder path that serves as the backup destination
        /// </summary>
        protected readonly string TARGETFOLDER = TestUtils.GetDefaultTarget(Path.Combine(BASEFOLDER, "autotest"));

        /// <summary>
        /// The folder that contains data to be backed up
        /// </summary>
        protected readonly string DATAFOLDER = Path.Combine(BASEFOLDER, "backup-data");

        /// <summary>
        /// The folder where data is restored into
        /// </summary>
        protected readonly string RESTOREFOLDER = Path.Combine(BASEFOLDER, "restored");
        /// <summary>
        /// The log file for manual examination
        /// </summary>
        protected readonly string LOGFILE = Path.Combine(BASEFOLDER, "logs/logfile.log");
        /// <summary>
        /// The database is fixed so it does not mess up the system where the test is performed
        /// </summary>
        protected readonly string DBFILE = Path.Combine(BASEFOLDER, "autotest.sqlite");

        /// <summary>
        /// Value indicating if all output is redirected to TestContext.Progress,
        /// this can be used to diagnose errors on a CI build instance by setting
        /// the environment variable DEBUG_OUTPUT=1 and running the job
        /// </summary>
        public static readonly bool DEBUG_OUTPUT = Library.Utility.Utility.ParseBool(Environment.GetEnvironmentVariable("DEBUG_OUTPUT"), false);

        protected static readonly ISystemIO systemIO = SystemIO.IO_OS;

        /// <summary>
        /// Writes a message to TestContext.Progress and Console.Out
        /// </summary>
        /// <param name="msg">The string to write.</param>
        /// <param name="args">The passed arguments.</param>
        public static void ProgressWriteLine(string msg, params object[] args)
        {
            if (!DEBUG_OUTPUT)
                TestContext.Progress.WriteLine(msg, args);
            Console.WriteLine("==> " + msg, args);
        }

        [OneTimeSetUp]
        public void BasicHelperOneTimeSetUp()
        {
            TestContext.Progress.WriteLine("One Time Setup {0}", TestContext.CurrentContext.Test.Name);
            if (DEBUG_OUTPUT)
            {
                Console.SetOut(TestContext.Progress);
            }

            systemIO.DirectoryCreate(BASEFOLDER);
            this.BasicHelperTearDown();
        }

        private const int RetainedFailedTestLogs = 10;
        private static int logArchiveSequence;


        private static Timer SetupTimer()
        {
            var timer = new Timer();

            timer.Interval = 2000;
            timer.Elapsed += delegate (object obj, ElapsedEventArgs e)
            {
                var nogc = GC.GetTotalMemory(false) / 1000 / 1000;
                var yesgc = GC.GetTotalMemory(true) / 1000 / 1000;

                var me = Process.GetCurrentProcess();
                var process = me.WorkingSet64 / 1000 / 1000;
                TestContext.Progress.WriteLine("Memory: {0}MB -> {1}MB ({2}MB)", nogc, yesgc, process);
            };
            timer.AutoReset = true;
            return timer;
        }
        private Timer memoryTimer = SetupTimer();

        [SetUp]
        public void BasicHelperSetUp()
        {
            memoryTimer.Enabled = true;
            var me = Process.GetCurrentProcess();
            TestContext.Progress.WriteLine("Setup {0} {1}MB {2}MB", TestContext.CurrentContext.Test.Name, GC.GetTotalMemory(true) / 1000 / 1000, me.WorkingSet64 / 1000 / 1000);
            systemIO.DirectoryCreate(this.DATAFOLDER);
            systemIO.DirectoryCreate(this.TARGETFOLDER);
            systemIO.DirectoryCreate(this.RESTOREFOLDER);
        }

        [TearDown]
        public void BasicHelperTearDown()
        {
            memoryTimer.Enabled = false;
            if (TestContext.CurrentContext.Test.MethodName != null)
            {
                var me = Process.GetCurrentProcess();
                TestContext.Progress.WriteLine("TearDown {0} {1}MB {2}MB", TestContext.CurrentContext.Test.MethodName, GC.GetTotalMemory(true) / 1000 / 1000, me.WorkingSet64 / 1000 / 1000);
            }
            if (systemIO.DirectoryExists(this.DATAFOLDER))
            {
                systemIO.DirectoryDelete(this.DATAFOLDER, true);
            }
            if (systemIO.DirectoryExists(this.TARGETFOLDER))
            {
                systemIO.DirectoryDelete(this.TARGETFOLDER, true);
            }
            if (systemIO.DirectoryExists(this.RESTOREFOLDER))
            {
                systemIO.DirectoryDelete(this.RESTOREFOLDER, true);
            }
            if (systemIO.FileExists(this.DBFILE))
            {
                systemIO.FileDelete(this.DBFILE);
            }
            if (systemIO.FileExists($"{this.DBFILE}-journal"))
            {
                systemIO.FileDelete($"{this.DBFILE}-journal");
            }
            if (systemIO.FileExists(this.LOGFILE))
            {
                CleanupLogFile(
                    this.LOGFILE,
                    TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed,
                    RetainedFailedTestLogs
                );
            }
        }

        internal static string CleanupLogFile(string logfile, bool retain, int retainedLogCount)
        {
            if (!File.Exists(logfile))
                return null;

            string archivePath = null;
            try
            {
                if (!retain)
                    return null;

                var archiveSequence = System.Threading.Interlocked.Increment(ref logArchiveSequence);
                archivePath = $"{logfile}.{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.{archiveSequence:D10}.gz";
                using (var source = new FileStream(logfile, FileMode.Open, FileAccess.Read))
                using (var destination = new FileStream(archivePath, FileMode.Create, FileAccess.Write))
                using (var gzip = new GZipStream(destination, CompressionMode.Compress))
                    source.CopyTo(gzip);

                var logDirectory = Path.GetDirectoryName(logfile);
                var logPattern = $"{Path.GetFileName(logfile)}.*.gz";
                foreach (var expiredArchive in Directory.GetFiles(logDirectory, logPattern)
                    .OrderByDescending(x => x, StringComparer.Ordinal)
                    .Skip(retainedLogCount))
                    File.Delete(expiredArchive);

                return archivePath;
            }
            finally
            {
                File.Delete(logfile);
            }
        }

        protected virtual Dictionary<string, string> TestOptions
        {
            get
            {
                var opts = TestUtils.DefaultOptions;
                //opts["blockhash-lookup-memory"] = "0";
                //opts["filehash-lookup-memory"] = "0";
                //opts["metadatahash-lookup-memory"] = "0";
                //opts["disable-filepath-cache"] = "true";

                opts["passphrase"] = "123456";
                opts["debug-output"] = "true";
                opts["log-file-log-level"] = nameof(Library.Logging.LogMessageType.Profiling);
                opts["log-file"] = LOGFILE;
                opts["dblock-size"] = "10mb";
                opts["dbpath"] = DBFILE;
                opts["blocksize"] = "10kb";
                opts["backup-test-samples"] = "0";
#if DEBUG
                // The "unittest-mode" option only exists in DEBUG builds,
                // passing it to a release build causes an unsupported option warning
                opts["unittest-mode"] = "true";
#endif
                opts["allow-sleep"] = "true";
                opts["snapshot-policy"] = "Off";

                return opts;
            }
        }

        /// <summary>
        /// Alternative to System.IO.Compression.ZipFile.ExtractToDirectory()
        /// that handles long paths.
        /// </summary>
        protected static void ZipFileExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName)
        {
            if (OperatingSystem.IsWindows())
            {
                // Handle long paths under Windows by extracting to a
                // temporary file and moving the resulting file to the
                // actual destination using functions that support
                // long paths.
                using (var archive = ZipFile.OpenRead(sourceArchiveFileName))
                {
                    foreach (var entry in archive.Entries)
                    {
                        // By the ZIP spec, directories end in a forward slash
                        var isDirectory = entry.FullName.EndsWith("/");
                        var destination =
                            systemIO.PathGetFullPath(systemIO.PathCombine(destinationDirectoryName, entry.FullName));
                        if (isDirectory)
                        {
                            systemIO.DirectoryCreate(destination);
                        }
                        else
                        {
                            // Not every directory is recorded separately,
                            // so create directories if needed
                            systemIO.DirectoryCreate(systemIO.PathGetDirectoryName(destination));
                            // Extract file to temporary file, then move to
                            // the (possibly) long path destination
                            var tempFile = Path.GetTempFileName();
                            try
                            {
                                entry.ExtractToFile(tempFile, true);
                                systemIO.FileMove(tempFile, destination);
                            }
                            finally
                            {
                                if (systemIO.FileExists(tempFile))
                                {
                                    systemIO.FileDelete(tempFile);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                ZipFile.ExtractToDirectory(sourceArchiveFileName, destinationDirectoryName);
            }
        }
    }
}

