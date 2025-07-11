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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest;

public class Issue6339 : BasicSetupHelper
{
    public class DblockIndex
    {
        [JsonPropertyName("blocks")] public System.Collections.Generic.List<BlockInfo> Blocks { get; set; }
        [JsonPropertyName("volumehash")] public string VolumeHash { get; set; }
        [JsonPropertyName("volumesize")] public long VolumeSize { get; set; }
    }

    public class BlockInfo
    {
        [JsonPropertyName("hash")] public string Hash { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    [Test]
    [Category("Targeted")]
    public void RepairShouldRecreateCompleteDindex([Values(1, 2)] int versions, [Values(true, false)] bool recreatedb)
    {
        var testopts = TestOptions.Expand(new { no_encryption = true, keep_versions = versions });

        var patha = Path.Combine(DATAFOLDER, "A.txt");
        var pathb = Path.Combine(DATAFOLDER, "B.txt");

        File.WriteAllText(patha, "A");
        File.WriteAllText(pathb, "B");

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { patha, pathb }));

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { patha }));

        var dindex = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).First();
        int dataBlocks;
        using (var tf = new TempFolder())
        {
            ZipFileExtractToDirectory(dindex, tf);
            var volFile = Directory.GetFiles(Path.Combine(tf, "vol"), "*", SearchOption.TopDirectoryOnly).First();
            var data = JsonSerializer.Deserialize<DblockIndex>(File.ReadAllText(volFile));
            dataBlocks = data.Blocks.Count;
            Assert.That(data.Blocks.Count, Is.GreaterThanOrEqualTo(3), "Expected original index to contain all blocks");
        }

        File.Delete(dindex);

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Repair());

        if (recreatedb)
        {
            File.Delete(DBFILE);

            using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
                TestUtils.AssertResults(c.Repair());
        }

        var newIndex = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).First();
        using (var tf = new TempFolder())
        {
            ZipFileExtractToDirectory(newIndex, tf);
            var volFile = Directory.GetFiles(Path.Combine(tf, "vol"), "*", SearchOption.TopDirectoryOnly).First();
            var data = JsonSerializer.Deserialize<DblockIndex>(File.ReadAllText(volFile));
            Assert.That(data.Blocks.Count, Is.EqualTo(dataBlocks), "Expected repaired index to contain all blocks");
        }

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Test(100));
    }
}
