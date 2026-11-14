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

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// --use-move-for-put turns off streaming for the file backend by itself, so the backup moves
    /// its volumes without --disable-streaming-transfers, which the help text used to require.
    /// </summary>
    [TestFixture]
    public class FileMoveForPutTests : BasicSetupHelper
    {
        /// <summary>
        /// Wraps the file backend and counts which kind of upload the backup uses.
        /// </summary>
        public class CountingFileBackend : IBackend, IStreamingBackend
        {
            public const string Key = "countingfile";

            public static int StreamPuts;
            public static int FilePuts;

            private readonly Library.Backend.File? m_backend;

            public CountingFileBackend()
            {
            }

            // ReSharper disable once UnusedMember.Global
            public CountingFileBackend(string url, Dictionary<string, string?> options)
            {
                m_backend = new Library.Backend.File(new Library.Utility.RelaxedUri(url).SetScheme("file").ToString(), options);
            }

            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
            {
                Interlocked.Increment(ref StreamPuts);
                return m_backend!.PutAsync(remotename, stream, cancelToken);
            }

            public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
            {
                Interlocked.Increment(ref FilePuts);
                return m_backend!.PutAsync(remotename, filename, cancelToken);
            }

            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, stream, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, filename, cancelToken);
            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken) => m_backend!.ListAsync(cancellationToken);
            public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend!.DeleteAsync(remotename, cancelToken);
            public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend!.TestAsync(alsoWrite, cancelToken);
            public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend!.CreateFolderAsync(cancelToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend!.GetDNSNamesAsync(cancelToken);
            public bool SupportsStreaming => m_backend!.SupportsStreaming;
            public string DisplayName => "Counting file backend";
            public string ProtocolKey => Key;
            public IList<ICommandLineArgument> SupportedCommands => m_backend?.SupportedCommands ?? new Library.Backend.File().SupportedCommands;
            public string Description => "A testing backend that counts the uploads of the file backend";
            public void Dispose() => m_backend?.Dispose();
        }

        [Test]
        [NonParallelizable]
        [TestCase(true)]
        [TestCase(false)]
        public async Task MoveForPutUploadsFilesWithoutDisableStreaming(bool moveForPut)
        {
            Library.DynamicLoader.BackendLoader.AddBackend(new CountingFileBackend());
            CountingFileBackend.StreamPuts = 0;
            CountingFileBackend.FilePuts = 0;

            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");
            var opts = TestOptions.Expand(new { use_move_for_put = moveForPut });
            Assert.IsFalse(opts.ContainsKey("disable-streaming-transfers"));

            using (var c = new Library.Main.Controller(CountingFileBackend.Key + "://" + TARGETFOLDER.Replace("\\", "/"), opts, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            if (moveForPut)
            {
                Assert.Greater(CountingFileBackend.FilePuts, 0, "The volumes should be handed over as files, so the backend can move them");
                Assert.AreEqual(0, CountingFileBackend.StreamPuts);
            }
            else
            {
                Assert.Greater(CountingFileBackend.StreamPuts, 0);
                Assert.AreEqual(0, CountingFileBackend.FilePuts);
            }

            using (var c = new Library.Main.Controller(CountingFileBackend.Key + "://" + TARGETFOLDER.Replace("\\", "/"), opts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
        }
    }
}
