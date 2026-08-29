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
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The backend tester requires the remote folder to be empty, and a listing can
    /// lag behind the deletes that emptied it - the tester writes and removes a file
    /// of its own on the line above the one that reads the folder. It also has to
    /// accept a delete that reports the file as missing, because a delete that timed
    /// out may well have landed.
    /// </summary>
    [TestFixture]
    [Category("BackendTesterVerification")]
    public class BackendTesterEmptyFolderTests
    {
        /// <summary>
        /// A backend that lists and deletes, with both outcomes under the test's control
        /// </summary>
        private sealed class FolderBackend : IBackend
        {
            private readonly Func<int, IEnumerable<IFileEntry>> _onList;
            private readonly Func<string, Exception?>? _onDelete;

            public FolderBackend(Func<int, IEnumerable<IFileEntry>> onList, Func<string, Exception?>? onDelete = null)
            {
                _onList = onList;
                _onDelete = onDelete;
            }

            /// <summary>
            /// The number of times the folder was read
            /// </summary>
            public int Listings { get; private set; }

            /// <summary>
            /// The names that were passed to a delete, in order
            /// </summary>
            public List<string> Deletes { get; } = new();

            public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                var entries = _onList(Listings++);
                foreach (var entry in entries)
                {
                    await Task.Yield();
                    yield return entry;
                }
            }

            public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
            {
                Deletes.Add(remotename);
                var error = _onDelete?.Invoke(remotename);
                if (error != null)
                    throw error;

                return Task.CompletedTask;
            }

            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task CreateFolderAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

            public string DisplayName => "Folder Backend";
            public string ProtocolKey => "folder";
            public string Description => "A testing backend that lists and deletes";
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public void Dispose()
            {
            }
        }

        private static IFileEntry File(string name) => new FileEntry(name, 17);

        private static IFileEntry Folder(string name) => new FileEntry(name) { IsFolder = true };

        [Test]
        public void AFolderThatBecomesEmptyIsAccepted()
        {
            // The delete had landed, the listing just had not caught up with it
            using var backend = new FolderBackend(attempt =>
                attempt == 0 ? new[] { File("left-over.tmp") } : Enumerable.Empty<IFileEntry>());

            var leftovers = Program.ListUntilFolderIsEmpty(backend, 1, 3);

            Assert.AreEqual(0, leftovers.Count);
            Assert.AreEqual(2, backend.Listings, "the folder has to be read again before it is given up on");
        }

        [Test]
        public void AFolderThatStaysOccupiedIsReported()
        {
            // Re-reading is not the same as accepting whatever the folder holds
            using var backend = new FolderBackend(_ => new[] { File("really-there.tmp") });

            var leftovers = Program.ListUntilFolderIsEmpty(backend, 1, 1);

            Assert.AreEqual(new[] { "really-there.tmp" }, leftovers);
            Assert.AreEqual(2, backend.Listings, "the attempts have to run out");
        }

        [Test]
        public void AnEmptyFolderIsNotReadAgain()
        {
            using var backend = new FolderBackend(_ => Enumerable.Empty<IFileEntry>());

            var leftovers = Program.ListUntilFolderIsEmpty(backend, 1, 3);

            Assert.AreEqual(0, leftovers.Count);
            Assert.AreEqual(1, backend.Listings);
        }

        [Test]
        public void FoldersDoNotCountAsFiles()
        {
            using var backend = new FolderBackend(_ => new[] { Folder("subfolder") });

            var leftovers = Program.ListUntilFolderIsEmpty(backend, 1, 3);

            Assert.AreEqual(0, leftovers.Count);
            Assert.AreEqual(1, backend.Listings);
        }

        [Test]
        public void ADeleteOfAFileThatIsAlreadyGoneIsNotAFailure()
        {
            // A delete that timed out on the way out may still have landed, and the
            // file being gone is what the delete was for
            using var backend = new FolderBackend(_ => Enumerable.Empty<IFileEntry>(),
                _ => new FileMissingException());

            var failures = Program.DeleteTestFiles(backend, new[] { "gone.tmp" }, 1);

            Assert.AreEqual(0, failures.Count, "a file that is already gone is not a failed delete");
        }

        [Test]
        public void AFailingDeleteIsReported()
        {
            // Anything other than the file being gone is still a real failure
            using var backend = new FolderBackend(_ => Enumerable.Empty<IFileEntry>(),
                _ => new IOException("connection reset"));

            var failures = Program.DeleteTestFiles(backend, new[] { "locked.tmp" }, 1);

            Assert.AreEqual(1, failures.Count);
            Assert.AreEqual("locked.tmp", failures[0].RemoteName);
            Assert.IsInstanceOf<IOException>(failures[0].Error);
        }

        [Test]
        public void EveryFileIsAttemptedAfterOneFails()
        {
            using var backend = new FolderBackend(_ => Enumerable.Empty<IFileEntry>(),
                name => name == "locked.tmp" ? new IOException("connection reset") : null);

            var failures = Program.DeleteTestFiles(backend, new[] { "first.tmp", "locked.tmp", "last.tmp" }, 1);

            Assert.AreEqual(new[] { "first.tmp", "locked.tmp", "last.tmp" }, backend.Deletes);
            Assert.AreEqual(1, failures.Count);
            Assert.AreEqual("locked.tmp", failures[0].RemoteName);
        }
    }
}
