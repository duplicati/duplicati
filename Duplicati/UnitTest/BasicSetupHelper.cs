//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using NUnit.Framework;
using System.IO;
using System.Collections.Generic;

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
        /// The log file for manual examiniation
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

            Directory.CreateDirectory(BASEFOLDER);
            this.TearDown();
        }

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(this.DATAFOLDER);
            Directory.CreateDirectory(this.TARGETFOLDER);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.DATAFOLDER))
            {
                Directory.Delete(this.DATAFOLDER, true);
            }
            if (Directory.Exists(this.TARGETFOLDER))
            {
                Directory.Delete(this.TARGETFOLDER, true);
            }
            if (File.Exists(this.LOGFILE))
            {
                File.Delete(this.LOGFILE);
            }
            if (File.Exists(this.DBFILE))
            {
                File.Delete(this.DBFILE);
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
                opts["keep-versions"] = "100";
                opts["unittest-mode"] = "true";

                return opts;
            }
        }

    }
}

