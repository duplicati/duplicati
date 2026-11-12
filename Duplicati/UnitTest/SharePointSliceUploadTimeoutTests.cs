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
using Microsoft.SharePoint.Client;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Each chunk of a SharePoint upload must stop when nothing has moved for the read-write
/// timeout: when the chunk is not being sent, and when it was sent and no answer comes.
/// A chunk that is sent slowly but steadily must not be cut off, and a context whose query
/// timed out must not be handed out again.
/// No network: the context runs a test query instead of sending its request.
/// </summary>
public class SharePointSliceUploadTimeoutTests : BasicSetupHelper
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A context that runs the given query in place of sending its request
    /// </summary>
    private sealed class FakeContext : ClientContext
    {
        public Func<Task> Query = () => Task.CompletedTask;

        public FakeContext() : base("https://example.invalid/sites/site") { }

        public override Task ExecuteQueryAsync() => Query();
    }

    private sealed class Upload
    {
        public readonly FakeContext Context = new();
        public readonly FakeContext Next = new();
        public readonly TaskCompletionSource Release = new();
        public Stream? Chunk;
        public readonly SharePointBackend Backend;

        public Upload()
        {
            var contexts = new Queue<ClientContext>(new ClientContext[] { Context, Next });
            Backend = new SharePointBackend("mssp://example.invalid/sites/site/folder", new Dictionary<string, string?>
            {
                ["auth-username"] = "user@example.invalid",
                ["auth-password"] = "password",
                ["read-write-timeout"] = "1s",
            })
            {
                ContextFactory = () => contexts.Dequeue(),
                ChunkStreamCreated = s => Chunk = s,
            };
        }

        public Task Start()
        {
            var ctx = Backend.GetSpClientContextAsync(false, CancellationToken.None).Result;
            var folder = ctx.Web.GetFolderByServerRelativeUrl("/sites/site/folder");
            return Backend.UploadFileSlicePerSlice(ctx, folder, new MemoryStream(new byte[10]), "file", CancellationToken.None);
        }
    }

    private static void AssertTimesOut(Upload upload, Task task)
    {
        try
        {
            Assert.That(Task.WhenAny(task, Task.Delay(Limit)).Result, Is.SameAs(task), "The upload must stop when nothing moves for the read-write timeout");
            Assert.ThrowsAsync<TimeoutException>(() => task);
            Assert.That(upload.Backend.GetSpClientContextAsync(false, CancellationToken.None).Result, Is.SameAs(upload.Next), "The context whose query timed out must not be used again");
        }
        finally
        {
            upload.Release.TrySetResult();
        }
    }

    [Test]
    [Category("Utility")]
    public void ChunkThatIsNotSentTimesOut()
    {
        var upload = new Upload();
        upload.Context.Query = () => upload.Release.Task;
        AssertTimesOut(upload, upload.Start());
    }

    [Test]
    [Category("Utility")]
    public void ChunkWithoutAnAnswerTimesOut()
    {
        var upload = new Upload();
        upload.Context.Query = async () =>
        {
            await upload.Chunk!.CopyToAsync(Stream.Null);
            await upload.Release.Task;
        };
        AssertTimesOut(upload, upload.Start());
    }

    [Test]
    [Category("Utility")]
    public void ChunkThatIsSentSlowlyIsNotCutOff()
    {
        var upload = new Upload();
        upload.Context.Query = async () =>
        {
            // 10 bytes, one every 300 ms: three seconds in all, with a one second timeout
            var buffer = new byte[1];
            while (await upload.Chunk!.ReadAsync(buffer) > 0)
                await Task.Delay(300);
        };

        var task = upload.Start();
        Assert.That(Task.WhenAny(task, Task.Delay(Limit)).Result, Is.SameAs(task));
        // The fake context does not load the file length, so the upload may fail after the
        // query; it must not fail because of the timeout
        Assert.That(task.Exception?.InnerException, Is.Not.InstanceOf<TimeoutException>());
    }
}
