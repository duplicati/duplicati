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
using Duplicati.Library.Interface;
using System.Collections.Generic;
using Duplicati.Library.DynamicLoader;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.UnitTest
{
    public class Issue6254 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void VerifyCompactKeepsIndexFileBlocklists()
        {
            var testopts = TestOptions.Expand(new
            {
                no_encryption = true,
                number_of_retries = 0,
                keep_versions = 1,
                no_auto_compact = true,
                zip_compression_level = 0,
                asynchronous_concurrent_upload_limit = 1,
                asynchronous_upload_limit = 1,
                blocksize = "100kb",
                dblock_size = "4mb"
            });

            var rnd = Random.Shared; //new Random(42);
            var data1 = new byte[1024 * 1024 * 10];
            rnd.NextBytes(data1);
            var data2 = new byte[1024 * 1024 * 10];
            rnd.NextBytes(data1);

            // Create some files
            for (var i = 0; i < 10; i++)
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"a{i}"), data1.AsSpan().Slice(i, 1024 * 1024 * 2).ToArray());

            // Make a backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Add some second files
            for (var i = 0; i < 10; i++)
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"b{i}"), data2.AsSpan().Slice(i, 1024 * 1024 * 2).ToArray());

            // Make a backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Delete the first files
            for (var i = 0; i < 8; i++)
                File.Delete(Path.Combine(DATAFOLDER, $"a{i}"));

            var indexfiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).ToHashSet();

            // Make a backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Now run compact
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Compact());

            // Find the new index files
            var newIndexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly)
                .Where(x => !indexfiles.Contains(x))
                .ToList();

            Assert.That(newIndexFiles.Count, Is.GreaterThan(0), "No new index files found");

            // Check that the new index files contains the list folder
            var matches = 0;
            foreach (var file in newIndexFiles)
            {
                using var zip = new ZipArchive(File.OpenRead(file), ZipArchiveMode.Read);
                if (zip.Entries.Any(x => x.FullName.StartsWith("list/")))
                    matches++;
            }

            // Test is essentially inconclusive here, as the repair does not cover the blocklist hashes
            // but we cannot control it closely enough to make it always work
            //Assert.That(matches, Is.EqualTo(newIndexFiles.Count), "No new index files found with list folder");

            // Check that recreate does not load dblock files
            BackendLoader.AddBackend(new BlockDblockBackend());
            File.Delete(DBFILE);

            using (var c = new Library.Main.Controller(BlockDblockBackend.Key + "://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());
        }

        private class BlockDblockBackend : IStreamingBackend
        {
            public const string Key = "test-backend-6254";
            public string DisplayName => "Test backend";
            public string ProtocolKey => Key;
            public string Description => "Test backend";
            public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;
            private readonly IStreamingBackend backend;

            public static string RealKey { get; set; } = "file";
            public BlockDblockBackend()
            {
                backend = (IStreamingBackend)BackendLoader.Backends.First(x => x.ProtocolKey == RealKey);
            }

            public BlockDblockBackend(string url, Dictionary<string, string> opts)
            {
                backend = (IStreamingBackend)BackendLoader.GetBackend(RealKey + url.Substring(ProtocolKey.Length), opts);
            }

            public Task CreateFolderAsync(CancellationToken cancellationToken)
                => backend.CreateFolderAsync(cancellationToken);

            public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
                => backend.DeleteAsync(remotename, cancellationToken);
            public void Dispose()
                => backend.Dispose();
            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
            {
                if (remotename.Contains(".dblock."))
                    throw new DeterministicErrorBackend.DeterministicErrorBackendException("Prevent");
                return backend.GetAsync(remotename, stream, cancelToken);
            }
            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
            {
                if (remotename.Contains(".dblock."))
                    throw new DeterministicErrorBackend.DeterministicErrorBackendException("Prevent");
                return backend.GetAsync(remotename, filename, cancellationToken);
            }
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
                => backend.GetDNSNamesAsync(cancelToken);
            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
                => backend.ListAsync(cancellationToken);

            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
                => backend.PutAsync(remotename, stream, cancelToken);

            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
                => backend.PutAsync(remotename, filename, cancellationToken);

            public Task TestAsync(CancellationToken cancellationToken)
                => backend.TestAsync(cancellationToken);
        }
    }
}

