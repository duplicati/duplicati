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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// With a point in time set, the S3 backend lists through the version listing, remembers the version
/// of each listed key, resolves keys it has not listed on demand, and downloads by version id.
/// </summary>
[TestFixture]
public class S3BackendPointInTimeTests
{
    private static readonly DateTime At = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A client that answers version listings from a table and records what it was asked
    /// </summary>
    private sealed class FakeS3Client : IS3Client
    {
        /// <summary>
        /// The version current at the point in time, by full key
        /// </summary>
        public readonly Dictionary<string, (string VersionId, long Size)> Current = new();
        public readonly List<(string Prefix, bool Recursive, DateTime At)> VersionListings = new();
        public readonly List<(string Key, string VersionId)> VersionReads = new();
        public readonly List<string> PlainReads = new();
        public int PlainListings;

        public async IAsyncEnumerable<(IFileEntry Entry, string VersionId)> ListBucketVersionsAtAsync(string bucketName, string prefix, bool recursive, DateTime atUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Assert.AreEqual("bucket", bucketName);
            VersionListings.Add((prefix, recursive, atUtc));
            await Task.Yield();

            foreach (var (key, (versionId, size)) in Current.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!key.StartsWith(prefix) || (key == prefix && key.EndsWith("/")))
                    continue;

                var name = key.Substring(prefix.Length);
                if (!recursive && name.Contains('/'))
                    continue;

                yield return (new FileEntry(name, size, At.AddHours(-1), At.AddHours(-1)), versionId);
            }
        }

        public Task GetFileStreamAsync(string bucketName, string keyName, string versionId, Stream target, CancellationToken cancelToken)
        {
            Assert.AreEqual("bucket", bucketName);
            VersionReads.Add((keyName, versionId));
            var bytes = Encoding.UTF8.GetBytes($"{keyName}@{versionId}");
            return target.WriteAsync(bytes, 0, bytes.Length, cancelToken);
        }

        public Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken)
        {
            PlainReads.Add(keyName);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, CancellationToken cancellationToken)
        {
            PlainListings++;
            return AsyncEnumerable.Empty<IFileEntry>();
        }

        public Task<IFileEntry?> GetFileEntryAsync(string bucketName, string keyName, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task AddBucketAsync(string bucketName, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken)
            => throw new NotImplementedException();
        public Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken)
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

    private static Dictionary<string, string?> Options(bool pointInTime) => pointInTime
        ? new() { [PointInTimeOptionsHelper.PointInTimeOption] = Duplicati.Library.Utility.Utility.SerializeDateTime(At) }
        : new();

    private static S3 Create(FakeS3Client client, bool pointInTime)
        => new("s3://bucket/backup", Options(pointInTime), client);

    private static async Task<string> ReadAsync(S3 backend, string name)
    {
        using var ms = new MemoryStream();
        await backend.GetAsync(name, ms, CancellationToken.None);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Test]
    [Category("Backend")]
    public async Task ListUsesTheVersionListingAtThePointInTime()
    {
        var client = new FakeS3Client();
        client.Current["backup/a.zip"] = ("v1", 10);
        client.Current["backup/b.zip"] = ("v2", 20);
        using var backend = Create(client, pointInTime: true);

        var names = await backend.ListAsync(CancellationToken.None).Select(x => x.Name).ToListAsync();

        Assert.AreEqual(new[] { "a.zip", "b.zip" }, names.ToArray());
        Assert.AreEqual(("backup/", false, At), client.VersionListings.Single());
        Assert.AreEqual(0, client.PlainListings);
    }

    [Test]
    [Category("Backend")]
    public async Task GetAfterListUsesTheRememberedVersion()
    {
        var client = new FakeS3Client();
        client.Current["backup/a.zip"] = ("v1", 10);
        using var backend = Create(client, pointInTime: true);

        await backend.ListAsync(CancellationToken.None).ToListAsync();
        var content = await ReadAsync(backend, "a.zip");

        Assert.AreEqual("backup/a.zip@v1", content);
        Assert.AreEqual(1, client.VersionListings.Count, "The version was remembered from the listing");
        Assert.AreEqual(("backup/a.zip", "v1"), client.VersionReads.Single());
        Assert.IsEmpty(client.PlainReads);
    }

    [Test]
    [Category("Backend")]
    public async Task GetWithoutAPriorListResolvesTheVersionByKey()
    {
        // A pooled or re-created backend has no listing to remember from, so the key is resolved on its own
        var client = new FakeS3Client();
        client.Current["backup/a.zip"] = ("v1", 10);
        client.Current["backup/a.zip.bak"] = ("v9", 10);
        using var backend = Create(client, pointInTime: true);

        var content = await ReadAsync(backend, "a.zip");

        Assert.AreEqual("backup/a.zip@v1", content);
        Assert.AreEqual(("backup/a.zip", true, At), client.VersionListings.Single());
        Assert.AreEqual(("backup/a.zip", "v1"), client.VersionReads.Single());
    }

    [Test]
    [Category("Backend")]
    public async Task ResolvedVersionsAreRemembered()
    {
        var client = new FakeS3Client();
        client.Current["backup/a.zip"] = ("v1", 10);
        using var backend = Create(client, pointInTime: true);

        await ReadAsync(backend, "a.zip");
        await ReadAsync(backend, "a.zip");

        Assert.AreEqual(1, client.VersionListings.Count);
        Assert.AreEqual(2, client.VersionReads.Count);
    }

    [Test]
    [Category("Backend")]
    public void GetOfAKeyAbsentAtTheTimeIsReportedAsMissing()
    {
        var client = new FakeS3Client();
        using var backend = Create(client, pointInTime: true);

        Assert.CatchAsync<FileMissingException>(async () => await ReadAsync(backend, "a.zip"));

        Assert.IsEmpty(client.VersionReads);
    }

    [Test]
    [Category("Backend")]
    public async Task GetEntryResolvesTheKeyAtThePointInTime()
    {
        var client = new FakeS3Client();
        client.Current["backup/a.zip"] = ("v1", 10);
        using var backend = Create(client, pointInTime: true);

        var found = await backend.GetEntryAsync("a.zip", CancellationToken.None);
        var missing = await backend.GetEntryAsync("b.zip", CancellationToken.None);

        Assert.IsNotNull(found);
        Assert.AreEqual(10, found!.Size);
        Assert.IsFalse(found.IsFolder);
        Assert.IsNull(missing);
    }

    [Test]
    [Category("Backend")]
    public void WritesAreRefused()
    {
        var client = new FakeS3Client();
        using var backend = Create(client, pointInTime: true);

        var writes = new Dictionary<string, Func<Task>>
        {
            ["put"] = () => backend.PutAsync("a.zip", new MemoryStream(new byte[] { 1 }), CancellationToken.None),
            ["delete"] = () => backend.DeleteAsync("a.zip", CancellationToken.None),
            ["rename"] = () => backend.RenameAsync("a.zip", "b.zip", CancellationToken.None),
            ["createfolder"] = () => backend.CreateFolderAsync(CancellationToken.None),
            ["set-lock"] = () => backend.SetObjectLockUntilAsync("a.zip", At.AddDays(30), CancellationToken.None),
        };

        foreach (var (name, write) in writes)
        {
            var ex = Assert.CatchAsync<UserInformationException>(async () => await write(), name);
            Assert.AreEqual(PointInTimeOptionsHelper.HelpIdDestinationReadOnly, ex!.HelpID, name);
        }
    }

    [Test]
    [Category("Backend")]
    public async Task WithoutThePointInTimeThePlainClientCallsAreUsed()
    {
        var client = new FakeS3Client();
        using var backend = Create(client, pointInTime: false);

        await backend.ListAsync(CancellationToken.None).ToListAsync();
        await ReadAsync(backend, "a.zip");

        Assert.AreEqual(1, client.PlainListings);
        Assert.AreEqual(new[] { "backup/a.zip" }, client.PlainReads.ToArray());
        Assert.IsEmpty(client.VersionListings);
        Assert.IsEmpty(client.VersionReads);
    }

    [Test]
    [Category("Backend")]
    public void TheMinioClientIsRefusedWhenTheBackendIsCreated()
    {
        var options = Options(pointInTime: true);
        options["s3-client"] = "minio";
        options["aws-access-key-id"] = "id";
        options["aws-secret-access-key"] = "secret";
        options["s3-server-name"] = "s3.example.com";

        var ex = Assert.Throws<UserInformationException>(() => new S3("s3://bucket/backup", options));

        Assert.AreEqual("S3PointInTimeRequiresAwsClient", ex!.HelpID);
    }
}
