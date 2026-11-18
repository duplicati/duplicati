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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Operation.Restore;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The file lister reads the files and folders to restore in batches, so a restore of millions of
/// files does not hold the whole list in memory (#7538). The batches together must hold every entry
/// once, in the order of the restore.
/// </summary>
[NonParallelizable]
public class RestoreFileListBatchTests : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task TheBatchesHoldEveryEntryOnceInTheOrderOfTheRestoreAsync()
    {
        // Files of different sizes, two of the same size, and folders, so the order and the ties cross the batches
        var sizes = new[] { 10, 3000, 500, 500, 7000, 1, 2500, 0 };
        for (var i = 0; i < sizes.Length; i++)
        {
            var folder = Path.Combine(DATAFOLDER, "f" + (i % 3));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"file{i}.bin"), Enumerable.Repeat((byte)i, sizes[i]).ToArray());
        }
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        await using var db = await LocalRestoreDatabase.CreateAsync(DBFILE, null, CancellationToken.None);
        await db.PrepareRestoreFilelistAsync(DateTime.UtcNow, null, null, false, CancellationToken.None);
        await db.SetTargetPathsAsync("", "", CancellationToken.None);
        await db.PrepareRestoreOrderAsync(CancellationToken.None);

        async Task<List<(long, string, long)>> ReadFiles(int count)
        {
            var all = new List<(long, string, long)>();
            for (var after = 0L; ;)
            {
                var batch = await db.GetFilesAndSymlinksToRestoreAsync(after, count, CancellationToken.None);
                if (batch.Count == 0)
                    return all;
                Assert.That(batch.Count, Is.LessThanOrEqualTo(count));
                all.AddRange(batch.Select(x => (x.File.ID, x.File.TargetPath, x.File.Length)));
                after = batch[^1].Rank;
            }
        }

        async Task<List<long>> ReadFolders(int count)
        {
            var all = new List<long>();
            for (var after = 0L; ;)
            {
                var batch = await db.GetFolderMetadataToRestoreAsync(after, count, CancellationToken.None);
                if (batch.Count == 0)
                    return all;
                Assert.That(batch.Count, Is.LessThanOrEqualTo(count));
                all.AddRange(batch.Select(x => x.ID));
                after = batch[^1].ID;
            }
        }

        var files = await ReadFiles(int.MaxValue);
        Assert.That(files, Has.Count.EqualTo(sizes.Length), "Every file is in the list");
        Assert.That(files.Select(x => x.Item3), Is.Ordered.Descending, "Larger files come first");
        Assert.That(await ReadFiles(2), Is.EqualTo(files), "Batches of two hold the same files in the same order");
        Assert.That(await ReadFiles(1), Is.EqualTo(files), "Batches of one hold the same files in the same order");

        var folders = await ReadFolders(int.MaxValue);
        Assert.That(folders, Has.Count.GreaterThanOrEqualTo(3), "The folders are in the list");
        Assert.That(folders, Is.Unique);
        Assert.That(await ReadFolders(2), Is.EqualTo(folders), "Batches of two hold the same folders");
    }
}

/// <summary>
/// The tests of the symbolic links that are restored after the entries below them, with batches of one
/// entry, so a link and the entries below it are always in different batches
/// </summary>
public class RestoreSymlinkOverExistingDataInBatchesTests : RestoreSymlinkOverExistingDataTests
{
    private int m_batchSize;

    [SetUp]
    public void UseBatchesOfOne()
    {
        m_batchSize = FileLister.BatchSize;
        FileLister.BatchSize = 1;
    }

    [TearDown]
    public void RestoreBatchSize() => FileLister.BatchSize = m_batchSize;
}

/// <summary>
/// The tests of the priority files of restore callback modules, with batches of one entry
/// </summary>
[NonParallelizable]
public class RestoreCallbackModuleInBatchesTests : RestoreCallbackModuleTests
{
    private int m_batchSize;

    [SetUp]
    public void UseBatchesOfOne()
    {
        m_batchSize = FileLister.BatchSize;
        FileLister.BatchSize = 1;
    }

    [TearDown]
    public void RestoreBatchSize() => FileLister.BatchSize = m_batchSize;
}

/// <summary>
/// The tests of the alternate data streams, which are restored after the files, with batches of one entry
/// </summary>
public class AlternateDataStreamInBatchesTests : AlternateDataStreamTests
{
    private int m_batchSize;

    [SetUp]
    public void UseBatchesOfOne()
    {
        m_batchSize = FileLister.BatchSize;
        FileLister.BatchSize = 1;
    }

    [TearDown]
    public void RestoreBatchSize() => FileLister.BatchSize = m_batchSize;
}
