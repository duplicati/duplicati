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
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation.Restore;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The volume manager is the only reader of the decrypted volumes. A volume it has been handed
/// and has not processed when it stops has nobody else to dispose it, so its decrypted temporary
/// file was left behind after an aborted restore.
/// </summary>
public class RestoreVolumeManagerShutdownTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    [Category("Targeted")]
    public async Task AVolumeThatWasHandedOverAndNotProcessedIsDisposedWhenTheManagerStopsAsync()
    {
        // Without buffers every handover waits for the other side, so each step below is known to
        // have happened when the write or read returns
        var options = new Options(new Dictionary<string, string?> { ["restore-channel-buffer-size"] = "0" });
        var channels = new Channels(options);
        var manager = VolumeManager.RunAsync(channels, options, new RestoreResults());

        // The first volume is requested and its download is taken, so the manager is waiting again
        await channels.VolumeRequest.WriteAsync(new BlockRequest(1, 0, "a", 10, 1, BlockRequestType.Download)).WaitAsync(Timeout);
        Assert.That(await channels.DownloadRequest.ReadAsync().WaitAsync(Timeout), Is.EqualTo(1));

        // The second volume is requested, and nothing takes its download, so the manager waits there
        await channels.VolumeRequest.WriteAsync(new BlockRequest(2, 0, "b", 10, 2, BlockRequestType.Download)).WaitAsync(Timeout);

        // The write returning does not mean the manager has chosen the request yet: the channel
        // completes the read on the thread pool, so the manager may still be choosing between its
        // two reads. A volume handed over in that window can be chosen first, and the manager then
        // waits forever to hand it to a decompressor that is not there. So the handover waits until
        // the manager is waiting to write the download request.
        await WaitForWaitingWriterAsync(channels.DownloadRequest).WaitAsync(Timeout);

        // The first volume is handed over while the manager waits for the download
        var file = new TempFile();
        var volume = new VolumeWrapper(file, null!);
        await channels.VolumeResponse.WriteAsync((1L, volume)).WaitAsync(Timeout);

        // The downloader stops, which stops the manager
        await channels.DownloadRequest.RetireAsync(true).WaitAsync(Timeout);
        try
        {
            await manager.WaitAsync(Timeout);
        }
        catch (RetiredException)
        {
        }

        Assert.That(File.Exists(file), Is.False, "The decrypted volume that was handed over was left behind");
    }

    /// <summary>
    /// Waits until a writer is waiting on the channel, without taking what it writes.
    /// </summary>
    /// <param name="channel">The channel to watch.</param>
    private static async Task WaitForWaitingWriterAsync<T>(IChannel<T> channel)
    {
        while (true)
        {
            var probe = new WaitingWriterProbe();
            try
            {
                await channel.ReadAsync(probe).ConfigureAwait(false);
            }
            catch (Exception) when (probe.WriterWasWaiting || probe.IsExpired)
            {
            }

            if (probe.WriterWasWaiting)
                return;
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// An offer for a read that never takes a value. The channel only asks the offer when a writer is
    /// waiting, and the read ends at once either way: declined when a writer is waiting, and expired
    /// when none is.
    /// </summary>
    private sealed class WaitingWriterProbe : IExpiringOffer
    {
        /// <summary>
        /// Whether the channel has looked for a waiting writer and found none.
        /// </summary>
        private bool probeComplete;

        /// <summary>
        /// Whether a writer was waiting when the read was made.
        /// </summary>
        public bool WriterWasWaiting { get; private set; }

        /// <inheritdoc />
        public DateTime Expires => DateTime.MinValue;

        /// <inheritdoc />
        public bool IsExpired => probeComplete;

        /// <inheritdoc />
        public Task<bool> OfferAsync(object caller)
        {
            WriterWasWaiting = true;
            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task CommitAsync(object caller) => Task.CompletedTask;

        /// <inheritdoc />
        public Task WithdrawAsync(object caller) => Task.CompletedTask;

        /// <inheritdoc />
        public void ProbeComplete() => probeComplete = true;
    }
}
