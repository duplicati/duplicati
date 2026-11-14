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
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The file destination can link a downloaded file to the stored one instead of copying it,
    /// so that compacting large volumes on the same disk does not copy them. Issue #6546.
    /// </summary>
    [TestFixture]
    public class FileBackendHardlinkGetTests : BasicSetupHelper
    {
        /// <summary>
        /// Stores a file in the destination folder and downloads it with the given options.
        /// </summary>
        /// <returns>The stored file and the downloaded file</returns>
        private async Task<(string Stored, TempFile Downloaded)> StoreAndGetAsync(Dictionary<string, string?> options)
        {
            var stored = Path.Combine(TARGETFOLDER, "volume.bin");
            File.WriteAllBytes(stored, new byte[] { 1, 2, 3 });

            // A link can only be made on the same file system, and the temporary folder can be
            // on another one (a tmpfs /tmp on Linux), so the download goes next to the destination
            var downloaded = TempFile.WrapExistingFile(Path.Combine(BASEFOLDER, "downloaded.bin"));
            File.WriteAllBytes(downloaded, []);
            using var backend = new Library.Backend.File("file://" + TARGETFOLDER, options);
            await backend.GetAsync("volume.bin", downloaded, CancellationToken.None);
            return (stored, downloaded);
        }

        /// <summary>
        /// Appends to the stored file and returns the length of the downloaded one, which only
        /// follows the stored file when the two are the same file.
        /// </summary>
        private static long LengthAfterAppendingToStored(string stored, string downloaded)
        {
            using (var fs = new FileStream(stored, FileMode.Append, FileAccess.Write))
                fs.WriteByte(4);
            return new FileInfo(downloaded).Length;
        }

        [Test]
        public async Task GetLinksTheStoredFileWhenEnabled()
        {
            var (stored, downloaded) = await StoreAndGetAsync(new Dictionary<string, string?> { ["use-hardlink-for-get"] = "true" });
            using (downloaded)
            {
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(downloaded));
                Assert.AreEqual(4, LengthAfterAppendingToStored(stored, downloaded), "The downloaded file should be a link to the stored file");
            }

            Assert.IsTrue(File.Exists(stored), "Removing the downloaded file must not remove the stored file");
        }

        [Test]
        public async Task GetCopiesByDefault()
        {
            var (stored, downloaded) = await StoreAndGetAsync(new Dictionary<string, string?>());
            using (downloaded)
                Assert.AreEqual(3, LengthAfterAppendingToStored(stored, downloaded), "Without the option the downloaded file should be a copy");
        }

        [Test]
        public void LinkingTurnsOffStreaming()
        {
            using var linking = new Library.Backend.File("file://" + TARGETFOLDER, new Dictionary<string, string?> { ["use-hardlink-for-get"] = "true" });
            using var plain = new Library.Backend.File("file://" + TARGETFOLDER, new Dictionary<string, string?>());

            // The streaming download writes into a new file, so it would never use the link
            Assert.IsFalse(linking.SupportsStreaming);
            Assert.IsTrue(plain.SupportsStreaming);
        }

        /// <summary>
        /// Writes files of random data to the source folder.
        /// </summary>
        private void WriteSourceFiles(int seed, params string[] names)
        {
            var rng = new Random(seed);
            var data = new byte[256 * 1024];
            foreach (var name in names)
            {
                rng.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, name), data);
            }
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task CompactAndRestoreWorkWithLinkedDownloads(bool encrypted)
        {
            var opts = TestOptions.Expand(new
            {
                use_hardlink_for_get = true,
                dblock_size = "300kb",
                no_auto_compact = true,
                keep_versions = 1
            });
            if (!encrypted)
                opts["no-encryption"] = "true";

            WriteSourceFiles(1, "a.bin", "b.bin", "c.bin", "d.bin");
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            // Replace half of the data, so the volumes of the deleted version are partly wasted
            File.Delete(Path.Combine(DATAFOLDER, "a.bin"));
            File.Delete(Path.Combine(DATAFOLDER, "c.bin"));
            WriteSourceFiles(2, "e.bin");
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts.Expand(new { threshold = 1 }), null))
            {
                var res = await c.CompactAsync();
                TestUtils.AssertResults(res);
                Assert.Greater(res.DownloadedFileCount, 0, "Compact should have downloaded volumes");
                Assert.Greater(res.DeletedFileCount, 0, "Compact should have deleted the downloaded volumes");
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts.Expand(new { full_remote_verification = true }), null))
                TestUtils.AssertResults(await c.TestAsync(long.MaxValue));

            var restoreopts = opts.Expand(new { restore_path = RESTOREFOLDER });
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, restoreopts, null))
                TestUtils.AssertResults(await c.RestoreAsync(["*"]));

            foreach (var name in new[] { "b.bin", "d.bin", "e.bin" })
                CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(DATAFOLDER, name)), File.ReadAllBytes(Path.Combine(RESTOREFOLDER, name)), name);
        }

        /// <summary>
        /// Writes a stand-in for the par2 program. It creates a parity file on "create", and on
        /// "repair" writes the bytes of <paramref name="goodCopy"/> over the file being repaired.
        /// </summary>
        private string WriteFakePar2(string goodCopy)
        {
            var folder = Path.Combine(BASEFOLDER, "fake-par2");
            Directory.CreateDirectory(folder);

            if (OperatingSystem.IsWindows())
            {
                var script = Path.Combine(folder, "par2.cmd");
                File.WriteAllText(script, string.Join("\r\n",
                    "@echo off",
                    "if \"%1\"==\"create\" ( echo parity> \"%~5\" & exit /b 0 )",
                    $"if \"%1\"==\"repair\" ( copy /y \"{goodCopy}\" \"%~4\" >nul & exit /b 0 )",
                    "exit /b 0",
                    ""));
                return script;
            }
            else
            {
                var script = Path.Combine(folder, "par2");
                File.WriteAllText(script, string.Join("\n",
                    "#!/bin/sh",
                    "if [ \"$1\" = create ]; then echo parity > \"$5\"; exit 0; fi",
                    $"if [ \"$1\" = repair ]; then cp \"{goodCopy}\" \"$4\"; exit 0; fi",
                    "exit 0",
                    ""));
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return script;
            }
        }

        [Test]
        public async Task ParityRepairDoesNotWriteToTheStoredVolume()
        {
            var goodCopy = Path.Combine(BASEFOLDER, "good-dblock");
            var opts = TestOptions.Expand(new
            {
                use_hardlink_for_get = true,
                no_encryption = true,
                parity_module = "par2"
            });
            opts["par2-program-path"] = WriteFakePar2(goodCopy);

            WriteSourceFiles(3, "data.bin");
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, opts, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var dblock = Directory.GetFiles(TARGETFOLDER, "*.dblock.zip").Single();
            File.Copy(dblock, goodCopy, true);

            // Damage the stored volume, so the download fails verification and is repaired
            using (var fs = new FileStream(dblock, FileMode.Open, FileAccess.Write))
            {
                fs.Position = fs.Length / 2;
                fs.Write(new byte[64], 0, 64);
            }
            var damaged = File.ReadAllBytes(dblock);

            var restoreopts = opts.Expand(new { restore_path = RESTOREFOLDER });
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, restoreopts, null))
                TestUtils.AssertResults(await c.RestoreAsync(["*"]), ["ParityRepairSuccess"]);

            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(DATAFOLDER, "data.bin")), File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "data.bin")));
            CollectionAssert.AreEqual(damaged, File.ReadAllBytes(dblock), "The repair must change only the downloaded copy, not the stored volume");
        }
    }
}
