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
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A repair of a database whose recreate was stopped partway failed with
/// "Missing block for blocklisthash" (#3364). It now stops with a message
/// that the earlier repair did not complete, and leaves the remote files alone
/// </summary>
public class Issue3364 : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task ARepairAfterAStoppedRecreateSaysTheRecreateDidNotCompleteAsync([Values("dindex", "dblock")] string failingVolume)
    {
        var rnd = new Random(3364);
        var opts = TestOptions;
        opts["blocksize"] = "1kb";
        opts["dblock-size"] = "64kb";
        for (var version = 0; version < 3; version++)
        {
            for (var i = 0; i < 6; i++)
            {
                // Several blocks per file, so the files have block lists
                var data = new byte[1024 * 20 + i];
                rnd.NextBytes(data);
                File.WriteAllBytes(Path.Combine(DATAFOLDER, $"f{version}-{i}"), data);
            }
            using var c = new Controller("file://" + TARGETFOLDER, opts, null);
            TestUtils.AssertResults(await c.BackupAsync(new[] { DATAFOLDER }));
        }

        // Without index files the recreate reads the dblock files
        if (failingVolume == "dblock")
            foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dindex.*"))
                File.Delete(f);

        var remoteBefore = Directory.GetFiles(TARGETFOLDER);

        // The recreate stops partway, when the second volume of the kind cannot be read
        File.Delete(DBFILE);
        BackendLoader.AddBackend(new DeterministicErrorBackend());
        var reads = 0;
        DeterministicErrorBackend.ErrorGenerator = (action, name) =>
            action == DeterministicErrorBackend.BackendAction.GetBefore && name.Contains($".{failingVolume}.") && ++reads == 2;
        try
        {
            using var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, opts.Expand(new { number_of_retries = 0 }), null);
            Assert.CatchAsync<Exception>(async () => await c.RepairAsync(), "The recreate should stop");
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }

        Assert.That(File.Exists(DBFILE), Is.True, "The stopped recreate should leave its database");

        using (var c = new Controller("file://" + TARGETFOLDER, opts, null))
        {
            var ex = Assert.CatchAsync<UserInformationException>(async () => await c.RepairAsync());
            Assert.That(ex!.HelpID, Is.EqualTo("DatabaseIsInRepairState"), ex.ToString());
        }

        Assert.That(Directory.GetFiles(TARGETFOLDER), Is.EquivalentTo(remoteBefore), "The remote files should not change");
    }
}
