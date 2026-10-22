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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Verifies that a backend listing with path components in the filenames
    /// cannot make the recovery tool download write files outside the target folder
    /// </summary>
    public class RecoveryToolDownloadPathTraversalTests : BasicSetupHelper
    {
        /// <summary>
        /// A backend that wraps the file backend and reports dlist volumes
        /// with a relative path component prepended to the name
        /// </summary>
        public class TraversingListBackend : IStreamingBackend
        {
            public const string Key = "traversinglist";
            public const string ListPrefix = "../";

            private readonly IStreamingBackend m_backend;

            public TraversingListBackend() { }

            // ReSharper disable once UnusedMember.Global
            public TraversingListBackend(string url, Dictionary<string, string> options)
            {
                var u = new Library.Utility.RelaxedUri(url).SetScheme("file").ToString();
                m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);
            }

            private static string Unwrap(string remotename)
                => remotename.StartsWith(ListPrefix) ? remotename.Substring(ListPrefix.Length) : remotename;

            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
                => m_backend.ListAsync(cancellationToken)
                    .Select(x => x.Name.Contains(".dlist.") ? new FileEntry(ListPrefix + x.Name, x.Size, x.LastAccess, x.LastModification) : x);

            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend.PutAsync(Unwrap(remotename), stream, cancelToken);
            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend.GetAsync(Unwrap(remotename), stream, cancelToken);
            public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend.PutAsync(Unwrap(remotename), filename, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend.GetAsync(Unwrap(remotename), filename, cancelToken);
            public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend.DeleteAsync(Unwrap(remotename), cancelToken);
            public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend.TestAsync(alsoWrite, cancelToken);
            public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend.CreateFolderAsync(cancelToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend.GetDNSNamesAsync(cancelToken);
            public string DisplayName => "Traversing list backend";
            public string ProtocolKey => Key;
            public IList<ICommandLineArgument> SupportedCommands => m_backend?.SupportedCommands ?? Library.DynamicLoader.BackendLoader.GetSupportedCommands("file://").ToList();
            public string Description => "A testing backend that reports dlist filenames with a relative path component";
            public bool SupportsStreaming => m_backend?.SupportsStreaming ?? false;
            public void Dispose() => m_backend?.Dispose();
        }

        [Test]
        [Category("RecoveryTool")]
        public async Task RecoveryToolDownloadRejectsTraversingRemoteNames()
        {
            File.WriteAllText(Path.Combine(DATAFOLDER, "a"), "some data");

            var options = new Dictionary<string, string>(TestOptions);
            options["no-encryption"] = "true";
            options.Remove("passphrase");

            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            var dlistName = Path.GetFileName(Directory.GetFiles(TARGETFOLDER, "*.dlist.*").Single());

            Library.DynamicLoader.BackendLoader.AddBackend(new TraversingListBackend());

            // Download into a subfolder, so an escaped write would land in RESTOREFOLDER
            var downloadFolder = Path.Combine(RESTOREFOLDER, "downloaded");
            Directory.CreateDirectory(downloadFolder);

            var status = CommandLine.RecoveryTool.Program.Main(new[] { "download", TraversingListBackend.Key + "://" + TARGETFOLDER, downloadFolder });

            Assert.AreNotEqual(0, status, "Expected download to report an error for the traversing filename");
            Assert.That(File.Exists(Path.Combine(RESTOREFOLDER, dlistName)), Is.False, "The dlist volume was written outside the download folder");
            Assert.That(File.Exists(Path.Combine(downloadFolder, dlistName)), Is.False, "The traversing dlist volume was downloaded");
            Assert.That(Directory.GetFiles(downloadFolder, "*.dblock.*").Length, Is.GreaterThan(0), "The dblock volume was not downloaded");
            Assert.That(Directory.GetFiles(RESTOREFOLDER, "*", SearchOption.AllDirectories).Length, Is.EqualTo(Directory.GetFiles(downloadFolder).Length), "Unexpected files outside the download folder");
        }
    }
}
