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
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Volumes;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using InterfaceFileEntry = Duplicati.Library.Interface.IFileEntry;

namespace Duplicati.UnitTest;

[TestFixture]
public class Issue1282 : BasicSetupHelper
{
    private sealed class CaseChangingBackend : IStreamingBackend
    {
        public const string Key = "test-case-changing-1282";
        private readonly IStreamingBackend _backend;

        public static bool ReturnCaseDuplicates { get; set; }

        public CaseChangingBackend()
        {
            _backend = (IStreamingBackend)BackendLoader.Backends.First(x => x.ProtocolKey == "file");
        }

        public CaseChangingBackend(string url, Dictionary<string, string> options)
        {
            _backend = (IStreamingBackend)BackendLoader.GetBackend("file" + url.Substring(Key.Length), options);
        }

        public string DisplayName => "Case-changing test backend";
        public string ProtocolKey => Key;
        public string Description => "Returns remote filenames with changed casing";
        public bool SupportsStreaming => _backend.SupportsStreaming;
        public IList<ICommandLineArgument> SupportedCommands => _backend.SupportedCommands;

        public async IAsyncEnumerable<InterfaceFileEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var entry in _backend.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return CopyWithName(entry, entry.Name.ToLowerInvariant());
                if (ReturnCaseDuplicates)
                    yield return CopyWithName(entry, entry.Name.ToUpperInvariant());
            }
        }

        private static InterfaceFileEntry CopyWithName(InterfaceFileEntry entry, string name)
            => new FileEntry(name, entry.Size, entry.LastAccess, entry.LastModification, entry.IsFolder, entry.IsArchived);

        public Task CreateFolderAsync(CancellationToken cancellationToken) => _backend.CreateFolderAsync(cancellationToken);
        public Task DeleteAsync(string remotename, CancellationToken cancellationToken) => _backend.DeleteAsync(remotename, cancellationToken);
        public Task GetAsync(string remotename, Stream stream, CancellationToken cancellationToken) => _backend.GetAsync(remotename, stream, cancellationToken);
        public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken) => _backend.GetAsync(remotename, filename, cancellationToken);
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancellationToken) => _backend.GetDNSNamesAsync(cancellationToken);
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancellationToken) => _backend.PutAsync(remotename, stream, cancellationToken);
        public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken) => _backend.PutAsync(remotename, filename, cancellationToken);
        public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken) => _backend.TestAsync(alsoWrite, cancellationToken);
        public void Dispose() => _backend.Dispose();
    }

    [Test]
    [Category("Targeted")]
    public void ParseFilenameHonorsCaseInsensitiveSetting()
    {
        const string canonical = "duplicati-20260102T030405Z.dlist.zip";
        const string lowerCase = "duplicati-20260102t030405z.dlist.zip";

        Assert.IsNotNull(VolumeBase.ParseFilename(canonical));
        Assert.IsNull(VolumeBase.ParseFilename(lowerCase));
        Assert.IsNotNull(VolumeBase.ParseFilename(lowerCase, caseInsensitive: true));
    }

    [Test]
    [Category("Targeted")]
    public async Task RemoteVerificationCanMatchCaseInsensitiveListingsAsync()
    {
        var options = TestOptions;
        options["no-encryption"] = "true";
        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");

        using (var controller = new Library.Main.Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await controller.BackupAsync([DATAFOLDER]));

        var dlist = Directory.EnumerateFiles(TARGETFOLDER, "*.dlist.*").Single();
        var foreignName = Path.GetFileName(dlist).Replace("duplicati-", "other-");
        File.Copy(dlist, Path.Combine(TARGETFOLDER, foreignName));

        BackendLoader.AddBackend(new CaseChangingBackend());
        var target = CaseChangingBackend.Key + "://" + TARGETFOLDER;

        CaseChangingBackend.ReturnCaseDuplicates = false;
        using (var controller = new Library.Main.Controller(target, options, null))
        {
            var error = Assert.ThrowsAsync<RemoteListVerificationException>(async () => await controller.TestAsync(1));
            Assert.AreEqual("MissingRemoteFiles", error.HelpID);
        }

        options["case-insensitive-remote"] = "true";
        using (var controller = new Library.Main.Controller(target, options, null))
            TestUtils.AssertResults(await controller.TestAsync(1));

        CaseChangingBackend.ReturnCaseDuplicates = true;
        using (var controller = new Library.Main.Controller(target, options, null))
        {
            var error = Assert.ThrowsAsync<RemoteListVerificationException>(async () => await controller.TestAsync(1));
            Assert.AreEqual("DuplicateRemoteFiles", error.HelpID);
        }
    }
}
