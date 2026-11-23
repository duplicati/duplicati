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
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;
using Duplicati.Library.Backend.AzureBlob;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A blob in the archive tier is rehydrated by moving it to an online tier, and is reported as
/// pending while that happens (#6228). The requests go to a handler instead of Azure.
/// </summary>
[TestFixture]
[Category("Backend")]
public class AzureBlobArchiveRestoreTests
{
    private sealed class ArchiveHandler : HttpMessageHandler
    {
        /// <summary>
        /// The tier and archive status of each blob
        /// </summary>
        public Dictionary<string, (string Tier, string? ArchiveStatus)> Blobs { get; } = new();
        /// <summary>
        /// The tier changes asked for, with the target tier and the priority
        /// </summary>
        public List<(string Blob, string? Tier, string? Priority)> TierChanges { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.AbsolutePath.Split('/').Last();
            string? Header(string key) => request.Headers.TryGetValues(key, out var values) ? values.First() : null;

            if (request.Method == HttpMethod.Head && Blobs.TryGetValue(name, out var blob))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x1\"");
                response.Content.Headers.LastModified = DateTimeOffset.UtcNow;
                response.Headers.Add("x-ms-blob-type", "BlockBlob");
                response.Headers.Add("x-ms-access-tier", blob.Tier);
                if (blob.ArchiveStatus != null)
                    response.Headers.Add("x-ms-archive-status", blob.ArchiveStatus);
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Put && request.RequestUri.Query.Contains("comp=tier", StringComparison.Ordinal))
            {
                TierChanges.Add((name, Header("x-ms-access-tier"), Header("x-ms-rehydrate-priority")));
                if (Blobs.TryGetValue(name, out var current) && current.ArchiveStatus != null)
                {
                    var conflict = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new ByteArrayContent([]) };
                    conflict.Headers.Add("x-ms-error-code", "BlobBeingRehydrated");
                    return Task.FromResult(conflict);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new ByteArrayContent([]) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
        }
    }

    private static AzureBlobWrapper CreateWrapper(ArchiveHandler handler)
        => new("account", null, "sv=2020-08-04&sig=test", "container", "", null,
            new HashSet<AccessTier>(), TimeoutOptionsHelper.Parse(new Dictionary<string, string?>()), 0, 4 * 1024 * 1024, handler);

    [Test]
    public async Task TheStateIsReadFromTheTierAndTheArchiveStatus_Async()
    {
        var handler = new ArchiveHandler();
        handler.Blobs["hot.zip"] = ("Hot", null);
        handler.Blobs["cold.zip"] = ("Cold", null);
        handler.Blobs["archive.zip"] = ("Archive", null);
        handler.Blobs["pending.zip"] = ("Archive", "rehydrate-pending-to-hot");
        var wrapper = CreateWrapper(handler);

        Assert.That(await wrapper.GetArchiveRestoreStateAsync("hot.zip", CancellationToken.None), Is.EqualTo(ArchiveRestoreState.Available));
        Assert.That(await wrapper.GetArchiveRestoreStateAsync("cold.zip", CancellationToken.None), Is.EqualTo(ArchiveRestoreState.Available), "The cold tier is read directly");
        Assert.That(await wrapper.GetArchiveRestoreStateAsync("archive.zip", CancellationToken.None), Is.EqualTo(ArchiveRestoreState.Archived));
        Assert.That(await wrapper.GetArchiveRestoreStateAsync("pending.zip", CancellationToken.None), Is.EqualTo(ArchiveRestoreState.RestoreInProgress));
    }

    [Test]
    public async Task ARehydrationMovesTheBlobToTheChosenTier_Async()
    {
        var handler = new ArchiveHandler();
        handler.Blobs["archive.zip"] = ("Archive", null);
        var wrapper = CreateWrapper(handler);

        await wrapper.RequestArchiveRestoreAsync("archive.zip", AccessTier.Cool, RehydratePriority.High, CancellationToken.None);

        Assert.That(handler.TierChanges, Is.EqualTo(new[] { ("archive.zip", (string?)"Cool", (string?)"High") }));
    }

    [Test]
    public void ARehydrationAlreadyInProgressIsNotAnError()
    {
        var handler = new ArchiveHandler();
        handler.Blobs["pending.zip"] = ("Archive", "rehydrate-pending-to-hot");
        var wrapper = CreateWrapper(handler);

        Assert.DoesNotThrowAsync(() => wrapper.RequestArchiveRestoreAsync("pending.zip", AccessTier.Hot, RehydratePriority.Standard, CancellationToken.None));
        Assert.That(handler.TierChanges, Has.Count.EqualTo(1));
    }
}
