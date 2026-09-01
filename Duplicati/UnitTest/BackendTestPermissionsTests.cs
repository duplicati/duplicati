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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Testing a destination writes a probe file, reads it back and removes it again.
    /// A listing can report a file that is already deleted, and a file that was just
    /// written is not always immediately addressable, so either delete of the probe
    /// file can report the file as missing. That used to fail the whole test, which
    /// is what the user sees when testing a destination.
    /// </summary>
    [TestFixture]
    public class BackendTestPermissionsTests
    {
        /// <summary>
        /// A backend that keeps the written files in memory, with replaceable listing
        /// and delete behavior
        /// </summary>
        private sealed class ProbeFileBackend : IBackend
        {
            private readonly Dictionary<string, string> _stored = new();

            /// <summary>
            /// The names to report from the listing
            /// </summary>
            public List<string> Listing { get; set; } = new();

            /// <summary>
            /// The error to raise for the given delete attempt number, if any
            /// </summary>
            public Func<int, Exception?> OnDelete { get; set; } = _ => null;

            /// <summary>
            /// The error to raise for the given read attempt number, if any
            /// </summary>
            public Func<int, Exception?> OnGet { get; set; } = _ => null;

            /// <summary>
            /// The error to raise when listing, if any
            /// </summary>
            public Func<Exception?> OnList { get; set; } = () => null;

            public int Deletes { get; private set; }

            public int Reads { get; private set; }

            public async IAsyncEnumerable<IFileEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                var error = OnList();
                if (error != null)
                    throw error;

                await Task.CompletedTask.ConfigureAwait(false);
                foreach (var name in Listing)
                    yield return new FileEntry(name);
            }

            public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
            {
                Deletes++;
                var error = OnDelete(Deletes);
                if (error != null)
                    throw error;

                _stored.Remove(remotename);
                return Task.CompletedTask;
            }

            public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
            {
                _stored[remotename] = File.ReadAllText(filename);
                return Task.CompletedTask;
            }

            public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
            {
                Reads++;
                var error = OnGet(Reads);
                if (error != null)
                    throw error;

                if (!_stored.TryGetValue(remotename, out var content))
                    throw new FileMissingException();

                File.WriteAllText(filename, content);
                return Task.CompletedTask;
            }

            public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken)
                => this.TestBackendAsync(alsoWrite, cancellationToken);

            public Task CreateFolderAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

            public string DisplayName => "Probe File Backend";
            public string ProtocolKey => "probefile";
            public string Description => "A testing backend that keeps files in memory";
            public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();

            public void Dispose()
            {
            }
        }

        [Test]
        [Category("Backend")]
        public async Task StaleListingEntryDoesNotFailTheTest_Async()
        {
            // The listing still reports the probe file from an earlier test, but it is
            // already gone by the time it is removed
            using var backend = new ProbeFileBackend
            {
                Listing = { BackendExtensions.TEST_FILE_NAME },
                OnDelete = attempt => attempt == 1 ? new FileMissingException() : null
            };

            await backend.TestReadWritePermissionsAsync(CancellationToken.None);

            Assert.AreEqual(2, backend.Deletes, "Both the initial removal and the cleanup should be attempted");
        }

        [Test]
        [Category("Backend")]
        public async Task MissingProbeFileOnCleanupDoesNotFailTheTest_Async()
        {
            // The probe file was written and read back, but is no longer addressable
            // when it is removed again
            using var backend = new ProbeFileBackend
            {
                OnDelete = _ => new FileMissingException()
            };

            await backend.TestReadWritePermissionsAsync(CancellationToken.None);

            Assert.AreEqual(1, backend.Deletes, "Only the cleanup should be attempted when the listing is empty");
        }

        [Test]
        [Category("Backend")]
        public void MissingFolderIsStillReported_Async()
        {
            // The auto-create flow keys on this exception, so it has to pass through
            using var backend = new ProbeFileBackend
            {
                OnList = () => new FolderMissingException()
            };

            Assert.ThrowsAsync<FolderMissingException>(() => backend.TestReadWritePermissionsAsync(CancellationToken.None));
        }

        [Test]
        [Category("Backend")]
        public void FailingCleanupIsStillReported_Async()
        {
            // A delete that fails for any other reason is a real problem
            using var backend = new ProbeFileBackend
            {
                OnDelete = _ => new IOException("connection reset")
            };

            Assert.ThrowsAsync<TestAfterConnectException>(() => backend.TestReadWritePermissionsAsync(CancellationToken.None));
        }

        [Test]
        [Category("Backend")]
        public async Task TransientReadFailureIsRetried_Async()
        {
            // A file that was just written is not always immediately addressable,
            // so a missing file on read is retried a few times
            using var backend = new ProbeFileBackend
            {
                OnGet = attempt => attempt <= 2 ? new FileMissingException() : null
            };

            await backend.TestReadWritePermissionsAsync([TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero], CancellationToken.None);

            Assert.AreEqual(3, backend.Reads, "The read should be retried until the file is visible");
        }

        [Test]
        [Category("Backend")]
        public void PersistentReadFailureIsStillReported_Async()
        {
            // When the file never becomes addressable, the read failure is reported
            using var backend = new ProbeFileBackend
            {
                OnGet = _ => new FileMissingException()
            };

            Assert.ThrowsAsync<TestAfterConnectException>(() => backend.TestReadWritePermissionsAsync([TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero], CancellationToken.None));

            Assert.AreEqual(4, backend.Reads, "The read should be attempted once plus the retries");
        }
    }
}
