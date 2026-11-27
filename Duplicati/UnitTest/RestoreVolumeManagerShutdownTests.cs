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

        // The second download is only started if the temporary folder has room for it. On a runner
        // with less free space than that, the manager would keep it waiting and process the first
        // volume instead, so the steps in `RunAsync` would not happen as written. The free space is set
        // to unlimited so the test does not depend on the disk it runs on.
        var original = VolumeManager.GetAvailableFreeSpace;
        VolumeManager.GetAvailableFreeSpace = _ => long.MaxValue;
        try
        {
            await RunAsync(options);
        }
        finally
        {
            VolumeManager.GetAvailableFreeSpace = original;
        }
    }

    private static async Task RunAsync(Options options)
    {
        var channels = new Channels(options);
        var manager = VolumeManager.RunAsync(channels, options, new RestoreResults());

        // The first volume is requested and its download is taken, so the manager is waiting again
        await channels.VolumeRequest.WriteAsync(new BlockRequest(1, 0, "a", 10, 1, BlockRequestType.Download)).WaitAsync(Timeout);
        Assert.That(await channels.DownloadRequest.ReadAsync().WaitAsync(Timeout), Is.EqualTo(1));

        // The second volume is requested, and nothing takes its download, so the manager waits there
        await channels.VolumeRequest.WriteAsync(new BlockRequest(2, 0, "b", 10, 2, BlockRequestType.Download)).WaitAsync(Timeout);

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
}
