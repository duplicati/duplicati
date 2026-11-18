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
using Duplicati.Library.SourceProvider;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the listing of a single folder (<see cref="IFolderEnabledBackend.ListAsync(string?, CancellationToken)"/>)
/// of the backends that store objects in a bucket. The callers (the sync operation and the
/// backend source provider) walk the folders one level at a time: they use the names as they
/// are, skip a name that is a path, and only go into what is listed as a folder. A listing of
/// everything below the folder therefore hides every object in a sub-folder from them.
/// </summary>
[TestFixture]
[Category("Idrivee2")]
public class Idrivee2FolderListTests
{
    /// <summary>
    /// A bucket held in memory, listed the way <see cref="S3AwsClient.ListBucketAsync"/> lists one:
    /// a recursive listing uses no delimiter and returns every object below the prefix, where only
    /// an object whose key ends in a separator is a folder; a listing that is not recursive
    /// uses the separator as the delimiter and returns the objects directly below the prefix,
    /// and one folder for each group of keys that continue past a separator.
    /// </summary>
    private sealed class BucketClient(params string[] keys) : IS3Client
    {
        public SortedSet<string> Keys { get; } = new(keys, StringComparer.Ordinal);

        /// <summary>The recursive flag of each listing, in order</summary>
        public List<bool> RecursiveFlags { get; } = [];

        /// <summary>The prefix of each listing, in order</summary>
        public List<string> Prefixes { get; } = [];

        public async IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RecursiveFlags.Add(recursive);
            await Task.Yield();

            if (!string.IsNullOrWhiteSpace(prefix))
                prefix = Util.AppendDirSeparator(prefix, "/");
            Prefixes.Add(prefix);

            var below = Keys.Where(key => key != prefix && key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (recursive)
            {
                foreach (var key in below)
                    yield return new FileEntry(key[prefix.Length..], 1) { IsFolder = key.EndsWith('/') };
                yield break;
            }

            // The common prefixes: the object that stands for a folder is grouped with what is in it
            var folders = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var key in below)
            {
                var name = key[prefix.Length..];
                var separator = name.IndexOf('/');
                if (separator >= 0)
                    folders.Add(name[..(separator + 1)]);
            }

            foreach (var folder in folders)
                yield return new FileEntry(folder, -1) { IsFolder = true };

            foreach (var key in below)
                if (!key[prefix.Length..].Contains('/'))
                    yield return new FileEntry(key[prefix.Length..], 1);
        }

        public Task<IFileEntry?> GetFileEntryAsync(string bucketName, string keyName, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken)
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
    /// Creates the backend of the given kind over the bucket
    /// </summary>
    /// <param name="kind">"s3" or "e2"</param>
    /// <param name="path">The bucket and the folder in it, as the url names them</param>
    /// <param name="client">The bucket</param>
    private static IFolderEnabledBackend Create(string kind, string path, BucketClient client)
        => kind switch
        {
            "s3" => new S3($"s3://{path}", new Dictionary<string, string?>(), client),
            "e2" => new Idrivee2Backend($"e2://{path}", new Dictionary<string, string?> { ["access_key_id"] = "id", ["access_key_secret"] = "secret" }, client),
            _ => throw new ArgumentException(kind)
        };

    /// <summary>
    /// A bucket with the backend folder "backup". The folder "sub" and the folder "inner" in it
    /// have no object that stands for them, as is the case for anything Duplicati uploads;
    /// the folder "marked" has one, as a folder made in a web console does.
    /// </summary>
    private static BucketClient Bucket()
        => new BucketClient(
            "backup/a.txt",
            "backup/sub/b.txt",
            "backup/sub/inner/c.txt",
            "backup/marked/",
            "backup/marked/d.txt",
            "backupfile",
            "other/e.txt");

    /// <summary>
    /// The names of the listed files and folders, each sorted
    /// </summary>
    private static async Task<(string[] Files, string[] Folders)> ListAsync(IFolderEnabledBackend backend, string? path)
    {
        var entries = await backend.ListAsync(path, CancellationToken.None).ToListAsync();
        return (
            entries.Where(x => !x.IsFolder).Select(x => x.Name).Order(StringComparer.Ordinal).ToArray(),
            entries.Where(x => x.IsFolder).Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Walks the folders as the sync operation does: the name of a folder loses its trailing
    /// separator, a name that is a path is skipped, and only a folder entry is gone into.
    /// </summary>
    /// <returns>The paths of the files that were reached, and the names that were skipped</returns>
    private static async Task<(List<string> Files, List<string> Skipped)> WalkAsync(IFolderEnabledBackend backend)
    {
        var files = new List<string>();
        var skipped = new List<string>();
        var work = new Queue<string>([""]);

        while (work.TryDequeue(out var folder))
            foreach (var entry in await backend.ListAsync(folder.Length == 0 ? null : folder, CancellationToken.None).ToListAsync())
            {
                var name = entry.IsFolder && entry.Name.Length > 1 && entry.Name[^1] == '/'
                    ? entry.Name[..^1]
                    : entry.Name;

                if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
                {
                    skipped.Add(entry.Name);
                    continue;
                }

                var path = folder.Length == 0 ? name : $"{folder}/{name}";
                if (entry.IsFolder)
                    work.Enqueue(path);
                else
                    files.Add(path);
            }

        files.Sort(StringComparer.Ordinal);
        return (files, skipped);
    }

    [TestCase("s3")]
    [TestCase("e2")]
    public async Task TheBackendFolderIsListedOneLevelDeepAsync(string kind)
    {
        var client = Bucket();
        using var backend = Create(kind, "bucket/backup", client);

        var (files, folders) = await ListAsync(backend, null);

        CollectionAssert.AreEqual(new[] { "a.txt" }, files, "Only the objects directly in the folder are its files");
        CollectionAssert.AreEqual(new[] { "marked/", "sub/" }, folders, "A sub-folder is listed as a folder, with or without an object that stands for it");
        CollectionAssert.AreEqual(new[] { "backup/" }, client.Prefixes);
        CollectionAssert.AreEqual(new[] { false }, client.RecursiveFlags, "A folder is listed with a delimiter, not with everything below it");
    }

    [TestCase("s3")]
    [TestCase("e2")]
    public async Task ASubFolderIsListedOneLevelDeepAsync(string kind)
    {
        var client = Bucket();
        using var backend = Create(kind, "bucket/backup", client);

        var (files, folders) = await ListAsync(backend, "sub");

        CollectionAssert.AreEqual(new[] { "b.txt" }, files);
        CollectionAssert.AreEqual(new[] { "inner/" }, folders);
        CollectionAssert.AreEqual(new[] { "backup/sub/" }, client.Prefixes);
    }

    [TestCase("s3", null)]
    [TestCase("s3", "")]
    [TestCase("e2", null)]
    [TestCase("e2", "")]
    public async Task ABackendAtTheTopOfTheBucketListsOneLevelDeepAsync(string kind, string? path)
    {
        var client = new BucketClient("a.txt", "sub/b.txt", "sub/inner/c.txt");
        using var backend = Create(kind, "bucket", client);

        var (files, folders) = await ListAsync(backend, path);

        CollectionAssert.AreEqual(new[] { "a.txt" }, files);
        CollectionAssert.AreEqual(new[] { "sub/" }, folders);
        CollectionAssert.AreEqual(new[] { "" }, client.Prefixes);
    }

    [TestCase("s3")]
    [TestCase("e2")]
    public async Task EveryObjectIsReachedByWalkingTheFoldersAsync(string kind)
    {
        var client = Bucket();
        using var backend = Create(kind, "bucket/backup", client);

        var (files, skipped) = await WalkAsync(backend);

        CollectionAssert.IsEmpty(skipped, "No name may be a path: the sync operation skips such an entry with an InvalidRemoteName warning");
        CollectionAssert.AreEqual(
            new[] { "a.txt", "marked/d.txt", "sub/b.txt", "sub/inner/c.txt" },
            files,
            "A file in a sub-folder is only found if the sub-folder is listed as a folder");
    }

    [Test]
    public async Task TheSourceProviderFindsFilesInAFolderWithoutAMarkerObjectAsync()
    {
        const string MOUNT = "/mnt/e2/";
        var client = Bucket();
        using var provider = new BackendSourceProvider(Create("e2", "bucket/backup", client), MOUNT, "e2://bucket/backup");

        var files = new List<string>();
        var work = new Queue<ISourceProviderEntry>(await provider.EnumerateAsync(CancellationToken.None).ToListAsync());
        while (work.TryDequeue(out var folder))
            await foreach (var entry in folder.Enumerate(CancellationToken.None))
                if (entry.IsFolder)
                    work.Enqueue(entry);
                else
                    files.Add(entry.Path.Replace('\\', '/'));

        files.Sort(StringComparer.Ordinal);
        CollectionAssert.AreEqual(
            new[] { "a.txt", "marked/d.txt", "sub/b.txt", "sub/inner/c.txt" }.Select(x => MOUNT + x).ToArray(),
            files,
            "The source provider drops a name that is a path, so it only backs up a file in a sub-folder if the sub-folder is listed as a folder");
    }
}
