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

using System.IO;
using NUnit.Framework;
using System;
using NUnit.Framework.Internal;
using Duplicati.Library.DynamicLoader;

namespace Duplicati.UnitTest
{
    public class Issue6200 : BasicSetupHelper
    {
        [Test]
        [Category("Targeted")]
        public void VerifyMultiVolumeCompactWorks()
        {
            const int FILESIZE = 500 * 1024;
            const int FILECOUNT = 20;

            var testopts = TestOptions.Expand(new
            {
                keep_versions = 1,
                no_auto_compact = true,
                zip_compression_level = 0,
                asynchronous_concurrent_upload_limit = 1,
                asynchronous_upload_limit = 1,
                blocksize = "100kb",
                dblock_size = "1mb"
            });

            var rnd = Random.Shared;
            // Create 100 files of 10MB each
            for (var i = 0; i < FILECOUNT; i++)
            {
                var data = new byte[FILESIZE];
                rnd.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"a{i}"), data);
            }

            // Make a backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            // Delete half the files
            for (var i = 0; i < FILECOUNT / 2; i++)
                File.Delete(Path.Combine(DATAFOLDER, $"a{i}"));

            // Make a backup
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Backup(new string[] { DATAFOLDER }));

            var fullBlockvolumeCount = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).Length;

            // Now run compact
            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Compact());

            var newBlockvolumeCount = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).Length;

            // Check that the number of dblock files is reduced by at least 4
            Assert.That(newBlockvolumeCount, Is.LessThan(fullBlockvolumeCount - 4), "Compact did not reduce the number of dblock files");

            BackendLoader.AddBackend(new DeterministicErrorBackend());
            File.Delete(DBFILE);

            DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
            {
                if (action.IsGetOperation && remotename.Contains(".dblock."))
                    return true;
                return false;
            };

            using (var c = new Library.Main.Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());
        }
    }
}

