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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.CommandLine.BackendTester;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Before testing, the backend tester removes the files that the remote folder
    /// already holds. A listing can still report a file that was just deleted, and
    /// deleting it again then reports it as missing, which used to abort the whole
    /// test run.
    /// </summary>
    [TestFixture]
    [Category("BackendTesterVerification")]
    public class BackendTesterCleanupTests
    {
        /// <summary>
        /// A backend that only supports deleting, with a replaceable outcome
        /// </summary>
        private sealed class DeletingBackend : IBackend
        {
            private readonly Func<string, Exception?> _onDelete;

            public List<string> Deletes { get; } = new();

            public DeletingBackend(Func<string, Exception?> onDelete)
                => _onDelete = onDelete;

            public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
            {
                Deletes.Add(remotename);
                var error = _onDelete(remotename);
                if (error != null)
                    throw error;

                return Task.CompletedTask;
            }

            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task CreateFolderAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

            public string DisplayName => "Deleting Backend";
            public string ProtocolKey => "deleting";
            public string Description => "A testing backend that only supports deleting";
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public void Dispose()
            {
            }
        }

        [Test]
        public async Task ListedFileThatIsAlreadyGoneIsAccepted_Async()
        {
            // The file is gone, which is what removing it was supposed to achieve
            using var backend = new DeletingBackend(_ => new FileMissingException());

            await Program.DeleteListedFileAsync(backend, "already-gone.tmp", 3);

            Assert.AreEqual(1, backend.Deletes.Count,
                "A file that is already gone should not be attempted again");
        }

        [Test]
        public void FailingDeleteIsStillReported_Async()
        {
            // Anything other than the file being gone is a real failure
            using var backend = new DeletingBackend(_ => new IOException("connection reset"));

            Assert.ThrowsAsync<IOException>(() => Program.DeleteListedFileAsync(backend, "locked.tmp", 2));

            Assert.AreEqual(2, backend.Deletes.Count, "A failing delete should be retried");
        }

        [Test]
        public async Task DeleteIsPerformedOnce_Async()
        {
            using var backend = new DeletingBackend(_ => null);

            await Program.DeleteListedFileAsync(backend, "present.tmp", 3);

            Assert.AreEqual(1, backend.Deletes.Count);
            Assert.AreEqual("present.tmp", backend.Deletes[0]);
        }
    }
}
