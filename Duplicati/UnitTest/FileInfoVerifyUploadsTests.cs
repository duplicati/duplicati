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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IS3Client = Duplicati.Library.Backend.IS3Client;
using S3 = Duplicati.Library.Backend.S3;
using Duplicati.Library.Common.IO;
using File = System.IO.File;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// With --list-verify-uploads, a backend that can look up a single file verifies each upload
    /// with that lookup, such as an HTTP HEAD request, instead of listing the whole folder.
    /// </summary>
    [TestFixture]
    public class FileInfoVerifyUploadsTests : BasicSetupHelper
    {
        /// <summary>
        /// Wraps the file backend, records the lookups and listings, and can falsify the lookup answer.
        /// </summary>
        public class LookupFileBackend : IBackend, IStreamingBackend, IFileInfoEnabledBackend
        {
            public const string Key = "lookupfile";

            public static readonly ConcurrentQueue<string> Lookups = new();
            public static int Listings;
            public static Func<IFileEntry?, IFileEntry?> Answer = e => e;

            private readonly Library.Backend.File? m_backend;

            public LookupFileBackend()
            {
            }

            // ReSharper disable once UnusedMember.Global
            public LookupFileBackend(string url, Dictionary<string, string?> options)
            {
                m_backend = new Library.Backend.File(new Library.Utility.RelaxedUri(url).SetScheme("file").ToString(), options);
            }

            public async Task<IFileEntry?> GetFileInfoAsync(string remotename, CancellationToken cancelToken)
            {
                Lookups.Enqueue(remotename);
                return Answer(await m_backend!.GetFileInfoAsync(remotename, cancelToken).ConfigureAwait(false));
            }

            public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Listings);
                return m_backend!.ListAsync(cancellationToken);
            }

            public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.PutAsync(remotename, stream, cancelToken);
            public Task PutAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.PutAsync(remotename, filename, cancelToken);
            public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, stream, cancelToken);
            public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, filename, cancelToken);
            public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend!.DeleteAsync(remotename, cancelToken);
            public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend!.TestAsync(alsoWrite, cancelToken);
            public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend!.CreateFolderAsync(cancelToken);
            public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend!.GetDNSNamesAsync(cancelToken);
            public bool SupportsStreaming => m_backend!.SupportsStreaming;
            public string DisplayName => "Lookup file backend";
            public string ProtocolKey => Key;
            public IList<ICommandLineArgument> SupportedCommands => m_backend?.SupportedCommands ?? new Library.Backend.File().SupportedCommands;
            public string Description => "A testing backend that records the single-file lookups of the file backend";
            public void Dispose() => m_backend?.Dispose();
        }

        private static void Reset(Func<IFileEntry?, IFileEntry?> answer)
        {
            Library.DynamicLoader.BackendLoader.AddBackend(new LookupFileBackend());
            LookupFileBackend.Lookups.Clear();
            LookupFileBackend.Listings = 0;
            LookupFileBackend.Answer = answer;
        }

        private string TargetUrl => LookupFileBackend.Key + "://" + TARGETFOLDER.Replace("\\", "/");

        [Test]
        [NonParallelizable]
        [Category("Backend")]
        public async Task VerifyUploadsLooksUpEachUploadedFile()
        {
            Reset(e => e);
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");

            using (var c = new Library.Main.Controller(TargetUrl, TestOptions, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            var listingsWithoutVerify = LookupFileBackend.Listings;
            Assert.IsEmpty(LookupFileBackend.Lookups, "Without the option, uploads are not verified");

            File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "more data");
            var before = Directory.GetFiles(TARGETFOLDER).Select(x => Path.GetFileName(x)).ToHashSet();
            LookupFileBackend.Listings = 0;

            using (var c = new Library.Main.Controller(TargetUrl, TestOptions.Expand(new { list_verify_uploads = true }), null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var uploaded = Directory.GetFiles(TARGETFOLDER).Select(x => Path.GetFileName(x)).Where(x => !before.Contains(x)).ToList();
            Assert.GreaterOrEqual(uploaded.Count, 3, "The backup should upload a dblock, a dindex and a dlist");
            CollectionAssert.AreEquivalent(uploaded, LookupFileBackend.Lookups, "Each upload should be verified by looking up that file");
            Assert.AreEqual(listingsWithoutVerify, LookupFileBackend.Listings, "The verification should not list the folder");
        }

        [Test]
        [NonParallelizable]
        [Category("Backend")]
        [TestCase(true, TestName = "VerifyUploadsFailsWhenTheLookupReportsAnotherSize")]
        [TestCase(false, TestName = "VerifyUploadsFailsWhenTheLookupFindsNoFile")]
        public void VerifyUploadsFailsOnAWrongLookup(bool wrongSize)
        {
            Reset(e => e == null || !wrongSize ? null : new FileEntry(e.Name, e.Size + 1, e.LastAccess, e.LastModification));
            File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "some data");

            var opts = TestOptions.Expand(new { list_verify_uploads = true, number_of_retries = 0 });
            var ex = Assert.CatchAsync(async () =>
            {
                using var c = new Library.Main.Controller(TargetUrl, opts, null);
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            });

            StringAssert.Contains(wrongSize ? "but expected to be" : "file was not found after upload", ex!.ToString());
            Assert.IsNotEmpty(LookupFileBackend.Lookups);
        }

        /// <summary>
        /// A client that answers object lookups from a table and records what it was asked
        /// </summary>
        private sealed class LookupS3Client : IS3Client
        {
            public readonly Dictionary<string, long> Objects = new();
            public readonly List<string> Lookups = new();
            public int Listings;

            public Task<IFileEntry?> GetFileEntryAsync(string bucketName, string keyName, CancellationToken cancellationToken)
            {
                Assert.AreEqual("bucket", bucketName);
                Lookups.Add(keyName);
                return Task.FromResult<IFileEntry?>(Objects.TryGetValue(keyName, out var size) ? new FileEntry(keyName, size) : null);
            }

            public async IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                Listings++;
                await Task.Yield();
                yield break;
            }

            public IAsyncEnumerable<(IFileEntry Entry, string VersionId)> ListBucketVersionsAtAsync(string bucketName, string prefix, bool recursive, DateTime atUtc, CancellationToken cancellationToken)
                => throw new NotImplementedException();
            public Task GetFileStreamAsync(string bucketName, string keyName, string versionId, Stream target, CancellationToken cancelToken)
                => throw new NotImplementedException();
            public Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken)
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

        [Test]
        [Category("Backend")]
        public async Task S3LooksUpTheObjectUnderThePrefix()
        {
            var client = new LookupS3Client();
            client.Objects["backup/duplicati-b1.dblock.zip"] = 42;
            IBackend backend = new S3("s3://bucket/backup", new Dictionary<string, string?>(), client);

            Assert.IsInstanceOf<IFileInfoEnabledBackend>(backend);
            var lookup = (IFileInfoEnabledBackend)backend;

            var found = await lookup.GetFileInfoAsync("duplicati-b1.dblock.zip", CancellationToken.None);
            Assert.IsNotNull(found);
            Assert.AreEqual(42, found!.Size);
            Assert.IsNull(await lookup.GetFileInfoAsync("duplicati-b2.dblock.zip", CancellationToken.None));

            CollectionAssert.AreEqual(new[] { "backup/duplicati-b1.dblock.zip", "backup/duplicati-b2.dblock.zip" }, client.Lookups);
            Assert.AreEqual(0, client.Listings, "The lookup should not list the bucket");
        }
    }
}
