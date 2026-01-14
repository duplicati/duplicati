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

using System.Collections.Generic;
using System.IO;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest;

[TestFixture]
public class MetadataContentInDatabaseTests : BasicSetupHelper
{
    private static long CountMetadatasetsWithContent(string dbPath)
    {
        using var db = SQLiteLoader.LoadConnection(dbPath);
        using var cmd = db.CreateCommand();

        return cmd.ExecuteScalarInt64(@"
            SELECT COUNT(*)
            FROM ""Metadataset""
            WHERE ""Content"" IS NOT NULL AND ""Content"" != ''
        ");
    }

    [Test]
    public void Backup_StoresMetadataContent_WhenOptionEnabled()
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            ["store-metadata-content-in-database"] = "true"
        };

        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder"));
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "folder", "file.txt"), "data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(c.Backup([this.DATAFOLDER]));

        Assert.That(CountMetadatasetsWithContent(options["dbpath"]), Is.GreaterThan(0));
    }

    [Test]
    public void Recreate_StoresMetadataContent_WhenOptionEnabled()
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            ["store-metadata-content-in-database"] = "true"
        };

        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder"));
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "folder", "file.txt"), "data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(c.Backup([this.DATAFOLDER]));

        // Force a recreate by deleting the local database.
        File.Delete(options["dbpath"]);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(c.Repair());

        Assert.That(CountMetadatasetsWithContent(options["dbpath"]), Is.GreaterThan(0));
    }
}
