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
using Duplicati.Library.Main.Operation.RemoteSynchronization;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests mirroring a Duplicati destination through the backend manager. The earlier remote
/// synchronization refused a backend that cannot stream: the file backend cannot with
/// --use-move-for-put, and backends that only implement IBackend, such as Rclone, could not be
/// used at all.
/// </summary>
[TestFixture]
public class RemoteReplicationTests : BasicSetupHelper
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

    private static RemoteSynchronizationToolConfig Config(string src, string dst, List<string>? srcOptions = null)
        => new(Src: src, Dst: dst, SecretProvider: "", SecretProviderPattern: "", SecretProviderCache: "", BackendRetries: 0, BackendRetryDelay: 0, Confirm: true, SrcOptions: srcOptions);

    private (string Source, string Destination) CreateFolders()
    {
        var source = Path.Combine(TARGETFOLDER, "source");
        var destination = Path.Combine(TARGETFOLDER, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllBytes(Path.Combine(source, "duplicati-b1.dblock.zip"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(source, "duplicati-i1.dindex.zip"), [4, 5]);
        return (source, destination);
    }

    [Test]
    [Category("Targeted")]
    public async Task ABackendWithoutStreamingWorksOnBothSidesAsync()
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new FileOnlyBackend());
        var (source, destination) = CreateFolders();
        File.WriteAllBytes(Path.Combine(destination, "duplicati-b2.dblock.zip"), [9]);

        var url = (string folder) => FileOnlyBackend.Key + "://" + folder.Replace("\\", "/");
        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(Config(url(source), url(destination)), CancellationToken.None));

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip")));
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-i1.dindex.zip")));
        Assert.IsFalse(File.Exists(Path.Combine(destination, "duplicati-b2.dblock.zip")), "A volume the source does not have was left in the destination.");
    }

    [Test]
    [Category("Targeted")]
    public async Task SynchronizationFromABackupWithMoveForPutCopiesTheFilesAsync()
    {
        var (source, destination) = CreateFolders();

        // The backup passes its own options, --use-move-for-put among them, to the source
        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(Config("file://" + source, "file://" + destination, ["use-move-for-put=true"]), CancellationToken.None));

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip")));
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-i1.dindex.zip")));
        Assert.IsTrue(File.Exists(Path.Combine(source, "duplicati-b1.dblock.zip")), "The source must be left as it was");
    }

    [Test]
    [Category("Targeted")]
    public async Task AVolumeThatChangedIsCopiedAgainAndForceCopiesEverythingAsync()
    {
        var (source, destination) = CreateFolders();
        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(Config("file://" + source, "file://" + destination), CancellationToken.None));

        // A different size is a change, whatever the timestamps say
        File.WriteAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip"), [7]);
        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(Config("file://" + source, "file://" + destination), CancellationToken.None));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip")));

        // The same size with a newer destination is left alone, and force overwrites it
        File.WriteAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip"), [3, 2, 1]);
        File.SetLastWriteTimeUtc(Path.Combine(destination, "duplicati-b1.dblock.zip"), DateTime.UtcNow.AddHours(1));
        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(Config("file://" + source, "file://" + destination), CancellationToken.None));
        CollectionAssert.AreEqual(new byte[] { 3, 2, 1 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip")));

        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(Config("file://" + source, "file://" + destination) with { Force = true }, CancellationToken.None));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(destination, "duplicati-b1.dblock.zip")));
    }

    [TestCase(true)]
    [TestCase(false)]
    [Category("Targeted")]
    public async Task KeepModificationTimeGivesTheCopiesTheTimeOfTheVolumesAsync(bool keepTime)
    {
        var (source, destination) = CreateFolders();
        var sourceTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        foreach (var file in Directory.EnumerateFiles(source))
            File.SetLastWriteTimeUtc(file, sourceTime);

        var config = Config("file://" + source, "file://" + destination) with
        {
            GlobalOptions = keepTime ? ["sync-keep-modification-time=true"] : []
        };
        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(config, CancellationToken.None));

        foreach (var file in Directory.EnumerateFiles(destination))
        {
            if (keepTime)
                Assert.AreEqual(sourceTime, File.GetLastWriteTimeUtc(file), Path.GetFileName(file));
            else
                Assert.Greater(File.GetLastWriteTimeUtc(file), sourceTime.AddYears(1), Path.GetFileName(file));
        }

        // A copy with the time of its volume is not older than the volume, so it is not copied again
        var marker = Path.Combine(destination, "duplicati-b1.dblock.zip");
        File.WriteAllBytes(marker, [3, 2, 1]);
        File.SetLastWriteTimeUtc(marker, keepTime ? sourceTime : DateTime.UtcNow);
        Assert.AreEqual(0, await RemoteSynchronizationTool.RunAsync(config, CancellationToken.None));
        CollectionAssert.AreEqual(new byte[] { 3, 2, 1 }, File.ReadAllBytes(marker), "An unchanged volume was copied again.");
    }

    [Test]
    [Category("Targeted")]
    public void TheVolumeFilterSelectsTheVolumesOfTheBackup()
    {
        var filter = RemoteReplication.CreateVolumeFilter("duplicati");
        var any = RemoteReplication.CreateVolumeFilter(null);
        bool Matches(Library.Utility.IFilter f, string name) => Library.Utility.FilterExpression.Matches(f, "/" + name, out _);

        foreach (var name in new[]
        {
            "duplicati-b0123456789abcdef0123456789abcdef.dblock.zip",
            "duplicati-b0123456789abcdef0123456789abcdef.dblock.zip.aes",
            "duplicati-i0123456789abcdef0123456789abcdef.dindex.zip.aes",
            "duplicati-20260101T120000Z.dlist.zip.aes",
            "duplicati-b0123456789abcdef0123456789abcdef.dblock.zip.aes.par2",
            "DUPLICATI-B0123456789ABCDEF0123456789ABCDEF.DBLOCK.ZIP",
        })
        {
            Assert.IsTrue(Matches(filter, name), name);
            Assert.IsTrue(Matches(any, name), name);
        }

        foreach (var name in new[] { "readme.txt", "duplicati-verification.json", "lost+found/", "duplicati-b01.dblock", "xduplicati-b01.dblock.zip" })
            Assert.IsFalse(Matches(filter, name), name);

        Assert.IsFalse(Matches(filter, "other-b0123456789abcdef0123456789abcdef.dblock.zip"), "A volume of another backup was selected.");
        Assert.IsTrue(Matches(any, "other-b0123456789abcdef0123456789abcdef.dblock.zip"));
        Assert.IsFalse(Matches(any, "readme.txt"));
    }
}
