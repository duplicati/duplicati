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

using System.IO;
using NUnit.Framework;
using System.Linq;
using System;
using NUnit.Framework.Internal;
using System.IO.Compression;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Main;
using System.Collections.Generic;

namespace Duplicati.UnitTest
{
    public class Issue6296 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void TestRepairIndexFilesWorks([Values(1, 2, 3)] int fileDistribution)
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
                number_of_retries = 0,
                zip_compression_level = 0,
                blocksize = "10kb",
                dblock_size = "4mb"
            });

            var rnd = Random.Shared; // new Random(42);

            // Create some files
            if (fileDistribution == 1)
            {
                var data1 = new byte[1024 * 1024 * 10];
                rnd.NextBytes(data1);

                for (var i = 0; i < 10; i++)
                    File.WriteAllBytes(Path.Combine(DATAFOLDER, $"a{i}"), data1.AsSpan().Slice(i, 1024 * 1024 * 2).ToArray());
            }
            else if (fileDistribution == 2)
            {
                for (var i = 0; i < 10; i++)
                {
                    var data1 = new byte[rnd.Next(0, 1024 * 1024 * 11)];
                    rnd.NextBytes(data1);
                    File.WriteAllBytes(Path.Combine(DATAFOLDER, $"a{i}"), data1);
                }
            }
            else if (fileDistribution == 3)
            {
                for (var i = 0; i < 10; i++)
                {
                    var data1 = new byte[rnd.Next(0, 1024 * 1024 * 11)];
                    File.WriteAllBytes(Path.Combine(DATAFOLDER, $"a{i}"), data1);
                }
            }

            // Make a backup
            using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Make sure the tests succeed
            var verifyopts = testopts.Expand(new
            {
                full_remote_verification = nameof(Options.RemoteTestStrategy.IndexOnly),
                dont_replace_faulty_index_files = true,
            });
            using (var c = new Controller("file://" + TARGETFOLDER, verifyopts, null))
                TestUtils.AssertResults(c.Test(short.MaxValue));

            // Manipulate the index files to remove the blocklist hashes
            var brokenIndexFiles = new HashSet<string>();
            foreach (var indexFile in Directory.GetFiles(TARGETFOLDER, "*.dindex.zip", SearchOption.AllDirectories))
            {
                List<string> entriesToRemove;
                using (var zip = ZipFile.Open(indexFile, ZipArchiveMode.Read))
                    entriesToRemove = zip.Entries
                        .Where(e => e.FullName.StartsWith("list/", StringComparison.OrdinalIgnoreCase))
                        .Select(e => e.FullName)
                        .ToList();

                using (var zip = ZipFile.Open(indexFile, ZipArchiveMode.Update))
                {
                    foreach (var entryName in entriesToRemove)
                    {
                        var entry = zip.GetEntry(entryName);
                        if (entry != null)
                        {
                            entry.Delete();
                            brokenIndexFiles.Add(indexFile);
                        }
                    }
                }
            }

            // Setup for repair with broken index files
            BackendLoader.AddBackend(new DeterministicErrorBackend());
            var anyblockfiles = false;
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action.IsGetOperation && remotename.Contains(".dblock."))
                    anyblockfiles = true;
                return false;
            };

            File.Delete(DBFILE);
            using (var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());

            // Check that the backup is valid, but missing data in the index files
            Assert.That(anyblockfiles, Is.True, "No dblock files were loaded during repair");

            // Run the test+repair index operation
            var repairopts = testopts.Expand(new
            {
                full_remote_verification = nameof(Options.RemoteTestStrategy.IndexOnly),
                dont_replace_faulty_index_files = false,
            });

            using (var c = new Controller("file://" + TARGETFOLDER, repairopts, null))
            {
                var res = c.Test(short.MaxValue);
                Assert.That(res.Warnings, Is.Not.Empty, "Expected warnings during test with broken index files");
            }

            var indexFilesAfter = Directory.GetFiles(TARGETFOLDER, "*.dindex.zip", SearchOption.TopDirectoryOnly).ToHashSet();
            Assert.That(brokenIndexFiles.Any(x => !indexFilesAfter.Contains(x)), Is.True, "Some index files were not repaired");
            Assert.That(indexFilesAfter.Any(x => !brokenIndexFiles.Contains(x)), Is.True, "Some index files were replaced?");

            // Prepare for repair that does not need to download dblock files
            File.Delete(DBFILE);
            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action.IsGetOperation && remotename.Contains(".dblock."))
                    return true;
                return false;
            };


            // Check that the recreate operation works without downloading dblock files
            using (var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());
        }
    }
}

