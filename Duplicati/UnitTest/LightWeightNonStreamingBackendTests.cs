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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Backend;
using Duplicati.Library.Main.Operation.RemoteSynchronization;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// The remote synchronization tool, which also runs after a backup, used to refuse a backend
/// that cannot stream. The file backend cannot with --use-move-for-put, and the backup passes its
/// own options to the source of the synchronization, so a backup to a local folder with that
/// option could not be synchronized. Backends that only implement IBackend, such as Rclone, could
/// not be used at all. Such backends are now used through temporary files.
/// </summary>
[TestFixture]
public class LightWeightNonStreamingBackendTests : BasicSetupHelper
{
    /// <summary>
    /// A backend that only implements <see cref="IBackend"/>, wrapping the file backend.
    /// </summary>
    public class FileOnlyBackend : IBackend
    {
        public const string Key = "fileonly";

        private readonly Library.Backend.File? m_backend;

        public FileOnlyBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public FileOnlyBackend(string url, Dictionary<string, string?> options)
        {
            m_backend = new Library.Backend.File(new Library.Utility.RelaxedUri(url).SetScheme("file").ToString(), options);
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken) => m_backend!.ListAsync(cancellationToken);
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.PutAsync(remotename, filename, cancelToken);
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, filename, cancelToken);
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend!.DeleteAsync(remotename, cancelToken);
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend!.TestAsync(alsoWrite, cancelToken);
        public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend!.CreateFolderAsync(cancelToken);
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend!.GetDNSNamesAsync(cancelToken);
        public string DisplayName => "File backend without streaming";
        public string ProtocolKey => Key;
        public IList<ICommandLineArgument> SupportedCommands => m_backend?.SupportedCommands ?? new Library.Backend.File().SupportedCommands;
        public string Description => "A testing backend that only implements IBackend";
        public void Dispose() => m_backend?.Dispose();
    }

    private static LightWeightBackendManager CreateManager(string url, Dictionary<string, string?> options)
        => new(url, options, maxRetries: 2, retryDelay: 10, autoCreateFolders: false, retryWithExponentialBackoff: false, progressUpdater: null, backendProgressUpdater: null);

    /// <summary>
    /// Puts, lists, gets, renames and deletes a file through the manager.
    /// </summary>
    private async Task RoundTripAsync(string url, Dictionary<string, string?> options)
    {
        using var manager = CreateManager(url, options);

        using (var data = new MemoryStream([1, 2, 3]))
            await manager.PutAsync("a.bin", data, CancellationToken.None);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(TARGETFOLDER, "a.bin")));

        var names = (await manager.ListAsync(CancellationToken.None)).Select(x => x.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "a.bin" }, names);

        using (var read = new MemoryStream())
        {
            await manager.GetAsync("a.bin", read, CancellationToken.None);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, read.ToArray());
        }

        await manager.RenameAsync("a.bin", "b.bin", CancellationToken.None);
        Assert.IsFalse(File.Exists(Path.Combine(TARGETFOLDER, "a.bin")));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(TARGETFOLDER, "b.bin")));

        await manager.DeleteAsync("b.bin", CancellationToken.None);
        Assert.IsFalse(File.Exists(Path.Combine(TARGETFOLDER, "b.bin")));
    }

    [Test]
    [Category("Targeted")]
    [TestCase(false)]
    [TestCase(true)]
    public Task AFileDestinationWithMoveForPutWorksAsync(bool preventRename)
        => RoundTripAsync("file://" + TARGETFOLDER, new Dictionary<string, string?>
        {
            ["use-move-for-put"] = "true",
            // Forces the rename through a download and an upload
            ["prevent-backend-rename"] = preventRename ? "true" : "false",
        });

    [Test]
    [Category("Targeted")]
    public Task ABackendWithoutStreamingWorksAsync()
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new FileOnlyBackend());
        return RoundTripAsync(FileOnlyBackend.Key + "://" + TARGETFOLDER.Replace("\\", "/"), new Dictionary<string, string?>());
    }

    [Test]
    [Category("Targeted")]
    public async Task SynchronizationFromABackupWithMoveForPutCopiesTheFilesAsync()
    {
        var source = Path.Combine(TARGETFOLDER, "source");
        var destination = Path.Combine(TARGETFOLDER, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllBytes(Path.Combine(source, "duplicati-b1.dblock.zip"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(source, "duplicati-i1.dindex.zip"), [4, 5]);

        // The backup passes its own options, --use-move-for-put among them, to the source
        var config = new RemoteSynchronizationConfig(
            Src: "file://" + source,
            Dst: "file://" + destination,
            AutoCreateFolders: true,
            BackendRetries: 2,
            BackendRetryDelay: 10,
            BackendRetryWithExponentialBackoff: false,
            Confirm: true,
            DryRun: false,
            DstOptions: [],
            Force: false,
            GlobalOptions: [],
            LogFile: "",
            LogLevel: "Information",
            ParseArgumentsOnly: false,
            Progress: false,
            Retention: false,
            Retry: 2,
            SrcOptions: ["use-move-for-put=true"],
            VerifyContents: false,
            VerifyGetAfterPut: false);

        Assert.AreEqual(0, await RemoteSynchronizationRunner.RunAsync(config, CancellationToken.None));

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip")));
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-i1.dindex.zip")));
        Assert.IsTrue(File.Exists(Path.Combine(source, "duplicati-b1.dblock.zip")), "The source must be left as it was");
    }
}
