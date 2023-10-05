using Duplicati.Library.Interface;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class CompactDisruptionTests : BasicSetupHelper
    {
        private string VerificationToString(IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<TestEntryStatus, string>>>> verifications)
        {
            StringBuilder res = new StringBuilder();
            foreach (var file in verifications)
            {
                if (file.Value.Any())
                {
                    res.AppendLine($"{file.Key}: {file.Value.Count()} errors");
                    foreach (var c in file.Value)
                    {
                        res.AppendLine($"\t{c.Key}: {c.Value}");
                    }
                }
            }
            return res.ToString();
        }

        [Test]
        [Category("Disruption"), Category("Bug"), Explicit("Known bug")]
        public void InterruptedCompact()
        {
            // Reproduction steps from issue #4129 with smaller sizes
            var testopts = TestOptions;
            testopts["backup-test-samples"] = "0";
            testopts["number-of-retries"] = "0";
            testopts["dblock-size"] = "20KB";
            // Reduce blocksize, because with default blocksize too few blocks fit in a volume
            testopts["blocksize"] = "1KB";
            testopts["keep-versions"] = "1";
            testopts["no-auto-compact"] = "true";
            testopts["threshold"] = "5";

            // This size seems to be appropriate to cause significant compacting
            // in combination with dblock-size
            const long filesize = 44971;

            string target = "file://" + TARGETFOLDER;
            string file1 = Path.Combine(DATAFOLDER, "f1");
            string file2 = Path.Combine(DATAFOLDER, "f2");
            string file3 = Path.Combine(DATAFOLDER, "f3");
            string file4 = Path.Combine(DATAFOLDER, "f4");
            string file5 = Path.Combine(DATAFOLDER, "f5");
            string file6 = Path.Combine(DATAFOLDER, "f6");
            // Add files 1,2
            TestUtils.WriteTestFile(file1, filesize);
            TestUtils.WriteTestFile(file2, filesize);
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IBackupResults backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);

                // Add files 3,4
                TestUtils.WriteTestFile(file3, filesize);
                TestUtils.WriteTestFile(file4, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);

                // Add files 5,6
                TestUtils.WriteTestFile(file5, filesize);
                TestUtils.WriteTestFile(file6, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);

                // Delete 1,3,5
                File.Delete(file1);
                File.Delete(file3);
                File.Delete(file5);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            // Deterministic error backend
            target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            // Fail the compact after the first dblock put is completed
            bool firstPutCompleted = false;
            DeterministicErrorBackend.ErrorGenerator = (string action, string remotename) =>
            {
                if (firstPutCompleted && (action == "get_0" || action == "get_1"))
                {
                    return true;
                }
                if (action == "put_1" || action == "put_async")
                {
                    firstPutCompleted = true;
                }
                return false;
            };
            // Expect error from backend
            Assert.Catch(() =>
            {
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    ICompactResults compactResults = c.Compact();
                    Assert.Greater(compactResults.DownloadedFileCount, 0, "No compact operation was performed");
                }
            });
            DeterministicErrorBackend.ErrorGenerator = null;
            testopts["full-remote-verification"] = "true";
            testopts["full-block-verification"] = "true";
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }
        }
    }
}
