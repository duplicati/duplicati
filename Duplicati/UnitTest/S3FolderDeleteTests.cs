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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the recursive folder delete (<see cref="IFolderDeleteEnabledBackend"/>) of the
/// backends that store objects in a bucket. A bucket has no folders, so the folder is
/// every object whose key starts with the folder path and a separator.
/// </summary>
[TestFixture]
[Category("Backend")]
public class S3FolderDeleteTests
{
    /// <summary>
    /// A bucket held in memory: the keys of its objects, listed and deleted as an S3 client does
    /// </summary>
    private sealed class BucketClient(params string[] keys) : IS3Client
    {
        public SortedSet<string> Keys { get; } = new(keys, StringComparer.Ordinal);

        /// <summary>The keys a delete was sent for, in order</summary>
        public List<string> Deletes { get; } = [];

        /// <summary>The number of listings that were started</summary>
        public int Listings { get; private set; }

        /// <summary>Whether a delete of a key the bucket does not have fails, as some servers make it</summary>
        public bool MissingKeyIsAnError { get; init; }

        /// <summary>The number of keys in one page of a listing</summary>
        public int PageSize { get; init; } = 2;

        /// <summary>The number of pages that were read, over all listings</summary>
        public int Pages { get; private set; }

        public async IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.IsTrue(recursive, "A folder is deleted from a listing of everything below it");
            Listings++;

            if (!string.IsNullOrWhiteSpace(prefix))
                prefix = Util.AppendDirSeparator(prefix, "/");

            // As S3 does: each page is read from the bucket as it is when the page is asked
            // for, and continues after the last key of the page before, so the keys the
            // caller deletes while it reads are gone from the pages that follow
            string? marker = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                Pages++;
                Assert.Less(Pages, 1000, "The listing does not end");

                var page = Keys
                    .Where(key => key != prefix && key.StartsWith(prefix, StringComparison.Ordinal))
                    .Where(key => marker == null || string.CompareOrdinal(key, marker) > 0)
                    .Take(PageSize)
                    .ToList();

                foreach (var key in page)
                    yield return new FileEntry(key[prefix.Length..], 1) { IsFolder = key.EndsWith('/') };

                if (page.Count < PageSize)
                    yield break;
                marker = page[^1];
            }
        }

        public Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken)
        {
            Deletes.Add(keyName);
            if (!Keys.Remove(keyName) && MissingKeyIsAnError)
                throw new FileMissingException($"File {keyName} not found");
            return Task.CompletedTask;
        }

        public Task<IFileEntry?> GetFileEntryAsync(string bucketName, string keyName, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task AddBucketAsync(string bucketName, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public Task GetFileStreamAsync(string bucketName, string keyName, string versionId, Stream target, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public IAsyncEnumerable<(IFileEntry Entry, string VersionId)> ListBucketVersionsAtAsync(string bucketName, string prefix, bool recursive, DateTime atUtc, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public string? GetDnsHost()
            => throw new NotImplementedException();
        public Task AddFileStreamAsync(string bucketName, string keyName, Stream source, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public Task<DateTime?> GetObjectLockUntilAsync(string bucketName, string keyName, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public Task SetObjectLockUntilAsync(string bucketName, string keyName, DateTime lockUntilUtc, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public void Dispose() { }
    }

    /// <summary>
    /// The paths no backend may delete: the backend folder itself and anything outside it
    /// </summary>
    private static readonly string[] RefusedPaths = ["", " ", "/gone", "gone/", "gone//inner", ".", "gone/../other", "..", "gone\\inner"];

    /// <summary>
    /// Creates the backend of the given kind over the bucket
    /// </summary>
    /// <param name="kind">"s3" or "e2"</param>
    /// <param name="path">The bucket and the folder in it, as the url names them</param>
    /// <param name="client">The bucket</param>
    private static IFolderDeleteEnabledBackend Create(string kind, string path, BucketClient client)
        => kind switch
        {
            "s3" => new S3($"s3://{path}", new Dictionary<string, string?>(), client),
            "e2" => new Idrivee2Backend($"e2://{path}", new Dictionary<string, string?> { ["access_key_id"] = "id", ["access_key_secret"] = "secret" }, client),
            _ => throw new ArgumentException(kind)
        };

    /// <summary>
    /// A bucket with the backend folder "backup", a folder "gone" in it with a sub-folder, and
    /// objects next to it whose keys start the same way
    /// </summary>
    private static BucketClient Bucket(bool missingKeyIsAnError)
        => new BucketClient(
            "backup/keep.txt",
            "backup/gone/",
            "backup/gone/a.txt",
            "backup/gone/inner/",
            "backup/gone/inner/b.txt",
            "backup/gone/inner/deeper/c.txt",
            "backup/gone-too/d.txt",
            "backup/gonefile",
            "other/gone/e.txt")
        { MissingKeyIsAnError = missingKeyIsAnError };

    [TestCase("s3")]
    [TestCase("e2")]
    public async Task TheFolderIsEveryObjectBelowItAsync(string kind)
    {
        var client = Bucket(false);
        using var backend = Create(kind, "bucket/backup", client);

        await backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "backup/gone-too/d.txt", "backup/gonefile", "backup/keep.txt", "other/gone/e.txt" },
            client.Keys.ToArray(),
            "Only the objects below the folder, and the object that stands for it, are deleted");
    }

    [TestCase("s3")]
    [TestCase("e2")]
    public async Task AFolderBelowAFolderIsDeletedOnItsOwnAsync(string kind)
    {
        var client = Bucket(false);
        using var backend = Create(kind, "bucket/backup", client);

        await backend.DeleteFolderRecursiveAsync("gone/inner", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "backup/gone-too/d.txt", "backup/gone/", "backup/gone/a.txt", "backup/gonefile", "backup/keep.txt", "other/gone/e.txt" },
            client.Keys.ToArray());
    }

    [TestCase("s3")]
    [TestCase("e2")]
    public async Task ABackendAtTheTopOfTheBucketDeletesOnlyTheFolderAsync(string kind)
    {
        var client = new BucketClient("keep.txt", "gone/a.txt", "gone/inner/b.txt", "gone-too/c.txt");
        using var backend = Create(kind, "bucket", client);

        await backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "gone-too/c.txt", "keep.txt" }, client.Keys.ToArray());
    }

    [TestCase("s3", false)]
    [TestCase("s3", true)]
    [TestCase("e2", false)]
    [TestCase("e2", true)]
    public async Task AFolderThatIsNotThereIsDeletedAsync(string kind, bool missingKeyIsAnError)
    {
        var client = Bucket(missingKeyIsAnError);
        var before = client.Keys.ToArray();
        using var backend = Create(kind, "bucket/backup", client);

        await backend.DeleteFolderRecursiveAsync("never-there", CancellationToken.None);

        CollectionAssert.AreEqual(before, client.Keys.ToArray());
    }

    [TestCase("s3")]
    [TestCase("e2")]
    public async Task AServerThatReportsAMissingKeyStillDeletesTheFolderAsync(string kind)
    {
        // The folder below has no object that stands for it, so the delete of that key finds nothing
        var client = new BucketClient("backup/keep.txt", "backup/gone/a.txt") { MissingKeyIsAnError = true };
        using var backend = Create(kind, "bucket/backup", client);

        await backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "backup/keep.txt" }, client.Keys.ToArray());
    }

    [TestCase("s3", 1)]
    [TestCase("s3", 7)]
    [TestCase("e2", 1)]
    [TestCase("e2", 7)]
    public async Task AFolderOfManyPagesIsDeletedWhileItIsListedAsync(string kind, int pageSize)
    {
        // Every key is deleted as the listing reads it, so each page is read from a bucket
        // the deletes have changed since the page before
        var keys = Enumerable.Range(0, 50).Select(i => $"backup/gone/{i:D3}.txt")
            .Concat(Enumerable.Range(0, 10).Select(i => $"backup/gone/sub{i}/x.txt"))
            .Append("backup/keep.txt")
            .Append("backup/gone-too/d.txt")
            .ToArray();
        var client = new BucketClient(keys) { PageSize = pageSize };
        using var backend = Create(kind, "bucket/backup", client);

        await backend.DeleteFolderRecursiveAsync("gone", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "backup/gone-too/d.txt", "backup/keep.txt" }, client.Keys.ToArray());
        Assert.AreEqual(60, client.Deletes.Count(x => x != "backup/gone/"), "A key was deleted twice, or not at all: " + string.Join(", ", client.Deletes));
        Assert.LessOrEqual(client.Pages, 60 / pageSize + 1, "The listing read pages it had read before");
    }

    [TestCase("s3")]
    [TestCase("e2")]
    public void APathOutsideTheBackendFolderIsRefused(string kind)
    {
        var client = Bucket(false);
        using var backend = Create(kind, "bucket/backup", client);

        foreach (var path in RefusedPaths)
            Assert.ThrowsAsync<ArgumentException>(() => backend.DeleteFolderRecursiveAsync(path, CancellationToken.None), $"\"{path}\"");

        Assert.AreEqual(0, client.Listings);
        Assert.IsEmpty(client.Deletes);
    }

    [Test]
    public void TheClientRefusesToDeleteTheWholeBucket()
    {
        var client = Bucket(false);

        foreach (var key in new[] { "", "/", " " })
            Assert.ThrowsAsync<ArgumentException>(() => client.DeleteFolderAsync("bucket", key, CancellationToken.None), $"\"{key}\"");

        Assert.AreEqual(0, client.Listings);
        Assert.IsEmpty(client.Deletes);
    }
}
