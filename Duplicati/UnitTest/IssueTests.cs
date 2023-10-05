using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class IssueTests : BasicSetupHelper
    {

        [Test]
        [Category("Targeted"), Category("Bug"), Explicit("Known bug")]
        public void Issue5023ReferencedFileMissing([Values] bool compactBeforeRecreate)
        {
            // Reproduction for part of issue #5023
            // Error during repair: "Remote file referenced as x by y, but not found in list, registering a missing remote file"
            // Can be caused by interrupted index upload followed by compact before the repair

            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["number-of-retries"] = "0",
                ["dblock-size"] = "20KB",
                ["threshold"] = "1",
                ["keep-versions"] = "1",
                ["no-auto-compact"] = "true",
                ["no-encryption"] = "true"
            };

            const long filesize = 1024;

            // First backup OK to create valid database
            string target = "file://" + TARGETFOLDER;
            string file1 = Path.Combine(DATAFOLDER, "f1");
            TestUtils.WriteTestFile(file1, filesize);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }
            // Sleep to ensure timestamps are different
            Thread.Sleep(1000);
            // Fail after index file put
            target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            string interruptedName = "";
            DeterministicErrorBackend.ErrorGenerator = (string action, string remotename) =>
            {
                // Fail dindex upload
                if (action.StartsWith("put") && remotename.Contains("dindex"))
                {
                    interruptedName = remotename;
                    return true;
                }
                return false;
            };
            string file2 = Path.Combine(DATAFOLDER, "f2");
            TestUtils.WriteTestFile(file2, filesize);
            Assert.Catch(() =>
            {
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                }
            });
            Console.WriteLine("Interrupted after upload of {0}", interruptedName);
            // Sleep to ensure timestamps are different
            Thread.Sleep(1000);
            // Complete upload
            target = "file://" + TARGETFOLDER;
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }
            // At this point there are two index files for the last dblock
            Console.WriteLine("Target folder contents (expect extra index file):");
            Console.WriteLine(string.Join("\n", from v in Directory.EnumerateFiles(TARGETFOLDER) select Path.GetFileName(v)));

            // Note: If there is a recreate here (between the above extra file creation and the following compact),
            // the bug does not occur as the extra index file will be recorded in the database and properly deleted by compact

            if (compactBeforeRecreate)
            {
                // Delete f2 and compact
                File.Delete(file2);
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                    TestUtils.AssertResults(backupResults);
                    ICompactResults compactResults = c.Compact();
                    TestUtils.AssertResults(compactResults);
                    Assert.Greater(compactResults.DeletedFileCount, 0);
                }
                // At this point there are two index files for the last dblock
                Console.WriteLine("Target folder contents (expect extra index file):");
                Console.WriteLine(string.Join("\n", from v in Directory.EnumerateFiles(TARGETFOLDER) select Path.GetFileName(v)));
            }

            // Database recreate should work (fails after compact)
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }
        }


        [Test]
        [Category("Disruption"), Category("Bug"), Explicit("Known bug")]
        public void TestSystematicErrors()
        {
            // Attempt to recreate other bugs from #5023, but not successful
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["number-of-retries"] = "0",
                ["dblock-size"] = "20KB",
                ["threshold"] = "1",
                ["keep-versions"] = "5",
                ["no-encryption"] = "true",
                ["disable-synthetic-filelist"] = "true"
            };
            //testopts["rebuild-missing-dblock-files"] = "true";
            string target = "file://" + TARGETFOLDER;
            string targetError = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            int maxFiles = 10;
            List<string> files = new List<string>();
            bool failed = false;
            long accessCounter = 0;
            long errorIdx = 0;
            DeterministicErrorBackend.ErrorGenerator = (string action, string remotename) =>
            {
                ++accessCounter;
                if (accessCounter >= errorIdx)
                {
                    return true;
                }
                return false;
            };
            for (int i = 0; i < maxFiles; ++i)
            {
                string f = Path.Combine(DATAFOLDER, "f" + i);
                TestUtils.WriteTestFile(f, 1024 * 20);
                files.Add(f);
            }
            // Initial backup
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }
            while (errorIdx < (maxFiles + 2))
            {
                if (errorIdx % 10 == 0)
                {
                    TestContext.WriteLine("Error index {0}", errorIdx);
                }
                accessCounter = 0;
                try
                {
                    using (var c = new Library.Main.Controller(targetError, testopts, null))
                    {
                        IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                        TestUtils.AssertResults(backupResults);
                    }
                }
                catch (AssertionException) { throw; }
                catch { }
                Thread.Sleep(1000);
                try
                {
                    using (var c = new Library.Main.Controller(target, testopts, null))
                    {
                        IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                        TestUtils.AssertResults(backupResults);
                    }
                }
                catch (UserInformationException e)
                {
                    TestContext.WriteLine("Error at index {0}: {1}", errorIdx, e.Message);
                    if (e.HelpID == "MissingRemoteFiles" || e.HelpID == "ExtraRemoteFiles")
                    {
                        using (var c = new Library.Main.Controller(target, testopts, null))
                        {
                            IRepairResults repairResults = c.Repair();
                            TestUtils.AssertResults(repairResults);
                        }
                    }
                    failed = true;
                }
                Thread.Sleep(1000);
                foreach (string f in files)
                {
                    TestUtils.WriteTestFile(f, 1024 * 20);
                }
                ++errorIdx;
            }
            TestContext.WriteLine("Ran {0} iterations", errorIdx);
            Assert.IsFalse(failed);
        }

        [Test, Sequential]
        [Category("Targeted"), Category("Bug"), Category("Non-critical"), Explicit("Known bug")]
        [TestCase(false, true), TestCase(true, true), TestCase(true, false)]
        public void Issue5038MissingListBlocklist(bool sameVersion, bool blockFirst)
        {
            // Backup containing the blocklist of a file BEFORE the file causes a dindex with missing blocklist entry
            // This is not critical, because it only requires extra block volume downloads
            var testopts = new Dictionary<string, string>(TestOptions)
            {
                ["no-encryption"] = "true"
            };

            string filename = Path.Combine(DATAFOLDER, "file");
            // Start with z to process blockfile after file (at least on some systems)
            string blockfile = Path.Combine(DATAFOLDER, blockFirst ? "block" : "zblock");
            string target = "file://" + TARGETFOLDER;

            byte[] block1 = new byte[10 * 1024];
            for (int i = 0; i < block1.Length; ++i)
            {
                block1[i] = 1;
            }
            byte[] block2 = new byte[10 * 1024];
            for (int i = 0; i < block1.Length; ++i)
            {
                block1[i] = 2;
            }

            HashAlgorithm blockhasher = Library.Utility.HashAlgorithmHelper.Create(new Options(testopts).BlockHashAlgorithm);

            var hash1 = blockhasher.ComputeHash(block1, 0, block1.Length);
            var hash2 = blockhasher.ComputeHash(block2, 0, block2.Length);

            byte[] blockfileContent = hash1.Concat(hash2).ToArray();
            TestUtils.WriteFile(blockfile, blockfileContent);
            if (!sameVersion)
            {
                // Backup blockfile first
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                    TestUtils.AssertResults(backupResults);
                }
            }

            byte[] combined = block1.Concat(block2).ToArray();
            TestUtils.WriteFile(filename, combined);
            // Backup file that would produce blockfile
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            // Recreate database downloads block volume
            File.Delete(DBFILE);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
                Assert.IsNull(repairResults.Messages.FirstOrDefault(v => v.Contains("ProcessingRequiredBlocklistVolumes")),
                    "Blocklist download pass was required");
            }
        }
    }
}
