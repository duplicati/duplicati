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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// GetEntryAsync looks up a single file or folder. A null answer means the entry does
    /// not exist, and callers pass that straight through, so a backend that always answers
    /// null reports every path as missing.
    /// </summary>
    [TestFixture]
    [Category("Idrivee2")]
    public class Idrivee2EntryLookupTests
    {
        /// <summary>
        /// Records what GetFileEntryAsync was asked for and answers with a set entry.
        /// Everything else is unused by these tests.
        /// </summary>
        private sealed class RecordingS3Client : IS3Client
        {
            public string? AskedBucket;
            public string? AskedKey;
            public int Calls;
            public IFileEntry? Answer;

            public Task<IFileEntry?> GetFileEntryAsync(string bucketName, string keyName, CancellationToken cancellationToken)
            {
                Calls++;
                AskedBucket = bucketName;
                AskedKey = keyName;
                return Task.FromResult(Answer);
            }

            public IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, CancellationToken cancellationToken)
                => throw new NotImplementedException();
            public Task AddBucketAsync(string bucketName, CancellationToken cancelToken)
                => throw new NotImplementedException();
            public Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken)
                => throw new NotImplementedException();
            public Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken)
                => throw new NotImplementedException();
            public Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken)
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

        private static Dictionary<string, string?> Options() => new()
        {
            ["access_key_id"] = "id",
            ["access_key_secret"] = "secret"
        };

        private static Idrivee2Backend Create(RecordingS3Client client, string url = "e2://mybucket/some/prefix/")
            => new Idrivee2Backend(url, Options(), client);

        [Test]
        public async Task TheEntryFromTheClientIsReturned()
        {
            var client = new RecordingS3Client { Answer = new FileEntry("file.txt", 42) };
            using var backend = Create(client);

            var entry = await backend.GetEntryAsync("file.txt", CancellationToken.None);

            Assert.IsNotNull(entry, "The entry the client answered with must be returned");
            Assert.AreEqual("file.txt", entry!.Name);
            Assert.AreEqual(42, entry.Size);
        }

        [Test]
        public async Task ThePrefixFromTheUrlIsAppliedToTheKey()
        {
            var client = new RecordingS3Client { Answer = new FileEntry("file.txt") };
            using var backend = Create(client);

            await backend.GetEntryAsync("file.txt", CancellationToken.None);

            Assert.AreEqual("some/prefix/file.txt", client.AskedKey,
                "The key has to carry the prefix from the url, as it does for every other operation");
        }

        [Test]
        public async Task TheBucketFromTheUrlIsUsed()
        {
            var client = new RecordingS3Client { Answer = new FileEntry("file.txt") };
            using var backend = Create(client);

            await backend.GetEntryAsync("file.txt", CancellationToken.None);

            Assert.AreEqual("mybucket", client.AskedBucket);
        }

        [Test]
        public async Task AMissingEntryStaysMissing()
        {
            var client = new RecordingS3Client { Answer = null };
            using var backend = Create(client);

            Assert.IsNull(await backend.GetEntryAsync("gone.txt", CancellationToken.None),
                "A null answer from the client means the entry does not exist");
            Assert.AreEqual(1, client.Calls);
        }

        [Test]
        public async Task TheRootIsAnsweredWithoutAskingTheClient()
        {
            var client = new RecordingS3Client();
            using var backend = Create(client);

            var entry = await backend.GetEntryAsync("", CancellationToken.None);

            Assert.IsNotNull(entry);
            Assert.IsTrue(entry!.IsFolder, "The root of the bucket is a folder");
            Assert.AreEqual(0, client.Calls, "The root does not need a lookup");
        }
    }
}
