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
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

public class Issue4988 : BasicSetupHelper
{
    private static string CalculateFileHash(string filename, string hashAlgorithm)
    {
        using (var fs = File.OpenRead(filename))
        using (var hasher = HashFactory.CreateHasher(hashAlgorithm))
            return Convert.ToBase64String(hasher.ComputeHash(fs));
    }

    [Test]
    [Category("ManualTamper")]
    public void TestManualDindexTamperAndRecreate()
    {
        var testopts = TestOptions.Expand(new { no_encryption = "true", allow_empty_source = "true" });
        var opts = new Options(testopts);

        // Step 1: Create an empty folder
        var emptyFolder = Path.Combine(DATAFOLDER, "folder_metadata");
        Directory.CreateDirectory(emptyFolder);

        // Step 2: Backup empty folder
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup([emptyFolder]));

        // Step 3: Find the dblock created
        var dblockPath = Directory.GetFiles(TARGETFOLDER, "*.dblock*", SearchOption.TopDirectoryOnly).First();
        var dblockName = Path.GetFileName(dblockPath);
        var originalDblockHash = CalculateFileHash(dblockPath, opts.FileHashAlgorithm);
        string blockName;

        // Step 4: Modify the dblock zip archive to simulate corruption
        using (var tf = new TempFolder())
        {
            ZipFile.ExtractToDirectory(dblockPath, tf);

            // Delete one of the blocks from inside the archive
            var blockFileToDelete = Directory.GetFiles(tf).FirstOrDefault(x => !x.EndsWith($"{Path.DirectorySeparatorChar}manifest"));
            Assert.IsNotNull(blockFileToDelete, "Expected at least one block inside dblock");
            File.Delete(blockFileToDelete);
            blockName = Library.Utility.Utility.Base64UrlToBase64Plain(Path.GetFileName(blockFileToDelete));

            // Recreate the dblock archive with missing block
            File.Delete(dblockPath);
            ZipFile.CreateFromDirectory(tf, dblockPath);
        }

        // Step 5+6: Copy and edit the dindex file to remove reference to that dblock
        using (var tf = new TempFolder())
        {
            var dindexPath = Directory.GetFiles(TARGETFOLDER, "*.dindex*", SearchOption.TopDirectoryOnly).First();
            ZipFile.ExtractToDirectory(dindexPath, tf);

            var contentFile = Directory.GetFiles(Path.Combine(tf, "vol")).FirstOrDefault(x => x.EndsWith($"{Path.DirectorySeparatorChar}{dblockName}"));
            Assert.IsNotNull(contentFile, "Expected to find the block inside dindex");

            var data = JsonSerializer.Deserialize<DblockIndex>(File.ReadAllText(contentFile));
            Assert.That(data.VolumeHash, Is.EqualTo(originalDblockHash), "Expected to find the original hash in dindex");
            data.VolumeHash = CalculateFileHash(dblockPath, opts.FileHashAlgorithm); ;
            data.VolumeSize = new FileInfo(dblockPath).Length;

            Assert.IsTrue(data.Blocks.Any(x => x.Hash == blockName), "Expected to find the block inside dindex");
            data.Blocks = data.Blocks.Where(x => x.Hash != blockName).ToList(); // Remove the block reference
            File.WriteAllText(contentFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false }));

            // Recreate the dindex archive with modified content
            File.Delete(dindexPath);
            ZipFile.CreateFromDirectory(tf, dindexPath);
        }

        // Step 7+8: Recreate the database (simulate DB reset)
        File.Delete(DBFILE);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
        {
            try
            {
                TestUtils.AssertResults(c.Repair());
                Assert.Fail("Expected recreate to fail due to corrupted dblock/dindex");
            }
            catch (UserInformationException ex) when (ex.HelpID == "DatabaseIsBrokenConsiderPurge")
            {
                Console.WriteLine("Expected recreate failure: " + ex.Message);
            }
        }

        // Step 9: List broken files
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
        {
            var res = c.ListBrokenFiles(null);
            Assert.IsTrue(res.BrokenFiles.Count() > 0, "Expected to find broken files");
            foreach (var f in res.BrokenFiles)
                Console.WriteLine("Broken file: " + f);
        }
    }

    public class DblockIndex
    {
        [JsonPropertyName("blocks")]
        public List<BlockInfo> Blocks { get; set; }

        [JsonPropertyName("volumehash")]
        public string VolumeHash { get; set; }

        [JsonPropertyName("volumesize")]
        public long VolumeSize { get; set; }
    }

    public class BlockInfo
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}


