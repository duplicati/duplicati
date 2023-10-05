using Duplicati.Library.Interface;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class IssueTests : BasicSetupHelper
    {

        [Test]
        [Category("Targeted")]
        [TestCase(true), TestCase(false)]
        public void Issue5023ReferencedFileMissing(bool compactBeforeRecreate)
        {
            // Reproduction for part of issue #5023
            // Error during repair: "Remote file referenced as x by y, but not found in list, registering a missing remote file"
            // Can be caused by interrupted index upload followed by compact before the repair

            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "20KB";
            testopts["threshold"] = "1";
            testopts["keep-versions"] = "1";
            testopts["no-auto-compact"] = "true";
            testopts["no-encryption"] = "true";
            testopts.Remove("passphrase");

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
    }
}
