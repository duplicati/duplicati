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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Files in archive storage have to be brought back before a restore can read them, so the
/// remote volumes a restore reads are listed first (#6228).
/// </summary>
public class ListRestoreVolumesTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => new(TestOptions)
    {
        ["no-encryption"] = "true",
        // Small volumes, so the two folders end up in different volumes
        ["dblock-size"] = "100kb",
        ["blocksize"] = "10kb",
    };

    private void CreateSourceData(string folder, int seed)
    {
        var random = new Random(seed);
        Directory.CreateDirectory(Path.Combine(DATAFOLDER, folder));
        for (var i = 0; i < 3; i++)
        {
            var data = new byte[200 * 1024];
            random.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, folder, $"file{i}.bin"), data);
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task TheListedVolumesAreTheOnesTheRestoreDownloadsAsync()
    {
        // The second folder is added by a later backup, so its blocks are only in the later volumes
        CreateSourceData("first", 6228);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        CreateSourceData("second", 6229);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var dblocks = Directory.EnumerateFiles(TARGETFOLDER, "*.dblock.*").Select(Path.GetFileName).ToHashSet();
        Assert.That(dblocks.Count, Is.GreaterThan(2), "The backup should have made several volumes");

        IListRestoreVolumesResults all;
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            all = await c.ListRestoreVolumesAsync(null);
        Assert.That(all.FileCount, Is.GreaterThanOrEqualTo(6));
        Assert.That(all.Volumes.Select(x => x.Name), Is.SubsetOf(dblocks), "Only block volumes are read");
        Assert.That(all.Volumes.All(x => x.Size == new FileInfo(Path.Combine(TARGETFOLDER, x.Name)).Length), Is.True, "The sizes are those of the remote files");

        IListRestoreVolumesResults first;
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            first = await c.ListRestoreVolumesAsync([Path.Combine(DATAFOLDER, "first") + Path.DirectorySeparatorChar + "*"]);
        Assert.That(first.Volumes.Select(x => x.Name), Is.SubsetOf(all.Volumes.Select(x => x.Name)));
        Assert.That(first.Volumes.Count(), Is.LessThan(all.Volumes.Count()), "Restoring one folder reads fewer volumes");

        // The destination is not contacted: the list is the same with a destination that is not there
        using (var c = new Controller("file://" + Path.Combine(BASEFOLDER, "not-there"), Options, null))
        {
            var offline = await c.ListRestoreVolumesAsync(null);
            Assert.That(offline.Volumes.Select(x => x.Name), Is.EqualTo(all.Volumes.Select(x => x.Name)));
        }
        Assert.That(Directory.Exists(Path.Combine(BASEFOLDER, "not-there")), Is.False);

        // A restore to an empty folder reads exactly the listed volumes
        var downloaded = new List<string>();
        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (action == DeterministicErrorBackend.BackendAction.GetBefore)
                lock (downloaded)
                    downloaded.Add(remotename);
            return false;
        };
        try
        {
            var restoreOptions = new Dictionary<string, string>(Options) { ["restore-path"] = RESTOREFOLDER };
            using var c = new Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, restoreOptions, null);
            TestUtils.AssertResults(await c.RestoreAsync(null));
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }
        Assert.That(downloaded, Is.EquivalentTo(all.Volumes.Select(x => x.Name)), "The restore read other volumes than the listed ones");
    }

    [Test]
    [Category("Targeted")]
    public void TheListNeedsTheLocalDatabase()
    {
        var options = new Dictionary<string, string>(Options) { ["dbpath"] = Path.Combine(BASEFOLDER, "missing.sqlite") };
        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => c.ListRestoreVolumesAsync(null));
        Assert.That(ex!.HelpID, Is.EqualTo("DatabaseDoesNotExist"));
    }
}
