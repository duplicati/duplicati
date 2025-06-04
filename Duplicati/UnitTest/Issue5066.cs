// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class Issue5066 : BasicSetupHelper
    {

        private static string CalculateFileHash(string filename)
        {
            using (var fs = File.OpenRead(filename))
            using (var hasher = HashFactory.CreateHasher("SHA256"))
                return Convert.ToBase64String(hasher.ComputeHash(fs));
        }

        [Test]
        [Category("Targeted")]
        public void TestDuplicatedBlocklists1([Values] bool deleteAllIndexFiles, [Values] bool restore_legacy)
        {
            var testopts = TestOptions.Expand(new
            {
                blocksize = "1kb",
                restore_legacy = restore_legacy.ToString()
            });
            var hashes = new List<string>();

            // Full blocklist with zeroes
            var data = new byte[32769];
            data.AsSpan().Fill((byte)'a');
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            hashes.Add(CalculateFileHash(Path.Combine(DATAFOLDER, "a")));

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // Make the first blocklist different
            data[0] = (byte)'b';
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            hashes.Insert(0, CalculateFileHash(Path.Combine(DATAFOLDER, "a")));

            // Record existing dindex files
            var existingDIndexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex*", SearchOption.TopDirectoryOnly).ToList();
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // Delete new index files
            foreach (var file in Directory.GetFiles(TARGETFOLDER, "*.dindex*", SearchOption.TopDirectoryOnly))
            {
                if (existingDIndexFiles.Contains(file) || deleteAllIndexFiles)
                    File.Delete(file);
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                TestUtils.AssertResults(c.Repair());
                TestUtils.AssertResults(c.Test());
            }

            for (var version = 0; version < 2; version++)
            {
                File.Delete(Path.Combine(DATAFOLDER, "a"));
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = version }), null))
                {
                    TestUtils.AssertResults(c.Restore(null));
                    var hash = CalculateFileHash(Path.Combine(DATAFOLDER, "a"));
                    Assert.AreEqual(hashes[version], hash, "Hash mismatch for version " + version);
                }
            }
        }

        [Test]
        [Category("Targeted")]
        [TestCase(true)]
        [TestCase(false)]
        public void TestDuplicatedBlocklists2(bool deleteAllIndexFiles)
        {
            var testopts = TestOptions.Expand(new { blocksize = "1kb" });
            var hashes = new List<string>();

            // Full blocklist with zeroes
            var data = new byte[32769];
            data.AsSpan().Fill((byte)'a');
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            hashes.Add(CalculateFileHash(Path.Combine(DATAFOLDER, "a")));

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // Expand to an new set
            data = new byte[32769 * 2 + 1];
            data.AsSpan().Fill((byte)'b');
            data[data.Length - 1] = (byte)'a';
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
            hashes.Insert(0, CalculateFileHash(Path.Combine(DATAFOLDER, "a")));

            // Record existing dindex files
            var existingDIndexFiles = Directory.GetFiles(TARGETFOLDER, "*.dindex*", SearchOption.TopDirectoryOnly).ToList();
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup([DATAFOLDER]));

            // Delete new index files
            foreach (var file in Directory.GetFiles(TARGETFOLDER, "*.dindex*", SearchOption.TopDirectoryOnly))
            {
                if (existingDIndexFiles.Contains(file) || deleteAllIndexFiles)
                    File.Delete(file);
            }

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            {
                TestUtils.AssertResults(c.Repair());
                TestUtils.AssertResults(c.Test());
            }

            for (var version = 0; version < 2; version++)
            {
                File.Delete(Path.Combine(DATAFOLDER, "a"));
                using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { version = version }), null))
                {
                    TestUtils.AssertResults(c.Restore(null));
                    var hash = CalculateFileHash(Path.Combine(DATAFOLDER, "a"));
                    Assert.AreEqual(hashes[version], hash, "Hash mismatch for version " + version);
                }
            }
        }
    }
}

