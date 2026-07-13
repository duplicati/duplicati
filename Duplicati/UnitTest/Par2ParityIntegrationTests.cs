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
using Duplicati.Library.Parity;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Exercises the PAR2 parity module through the full backup/restore flow: a backup with
    /// parity enabled, a corrupted remote data volume, detection of the corruption during
    /// verification, and recovery of the data on restore. These tests require the external
    /// "par2" (par2cmdline) program; if it is not available they are skipped.
    /// </summary>
    public class Par2ParityIntegrationTests : BasicSetupHelper
    {
        /// <summary>
        /// A high redundancy so that a small corruption is comfortably recoverable.
        /// </summary>
        private const int Redundancy = 30;

        /// <summary>
        /// Skips the test if the par2 program is not available. Honors the PAR2_PROGRAM_PATH
        /// environment variable (used by the Docker-based test run) as an explicit path.
        /// </summary>
        /// <returns>The explicit par2 program path, or null to search the system path</returns>
        private static string RequirePar2()
        {
            var programPath = Environment.GetEnvironmentVariable("PAR2_PROGRAM_PATH");
            var opts = new Dictionary<string, string> { { "parity-redundancy-level", Redundancy.ToString() } };
            if (!string.IsNullOrWhiteSpace(programPath))
                opts["par2-program-path"] = programPath;

            using var probe = new Par2Parity(opts);
            if (!probe.IsAvailable)
                Assert.Ignore("The par2 (par2cmdline) program is not available on this system.");
            return programPath;
        }

        /// <summary>
        /// Builds a set of options with the par2 parity module enabled.
        /// </summary>
        private Dictionary<string, string> ParityTestOptions(string programPath)
        {
            var opts = TestOptions.Expand(new
            {
                parity_module = "par2",
                parity_redundancy_level = Redundancy
            });
            if (!string.IsNullOrWhiteSpace(programPath))
                opts["par2-program-path"] = programPath;
            return opts;
        }

        /// <summary>
        /// Returns the data volume (dblock) files on the backend, excluding parity companions.
        /// </summary>
        private string[] DblockFiles()
            => Directory.GetFiles(TARGETFOLDER)
                .Where(f => f.Contains(".dblock.") && !f.EndsWith(".par2", StringComparison.OrdinalIgnoreCase))
                .ToArray();

        /// <summary>
        /// Overwrites a run of bytes in the middle of a file with random data.
        /// </summary>
        private static void CorruptFileMiddle(string path, int count)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            var offset = Math.Max(0, (fs.Length / 2) - (count / 2));
            fs.Seek(offset, SeekOrigin.Begin);
            var junk = new byte[count];
            new Random(4242).NextBytes(junk);
            fs.Write(junk, 0, count);
        }

        [Test]
        [Category("Parity")]
        public async Task BackupCreatesParityDetectsCorruptionAndRestoreRecovers()
        {
            var programPath = RequirePar2();

            // A single multi-MB file lands in one dblock (dblock-size 10mb, blocksize 10kb).
            var buffer = new byte[4 * 1024 * 1024];
            new Random(1).NextBytes(buffer);
            var sourceFile = Path.Combine(DATAFOLDER, "data.bin");
            File.WriteAllBytes(sourceFile, buffer);
            var originalBytes = File.ReadAllBytes(sourceFile);

            // Backup with parity enabled.
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, ParityTestOptions(programPath), null))
                TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));

            // A parity companion should have been created next to each data volume.
            var dblocks = DblockFiles();
            Assert.IsNotEmpty(dblocks, "Expected at least one dblock volume");
            foreach (var d in dblocks)
                Assert.IsTrue(File.Exists(d + ".par2"), $"Missing parity file for {Path.GetFileName(d)}");

            // Corrupt the data volume on the backend (small enough to be within redundancy).
            CorruptFileMiddle(dblocks[0], 2000);

            // Verification must DETECT the corruption. In the test flow parity repair is
            // disabled, so the damage is reported rather than silently masked. Detection may
            // surface either as a per-volume verification error or as a thrown exception.
            var detected = false;
            try
            {
                var testopts = ParityTestOptions(programPath);
                testopts["full-remote-verification"] = "true";
                using var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null);
                var testResults = await c.TestAsync(long.MaxValue);
                detected = testResults.Verifications.Any(v => v.Value.Any());
            }
            catch (Exception)
            {
                detected = true;
            }
            Assert.IsTrue(detected, "The corrupted remote volume should be detected during verification");

            // Restore: the damaged data volume is repaired in place using the parity file
            // during download, so the original content is recovered. A ParityRepairSuccess
            // warning is expected (the remote copy is damaged), so it is ignored here.
            var restoreopts = ParityTestOptions(programPath);
            restoreopts["restore-path"] = RESTOREFOLDER;
            if (Directory.Exists(RESTOREFOLDER))
                Directory.Delete(RESTOREFOLDER, true);
            Directory.CreateDirectory(RESTOREFOLDER);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, restoreopts, null))
                TestUtils.AssertResults(await c.RestoreAsync(new[] { sourceFile }), ["ParityRepairSuccess"]);

            var restoredFile = Path.Combine(RESTOREFOLDER, "data.bin");
            Assert.IsTrue(File.Exists(restoredFile), "Restored file should exist");
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(restoredFile), "Restored content should match the original after parity recovery");
        }
    }
}
