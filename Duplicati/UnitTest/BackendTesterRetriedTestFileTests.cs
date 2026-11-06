// Copyright (C) 2026, The Duplicati Team
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.CommandLine.BackendTester;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Each test of a destination writes a file with a name of its own, so a test that
    /// failed after writing leaves a file the retried test does not know about. The
    /// backend tester requires an empty folder, and must not abandon the run over that.
    /// </summary>
    [TestFixture]
    [Category("BackendTesterVerification")]
    public class BackendTesterRetriedTestFileTests
    {
        /// <summary>
        /// A backend that keeps its files in memory, and fails the given number of deletes
        /// </summary>
        private sealed class MemoryBackend : IBackend
        {
            private int _deletesToFail;

            public MemoryBackend(int deletesToFail = 0)
            {
                _deletesToFail = deletesToFail;
            }

            /// <summary>
            /// The files on the destination
            /// </summary>
            public Dictionary<string, string> Files { get; } = new();

            public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (var name in Files.Keys.ToList())
                {
                    await Task.Yield();
                    yield return new FileEntry(name, Files[name].Length);
                }
            }

            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
            {
                Files[remotename] = System.IO.File.ReadAllText(filename);
                return Task.CompletedTask;
            }

            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
            {
                System.IO.File.WriteAllText(filename, Files[remotename]);
                return Task.CompletedTask;
            }

            public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
            {
                if (_deletesToFail > 0)
                {
                    _deletesToFail--;
                    throw new IOException("The delete failed");
                }

                if (!Files.Remove(remotename))
                    throw new FileMissingException();

                return Task.CompletedTask;
            }

            public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken) => this.TestBackendAsync(alsoWrite, cancellationToken);
            public Task CreateFolderAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

            public string DisplayName => "Memory Backend";
            public string ProtocolKey => "memory";
            public string Description => "A testing backend that keeps its files in memory";
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public void Dispose()
            {
            }
        }

        [Test]
        public async Task RetriedTestDoesNotLeaveTheFolderOccupied()
        {
            using var backend = new MemoryBackend(deletesToFail: 1);

            Assert.ThrowsAsync<TestAfterConnectException>(() => backend.TestAsync(true, CancellationToken.None));
            await backend.TestAsync(true, CancellationToken.None);

            Assert.AreEqual(1, backend.Files.Count, "The failed test should leave its file");

            var leftovers = Program.ListAfterRemovingTestFiles(backend, 1, 0);

            Assert.That(leftovers, Is.Empty);
            Assert.That(backend.Files.Keys, Is.Empty);
        }

        [Test]
        public void OtherFilesAreKeptAndReported()
        {
            using var backend = new MemoryBackend();
            var testFile = BackendExtensions.TEST_FILE_PREFIX + "abc" + BackendExtensions.TEST_FILE_EXTENSION;
            var otherFile = BackendExtensions.TEST_FILE_PREFIX + "abc.dat";
            backend.Files[testFile] = "test";
            backend.Files[otherFile] = "other";
            backend.Files["duplicati-20260101T000000Z.dlist.zip"] = "other";

            var leftovers = Program.ListAfterRemovingTestFiles(backend, 1, 0);

            Assert.That(leftovers, Is.EquivalentTo(new[] { otherFile, "duplicati-20260101T000000Z.dlist.zip" }));
            Assert.That(backend.Files.Keys, Is.EquivalentTo(leftovers));
        }

        [Test]
        public void EmptyFolderIsLeftAlone()
        {
            using var backend = new MemoryBackend(deletesToFail: 1);

            Assert.That(Program.ListAfterRemovingTestFiles(backend, 1, 0), Is.Empty);
        }
    }
}
