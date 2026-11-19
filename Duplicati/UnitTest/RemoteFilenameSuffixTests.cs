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
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest;

[TestFixture]
[Category("RemoteFilenameSuffix")]
public class RemoteFilenameSuffixTests : BasicSetupHelper
{
    private const string Suffix = ".bin";

    [Test]
    public async Task ABackupWithASuffixStoresEveryRemoteFileWithItAndCanBeRestoredAndRecreatedAsync()
    {
        var data = new byte[10_000];
        new Random(5601).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a.bin"), data);
        var options = TestOptions.Expand(new { remote_filename_suffix = Suffix });

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var remote = Directory.GetFiles(TARGETFOLDER).Select(Path.GetFileName).ToList();
        Assert.That(remote.Where(x => x!.Contains(".dblock.")), Is.Not.Empty);
        Assert.That(remote.Where(x => x!.Contains(".dlist.")), Is.Not.Empty);
        Assert.That(remote, Has.All.EndsWith(".aes" + Suffix));

        // Recreate the database from the remote files, which are only seen with the suffix
        File.Delete(DBFILE);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RepairAsync());

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, options.Expand(new { restore_path = RESTOREFOLDER }), null))
            TestUtils.AssertResults(await c.RestoreAsync(["*"]));
        Assert.That(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "a.bin")), Is.EqualTo(data));
    }

    [Test]
    public async Task ABackupThatLocksFilesOnStorageWithoutLockingSkipsTheLockAsync()
    {
        // The file backend does not support locking. The wrapper implements the locking
        // interface, so a check that asks the wrapper itself would try to lock and fail.
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "data");
        var options = TestOptions.Expand(new { remote_filename_suffix = Suffix, remote_file_lock_duration = "1D" });

        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, options, null);
        var result = await c.BackupAsync([DATAFOLDER]);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings, Has.Some.Contains("BackendDoesNotSupportLocking"));
    }

    [Test]
    public async Task NamesGetTheSuffixOnTheRemoteAndLoseItInTheListingAsync()
    {
        File.WriteAllText(Path.Combine(TARGETFOLDER, "foreign.txt"), "not ours");
        var source = Path.Combine(BASEFOLDER, "source.txt");
        File.WriteAllText(source, "content");
        var restored = Path.Combine(BASEFOLDER, "restored.txt");

        using var backend = BackendLoader.GetBackend("file://" + TARGETFOLDER, new Dictionary<string, string> { [RemoteFilenameSuffixBackend.OptionName] = Suffix });
        Assert.That(backend, Is.TypeOf<RemoteFilenameSuffixBackend>());

        await backend.PutAsync("x", source, CancellationToken.None);
        Assert.That(File.Exists(Path.Combine(TARGETFOLDER, "x" + Suffix)), Is.True);
        Assert.That(await Names(backend), Is.EqualTo(new[] { "x" }), "A file without the suffix is not one of ours");

        Assert.That(backend.TryGetCapability<IRenameEnabledBackend>(out var rename), Is.True);
        await rename!.RenameAsync("x", "y", CancellationToken.None);
        Assert.That(await Names(backend), Is.EqualTo(new[] { "y" }));

        await backend.GetAsync("y", restored, CancellationToken.None);
        Assert.That(File.ReadAllText(restored), Is.EqualTo("content"));

        await backend.DeleteAsync("y", CancellationToken.None);
        Assert.That(Directory.GetFiles(TARGETFOLDER).Select(Path.GetFileName), Is.EqualTo(new[] { "foreign.txt" }));
    }

    [Test]
    public void TheBackendIsOnlyWrappedWithTheOption()
    {
        using var plain = BackendLoader.GetBackend("file://" + TARGETFOLDER, []);
        Assert.That(plain, Is.Not.InstanceOf<IWrappingBackend>());

        using var empty = BackendLoader.GetBackend("file://" + TARGETFOLDER, new Dictionary<string, string> { [RemoteFilenameSuffixBackend.OptionName] = "" });
        Assert.That(empty, Is.Not.InstanceOf<IWrappingBackend>());
    }

    [Test]
    public void ASuffixWithAPathSeparatorIsRefused()
    {
        var ex = Assert.Throws<UserInformationException>(() => new RemoteFilenameSuffixBackend(new MinimalBackend(), "/x"));
        Assert.That(ex!.HelpID, Is.EqualTo("RemoteFilenameSuffixHasPathSeparator"));
    }

    /// <summary>
    /// Every optional backend interface in the interface assembly
    /// </summary>
    private static IEnumerable<Type> OptionalBackendInterfaces()
        => typeof(IBackend).Assembly.GetTypes()
            .Where(t => t.IsInterface && t != typeof(IBackend) && t != typeof(IWrappingBackend) && typeof(IBackend).IsAssignableFrom(t));

    [Test]
    public void TheWrapperImplementsEveryOptionalBackendInterface()
    {
        // A new interface that the wrapper does not implement would be lost for every wrapped backend
        Assert.That(OptionalBackendInterfaces(), Is.Not.Empty);
        Assert.That(OptionalBackendInterfaces().Where(t => !t.IsAssignableFrom(typeof(RemoteFilenameSuffixBackend))), Is.Empty);
    }

    [Test]
    public void TheWrapperSupportsWhatTheWrappedBackendSupports()
    {
        var supports = typeof(BackendCapabilityExtensions).GetMethod(nameof(BackendCapabilityExtensions.Supports))!;
        using var file = BackendLoader.GetBackend("file://" + TARGETFOLDER, []);
        foreach (var inner in new IBackend[] { new MinimalBackend(), file })
        {
            var wrapper = new RemoteFilenameSuffixBackend(inner, Suffix);
            foreach (var type in OptionalBackendInterfaces())
                Assert.That(supports.MakeGenericMethod(type).Invoke(null, [wrapper]), Is.EqualTo(type.IsInstanceOfType(inner)), $"{inner.GetType().Name} {type.Name}");
        }
    }

    private static async Task<List<string>> Names(IBackend backend)
    {
        var names = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            names.Add(entry.Name);
        return names;
    }

    /// <summary>
    /// A backend with none of the optional interfaces
    /// </summary>
    private sealed class MinimalBackend : IBackend
    {
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string DisplayName => "Minimal";
        public string ProtocolKey => "minimal";
        public string Description => "Minimal";
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(string remotename, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => throw new NotImplementedException();
        public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task CreateFolderAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Dispose() { }
    }
}
