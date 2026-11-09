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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Holds every upload until released, and counts how many are held at once
/// </summary>
public class HoldingUploadBackend : IBackend, IStreamingBackend
{
    public const string Key = "holdput";

    public static readonly ManualResetEventSlim Release = new(false);
    public static int HeldPuts;

    private readonly IStreamingBackend? m_backend;

    public HoldingUploadBackend() { }

    public HoldingUploadBackend(string url, Dictionary<string, string> options)
    {
        var u = new Library.Utility.RelaxedUri(url).SetScheme("file").ToString();
        m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);
    }

    private static async Task HoldAsync()
    {
        Interlocked.Increment(ref HeldPuts);
        await Task.Run(() => Release.Wait()).ConfigureAwait(false);
    }

    public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        await HoldAsync().ConfigureAwait(false);
        await m_backend!.PutAsync(remotename, stream, cancelToken).ConfigureAwait(false);
    }
    public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await HoldAsync().ConfigureAwait(false);
        await m_backend!.PutAsync(remotename, filename, cancelToken).ConfigureAwait(false);
    }
    public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, stream, cancelToken);
    public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, filename, cancelToken);
    public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken) => m_backend!.ListAsync(cancelToken);
    public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend!.DeleteAsync(remotename, cancelToken);
    public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend!.TestAsync(alsoWrite, cancelToken);
    public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend!.CreateFolderAsync(cancelToken);
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend!.GetDNSNamesAsync(cancelToken);
    public string DisplayName => "Holding upload test backend";
    public string ProtocolKey => Key;
    public IList<ICommandLineArgument> SupportedCommands
        => m_backend?.SupportedCommands ?? Library.DynamicLoader.BackendLoader.GetSupportedCommands("file://").ToList();
    public string Description => "A testing backend that holds every upload until released";
    public bool SupportsStreaming => m_backend?.SupportsStreaming ?? false;
    public void Dispose() => m_backend?.Dispose();
}

/// <summary>
/// The help for --asynchronous-upload-limit says zero disables the limit, but zero
/// was read as one, so asking for no limit made every upload wait for the one before it
/// </summary>
public class UploadLimitZeroTests : BasicSetupHelper
{
    /// <summary>
    /// Starts a backup of many volumes whose uploads are all held, and returns the
    /// most uploads that were held at once before they were let through
    /// </summary>
    private async Task<int> HeldUploadsWithLimit(string limit)
    {
        var rng = new Random(42);
        var data = new byte[64 * 1024];
        for (var i = 0; i < 40; i++)
        {
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}"), data);
        }

        Library.DynamicLoader.BackendLoader.AddBackend(new HoldingUploadBackend());
        HoldingUploadBackend.Release.Reset();
        HoldingUploadBackend.HeldPuts = 0;

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["dblock-size"] = "100kb",
            ["blocksize"] = "4kb",
            ["asynchronous-upload-limit"] = limit,
            ["snapshot-policy"] = "off",
        };

        using var c = new Controller(HoldingUploadBackend.Key + "://" + this.TARGETFOLDER, options, null);
        var backupTask = Task.Run(async () => await c.BackupAsync(new[] { this.DATAFOLDER }));

        int held;
        try
        {
            // Wait for the first upload, then give the others time to pile up behind it
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (HoldingUploadBackend.HeldPuts < 1 && sw.Elapsed < TimeSpan.FromMinutes(1) && !backupTask.IsCompleted)
                await Task.Delay(100);
            Assert.That(backupTask.IsCompleted, Is.False, "The backup finished before any upload was held");
            await Task.Delay(5000);
            held = Volatile.Read(ref HoldingUploadBackend.HeldPuts);        }
        finally
        {
            HoldingUploadBackend.Release.Set();
        }

        var results = await backupTask;
        Assert.That(results.Errors, Is.Empty);
        return held;
    }

    [Test]
    [Category("Targeted")]
    public async Task ZeroLetsMoreThanTheDefaultUploadAtOnceAsync()
    {
        // The default is 4 for this destination, so more than 4 shows there is no limit
        var held = await HeldUploadsWithLimit("0");
        Assert.That(held, Is.GreaterThan(4));
    }

    [Test]
    [Category("Targeted")]
    public async Task AGivenLimitIsStillKeptAsync()
    {
        var held = await HeldUploadsWithLimit("2");
        Assert.That(held, Is.EqualTo(2));
    }
}
