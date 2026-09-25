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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using NUnit.Framework;
using FileEntry = Duplicati.Library.Common.IO.FileEntry;

namespace Duplicati.UnitTest;

/// <summary>
/// Regression tests for https://github.com/duplicati/duplicati/issues/7271.
///
/// A --prefix value containing a '/' (e.g. "myjob/duplicati") produces remote
/// volume names like "myjob/duplicati-....dlist.zip". The folder part is part of
/// the opaque volume name and must NOT be split off by the backend manager's
/// path translation, which exists only for the sync operation's explicit
/// relative paths. On backends without folder support the split previously
/// dropped the folder part entirely (the backend was pointed at a sub-folder
/// URL it could not honour), so the volumes landed at the destination root and
/// the next list reported them as missing.
/// </summary>
[TestFixture]
[Category("Targeted")]
public class Issue7271Tests : BasicSetupHelper
{
    /// <summary>
    /// A minimal flat-namespace backend that mimics how Azure Blob Storage (and
    /// similar object stores) treat names: the name is an opaque key that may
    /// contain '/' characters, there is a single flat listing per URL, and the
    /// URL path is ignored (as the pre-fix Azure backend did). The backend does
    /// NOT implement <see cref="IFolderEnabledBackend"/>, so it exercises the
    /// backend manager's non-folder code path.
    /// </summary>
    private class FlatMemoryBackend : IStreamingBackend
    {
        /// <summary>
        /// The shared per-URL store of (name -> contents). Static so all backend
        /// instances created for the same URL (e.g. via BackendUrlOverride) share
        /// the same storage, like a real remote store.
        /// </summary>
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte[]>> stores = new();

        private readonly string urlKey;
        private readonly ConcurrentDictionary<string, byte[]> store;

        public FlatMemoryBackend()
        {
            // Required by the backend loader for option discovery; never used.
            urlKey = null!;
            store = null!;
        }

        public FlatMemoryBackend(string url, Dictionary<string, string?> options)
        {
            urlKey = url;
            store = stores.GetOrAdd(urlKey, _ => new ConcurrentDictionary<string, byte[]>());
        }

        /// <summary>
        /// Returns the stored (name -> contents) map for the given URL.
        /// </summary>
        public static IReadOnlyDictionary<string, byte[]> GetStore(string url)
            => stores.GetOrAdd(url, _ => new ConcurrentDictionary<string, byte[]>());

        public string DisplayName => "Flat Memory Backend";
        public string ProtocolKey => "flatmem";
        public string Description => "A flat-namespace in-memory test backend";
        public bool SupportsStreaming => true;

        public IList<ICommandLineArgument> SupportedCommands => [];

        public async IAsyncEnumerable<IFileEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancelToken)
        {
            foreach (var kvp in store)
                yield return new FileEntry(kvp.Key, kvp.Value.Length, DateTime.UtcNow, DateTime.UtcNow);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            store[remotename] = File.ReadAllBytes(localname);
            return Task.CompletedTask;
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            using var ms = new MemoryStream();
            await input.CopyToAsync(ms, cancelToken).ConfigureAwait(false);
            store[remotename] = ms.ToArray();
        }

        public Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
        {
            if (!store.TryGetValue(remotename, out var data))
                throw new FileMissingException($"File not found: {remotename}");
            File.WriteAllBytes(localname, data);
            return Task.CompletedTask;
        }

        public async Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
        {
            if (!store.TryGetValue(remotename, out var data))
                throw new FileMissingException($"File not found: {remotename}");
            await output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            if (!store.TryRemove(remotename, out _))
                throw new FileMissingException($"File not found: {remotename}");
            return Task.CompletedTask;
        }

        public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateFolderAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult<string[]>([]);
        public void Dispose() { }
    }

    private const string BackendUrl = "flatmem://issue7271";

    [SetUp]
    public void Setup()
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new FlatMemoryBackend());
    }

    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        prefix = "myjob/duplicati"
    });

    /// <summary>
    /// A backup with a slash in the prefix must store the volumes under the full
    /// unsplit name, and the verification after the backup must find them.
    /// </summary>
    [Test]
    public async Task BackupWithSlashInPrefixKeepsFullVolumeNamesAsync()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");

        using (var c = new Library.Main.Controller(BackendUrl, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var store = FlatMemoryBackend.GetStore(BackendUrl);

        // The volumes must be stored with the full "myjob/duplicati-..." name;
        // nothing may be stored with a stripped "duplicati-..." name.
        var strippedNames = store.Keys.Where(x => x.StartsWith("duplicati-", StringComparison.Ordinal)).ToArray();
        Assert.That(strippedNames, Is.Empty, "Volumes must not be stored with the folder part stripped");

        var fullNames = store.Keys.Where(x => x.StartsWith("myjob/duplicati-", StringComparison.Ordinal)).ToArray();
        Assert.That(fullNames.Length, Is.GreaterThan(0), "Volumes must be stored under the full prefix name");

        // A list must find the volumes under the same names the database recorded,
        // which is exactly what the pre-fix version failed to do.
        using (var c = new Library.Main.Controller(BackendUrl, Options, null))
            TestUtils.AssertResults(await c.ListAsync());

        // A test of all files must pass as well (this is the operation that
        // reported the volumes as missing in the regression).
        using (var c = new Library.Main.Controller(BackendUrl, Options, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
    }
}
