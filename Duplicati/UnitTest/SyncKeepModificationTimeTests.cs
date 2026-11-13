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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using File = System.IO.File;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// With --sync-keep-modification-time a sync gives the uploaded files the time of the local
    /// files, where the destination can store it. Issue #7702.
    /// </summary>
    [TestFixture]
    [Category("Sync")]
    public class SyncKeepModificationTimeTests : BasicSetupHelper
    {
        /// <summary>
        /// Answers every request with "created" and keeps the headers of the last one.
        /// </summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }
        }

        [Test]
        public async Task WebDavSendsTheTimeInSecondsSinceTheEpoch()
        {
            var handler = new RecordingHandler();
            using var backend = new WEBDAV("webdav://host/folder/", new Dictionary<string, string?>(), handler);
            using var data = new MemoryStream(new byte[] { 1, 2, 3 });

            await ((IModificationTimeEnabledBackend)backend).PutAsync("file.txt", data, new DateTime(2001, 2, 3, 4, 5, 6, 789, DateTimeKind.Utc), CancellationToken.None);

            Assert.AreEqual(HttpMethod.Put, handler.LastRequest?.Method);
            Assert.IsTrue(handler.LastRequest!.Headers.TryGetValues("X-OC-Mtime", out var values), "The time header was not sent");
            Assert.AreEqual("981173106", values!.Single());
        }

        [Test]
        public async Task WebDavSendsNoTimeForAPlainUpload()
        {
            var handler = new RecordingHandler();
            using var backend = new WEBDAV("webdav://host/folder/", new Dictionary<string, string?>(), handler);
            using var data = new MemoryStream(new byte[] { 1, 2, 3 });

            await backend.PutAsync("file.txt", data, CancellationToken.None);

            Assert.AreEqual(HttpMethod.Put, handler.LastRequest?.Method);
            Assert.IsFalse(handler.LastRequest!.Headers.Contains("X-OC-Mtime"));
        }

        /// <summary>
        /// A destination that stores the modification time in whole seconds, as a WebDAV server
        /// does with X-OC-Mtime. It wraps the built-in file backend.
        /// </summary>
        public class SecondsFileBackend : IBackend, IStreamingBackend, IModificationTimeEnabledBackend
        {
            public const string Key = "secondsfile";

            private readonly Library.Backend.File? m_backend;

            public SecondsFileBackend()
            {
            }

            // ReSharper disable once UnusedMember.Global
            public SecondsFileBackend(string url, Dictionary<string, string?> options)
            {
                m_backend = new Library.Backend.File(new Library.Utility.RelaxedUri(url).SetScheme("file").ToString(), options);
            }

            public Task PutAsync(string remotename, Stream stream, DateTime lastModifiedUtc, CancellationToken cancelToken)
                => ((IModificationTimeEnabledBackend)m_backend!).PutAsync(remotename, stream, Library.Utility.Utility.NormalizeDateTime(lastModifiedUtc), cancelToken);
            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.PutAsync(remotename, stream, cancelToken);
            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, stream, cancelToken);
            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken) => m_backend!.ListAsync(cancellationToken);
            public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.PutAsync(remotename, filename, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, filename, cancelToken);
            public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend!.DeleteAsync(remotename, cancelToken);
            public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend!.TestAsync(alsoWrite, cancelToken);
            public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend!.CreateFolderAsync(cancelToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend!.GetDNSNamesAsync(cancelToken);
            public string DisplayName => "Whole-second file backend";
            public string ProtocolKey => Key;
            public IList<ICommandLineArgument> SupportedCommands => m_backend?.SupportedCommands ?? new Library.Backend.File().SupportedCommands;
            public string Description => "A testing backend that stores modification times in whole seconds";
            public bool SupportsStreaming => true;
            public void Dispose() => m_backend?.Dispose();
        }

        /// <summary>
        /// A modification time with a fraction of a second, which a whole-second destination cannot keep.
        /// </summary>
        private static readonly DateTime SourceTime = new DateTime(2001, 2, 3, 4, 5, 6, 789, DateTimeKind.Utc);

        private string targetDir = null!;
        private string dataDir = null!;

        [SetUp]
        public void Setup()
        {
            targetDir = Path.Combine(BASEFOLDER, "keep-mtime-target");
            dataDir = Path.Combine(BASEFOLDER, "keep-mtime-data");
            foreach (var d in new[] { targetDir, dataDir })
            {
                if (Directory.Exists(d))
                    Directory.Delete(d, true);
                Directory.CreateDirectory(d);
            }

            var file = Path.Combine(dataDir, "file.txt");
            File.WriteAllText(file, "content");
            File.SetLastWriteTimeUtc(file, SourceTime);
        }

        private Dictionary<string, string> Options(bool keepTime)
            => new Dictionary<string, string>
            {
                ["no-encryption"] = "true",
                ["snapshot-policy"] = "off",
                // A database per test, as the sync handler keeps its database open on Windows
                ["dbpath"] = Path.Combine(BASEFOLDER, $"keep-mtime-{Guid.NewGuid():N}.sqlite"),
                ["sync-keep-modification-time"] = keepTime ? "true" : "false",
            };

        private static async Task<ISyncResults> SyncAsync(string url, Dictionary<string, string> opts, string source)
        {
            using var c = new Controller(url, opts, null);
            return await c.SyncAsync([source], null);
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task LocalFolderGetsTheTimeOfTheSourceFile(bool keepTime)
        {
            var res = await SyncAsync("file://" + targetDir, Options(keepTime), dataDir);
            TestUtils.AssertResults(res);

            var uploaded = File.GetLastWriteTimeUtc(Path.Combine(targetDir, "file.txt"));
            if (keepTime)
                Assert.AreEqual(SourceTime, uploaded);
            else
                Assert.AreNotEqual(SourceTime, uploaded, "Without the option the file keeps the time of the upload");
        }

        [Test]
        public async Task AWholeSecondTimeIsNotUploadedAgain()
        {
            Library.DynamicLoader.BackendLoader.AddBackend(new SecondsFileBackend());
            var url = SecondsFileBackend.Key + "://" + targetDir.Replace("\\", "/");
            var opts = Options(true);

            var first = await SyncAsync(url, opts, dataDir);
            TestUtils.AssertResults(first);
            Assert.AreEqual(1, first.FilesUploaded);
            Assert.AreEqual(Library.Utility.Utility.NormalizeDateTime(SourceTime), File.GetLastWriteTimeUtc(Path.Combine(targetDir, "file.txt")));

            // The stored time is a fraction of a second older than the local one, which must
            // not count as a change
            var second = await SyncAsync(url, opts, dataDir);
            TestUtils.AssertResults(second);
            Assert.AreEqual(0, second.FilesUploaded, "The unchanged file was uploaded again");
        }
    }
}
