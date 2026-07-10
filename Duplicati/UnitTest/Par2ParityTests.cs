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
using Duplicati.Library.Parity;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the default PAR2 parity implementation end to end. These tests require the
    /// external "par2" (par2cmdline) program; if it is not available they are skipped.
    /// </summary>
    public class Par2ParityTests : BasicSetupHelper
    {
        /// <summary>
        /// Creates a par2 parity module, skipping the test if the par2 program is not available.
        /// </summary>
        private static Par2Parity RequirePar2()
        {
            var parity = new Par2Parity(new Dictionary<string, string> { { "parity-redundancy-level", "10" } });
            if (!parity.IsAvailable)
            {
                parity.Dispose();
                Assert.Ignore("The par2 (par2cmdline) program is not available on this system.");
            }
            return parity;
        }

        /// <summary>
        /// Writes deterministic pseudo-random data to a file.
        /// </summary>
        private static void WriteRandomData(string path, int length, int seed)
        {
            var rnd = new Random(seed);
            var buffer = new byte[length];
            rnd.NextBytes(buffer);
            File.WriteAllBytes(path, buffer);
        }

        /// <summary>
        /// Corrupts a run of bytes in the middle of a file.
        /// </summary>
        private static void Corrupt(string path, int offset, int count)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            fs.Seek(offset, SeekOrigin.Begin);
            var junk = new byte[count];
            new Random(999).NextBytes(junk);
            fs.Write(junk, 0, count);
        }

        [Test]
        [Category("Parity")]
        public void CreateVerifyRepairRestoresContent()
        {
            using var parity = RequirePar2();
            using var data = new TempFile();
            using var original = new TempFile();
            using var parityfile = new TempFile();

            WriteRandomData(data, 2_000_000, 1);
            File.Copy(data, original, true);

            parity.Create(data, parityfile);
            Assert.IsTrue(new FileInfo(parityfile).Length > 0, "Parity file should not be empty");
            Assert.IsTrue(parity.Verify(data, parityfile), "Intact file should verify");

            Corrupt(data, 500_000, 4000);
            Assert.IsFalse(parity.Verify(data, parityfile), "Corrupted file should fail verification");

            Assert.IsTrue(parity.Repair(data, parityfile), "Repair should succeed");
            Assert.IsTrue(parity.Verify(data, parityfile), "Repaired file should verify");
            CollectionAssert.AreEqual(File.ReadAllBytes(original), File.ReadAllBytes(data), "Repaired content should match original");
        }

        [Test]
        [Category("Parity")]
        public void RepairWorksWithDifferentInputName()
        {
            // In the real pipeline the file is created under one temp name (on upload)
            // and repaired under a different temp name (on download). par2 stores the
            // protected file by name, so this verifies the module handles the mismatch.
            using var parity = RequirePar2();
            using var uploadFile = new TempFile();
            using var downloadFile = new TempFile();
            using var original = new TempFile();
            using var parityfile = new TempFile();

            WriteRandomData(uploadFile, 2_000_000, 2);
            File.Copy(uploadFile, original, true);
            parity.Create(uploadFile, parityfile);

            // Simulate the same content downloaded under a different name, then corrupted
            File.Copy(original, downloadFile, true);
            Corrupt(downloadFile, 800_000, 5000);

            Assert.IsTrue(parity.Repair(downloadFile, parityfile), "Repair should succeed despite the different filename");
            CollectionAssert.AreEqual(File.ReadAllBytes(original), File.ReadAllBytes(downloadFile), "Repaired content should match original");
        }

        [Test]
        [Category("Parity")]
        public void RepairFailsWhenDamageExceedsRedundancy()
        {
            using var parity = RequirePar2();
            using var data = new TempFile();
            using var parityfile = new TempFile();

            WriteRandomData(data, 1_000_000, 3);
            parity.Create(data, parityfile);

            // Overwrite most of the file; 10% redundancy cannot recover this
            WriteRandomData(data, 1_000_000, 4);

            Assert.IsFalse(parity.Repair(data, parityfile), "Repair should fail when damage exceeds redundancy");
        }
    }
}
