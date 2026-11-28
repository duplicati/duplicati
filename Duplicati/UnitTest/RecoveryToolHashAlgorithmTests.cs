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
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The recovery tool used the block hash algorithm for the files, and looked up the block of a file with
    /// a single block by the hash of the file, so it failed when the two algorithms are not the same.
    /// </summary>
    [Category("RecoveryTool")]
    public class RecoveryToolHashAlgorithmTests : BasicSetupHelper
    {
        private string m_originalCurrentDirectory = "";

        // The recovery tool changes the current directory
        [SetUp]
        public void SaveCurrentDirectory()
            => m_originalCurrentDirectory = Directory.GetCurrentDirectory();

        [TearDown]
        public void RestoreCurrentDirectory()
            => Directory.SetCurrentDirectory(m_originalCurrentDirectory);

        [Test]
        [TestCase("SHA256", "SHA256")]
        [TestCase("SHA256", "SHA512")]
        [TestCase("SHA512", "SHA256")]
        [TestCase("MD5", "SHA1")]
        public async Task ABackupIsRestoredWithTheHashAlgorithmsItHas(string blockHash, string fileHash)
        {
            // A file with no blocks, with one block, with a blocklist, and with more than one blocklist
            foreach (var size in new[] { 0, 100, 5_000, 400_000 })
            {
                var data = new byte[size];
                new Random(size).NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file-{size}.bin"), data);
            }

            var options = new Dictionary<string, string>(TestOptions)
            {
                ["blocksize"] = "1kb",
                ["block-hash-algorithm"] = blockHash,
                ["file-hash-algorithm"] = fileHash
            };
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

            var downloaded = Path.Combine(RESTOREFOLDER, "downloaded");
            var restored = Path.Combine(RESTOREFOLDER, "restored");
            Assert.That(CommandLine.RecoveryTool.Program.Main(["download", "file://" + TARGETFOLDER, downloaded, "--passphrase=" + options["passphrase"]]), Is.EqualTo(0));
            Assert.That(CommandLine.RecoveryTool.Program.Main(["index", downloaded]), Is.EqualTo(0));
            Assert.That(CommandLine.RecoveryTool.Program.Main(["restore", downloaded, "--targetpath=" + restored]), Is.EqualTo(0));

            var files = Directory.GetFiles(DATAFOLDER);
            Assert.That(Directory.GetFiles(restored), Has.Length.EqualTo(files.Length));
            foreach (var file in files)
                Assert.That(File.ReadAllBytes(Path.Combine(restored, Path.GetFileName(file))), Is.EqualTo(File.ReadAllBytes(file)), file);
        }
    }
}
