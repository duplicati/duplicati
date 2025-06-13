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

using System;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Duplicati.Library.DynamicLoader;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class ArchiveAttributeTests : BasicSetupHelper
    {
        private class ArchiveEnabledBackend : IStreamingBackend
        {
            public const string Key = "test-archive-backend-1";
            public string DisplayName => "Test Archive Enabled Backend";
            public string ProtocolKey => Key;
            public string Description => "Test Archive Enabled";
            public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;
            private readonly IStreamingBackend backend;

            public static string RealKey { get; set; }
            public static Func<IAsyncEnumerable<IFileEntry>, IAsyncEnumerable<IFileEntry>> ListModifier { get; set; }

            public ArchiveEnabledBackend()
            {
                backend = (IStreamingBackend)BackendLoader.Backends.First(x => x.ProtocolKey == RealKey);
            }

            public ArchiveEnabledBackend(string url, Dictionary<string, string> opts)
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
                => backend.GetAsync(remotename, stream, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
                => backend.GetAsync(remotename, filename, cancellationToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
                => backend.GetDNSNamesAsync(cancelToken);
            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
                => ListModifier(backend.ListAsync(cancellationToken));

            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
                => backend.PutAsync(remotename, stream, cancelToken);

            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
                => backend.PutAsync(remotename, filename, cancellationToken);

            public Task TestAsync(CancellationToken cancellationToken)
                => backend.TestAsync(cancellationToken);
        }

        [Test]
        [Category("Backend")]
        public void TestArchiveAttributes()
        {
            var testopts = TestOptions;
            testopts["blocksize"] = "100kb";
            testopts["dblock-size"] = "500kb";

            var opts = new Library.Main.Options(testopts);

            // Make a backup of 2MiB in 500KiB files
            var data = new byte[1024 * 1024 * 2];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var backupResults = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), new byte[1024 * 1024 * 2]);
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                var backupResults = c.Backup(new string[] { DATAFOLDER });
                Assert.AreEqual(0, backupResults.Errors.Count());
                Assert.AreEqual(0, backupResults.Warnings.Count());
            }

            var remotefilenames = Directory.EnumerateFiles(TARGETFOLDER)
                .ToDictionary(x => Path.GetFileName(x), x => false);

            // If there are no lists, the tests do not test any files
            var protectedfiles = remotefilenames.Keys.Where(x => x.Contains(".dlist.")).Take(1).ToHashSet();
            foreach (var k in protectedfiles)
                remotefilenames.Remove(k);

            // Prepare the backend
            ArchiveEnabledBackend.RealKey = "file";
            ArchiveEnabledBackend.ListModifier = files =>
                files.Select(x => new FileEntry(x.Name, x.Size, x.LastAccess, x.LastModification, x.IsFolder,
                    remotefilenames.TryGetValue(x.Name, out var r) ? r : false));
            BackendLoader.AddBackend(new ArchiveEnabledBackend());

            void RunTest()
            {
                using (var c = new Library.Main.Controller(ArchiveEnabledBackend.Key + "://" + TARGETFOLDER, testopts, null))
                {
                    var res = c.Test(remotefilenames.Count);
                    TestUtils.AssertResults(res);
                    Assert.AreEqual(remotefilenames.Where(x => !x.Value).Count() + protectedfiles.Count, res.Verifications.Count());
                }
            }

            // Run the test with no files archived
            RunTest();

            // Archive some files
            var archived = remotefilenames.Keys.Take(5).ToArray();
            foreach (var k in archived)
                remotefilenames[k] = true;

            // Run the test with some files archived
            RunTest();

            // Archive all files
            foreach (var k in remotefilenames.Keys.ToArray())
                remotefilenames[k] = true;

            // Run the test with all files archived
            RunTest();

            // Unarchive some files
            foreach (var k in archived)
                remotefilenames[k] = true;

            // Run the test with some files unarchived
            RunTest();

            // Unarchive all files
            foreach (var k in remotefilenames.Keys.ToArray())
                remotefilenames[k] = false;

            // Run the test with all files unarchived
            RunTest();
        }
    }
}

