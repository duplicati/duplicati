using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.UnitTest.DiskImage.Helpers;
using NUnit.Framework;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{
    /// <summary>
    /// A disk was disposed while a listing still read it, as when a backup returned before the
    /// file counter's listing ended. Dispose freed the aligned buffer the read was copying from,
    /// and the process crashed with an AccessViolationException. Reads that run while the disk
    /// is disposed now either complete or stop with an exception.
    /// </summary>
    [Test]
    public async Task Test_RawDisk_DisposeWhileReading_DoesNotCrash_Async()
    {
        var disk = DiskImageTestHelpers.CreateRawDiskForIdentifier(s_gptDiskIdentifier);
        var msg = await disk.InitializeAsync(false, CancellationToken.None);
        Assert.That(msg, Is.Null, "Initializing the disk failed: " + msg);

        // Large reads spend most of their time copying out of the aligned buffer
        var length = (int)Math.Min(8 * MiB, s_gptPartition1Size);
        var started = new SemaphoreSlim(0);
        var completed = 0;
        var stopped = 0;

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var buffer = new byte[length];
            started.Release();
            for (var i = 0; i < 50; i++)
            {
                try
                {
                    await disk.ReadBytesAsync(s_gptPartition1Offset, buffer.AsMemory(), CancellationToken.None);
                    Interlocked.Increment(ref completed);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    // A read that starts after the disk is disposed stops
                    Interlocked.Increment(ref stopped);
                    return;
                }
            }
        })).ToArray();

        for (var i = 0; i < readers.Length; i++)
            await started.WaitAsync();
        await Task.Delay(200);

        disk.Dispose();

        var all = Task.WhenAll(readers);
        Assert.That(await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(60))), Is.SameAs(all), "The reads did not end after the disk was disposed");
        await all;

        Assert.That(stopped, Is.EqualTo(readers.Length), "Every reader should have been stopped by the disposed disk");
        Assert.That(completed, Is.GreaterThan(0), "No read ran before the disk was disposed");

        // Disposing again does nothing
        disk.Dispose();
    }
}
