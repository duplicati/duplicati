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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Storj;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using uplink.NET.Interfaces;
using uplink.NET.Models;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// uplink.NET reports a download that failed part way through by setting <c>Failed</c> on the
/// operation, not by failing its task, so a failed download was handed back as a successful one
/// with the data received so far. The downloads are uplink.NET's own operation objects, made
/// without their constructor so that the native library is not loaded.
/// </summary>
[TestFixture]
public class StorjDownloadFailureTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Error = "piece download failed";

    private static readonly byte[] Data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();

    /// <summary>
    /// Answers the calls made on a uplink.NET service interface
    /// </summary>
    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> Handler = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler(targetMethod!);
    }

    private static T CreateService<T>(Func<MethodInfo, object?> handler) where T : class
    {
        var service = DispatchProxy.Create<T, ServiceProxy>();
        ((ServiceProxy)(object)service).Handler = handler;
        return service;
    }

    private static async Task<Exception?> CatchAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static Storj CreateBackend(DownloadOperation download)
    {
        var bucket = (Bucket)RuntimeHelpers.GetUninitializedObject(typeof(Bucket));
        var bucketService = CreateService<IBucketService>(m => m.Name == nameof(IBucketService.EnsureBucketAsync)
            ? Task.FromResult(bucket)
            : throw new NotSupportedException(m.Name));
        var objectService = CreateService<IObjectService>(m => m.Name == nameof(IObjectService.DownloadObjectAsync)
            ? Task.FromResult(download)
            : throw new NotSupportedException(m.Name));
        var timeout = TimeSpan.FromSeconds(30);
        return new Storj(bucketService, objectService, new TimeoutOptionsHelper.Timeouts(timeout, timeout, timeout));
    }

    /// <summary>
    /// Creates a download that delivers the first bytes of <see cref="Data"/> the way uplink.NET
    /// does, and then fails with <see cref="Error"/>, or completes when all bytes were delivered
    /// </summary>
    /// <param name="delivered">The number of bytes received before the download ends</param>
    /// <param name="hasHandler">If true, the download reports progress to a handler that the backend attaches first</param>
    private static DownloadOperation CreateDownload(int delivered, bool hasHandler)
    {
        var type = typeof(DownloadOperation);
        var op = (DownloadOperation)RuntimeHelpers.GetUninitializedObject(type);
        var buffer = new byte[Data.Length];
        type.GetField("_bytesToDownload", Instance)!.SetValue(op, buffer);
        type.GetProperty(nameof(DownloadOperation.TotalBytes))!.SetValue(op, (long)Data.Length);

        var handlerField = type.GetField(nameof(DownloadOperation.DownloadOperationProgressChanged), Instance)!;
        type.GetField("_downloadTask", Instance)!.SetValue(op, Task.Run(async () =>
        {
            while (hasHandler && handlerField.GetValue(op) == null)
                await Task.Delay(10);

            Array.Copy(Data, buffer, delivered);
            type.GetProperty(nameof(DownloadOperation.BytesReceived))!.SetValue(op, (long)delivered);
            ((DownloadOperationProgressChanged?)handlerField.GetValue(op))?.Invoke(op);

            if (delivered == Data.Length)
                type.GetProperty(nameof(DownloadOperation.Completed))!.SetValue(op, true);
            else
            {
                type.GetField("_errorMessage", Instance)!.SetValue(op, Error);
                op.Failed = true;
            }
        }));

        return op;
    }

    [Test]
    public async Task ADownloadToAStreamThatFailsPartWayIsAnError()
    {
        using var backend = CreateBackend(CreateDownload(400, true));
        using var target = new MemoryStream();

        var ex = await CatchAsync(() => backend.GetAsync("file", target, CancellationToken.None));

        // The backend closes the target stream, which a memory stream can still be read from
        TestContext.Out.WriteLine($"Bytes written to the target: {target.ToArray().Length}");
        Assert.That(ex?.Message, Does.Contain(Error));
    }

    [Test]
    public async Task ADownloadToAStreamThatCompletesWritesAllData()
    {
        using var backend = CreateBackend(CreateDownload(Data.Length, true));
        using var target = new MemoryStream();

        await backend.GetAsync("file", target, CancellationToken.None);

        Assert.That(target.ToArray(), Is.EqualTo(Data));
    }

    [Test]
    public async Task ADownloadToAFileThatFailsPartWayIsAnError()
    {
        using var backend = CreateBackend(CreateDownload(400, false));
        using var target = new TempFile();

        var ex = await CatchAsync(() => backend.GetAsync("file", (string)target, CancellationToken.None));

        TestContext.Out.WriteLine($"Target file exists: {File.Exists(target)}");
        Assert.That(ex?.Message, Does.Contain(Error));
    }

    [Test]
    public async Task ADownloadToAFileThatCompletesWritesAllData()
    {
        using var backend = CreateBackend(CreateDownload(Data.Length, false));
        using var target = new TempFile();

        await backend.GetAsync("file", (string)target, CancellationToken.None);

        Assert.That(File.ReadAllBytes(target), Is.EqualTo(Data));
    }

    [Test]
    public async Task ADownloadThatFailedBeforeItStartedIsAnError()
    {
        // uplink.NET marks the download as failed, and returns no task for it, when the
        // download could not be opened
        var download = (DownloadOperation)RuntimeHelpers.GetUninitializedObject(typeof(DownloadOperation));
        typeof(DownloadOperation).GetField("_errorMessage", Instance)!.SetValue(download, Error);
        download.Failed = true;
        using var backend = CreateBackend(download);
        using var target = new MemoryStream();

        var ex = await CatchAsync(() => backend.GetAsync("file", target, CancellationToken.None));

        Assert.That(ex?.Message, Does.Contain(Error));
    }
}
