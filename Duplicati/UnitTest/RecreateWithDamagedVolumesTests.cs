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
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A single index or block volume that could not be decrypted made the whole recreate fail, and left
    /// a database that no other operation accepts. A damaged volume must only cost what is stored in it.
    /// </summary>
    [Category("RepairHandler")]
    public class RecreateWithDamagedVolumesTests : BasicSetupHelper
    {
        private string Target => "file://" + TARGETFOLDER;

        private Dictionary<string, string> UserOptions
        {
            get
            {
                var opts = new Dictionary<string, string>(TestOptions)
                {
                    ["dblock-size"] = "1mb",
                    ["number-of-retries"] = "0"
                };
#if DEBUG
                // Behave as for the user, who gets the recreate to continue. The option only exists in DEBUG builds.
                opts["unittest-mode"] = "false";
#endif
                return opts;
            }
        }

        private string[] Remote(string type)
            => Directory.GetFiles(TARGETFOLDER, $"*.{type}.*").OrderBy(x => x, StringComparer.Ordinal).ToArray();

        /// <summary>
        /// Makes a backup with several volumes, with a file larger than a blocklist block, and removes the database
        /// </summary>
        private async Task MakeBackupAsync(Dictionary<string, string> options)
        {
            var random = new Random(42);
            for (var i = 0; i < 12; i++)
            {
                var data = new byte[300_000 + i];
                random.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file-{i}.bin"), data);
            }

            using (var c = new Controller(Target, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            Assert.That(Remote("dblock"), Has.Length.GreaterThan(2));
            File.Delete(DBFILE);
        }

        private static void Damage(string path, string how)
        {
            var data = File.ReadAllBytes(path);
            File.WriteAllBytes(path, how switch
            {
                "truncated" => data[..100],
                "empty" => [],
                "garbage" => Enumerable.Range(0, data.Length).Select(x => (byte)(x * 7)).ToArray(),
                _ => throw new ArgumentException(how)
            });
        }

        /// <summary>
        /// The blocks of a damaged index file are found in the block volumes, so nothing is lost
        /// </summary>
        [Test]
        [TestCase("truncated")]
        [TestCase("empty")]
        [TestCase("garbage")]
        public async Task ARecreateContinuesWithoutADamagedIndexFile(string how)
        {
            await MakeBackupAsync(UserOptions);
            Damage(Remote("dindex")[0], how);

            using (var c = new Controller(Target, UserOptions, null))
            {
                var results = await c.RepairAsync();
                Assert.That(results.Errors.Select(x => x.Split('\n')[0]), Has.All.Contains(Path.GetFileName(Remote("dindex")[0])), "The damaged file should be reported, and nothing else");
            }

            await AssertRestoresAsync(12);
        }

        /// <summary>
        /// The blocklist of a file can be in a block volume that no other index file points to, when the
        /// index file of that volume is damaged or missing. The recreate must still read that volume.
        /// Which volume a blocklist ends up in depends on the timing of the backup, so this made the test
        /// above fail now and then. Here the order is fixed, and the index file of each volume is tried.
        /// </summary>
        [Test]
        [TestCase("garbage")]
        [TestCase("missing")]
        public async Task ARecreateReadsABlockVolumeThatOnlyItsIndexFilePointedTo(string how)
        {
            var options = UserOptions;
            options["dblock-size"] = "200kb";
            // One compressor, so the blocks are written in order and the blocklist ends in the last volume
            options["concurrency-compressors"] = "1";

            // One file with more blocks than fit in a blocklist block, so its blocks are listed in a blocklist
            var data = new byte[300_000];
            new Random(42).NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "file-0.bin"), data);

            using (var c = new Controller(Target, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            Assert.That(Remote("dblock"), Has.Length.EqualTo(2));
            File.Delete(DBFILE);

            var original = Path.Combine(BASEFOLDER, "original-backup");
            Directory.CreateDirectory(original);
            foreach (var file in Directory.GetFiles(TARGETFOLDER))
                File.Copy(file, Path.Combine(original, Path.GetFileName(file)));

            for (var i = 0; i < 2; i++)
            {
                foreach (var file in Directory.GetFiles(TARGETFOLDER))
                    File.Delete(file);
                foreach (var file in Directory.GetFiles(original))
                    File.Copy(file, Path.Combine(TARGETFOLDER, Path.GetFileName(file)));
                if (File.Exists(DBFILE))
                    File.Delete(DBFILE);
                if (Directory.Exists(RESTOREFOLDER))
                    Directory.Delete(RESTOREFOLDER, true);

                var index = Remote("dindex")[i];
                if (how == "missing")
                    File.Delete(index);
                else
                    Damage(index, how);

                using (var c = new Controller(Target, options, null))
                    await c.RepairAsync();

                await AssertRestoresAsync(1);
            }
        }

        /// <summary>
        /// Files are lost with a damaged block volume, and purging them gives a backup that works
        /// </summary>
        [Test]
        [TestCase("truncated")]
        [TestCase("garbage")]
        public async Task ARecreateWithADamagedBlockVolumeCanBePurged(string how)
        {
            // Without index files the block volumes are read by the recreate
            var options = UserOptions;
            options["index-file-policy"] = "none";
            await MakeBackupAsync(options);
            var damaged = Remote("dblock")[0];
            Damage(damaged, how);

            using (var c = new Controller(Target, options, null))
            {
                var ex = Assert.CatchAsync<UserInformationException>(async () => await c.RepairAsync());
                Assert.That(ex!.HelpID, Is.EqualTo("DatabaseIsBrokenConsiderPurge"));
            }

            using (var c = new Controller(Target, options, null))
                Assert.That((await c.PurgeBrokenFilesAsync(null)).Errors, Is.Empty);

            using (var c = new Controller(Target, options, null))
                Assert.That((await c.BackupAsync([DATAFOLDER])).Errors, Is.Empty);

            await AssertRestoresAsync(12);
        }

        private async Task AssertRestoresAsync(int count)
        {
            var options = UserOptions;
            options["restore-path"] = RESTOREFOLDER;
            options["restore-with-local-blocks"] = "false";
            using (var c = new Controller(Target, options, null))
                TestUtils.AssertResults(await c.RestoreAsync(null));

            var restored = Directory.GetFiles(RESTOREFOLDER, "*.bin");
            Assert.That(restored, Has.Length.EqualTo(count));
            foreach (var file in restored)
                Assert.That(File.ReadAllBytes(file), Is.EqualTo(File.ReadAllBytes(Path.Combine(DATAFOLDER, Path.GetFileName(file)))), file);
        }

        /// <summary>
        /// A destination that fails is not a damaged file, and the recreate must not go on without its data
        /// </summary>
        [Test]
        [TestCase("dindex")]
        [TestCase("dblock")]
        public async Task ARecreateStopsWhenAVolumeCannotBeDownloaded(string type)
        {
            var options = UserOptions;
            if (type == "dblock")
                options["index-file-policy"] = "none";
            await MakeBackupAsync(options);

            Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
            DeterministicErrorBackend.ErrorGenerator = (action, name) => action == DeterministicErrorBackend.BackendAction.GetBefore && name.Contains($".{type}.");
            try
            {
                using var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, options, null);
                Assert.CatchAsync<DeterministicErrorBackend.DeterministicErrorBackendException>(async () => await c.RepairAsync());
            }
            finally
            {
                DeterministicErrorBackend.ErrorGenerator = null;
            }
        }
    }
}
