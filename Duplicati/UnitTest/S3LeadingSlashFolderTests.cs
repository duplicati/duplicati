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
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that an S3 folder path starting with "/" tells why it fails: many S3 compatible servers
/// do not accept object names that start with "/" (issue #3471)
/// </summary>
[TestFixture]
[Category("Backend")]
public class S3LeadingSlashFolderTests
{
    /// <summary>
    /// A client that lists nothing and remembers the prefixes it was asked to list
    /// </summary>
    private sealed class EmptyBucketClient : IS3Client
    {
        public List<string> Prefixes { get; } = [];

        public async IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Prefixes.Add(prefix);
            await Task.CompletedTask;
            yield break;
        }

        public IAsyncEnumerable<(IFileEntry Entry, string VersionId)> ListBucketVersionsAtAsync(string bucketName, string prefix, bool recursive, DateTime atUtc, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task GetFileStreamAsync(string bucketName, string keyName, string versionId, Stream target, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task AddBucketAsync(string bucketName, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken) => throw new NotImplementedException();
        public string? GetDnsHost() => null;
        public Task AddFileStreamAsync(string bucketName, string keyName, Stream source, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task<DateTime?> GetObjectLockUntilAsync(string bucketName, string keyName, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task SetObjectLockUntilAsync(string bucketName, string keyName, DateTime lockUntilUtc, CancellationToken cancelToken) => throw new NotImplementedException();
        public Task<IFileEntry?> GetFileEntryAsync(string bucketName, string keyName, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Dispose() { }
    }

    /// <summary>
    /// Lists the backend for the url twice and returns the prefixes listed and the warnings logged
    /// </summary>
    private static async Task<(List<string> Prefixes, List<LogEntry> Warnings)> ListTwiceAsync(string url)
    {
        var client = new EmptyBucketClient();
        using var backend = new S3(url, new Dictionary<string, string?>(), client);

        var messages = new List<LogEntry>();
        using (Log.StartIsolatingScope(true))
        using (Log.StartScope(messages.Add))
        {
            for (var i = 0; i < 2; i++)
                await foreach (var _ in backend.ListAsync(CancellationToken.None)) { }
        }

        return (client.Prefixes, messages.Where(x => x.Level == LogMessageType.Warning).ToList());
    }

    [TestCase("s3://bucket//", "/")]
    [TestCase("s3://bucket//folder", "/folder/")]
    public async Task AFolderThatStartsWithASlashTellsWhyItFails(string url, string prefix)
    {
        var (prefixes, warnings) = await ListTwiceAsync(url);

        Assert.That(prefixes, Is.EqualTo(new[] { prefix, prefix }), "The object names keep the leading slash");
        Assert.That(warnings, Has.Count.EqualTo(1), "The backend warns once");
        Assert.That(warnings[0].Id, Does.EndWith("LeadingSlashInFolder"));
        Assert.That(warnings[0].FormattedMessage, Does.Contain($"\"{prefix}\"").And.Contain("Remove the leading \"/\""));
    }

    [TestCase("s3://bucket/", "")]
    [TestCase("s3://bucket/folder", "folder/")]
    public async Task AFolderWithoutALeadingSlashGivesNoWarning(string url, string prefix)
    {
        var (prefixes, warnings) = await ListTwiceAsync(url);

        Assert.That(prefixes, Is.EqualTo(new[] { prefix, prefix }));
        Assert.That(warnings, Is.Empty);
    }
}
