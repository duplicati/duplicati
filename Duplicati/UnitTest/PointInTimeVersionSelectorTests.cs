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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The point-in-time selector turns a version listing into the objects that existed at a time.
/// </summary>
[TestFixture]
public class PointInTimeVersionSelectorTests
{
    private static readonly DateTime At = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ObjectVersion Version(string key, string versionId, int hoursFromAt, bool deleteMarker = false)
        => new(key, versionId, At.AddHours(hoursFromAt), deleteMarker, deleteMarker ? 0 : 100 + hoursFromAt, "STANDARD");

    private static async Task<List<ObjectVersion>> SelectAsync(params ObjectVersion[] versions)
        => await PointInTimeVersionSelector.SelectCurrentAt(versions.ToAsyncEnumerable(), At, CancellationToken.None).ToListAsync();

    [Test]
    [Category("Backend")]
    public async Task NewestVersionNotAfterTheTimeIsChosen()
    {
        // Newest first within a key, as S3 lists them
        var selected = await SelectAsync(
            Version("a", "v3", +2),
            Version("a", "v2", -1),
            Version("a", "v1", -5));

        Assert.AreEqual(1, selected.Count);
        Assert.AreEqual("v2", selected[0].VersionId);
    }

    [Test]
    [Category("Backend")]
    public async Task VersionCreatedExactlyAtTheTimeIsChosen()
    {
        var selected = await SelectAsync(
            Version("a", "v2", 0),
            Version("a", "v1", -5));

        Assert.AreEqual("v2", selected.Single().VersionId);
    }

    [Test]
    [Category("Backend")]
    public async Task DeleteMarkerAtTheTimeHidesTheKey()
    {
        var selected = await SelectAsync(
            Version("a", "v3", +2),
            Version("a", "d1", -1, deleteMarker: true),
            Version("a", "v1", -5));

        Assert.IsEmpty(selected);
    }

    [Test]
    [Category("Backend")]
    public async Task KeyCreatedAfterTheTimeIsAbsent()
    {
        var selected = await SelectAsync(
            Version("a", "v2", +3),
            Version("a", "v1", +1));

        Assert.IsEmpty(selected);
    }

    [Test]
    [Category("Backend")]
    public async Task ReuploadAfterADeleteMarkerIsChosen()
    {
        var selected = await SelectAsync(
            Version("a", "v2", -1),
            Version("a", "d1", -3, deleteMarker: true),
            Version("a", "v1", -5));

        Assert.AreEqual("v2", selected.Single().VersionId);
    }

    [Test]
    [Category("Backend")]
    public async Task DeleteMarkerAfterTheTimeDoesNotHideTheKey()
    {
        // The compromised state: the object was deleted after the point in time
        var selected = await SelectAsync(
            Version("a", "d1", +1, deleteMarker: true),
            Version("a", "v1", -5));

        Assert.AreEqual("v1", selected.Single().VersionId);
    }

    [Test]
    [Category("Backend")]
    public async Task VersionsOfOneKeySplitAcrossPagesAreDecidedOnce()
    {
        // Pages are concatenated by the caller; the selector must not restart at a page boundary
        var page1 = new[] { Version("a", "v3", +2), Version("a", "v2", -1) };
        var page2 = new[] { Version("a", "v1", -5), Version("b", "w1", -2) };

        var selected = await PointInTimeVersionSelector
            .SelectCurrentAt(page1.Concat(page2).ToAsyncEnumerable(), At, CancellationToken.None)
            .ToListAsync();

        Assert.AreEqual(new[] { "v2", "w1" }, selected.Select(x => x.VersionId).ToArray());
    }

    [Test]
    [Category("Backend")]
    public async Task EachKeyIsResolvedOnItsOwn()
    {
        var selected = await SelectAsync(
            Version("a", "d1", -1, deleteMarker: true),
            Version("a", "v1", -5),
            Version("b", "w2", +1),
            Version("b", "w1", -1),
            Version("c", "x1", +4));

        Assert.AreEqual(new[] { "w1" }, selected.Select(x => x.VersionId).ToArray());
        Assert.AreEqual("b", selected[0].Key);
    }

    [Test]
    [Category("Backend")]
    public async Task EmptyListingYieldsNothing()
    {
        var selected = await SelectAsync();

        Assert.IsEmpty(selected);
    }

    private static async Task<List<(IFileEntry Entry, string VersionId)>> SelectEntriesAsync(string prefix, bool recursive, params ObjectVersion[] versions)
        => await PointInTimeVersionSelector.SelectEntriesAt(versions.ToAsyncEnumerable(), prefix, recursive, At, cls => cls == "GLACIER", CancellationToken.None).ToListAsync();

    [Test]
    [Category("Backend")]
    public async Task EntryNamesAreRelativeToThePrefix()
    {
        var selected = await SelectEntriesAsync("backup/", true,
            Version("backup/a.zip", "v1", -1),
            new ObjectVersion("backup/b.zip", "v2", At.AddHours(-2), false, 7, "GLACIER"),
            Version("other/c.zip", "v3", -1));

        Assert.AreEqual(new[] { "a.zip", "b.zip" }, selected.Select(x => x.Entry.Name).ToArray());
        Assert.AreEqual(new[] { "v1", "v2" }, selected.Select(x => x.VersionId).ToArray());
        Assert.IsFalse(selected[0].Entry.IsArchived);
        Assert.IsTrue(selected[1].Entry.IsArchived);
        Assert.AreEqual(7, selected[1].Entry.Size);
        Assert.AreEqual(At.AddHours(-2), selected[1].Entry.LastModification);
    }

    [Test]
    [Category("Backend")]
    public async Task AKeyEqualToThePrefixIsTheObjectItself()
    {
        // A single key is resolved by listing with the key as the prefix; the match has an empty name
        var selected = await SelectEntriesAsync("backup/a.zip", true,
            Version("backup/a.zip", "v2", -1),
            Version("backup/a.zip", "v1", -5),
            Version("backup/a.zip.bak", "v3", -1));

        Assert.AreEqual(new[] { "", ".bak" }, selected.Select(x => x.Entry.Name).ToArray());
        Assert.AreEqual("v2", selected[0].VersionId);
        Assert.IsFalse(selected[0].Entry.IsFolder);
    }

    [Test]
    [Category("Backend")]
    public async Task TheFolderMarkerOfThePrefixIsSkipped()
    {
        var selected = await SelectEntriesAsync("backup/", true,
            Version("backup/", "m1", -1),
            Version("backup/a.zip", "v1", -1));

        Assert.AreEqual(new[] { "a.zip" }, selected.Select(x => x.Entry.Name).ToArray());
    }

    [Test]
    [Category("Backend")]
    public async Task NestedObjectsAreFoldedIntoTheirFolderWhenNotRecursive()
    {
        var selected = await SelectEntriesAsync("backup/", false,
            Version("backup/a.zip", "v1", -1),
            Version("backup/sub/", "m1", -1),
            Version("backup/sub/b.zip", "v2", -1),
            Version("backup/sub/deeper/c.zip", "v3", -1));

        Assert.AreEqual(new[] { "a.zip", "sub/" }, selected.Select(x => x.Entry.Name).ToArray());
        Assert.IsTrue(selected[1].Entry.IsFolder);
        Assert.AreEqual(string.Empty, selected[1].VersionId);
    }

    [Test]
    [Category("Backend")]
    public async Task AFolderWithNoObjectAtTheTimeIsAbsent()
    {
        // Folders come from the objects that existed then, not from every key that ever existed
        var selected = await SelectEntriesAsync("backup/", false,
            Version("backup/later/a.zip", "v1", +1),
            Version("backup/gone/b.zip", "d1", -1, deleteMarker: true),
            Version("backup/gone/b.zip", "v2", -5),
            Version("backup/kept/c.zip", "v3", -1));

        Assert.AreEqual(new[] { "kept/" }, selected.Select(x => x.Entry.Name).ToArray());
    }

    [Test]
    [Category("Backend")]
    public async Task NestedObjectsAreReportedWhenRecursive()
    {
        var selected = await SelectEntriesAsync("backup/", true,
            Version("backup/sub/", "m1", -1),
            Version("backup/sub/b.zip", "v2", -1));

        Assert.AreEqual(new[] { "sub/", "sub/b.zip" }, selected.Select(x => x.Entry.Name).ToArray());
        Assert.IsTrue(selected[0].Entry.IsFolder);
        Assert.IsFalse(selected[1].Entry.IsFolder);
    }
}
