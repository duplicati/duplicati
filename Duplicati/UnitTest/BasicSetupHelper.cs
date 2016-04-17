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
            ? Library.Utility.Utility.ExpandEnvironmentVariables(Path.Combine("~", "testdata"))
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

        [TestFixtureSetUp()]
        public virtual void PrepareSourceData()
        {
            Console.WriteLine("Deleting backup data and log...");
            if (Directory.Exists(DATAFOLDER))
                Directory.Delete(DATAFOLDER, true);
            if (File.Exists(LOGFILE))
                File.Delete(LOGFILE);
            Console.WriteLine("Deleting older data");
            if (File.Exists(DBFILE))
                File.Delete(DBFILE);
            if (Directory.Exists(TARGETFOLDER))
                Directory.Delete(TARGETFOLDER, true);
        }


        protected virtual Dictionary<string, string> TestOptions
        {
            get
            {
                var opts = TestUtils.DefaultOptions;
                //opts["blockhash-lookup-memory"] = "0";
                //opts["filehash-lookup-memory"] = "0";
                //opts["metadatahash-lookup-memory"] = "0";
                //opts["disable-filepath-cache"] = "";

                opts["passphrase"] = "123456";
                opts["debug-output"] = "";
                opts["log-level"] = "profiling";
                opts["log-file"] = LOGFILE;
                opts["dblock-size"] = "10mb";
                opts["dbpath"] = DBFILE;
                opts["blocksize"] = "10kb";
                opts["backup-test-samples"] = "0";
                opts["keep-versions"] = "100";

                return opts;
            }
        }

    }
}

