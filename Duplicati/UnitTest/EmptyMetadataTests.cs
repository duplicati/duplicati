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
using System.Data;
using System.IO;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Interface;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest;

public class EmptyMetadataTests : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task ReplaceMissingMetadataRestoresConsistency()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });

        // Create a simple folder structure
        Directory.CreateDirectory(Path.Combine(DATAFOLDER, "folder"));
        File.WriteAllText(Path.Combine(DATAFOLDER, "folder", "file.txt"), "data");

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(c.Backup(new[] { DATAFOLDER }));

        long metaBlocksetId;
        long filesetId;

        using (var db = SQLiteLoader.LoadConnection(DBFILE))
        using (var cmd = db.CreateCommand())
        {
            // Find metadata blockset ID and fileset ID for the backed up folder
            cmd.SetCommandAndParameters($@"SELECT M.""BlocksetID"", FE.""FilesetID""
                                         FROM ""File"" F
                                         JOIN ""FilesetEntry"" FE ON FE.""FileID"" = F.""ID""
                                         JOIN ""Metadataset"" M ON F.""MetadataID"" = M.""ID""
                                         WHERE F.""Path"" LIKE @Path AND F.""BlocksetID"" = {LocalDatabase.FOLDER_BLOCKSET_ID}
                                         ORDER BY FE.""FilesetID"" DESC LIMIT 1");
            cmd.SetParameterValue("@Path", "%folder%");
            using (var rd = cmd.ExecuteReader())
            {
                Assert.That(rd.Read(), Is.True, "Folder entry not found");
                metaBlocksetId = rd.ConvertValueToInt64(0);
                filesetId = rd.ConvertValueToInt64(1);
            }

            // Remove metadata blockset entries to simulate missing metadata
            cmd.SetCommandAndParameters("DELETE FROM \"BlocksetEntry\" WHERE \"BlocksetID\" = @Id");
            cmd.SetParameterValue("@Id", metaBlocksetId);
            cmd.ExecuteNonQuery();

            // Remove the now orphaned blockset record
            cmd.SetCommandAndParameters("DELETE FROM \"Blockset\" WHERE \"ID\" = @Id");
            cmd.SetParameterValue("@Id", metaBlocksetId);
            cmd.ExecuteNonQuery();
        }

        var opts = new Options(testopts);
        var emptyMeta = Duplicati.Library.Main.Utility.WrapMetadata(new Dictionary<string, string>(), opts);

        Assert.Throws<DatabaseInconsistencyException>(() =>
        {
            using var db = LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "verify", true, null, CancellationToken.None).Await();
            db.VerifyConsistency(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None).Await();
        });

        await using (var db = await LocalListBrokenFilesDatabase.CreateAsync(DBFILE, null, CancellationToken.None).ConfigureAwait(false))
        {
            var blockVolumeIds = Array.Empty<long>();

            var emptyId = await db.GetEmptyMetadataBlocksetId(blockVolumeIds, emptyMeta.FileHash, emptyMeta.Blob.Length, CancellationToken.None);
            Assert.That(emptyId, Is.GreaterThanOrEqualTo(0), "Empty metadata blockset not found");

            var replaced = await db.ReplaceMetadata(filesetId, emptyId, CancellationToken.None);
            Assert.That(replaced, Is.GreaterThan(0), "No metadata rows replaced");

            await db.Transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "verify", true, null, CancellationToken.None))
            await db.VerifyConsistency(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
    }
}
