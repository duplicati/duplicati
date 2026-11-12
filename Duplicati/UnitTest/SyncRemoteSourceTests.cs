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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Sync with a remote source on the form "@mountpoint|url" and with several sources (#7783)
/// </summary>
[TestFixture]
[Category("Sync")]
public class SyncRemoteSourceTests : BasicSetupHelper
{
    private string target = null!;

    [SetUp]
    public void SetUpTarget()
    {
        target = Path.Combine(BASEFOLDER, "sync-target");
        if (Directory.Exists(target))
            Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        BackendLoader.AddBackend(new NoEntryLookupBackend());
    }

    private string CreateSource(string name)
    {
        var dir = Path.Combine(BASEFOLDER, name);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a " + name);
        File.WriteAllText(Path.Combine(dir, "sub", "b.txt"), "b " + name);
        return dir;
    }

    /// <summary>
    /// A remote source for a local folder, mounted at a folder that does not exist,
    /// with a query that must not end up in a folder name.
    /// The default query is an option the file backend supports on every system.
    /// </summary>
    private static string RemoteSource(string folder, string mountName, string scheme = "file", string query = "use-move-for-put=false")
        => "@" + Path.Combine(Path.GetPathRoot(folder)!, "duplicati-virtual-7783", mountName)
            + "|" + scheme + "://" + folder.Replace('\\', '/') + "?" + query;

    private async Task<ISyncResults> SyncAsync(string[] sources, Dictionary<string, string>? extra = null)
    {
        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
        };
        foreach (var kv in extra ?? [])
            opts[kv.Key] = kv.Value;

        using var c = new Controller("file://" + target.Replace('\\', '/'), opts, null);
        var res = await c.SyncAsync(sources, null);
        Assert.That(res.Errors, Is.Empty, string.Join(Environment.NewLine, res.Errors));
        return res;
    }

    [Test]
    [TestCase("file")]
    [TestCase(NoEntryLookupBackend.Key)]
    public async Task ARemoteSourceIsSyncedAsync(string scheme)
    {
        var source = CreateSource("remote-only");
        var sources = new[] { RemoteSource(source, "music", scheme) };

        var first = await SyncAsync(sources);
        Assert.That(first.Warnings, Is.Empty, string.Join(Environment.NewLine, first.Warnings));
        Assert.That(first.FilesUploaded, Is.EqualTo(2));
        Assert.That(File.ReadAllText(Path.Combine(target, "a.txt")), Is.EqualTo("a remote-only"));
        Assert.That(File.ReadAllText(Path.Combine(target, "sub", "b.txt")), Is.EqualTo("b remote-only"));

        var second = await SyncAsync(sources);
        Assert.That(second.Warnings, Is.Empty, string.Join(Environment.NewLine, second.Warnings));
        Assert.That(second.FilesUploaded, Is.Zero);
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EachOfSeveralSourcesHasItsOwnFolderAsync(bool firstIsRemote)
    {
        var a = CreateSource("several-a");
        var b = CreateSource("several-b");
        var sources = new[] { firstIsRemote ? RemoteSource(a, "music") : a, b };
        var firstFolder = firstIsRemote ? "music" : "several-a";

        // A file next to the source folders on the destination is not part of any source
        File.WriteAllText(Path.Combine(target, "unrelated.txt"), "keep");

        var first = await SyncAsync(sources);
        Assert.That(first.Warnings, Is.Empty, string.Join(Environment.NewLine, first.Warnings));
        Assert.That(first.FilesUploaded, Is.EqualTo(4));
        Assert.That(File.ReadAllText(Path.Combine(target, firstFolder, "a.txt")), Is.EqualTo("a several-a"));
        Assert.That(File.ReadAllText(Path.Combine(target, firstFolder, "sub", "b.txt")), Is.EqualTo("b several-a"));
        Assert.That(File.ReadAllText(Path.Combine(target, "several-b", "sub", "b.txt")), Is.EqualTo("b several-b"));
        Assert.That(Directory.GetDirectories(target).Select(Path.GetFileName), Is.EquivalentTo(new[] { firstFolder, "several-b" }));
        Assert.That(File.ReadAllText(Path.Combine(target, "unrelated.txt")), Is.EqualTo("keep"));

        var second = await SyncAsync(sources);
        Assert.That(second.FilesUploaded, Is.Zero);
        Assert.That(second.FoldersCreated, Is.Zero);
    }

    [Test]
    public async Task SnapshotFoldersUseTheMountNameOfARemoteSourceAsync()
    {
        var a = CreateSource("snapshot-a");
        var b = CreateSource("snapshot-b");

        var res = await SyncAsync([RemoteSource(a, "music"), b], new() { [Options.SYNC_SNAPSHOT_FOLDERS_OPTION] = "true" });

        Assert.That(res.Warnings, Is.Empty, string.Join(Environment.NewLine, res.Warnings));
        var snapshot = Directory.GetDirectories(target).Single();
        Assert.That(Directory.GetDirectories(snapshot).Select(Path.GetFileName), Is.EquivalentTo(new[] { "music", "snapshot-b" }));
        Assert.That(File.ReadAllText(Path.Combine(snapshot, "music", "sub", "b.txt")), Is.EqualTo("b snapshot-a"));
    }

    [Test]
    public async Task AWarningForARemoteSourceDoesNotShowItsCredentialsAsync()
    {
        var missing = Path.Combine(BASEFOLDER, "missing-remote-source");
        if (Directory.Exists(missing))
            Directory.Delete(missing, true);
        var b = CreateSource("warning-b");

        // The file backend supports the credentials only on Windows; elsewhere they add
        // a warning about unsupported options, which must not show them either
        var res = await SyncAsync([RemoteSource(missing, "music", query: "auth-username=user&auth-password=secret-7783"), b]);

        Assert.That(res.Warnings, Is.Not.Empty);
        Assert.That(res.Warnings, Has.None.Contains("secret-7783"));
        Assert.That(File.ReadAllText(Path.Combine(target, "warning-b", "a.txt")), Is.EqualTo("a warning-b"));
    }

    [Test]
    public void TheTimesOfARemoteEntryAreInUtc()
    {
        // The file backend reports local times; a sync compares them with UTC times, so a
        // local time taken as UTC makes every file look changed, in any time zone but UTC
        var local = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var fromLocal = new Library.SourceProvider.BackendSourceFileEntry(null!, "a.txt", false, false, local, local, 1);
        var fromUtc = new Library.SourceProvider.BackendSourceFileEntry(null!, "a.txt", false, false, utc, utc, 1);

        Assert.That(fromLocal.LastModificationUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(fromLocal.LastModificationUtc, Is.EqualTo(local.ToUniversalTime()));
        Assert.That(fromLocal.CreatedUtc, Is.EqualTo(local.ToUniversalTime()));
        Assert.That(fromUtc.LastModificationUtc, Is.EqualTo(utc));
        Assert.That(fromUtc.CreatedUtc, Is.EqualTo(utc));
    }

    /// <summary>
    /// A file backend that cannot look up a single entry, as the SSH backend
    /// </summary>
    private sealed class NoEntryLookupBackend : IStreamingBackend, IFolderEnabledBackend
    {
        public const string Key = "test-sync-no-entry-lookup";
        private readonly Library.Backend.File backend;

        public NoEntryLookupBackend() => backend = new Library.Backend.File();

        public NoEntryLookupBackend(string url, Dictionary<string, string?> options)
            => backend = new Library.Backend.File("file" + url.Substring(Key.Length), options);

        public string ProtocolKey => Key;
        public string DisplayName => "Test backend without entry lookup";
        public string Description => DisplayName;
        public bool SupportsStreaming => true;
        public IList<ICommandLineArgument> SupportedCommands => backend.SupportedCommands;

        public bool HasCaseSensitivePaths => false;

        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(null);

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken) => backend.ListAsync(cancellationToken);
        public IAsyncEnumerable<IFileEntry> ListAsync(string? path, CancellationToken cancellationToken) => backend.ListAsync(path, cancellationToken);
        public Task GetAsync(string remotename, Stream stream, CancellationToken cancellationToken) => backend.GetAsync(remotename, stream, cancellationToken);
        public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken) => backend.GetAsync(remotename, filename, cancellationToken);
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string remotename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateFolderAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken) => backend.TestAsync(false, cancellationToken);
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancellationToken) => backend.GetDNSNamesAsync(cancellationToken);
        public void Dispose() => backend.Dispose();
    }
}
