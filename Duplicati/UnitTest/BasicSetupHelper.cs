// Copyright (C) 2024, The Duplicati Team
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
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest
{
    public abstract class BasicSetupHelper
    {
        /// <summary>
        /// The base folder where all data is trashed around
        /// </summary>
        protected static readonly string BASEFOLDER =
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITTEST_BASEFOLDER"))
            ? Path.Combine(Library.Utility.Utility.HOME_PATH, "duplicati_testdata")
            : Environment.GetEnvironmentVariable("UNITTEST_BASEFOLDER");

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
        protected readonly string LOGFILE = Path.Combine(BASEFOLDER, "logfile.log");
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
        public virtual void OneTimeSetUp()
        {
            if (DEBUG_OUTPUT)
            {
                Console.SetOut(TestContext.Progress);
            }

            systemIO.DirectoryCreate(BASEFOLDER);
            this.TearDown();
            this.OneTimeTearDown();
        }

        [OneTimeTearDown]
        public virtual void OneTimeTearDown()
        {
            // No-op by default.
        }

        [SetUp]
        public virtual void SetUp()
        {
            systemIO.DirectoryCreate(this.DATAFOLDER);
            systemIO.DirectoryCreate(this.TARGETFOLDER);
            systemIO.DirectoryCreate(this.RESTOREFOLDER);
        }

        [TearDown]
        public virtual void TearDown()
        {
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
            if (systemIO.FileExists(this.LOGFILE))
            {
                systemIO.FileDelete(this.LOGFILE);
            }
            if (systemIO.FileExists(this.DBFILE))
            {
                systemIO.FileDelete(this.DBFILE);
            }
            if (systemIO.FileExists($"{this.DBFILE}-journal"))
            {
                systemIO.FileDelete($"{this.DBFILE}-journal");
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
                opts["unittest-mode"] = "true";

                return opts;
            }
        }

        /// <summary>
        /// Alternative to System.IO.Compression.ZipFile.ExtractToDirectory()
        /// that handles long paths.
        /// </summary>
        protected static void ZipFileExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName)
        {
            if (Platform.IsClientWindows)
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

