// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Interface;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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

        /// <summary>
        /// Each backup run needs a unique timestamp to avoid the same backup being created.
        /// During testing we often need to run backups in quick succession, so we need to
        /// sleep until the next second to make sure the next backup timestamp is not the same.
        /// </summary>
        /// <param name="prevTimestamp">The timestamp of the previous backup</param>
        private void SleepUntilNextSecond(DateTime prevTimestamp)
        {
            // Sleep until the next second to make sure the next backup is not the same
            var nextSecond = prevTimestamp.AddMilliseconds(-prevTimestamp.Millisecond).AddSeconds(2);
            var remainder = Math.Max(2000, (nextSecond - DateTime.Now).TotalMilliseconds);
            if (remainder > 0)
            {
                System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(remainder));
            }
        }

        /// <summary>
        /// Each backup run needs a unique timestamp to avoid the same backup being created.
        /// During testing we often need to run backups in quick succession, so we need to
        /// sleep until the next second to make sure the next backup timestamp is not the same.
        /// </summary>
        /// <param name="backupResults">The results of the previous backup</param>
        private void SleepUntilNextSecond(IBackupResults backupResults)
            => SleepUntilNextSecond(backupResults.BeginTime);

        [Test]
        [Category("Disruption"), Category("Bug")]
        public void InterruptedCompact5184()
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
                SleepUntilNextSecond(backupResults);

                // Add files 3,4
                TestUtils.WriteTestFile(file3, filesize);
                TestUtils.WriteTestFile(file4, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Add files 5,6
                TestUtils.WriteTestFile(file5, filesize);
                TestUtils.WriteTestFile(file6, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Delete 1,3,5
                File.Delete(file1);
                File.Delete(file3);
                File.Delete(file5);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            // Fail the compact after the first dblock put is completed
            bool firstPutCompleted = false;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (firstPutCompleted && action.IsGetOperation)
                {
                    return true;
                }
                if (action == DeterministicErrorBackend.BackendAction.PutAfter)
                {
                    firstPutCompleted = true;
                }
                return false;
            };
            // Expect error from backend
            Assert.Catch<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
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
            target = "file://" + TARGETFOLDER;
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }

            // Remove the local database
            File.Delete(DBFILE);

            // Repair to recreate the local database
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }

            // Re-do the full verification
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }
        }

        [Test]
        [Category("Disruption"), Category("Bug")]
        public void InterruptedCompactPlusNormalCompact5184()
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
                SleepUntilNextSecond(backupResults);

                // Add files 3,4
                TestUtils.WriteTestFile(file3, filesize);
                TestUtils.WriteTestFile(file4, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Add files 5,6
                TestUtils.WriteTestFile(file5, filesize);
                TestUtils.WriteTestFile(file6, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Delete 1,3,5
                File.Delete(file1);
                File.Delete(file3);
                File.Delete(file5);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            // Fail the compact after the first dblock put is completed
            bool firstPutCompleted = false;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (firstPutCompleted && action.IsGetOperation)
                {
                    return true;
                }
                if (action == DeterministicErrorBackend.BackendAction.PutAfter)
                {
                    firstPutCompleted = true;
                }
                return false;
            };
            // Expect error from backend
            Assert.Catch<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
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

            target = "file://" + TARGETFOLDER;
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }

            // Make sure we can compact after the interrupted compact
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ICompactResults compactResults = c.Compact();
                Assert.Greater(compactResults.DownloadedFileCount, 0, "No compact operation was performed");
            }

            // Make sure there are no errors after success compacting
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }

            // Remove the local database
            File.Delete(DBFILE);

            // Repair to recreate the local database
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }

            // Re-do the full verification
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }
        }

        [Test]
        [Category("Disruption"), Category("Bug")]
        public void DoubleInterruptedCompact5184()
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
                SleepUntilNextSecond(backupResults);

                // Add files 3,4
                TestUtils.WriteTestFile(file3, filesize);
                TestUtils.WriteTestFile(file4, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Add files 5,6
                TestUtils.WriteTestFile(file5, filesize);
                TestUtils.WriteTestFile(file6, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Delete 1,3,5
                File.Delete(file1);
                File.Delete(file3);
                File.Delete(file5);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            // Fail the compact after the first dblock put is completed
            bool firstPutCompleted = false;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (firstPutCompleted && action.IsGetOperation)
                {
                    return true;
                }
                if (action == DeterministicErrorBackend.BackendAction.PutAfter)
                {
                    firstPutCompleted = true;
                }
                return false;
            };
            // Expect error from backend
            Assert.Catch<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
            {
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    ICompactResults compactResults = c.Compact();
                    Assert.Greater(compactResults.DownloadedFileCount, 0, "No compact operation was performed");
                }
            });

            // Expect error from backend, again
            Assert.Catch<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
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

            // Make sure we can compact after two interrupted compacts
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ICompactResults compactResults = c.Compact();
                Assert.Greater(compactResults.DownloadedFileCount, 0, "No compact operation was performed");
            }

            // Make sure there are no errors after success compacting
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }

            // Remove the local database
            File.Delete(DBFILE);

            // Repair to recreate the local database
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }

            // Re-do the full verification
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }
        }

        [Test]
        [Category("Disruption"), Category("Bug")]
        public void RestoreAfterDoubleInterruptedCompact5184()
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
                SleepUntilNextSecond(backupResults);

                // Add files 3,4
                TestUtils.WriteTestFile(file3, filesize);
                TestUtils.WriteTestFile(file4, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Add files 5,6
                TestUtils.WriteTestFile(file5, filesize);
                TestUtils.WriteTestFile(file6, filesize);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                SleepUntilNextSecond(backupResults);

                // Delete 1,3,5
                File.Delete(file1);
                File.Delete(file3);
                File.Delete(file5);
                backupResults = c.Backup(new[] { DATAFOLDER });
                TestUtils.AssertResults(backupResults);
            }

            // Deterministic error backend
            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
            // Fail the compact after the first dblock put is completed
            bool firstPutCompleted = false;
            DeterministicErrorBackend.ErrorGenerator = (DeterministicErrorBackend.BackendAction action, string remotename) =>
            {
                if (firstPutCompleted && action.IsGetOperation)
                {
                    return true;
                }
                if (action == DeterministicErrorBackend.BackendAction.PutAfter)
                {
                    firstPutCompleted = true;
                }
                return false;
            };
            // Expect error from backend
            Assert.Catch<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
            {
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    ICompactResults compactResults = c.Compact();
                    Assert.Greater(compactResults.DownloadedFileCount, 0, "No compact operation was performed");
                }
            });

            // Expect error from backend, again
            Assert.Catch<DeterministicErrorBackend.DeterministicErrorBackendException>(() =>
            {
                using (var c = new Library.Main.Controller(target, testopts, null))
                {
                    ICompactResults compactResults = c.Compact();
                    Assert.Greater(compactResults.DownloadedFileCount, 0, "No compact operation was performed");
                }
            });

            DeterministicErrorBackend.ErrorGenerator = null;
            target = "file://" + TARGETFOLDER;
            testopts["full-remote-verification"] = "true";
            testopts["full-block-verification"] = "true";
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }

            // Remove the local database
            File.Delete(DBFILE);

            // Repair to recreate the local database
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                IRepairResults repairResults = c.Repair();
                TestUtils.AssertResults(repairResults);
            }

            // Re-do the full verification
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ITestResults testResults = c.Test(long.MaxValue);
                // Expect no verification errors
                Assert.IsTrue(testResults.Verifications.All(v => !v.Value.Any()),
                    "Test verification failed:\n {0}", VerificationToString(testResults.Verifications));
            }

            // Compact without issues on the repaired database
            using (var c = new Library.Main.Controller(target, testopts, null))
            {
                ICompactResults compactResults = c.Compact();
                Assert.Greater(compactResults.DownloadedFileCount, 0, "No compact operation was performed");
            }

            // Make sure there are no errors after success compacting
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
