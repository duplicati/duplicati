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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the metadata produced by the <see cref="BackendSourceProvider"/>
/// </summary>
[TestFixture]
public class BackendSourceProviderTests
{
    /// <summary>
    /// The timestamp reported by the stub backend
    /// </summary>
    private static readonly DateTime TIMESTAMP = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// <summary>
    /// A backend that lists a single file
    /// </summary>
    private sealed class StubBackend : IFolderEnabledBackend
    {
        public string DisplayName => "Stub Backend";
        public string ProtocolKey => "stub";
        public string Description => "A testing backend";
        public IList<ICommandLineArgument> SupportedCommands => [];

        public async IAsyncEnumerable<IFileEntry> ListAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new FileEntry("file.txt", 42, TIMESTAMP, TIMESTAMP, false, false) { Created = TIMESTAMP };
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            => ListAsync("", cancellationToken);

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());
        public Task<IFileEntry> GetEntryAsync(string path, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => Task.CompletedTask;
        public Task CreateFolderAsync(CancellationToken cancelToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// Gets the root entry of a provider wrapping a stub backend
    /// </summary>
    /// <param name="url">The url the backend was created from</param>
    /// <returns>The provider and its root entry</returns>
    private static async Task<(BackendSourceProvider Provider, ISourceProviderEntry Root)> GetRootAsync(string url)
    {
        var provider = new BackendSourceProvider(new StubBackend(), "/mnt/stub/", url);
        var root = await provider.EnumerateAsync(CancellationToken.None).SingleAsync();
        return (provider, root);
    }

    [Test]
    [Category("SourceProvider")]
    public async Task RootMetadataDescribesBackendAsync()
    {
        var (provider, root) = await GetRootAsync("s3://mybucket.s3.amazonaws.com/path?auth-username=user");
        using (provider)
        {
            var metadata = await root.GetMinorMetadata(CancellationToken.None);

            Assert.That(metadata["backend:v"], Is.EqualTo(BackendSourceProvider.METADATA_VERSION));
            Assert.That(metadata["backend:Type"], Is.EqualTo(BackendSourceProvider.METADATA_ROOT_TYPE));
            Assert.That(metadata["backend:Name"], Is.EqualTo("Stub Backend"));
            Assert.That(metadata["backend:Protocol"], Is.EqualTo("stub"));
            Assert.That(metadata["backend:Host"], Is.EqualTo(".amazonaws.com"));

            // The backup process adds to the result, so each call must return its own instance
            Assert.That(await root.GetMinorMetadata(CancellationToken.None), Is.Not.SameAs(metadata));
        }
    }

    [Test]
    [Category("SourceProvider")]
    public async Task RootMetadataOmitsUnknownS3HostAsync()
    {
        var (provider, root) = await GetRootAsync("s3://nas.internal.example.com/path");
        using (provider)
        {
            var metadata = await root.GetMinorMetadata(CancellationToken.None);

            Assert.That(metadata["backend:Protocol"], Is.EqualTo("stub"));
            Assert.That(metadata.ContainsKey("backend:Host"), Is.False);
        }
    }

    [Test]
    [Category("SourceProvider")]
    public async Task RootMetadataOmitsHostForNonS3Async()
    {
        // Only S3 urls have the host reported, even if the host is a known public server
        var (provider, root) = await GetRootAsync("ssh://files.s3.amazonaws.com/path");
        using (provider)
        {
            var metadata = await root.GetMinorMetadata(CancellationToken.None);

            Assert.That(metadata["backend:Protocol"], Is.EqualTo("stub"));
            Assert.That(metadata.ContainsKey("backend:Host"), Is.False);
        }
    }

    [Test]
    [Category("SourceProvider")]
    public async Task ChildEntriesCarryBackendTimestampsAsync()
    {
        var (provider, root) = await GetRootAsync("stub://host/path");
        using (provider)
        {
            var child = await root.Enumerate(CancellationToken.None).SingleAsync();

            Assert.That(child.LastModificationUtc, Is.EqualTo(TIMESTAMP));
            Assert.That(child.CreatedUtc, Is.EqualTo(TIMESTAMP));
            Assert.That(child.Size, Is.EqualTo(42));
            Assert.That(await child.GetMinorMetadata(CancellationToken.None), Is.Empty);
        }
    }
}
