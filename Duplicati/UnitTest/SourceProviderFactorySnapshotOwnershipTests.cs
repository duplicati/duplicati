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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.SourceProvider;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The snapshot the source provider factory creates is shared by the file source and
/// the snapshot-aware providers. These tests pin down who releases it.
/// </summary>
[TestFixture]
public class SourceProviderFactorySnapshotOwnershipTests : BasicSetupHelper
{
    /// <summary>
    /// A snapshot-aware provider that records the snapshot it is given and whether it was disposed
    /// </summary>
    private sealed class SnapshotAwareProvider : ISourceProviderModule, ISnapshotAwareModule
    {
        public const string SCHEME = "test-snapshot-aware";

        /// <summary>
        /// The provider instances the factory created, so a test can look at them
        /// </summary>
        public static readonly List<SnapshotAwareProvider> Created = [];

        private readonly string _mountPoint;
        private readonly string _snapshotPath;

        public ISnapshotService? ReceivedSnapshot { get; private set; }
        public bool Disposed { get; private set; }

        public SnapshotAwareProvider()
        {
            _mountPoint = string.Empty;
            _snapshotPath = string.Empty;
        }

        public SnapshotAwareProvider(string url, string mountPoint, Dictionary<string, string?> options)
        {
            _mountPoint = mountPoint;
            // The path to snapshot is carried in the url, as "scheme://<path>"
            _snapshotPath = url.Substring(SCHEME.Length + 3);
            Created.Add(this);
        }

        public string Key => SCHEME;
        public string DisplayName => "Test snapshot-aware provider";
        public string Description => "Test provider that reads through the shared snapshot";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => false;

        public Task<IEnumerable<string>> GetSnapshotPathsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<string>>([_snapshotPath]);

        public void SetSnapshotService(ISnapshotService? snapshotService)
            => ReceivedSnapshot = snapshotService;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// A snapshot that only records whether it was disposed
    /// </summary>
    private sealed class RecordingSnapshotService : ISnapshotService
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;

        public IEnumerable<string> SourceEntries => throw new NotImplementedException();
        public IEnumerable<ISourceProviderEntry> EnumerateFilesystemEntries() => throw new NotImplementedException();
        public IEnumerable<ISourceProviderEntry> EnumerateFilesystemEntries(ISourceProviderEntry parent) => throw new NotImplementedException();
        public ISourceProviderEntry? GetFilesystemEntry(string path, bool isFolder) => throw new NotImplementedException();
        public DateTime GetLastWriteTimeUtc(string localPath) => throw new NotImplementedException();
        public DateTime GetCreationTimeUtc(string localPath) => throw new NotImplementedException();
        public Stream OpenRead(string localPath) => throw new NotImplementedException();
        public Task<Stream> OpenReadAsync(string localPath, CancellationToken cancellationToken) => throw new NotImplementedException();
        public long GetFileSize(string localPath) => throw new NotImplementedException();
        public string GetSymlinkTarget(string localPath) => throw new NotImplementedException();
        public FileAttributes GetAttributes(string localPath) => throw new NotImplementedException();
        public Dictionary<string, string?> GetMetadata(string localPath, bool isSymlink) => throw new NotImplementedException();
        public bool IsBlockDevice(string localPath) => throw new NotImplementedException();
        public string? HardlinkTargetID(string localPath) => throw new NotImplementedException();
        public string ConvertToLocalPath(string snapshotPath) => throw new NotImplementedException();
        public string ConvertToSnapshotPath(string localPath) => throw new NotImplementedException();
        public bool FileExists(string localFilePath) => throw new NotImplementedException();
        public bool DirectoryExists(string localFolderPath) => throw new NotImplementedException();
    }

    private static Options SnapshotOffOptions()
        => new Options(new Dictionary<string, string?>
        {
            ["snapshot-policy"] = "off",
            ["backupread-policy"] = "off",
        });

    private string SnapshotAwareSource()
        => $"@{Path.Combine(Path.GetPathRoot(this.DATAFOLDER)!, "snapmnt")}|{SnapshotAwareProvider.SCHEME}://{this.DATAFOLDER}";

    [SetUp]
    public void RegisterProvider()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new SnapshotAwareProvider());
        SnapshotAwareProvider.Created.Clear();
    }

    [Test]
    public void Decorator_disposes_the_provider_and_then_the_snapshot()
    {
        var inner = new SnapshotAwareProvider();
        var snapshot = new RecordingSnapshotService();

        var owning = new SnapshotOwningSourceProvider(inner, snapshot);
        owning.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(inner.Disposed, Is.True, "The wrapped provider must be disposed");
            Assert.That(snapshot.Disposed, Is.True, "The snapshot must be disposed with the provider");
        });
    }

    [Test]
    public async Task Without_a_file_source_the_snapshot_is_released_with_the_provider_that_uses_itAsync()
    {
        var provider = await SourceProviderFactory.GetSourceProviderAsync([SnapshotAwareSource()], SnapshotOffOptions(), CancellationToken.None);

        Assert.That(SnapshotAwareProvider.Created, Has.Count.EqualTo(1));
        var created = SnapshotAwareProvider.Created[0];

        Assert.That(created.ReceivedSnapshot, Is.Not.Null, "The provider must be given the shared snapshot");
        Assert.That(provider, Is.InstanceOf<SnapshotOwningSourceProvider>(), "With no file source, the provider that uses the snapshot must release it");

        var owning = (SnapshotOwningSourceProvider)provider;
        Assert.Multiple(() =>
        {
            Assert.That(owning.Inner, Is.SameAs(created));
            Assert.That(owning.SnapshotService, Is.SameAs(created.ReceivedSnapshot));
        });

        provider.Dispose();
        Assert.That(created.Disposed, Is.True);
    }

    [Test]
    public async Task With_a_file_source_the_file_source_keeps_the_snapshotAsync()
    {
        var provider = await SourceProviderFactory.GetSourceProviderAsync([this.DATAFOLDER, SnapshotAwareSource()], SnapshotOffOptions(), CancellationToken.None);
        using var _ = provider;

        Assert.That(SnapshotAwareProvider.Created, Has.Count.EqualTo(1));
        var created = SnapshotAwareProvider.Created[0];

        Assert.That(provider, Is.InstanceOf<Combiner>());
        var providers = ((Combiner)provider).Providers.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(providers.OfType<LocalFileSource>().Count(), Is.EqualTo(1), "The file source owns the snapshot");
            Assert.That(providers, Does.Contain(created), "The snapshot-aware provider is not wrapped when a file source exists");
            Assert.That(providers.OfType<SnapshotOwningSourceProvider>(), Is.Empty);
        });
    }
}
