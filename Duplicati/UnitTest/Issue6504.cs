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
using NUnit.Framework;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Main.Database;

namespace Duplicati.UnitTest;

public class Issue6504 : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public void RecreateIndexFilesShouldHandleDuplicatedBlocks()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true, keep_versions = 1 });

        var patha = Path.Combine(DATAFOLDER, "A.txt");
        var pathb = Path.Combine(DATAFOLDER, "B.txt");

        File.WriteAllText(patha, "A");
        File.WriteAllText(pathb, "B");

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { patha, pathb }));

        var original_dblock_file = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).First();
        var original_dindex_file = Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly).First();

        var opts = new Library.Main.Options(testopts);

        // Create a duplicated dblock file
        var newdblockpath = Path.Combine(TARGETFOLDER, VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts, VolumeWriterBase.GenerateGuid(), System.DateTime.UtcNow));
        File.Copy(original_dblock_file, newdblockpath);

        var oldname = Path.GetFileName(original_dblock_file);
        var newname = Path.GetFileName(newdblockpath);

        // Duplicate the index file as well, but pointing to the new duplicated block
        var newdindexpath_broken = Path.Combine(TARGETFOLDER, VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Index, new Library.Main.Options(testopts), VolumeWriterBase.GenerateGuid(), System.DateTime.UtcNow));
        var newdindexpath_correct = Path.Combine(TARGETFOLDER, VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Index, new Library.Main.Options(testopts), VolumeWriterBase.GenerateGuid(), System.DateTime.UtcNow));
        using (var wr = new IndexVolumeWriter(opts))
        {
            using (var rd = new IndexVolumeReader(opts.CompressionModule, original_dindex_file, opts, opts.BlockhashSize))
            {
                foreach (var n in rd.Volumes)
                {
                    wr.StartVolume(newname);
                    foreach (var x in n.Blocks.Take(2)) // Only take two to simulate a corrupted index file
                        wr.AddBlock(x.Key, x.Value);
                    wr.FinishVolume(n.Hash, n.Length);
                }
            }
            wr.Close();

            File.Copy(wr.LocalFilename, newdindexpath_broken);
        }

        using (var wr = new IndexVolumeWriter(opts))
        {
            using (var rd = new IndexVolumeReader(opts.CompressionModule, original_dindex_file, opts, opts.BlockhashSize))
                wr.CopyFrom(rd, (_) => newname);
            wr.Close();

            File.Copy(wr.LocalFilename, newdindexpath_correct);
        }


        // Prepare for accepting the new duplicated block by recreating the database
        File.Delete(DBFILE);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Repair());

        // Force the error by making sure the duplicate blocks are from both dblock files
        using (var db = SQLiteLoader.LoadConnection(DBFILE, 0))
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT BlockId,VolumeID FROM DuplicateBlock";
            var duplicatedBlocks = cmd.ExecuteReaderEnumerable().Select(r => new { BlockId = r.GetInt64(0), VolumeID = r.GetInt64(1) }).ToList();

            cmd.CommandText = @"SELECT ID,VolumeID,Hash FROM Block";
            var blocks = cmd.ExecuteReaderEnumerable().Select(r => new { ID = r.GetInt64(0), VolumeID = r.GetInt64(1), Hash = r.GetString(2) }).ToList();

            if (duplicatedBlocks.Select(x => x.VolumeID).Distinct().Count() == 1)
            {
                // All duplicated blocks are from the same volume, change one to be from the other volume
                var blockToChange = duplicatedBlocks.First();
                var duplicateVolumeId = blockToChange.VolumeID;
                var otherVolumeId = blocks.First(x => x.VolumeID != duplicateVolumeId && x.ID != blockToChange.BlockId).VolumeID;

                cmd.CommandText = "UPDATE DuplicateBlock SET VolumeID=@VolumeID WHERE BlockID=@BlockID";
                cmd.AddNamedParameter("@VolumeID", otherVolumeId);
                cmd.AddNamedParameter("@BlockID", blockToChange.BlockId);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();

                cmd.CommandText = "UPDATE Block SET VolumeID=@VolumeID WHERE ID=@BlockID";
                cmd.AddNamedParameter("@VolumeID", duplicateVolumeId);
                cmd.AddNamedParameter("@BlockID", blockToChange.BlockId);
                cmd.ExecuteNonQuery();
            }
        }


        // Make sure the test rewrites the faulty index file
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
        {
            var res = c.Test(100);
            Assert.That(res.Warnings.Count, Is.EqualTo(1), "Expected one warning about faulty index files");
            Assert.That(res.Warnings.Any(c => c.Contains("FaultyIndexFiles")), Is.True, "Expected a warning about faulty index files");
        }

        // Second run should not have faulty index files
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Test(100));
    }
}
