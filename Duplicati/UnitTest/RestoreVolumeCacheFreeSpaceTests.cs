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
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation.Restore;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A volume that is downloaded and decrypted needs room in the temporary folder before it is
    /// cached. The cache made room only when a decrypted volume was added, so the volumes in flight
    /// could fill the disk while decrypting ("There is not enough space on the disk", #6606).
    /// Room is now also made before a volume is downloaded.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class RestoreVolumeCacheFreeSpaceTests : BasicSetupHelper
    {
        [Test]
        public async Task RoomIsMadeBeforeAVolumeIsDownloadedAsync()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true",
                ["dblock-size"] = "100kb",
                ["blocksize"] = "10kb"
            };

            var expected = new Dictionary<string, byte[]>();
            var rnd = new Random(6606);
            for (var i = 0; i < 6; i++)
            {
                var data = new byte[90 * 1024];
                rnd.NextBytes(data);
                var path = Path.Combine(this.DATAFOLDER, $"file{i}.bin");
                File.WriteAllBytes(path, data);
                expected[$"file{i}.bin"] = data;
            }

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));
            Assert.That(Directory.GetFiles(this.TARGETFOLDER, "*.dblock.zip").Length, Is.GreaterThan(2), "The test needs several volumes");

            // The disk reports just above the minimum free space, so adding a decrypted volume to
            // the cache never needs room, but downloading the next one does
            const long free = 100L * 1024 * 1024;
            var restoreOptions = new Dictionary<string, string>(options)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-volume-cache-min-free"] = $"{free - 1024}b",
                ["restore-volume-downloaders"] = "1",
                ["restore-volume-decryptors"] = "1",
                ["restore-file-processors"] = "1"
            };

            var original = VolumeManager.GetAvailableFreeSpace;
            VolumeManager.GetAvailableFreeSpace = _ => (long?)free;
            try
            {
                using var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null);
                var results = await c.RestoreAsync(new[] { "*" });
                // Evicting for disk pressure is expected here, and warns after a few evictions
                TestUtils.AssertResults(results, "CachePressure");

                Assert.That(((RestoreResults)results).CachePressureEvictions, Is.GreaterThan(0),
                    "Cached volumes are evicted to make room for the volume that is downloaded");
            }
            finally
            {
                VolumeManager.GetAvailableFreeSpace = original;
            }

            foreach (var (name, data) in expected)
                Assert.That(File.ReadAllBytes(Path.Combine(this.RESTOREFOLDER, name)), Is.EqualTo(data), name);
        }

        [Test]
        public async Task ADownloadWaitsWhileThereIsNoRoomForItAsync()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true",
                ["dblock-size"] = "100kb",
                ["blocksize"] = "10kb"
            };

            // One file that spans several volumes: its blocks are requested in a burst, so
            // several volumes are requested before the first one is downloaded
            var data = new byte[600 * 1024];
            new Random(6606).NextBytes(data);
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file.bin"), data);

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { this.DATAFOLDER }));
            Assert.That(Directory.GetFiles(this.TARGETFOLDER, "*.dblock.zip").Length, Is.GreaterThan(2), "The test needs several volumes");

            // The disk never has room for more than the minimum free space, and evicting does
            // not help, so only one volume at a time can be in flight and the rest must wait
            const long free = 100L * 1024 * 1024;
            var restoreOptions = new Dictionary<string, string>(options)
            {
                ["restore-path"] = this.RESTOREFOLDER,
                ["restore-volume-cache-min-free"] = $"{free - 1024}b",
                ["restore-channel-buffer-size"] = "64",
                ["restore-file-processors"] = "1"
            };

            var waiting = new List<string>();
            var resumed = new List<string>();
            var original = VolumeManager.GetAvailableFreeSpace;
            VolumeManager.GetAvailableFreeSpace = _ => (long?)free;
            try
            {
                using (Library.Logging.Log.StartScope(entry =>
                {
                    var message = entry.FormattedMessage;
                    lock (waiting)
                        if (message.Contains("waits for room in the temporary folder"))
                            waiting.Add(message);
                        else if (message.Contains("which waited for room in the temporary folder"))
                            resumed.Add(message);
                }))
                {
                    using var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null);
                    TestUtils.AssertResults(await c.RestoreAsync(new[] { "*" }), "CachePressure");
                }
            }
            finally
            {
                VolumeManager.GetAvailableFreeSpace = original;
            }

            Assert.That(waiting, Is.Not.Empty, "A volume that is requested while another is in flight waits for room");
            Assert.That(resumed.Count, Is.EqualTo(waiting.Count), "Every volume that waited is downloaded");
            Assert.That(File.ReadAllBytes(Path.Combine(this.RESTOREFOLDER, "file.bin")), Is.EqualTo(data));
        }
    }
}
