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
using System.Text.Json;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Proprietary.Office365;
using Duplicati.Proprietary.Office365.SourceItems;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The carry-forward decisions and state handling of the Microsoft 365 change tracker,
/// and the folder map a drive keeps between backups
/// </summary>
public class Office365ChangeTrackerTests
{
    private static string D(params string[] parts) => Util.AppendDirSeparator(Path.Combine(parts));
    private static string F(params string[] parts) => Path.Combine(parts);

    private static readonly string Mount = D(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "o365")));

    private static ChangeTracker ActiveTracker(Dictionary<string, string>? state = null)
    {
        var tracker = new ChangeTracker();
        tracker.Load(state ?? new Dictionary<string, string>());
        return tracker;
    }

    [Test]
    [Category("ChangeTracking")]
    public void EverythingIsExaminedUntilSomethingIsIncremental()
    {
        var tracker = ActiveTracker();
        Assert.That(tracker.WasPathEnumerated(F(Mount, "users", "u1", "mailbox", "f1", "m1", "content.eml")), Is.True);

        tracker.MarkExaminedSubtree(D(Mount, "users", "u1", "mailbox", "f1"));
        Assert.That(tracker.WasPathEnumerated(F(Mount, "users", "u1", "mailbox", "f1", "m1", "content.eml")), Is.True);
        Assert.That(tracker.WasPathEnumerated(F(Mount, "users", "u1", "calendar", "e1.json")), Is.True, "Paths outside any mark are examined");
    }

    [Test]
    [Category("ChangeTracking")]
    public void IncrementalFolderOnlyCountsMarkedPaths()
    {
        var tracker = ActiveTracker();
        var folder = D(Mount, "users", "u1", "mailbox", "f1");
        tracker.MarkIncremental(folder);

        Assert.That(tracker.WasPathEnumerated(F(folder, "m1", "content.eml")), Is.False, "An unmarked message below an incremental folder is carried forward");
        Assert.That(tracker.WasPathEnumerated(D(folder, "m1")), Is.False);
        Assert.That(tracker.WasPathEnumerated(folder), Is.True, "The incremental folder's own entry is listed by its parent");

        tracker.MarkExaminedSubtree(D(folder, "m2"));
        Assert.That(tracker.WasPathEnumerated(D(folder, "m2")), Is.True);
        Assert.That(tracker.WasPathEnumerated(F(folder, "m2", "content.eml")), Is.True);
        Assert.That(tracker.WasPathEnumerated(F(folder, "m2", "metadata.json")), Is.True);
        Assert.That(tracker.WasPathEnumerated(F(folder, "m22", "content.eml")), Is.False, "A prefix match needs a whole segment");

        tracker.MarkExamined(D(folder, "child"));
        Assert.That(tracker.WasPathEnumerated(D(folder, "child")), Is.True, "A listed child folder entry is examined");
        Assert.That(tracker.WasPathEnumerated(F(folder, "child", "m3", "content.eml")), Is.False, "Its contents are decided by its own marks");
    }

    [Test]
    [Category("ChangeTracking")]
    public void NestedMarksAreDecidedByTheLongestPrefix()
    {
        var tracker = ActiveTracker();
        var root = D(Mount, "users", "u1", "mailbox");
        var child = D(root, "child");
        var grandchild = D(child, "grandchild");

        tracker.MarkIncremental(root);
        tracker.MarkExaminedSubtree(child);
        tracker.MarkIncremental(grandchild);

        Assert.That(tracker.WasPathEnumerated(F(root, "m1", "content.eml")), Is.False);
        Assert.That(tracker.WasPathEnumerated(F(child, "m2", "content.eml")), Is.True, "A fully walked folder inside an incremental one is examined");
        Assert.That(tracker.WasPathEnumerated(F(grandchild, "m3", "content.eml")), Is.False, "An incremental folder inside a fully walked one is not");
        Assert.That(tracker.WasPathEnumerated(grandchild), Is.True, "The incremental folder's entry is examined as part of the fully walked parent");

        tracker.MarkExaminedSubtree(grandchild);
        Assert.That(tracker.WasPathEnumerated(F(grandchild, "m3", "content.eml")), Is.True, "A resync marks the incremental folder as fully walked");
    }

    [Test]
    [Category("ChangeTracking")]
    public void ReportedIdsDecideBelowAnIncrementalDrive()
    {
        var tracker = ActiveTracker();
        var drive = D(Mount, "sites", "s1", "d1");
        tracker.MarkIncremental(drive);

        Assert.That(tracker.WasPathEnumerated(F(drive, "A")), Is.False);
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "B")), Is.False);

        tracker.MarkIdTouched("F2");
        Assert.That(tracker.WasPathEnumerated(D(drive, "F1", "F2")), Is.True, "A reported folder's own entry is examined");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "F2", "C")), Is.False, "Its unreported children are carried forward");

        tracker.MarkIdTouched("B");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "B")), Is.True, "A reported file is examined wherever it was");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "B2")), Is.False);

        tracker.MarkIdSubtreeGone("F1");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "F2", "C")), Is.True, "Everything below a removed folder is examined");
        Assert.That(tracker.WasPathEnumerated(D(drive, "F1")), Is.True);
        Assert.That(tracker.WasPathEnumerated(F(drive, "F10", "X")), Is.False, "An id match needs a whole segment");
        Assert.That(tracker.WasPathEnumerated(F(Mount, "sites", "s1", "d2", "F1", "X")), Is.True, "Ids only apply below incremental folders");
    }

    [Test]
    [Category("ChangeTracking")]
    public void ExportKeepsOnlyReExportedKeysAndHoldsBackFailedOnes()
    {
        var tracker = ActiveTracker(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["d"] = "4", ["f"] = "6" });
        Assert.That(tracker.GetState("a"), Is.EqualTo("1"));
        Assert.That(tracker.GetState("missing"), Is.Null);
        Assert.That(tracker.GetLoadedKeysWithPrefix("a"), Is.EqualTo(new[] { "a" }));

        tracker.SetState("a", "10");
        tracker.MarkStateFailed("a");
        tracker.SetState("c", "3");
        tracker.SetState("d", "40");
        tracker.MarkStateFailed("e");
        tracker.MarkStateFailed("f");

        var export = tracker.Export();
        Assert.That(export, Is.EqualTo(new Dictionary<string, string> { ["a"] = "1", ["c"] = "3", ["d"] = "40", ["f"] = "6" }), "A failed key keeps its previous value whether or not it was set again, a key that is not re-exported is dropped");
    }

    [Test]
    [Category("ChangeTracking")]
    public void LoadedKeysWithPrefixAreFoundByExactPrefix()
    {
        var tracker = ActiveTracker(new Dictionary<string, string>
        {
            ["mail:u1:p1:a"] = "1",
            ["mail:u1:p1:b"] = "2",
            ["mail:u1:p10:c"] = "3",
            ["mail:u1:p1"] = "4",
            ["mail:u1:p0:d"] = "5",
            ["drive:d1"] = "6",
        });

        Assert.That(tracker.GetLoadedKeysWithPrefix("mail:u1:p1:"), Is.EquivalentTo(new[] { "mail:u1:p1:a", "mail:u1:p1:b" }), "A longer parent id with the same leading characters does not match");
        Assert.That(tracker.GetLoadedKeysWithPrefix("mail:u1:p10:"), Is.EquivalentTo(new[] { "mail:u1:p10:c" }));
        Assert.That(tracker.GetLoadedKeysWithPrefix("mail:u1:p2:"), Is.Empty);
        Assert.That(tracker.GetLoadedKeysWithPrefix("zzz"), Is.Empty, "A prefix past the last key matches nothing");
        Assert.That(tracker.GetLoadedKeysWithPrefix("").Count(), Is.EqualTo(6));
    }

    private sealed class CountingStream : MemoryStream
    {
        public int Disposals;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Disposals++;
            base.Dispose(disposing);
        }
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task FailureReportingStreamDisposesItsInnerStreamOnceAsync()
    {
        var inner = new CountingStream();
        await using (new FailureReportingStream(inner, () => { }))
        {
        }
        Assert.That(inner.Disposals, Is.EqualTo(1), "Asynchronous disposal releases the inner stream once");

        inner = new CountingStream();
        using (new FailureReportingStream(inner, () => { }))
        {
        }
        Assert.That(inner.Disposals, Is.EqualTo(1), "Synchronous disposal releases the inner stream once");
    }

    [Test]
    [Category("ChangeTracking")]
    public void InactiveTrackerIgnoresState()
    {
        var tracker = new ChangeTracker();
        Assert.That(tracker.IsActive, Is.False);
        tracker.SetState("a", "1");
        Assert.That(tracker.GetState("a"), Is.Null);
        Assert.That(tracker.Export(), Is.Empty);
        Assert.That(tracker.GetLoadedKeysWithPrefix(""), Is.Empty);
    }

    [Test]
    [Category("ChangeTracking")]
    public void DeltaTokenIsExtractedFromLinks()
    {
        Assert.That(DeltaToken.FromMailDeltaLink("https://graph.microsoft.com/v1.0/users/u/mailFolders/f/messages/delta?$deltatoken=Lzt%2BZw%3D%3D"), Is.EqualTo("Lzt+Zw=="));
        Assert.That(DeltaToken.FromMailDeltaLink("https://graph.microsoft.com/v1.0/users/u/mailFolders/f/messages/delta?%24deltatoken=abc"), Is.EqualTo("abc"), "The parameter name may be encoded");
        Assert.That(DeltaToken.FromMailDeltaLink("https://graph.microsoft.com/v1.0/users/u/mailFolders/f/messages/delta?$skiptoken=abc"), Is.Null, "A next link is not a delta link");
        Assert.That(DeltaToken.FromMailDeltaLink(null), Is.Null);

        Assert.That(DeltaToken.FromDriveDeltaLink("https://graph.microsoft.com/v1.0/drives/d/root/delta?$select=id&token=MzslMjM7JTIzOyUyMzQ"), Is.EqualTo("MzslMjM7JTIzOyUyMzQ"));
        Assert.That(DeltaToken.FromDriveDeltaLink("https://graph.microsoft.com/v1.0/drives/d/root/delta?$deltatoken=xyz"), Is.EqualTo("xyz"), "Either spelling of the token parameter is accepted");
        Assert.That(DeltaToken.FromDriveDeltaLink("https://graph.microsoft.com/v1.0/drives/d/root/delta"), Is.Null);
    }

    [Test]
    [Category("ChangeTracking")]
    public void FolderMapRoundTripsAndCompressesLargeMaps()
    {
        var map = new DriveFolderMap { Token = "tok", RootId = "root" };
        map.SetParent("F1", "root");
        map.SetParent("F2", "F1");

        var small = map.Serialize();
        Assert.That(small, Does.Not.StartWith("gz:"));
        var restored = DriveFolderMap.Deserialize(small)!;
        Assert.That(restored.Token, Is.EqualTo("tok"));
        Assert.That(restored.RootId, Is.EqualTo("root"));
        Assert.That(restored.TryGetParent("F2", out var parent) && parent == "F1", Is.True);
        Assert.That(restored.Count, Is.EqualTo(2));

        for (var i = 0; i < 2000; i++)
            map.SetParent($"01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD{i:D5}", i % 2 == 0 ? "F1" : "F2");

        var large = map.Serialize();
        Assert.That(large, Does.StartWith("gz:"));
        Assert.That(large.Length, Is.LessThan(map.Count * 40), "Ids compress well");
        restored = DriveFolderMap.Deserialize(large)!;
        Assert.That(restored.Count, Is.EqualTo(map.Count));

        Assert.That(DriveFolderMap.Deserialize("not json"), Is.Null);
        Assert.That(DriveFolderMap.Deserialize("gz:not base64!"), Is.Null);
        Assert.That(DriveFolderMap.Deserialize("{\"token\":\"t\",\"folders\":{}}"), Is.Null, "A map without a root is unusable");
        Assert.That(DriveFolderMap.Deserialize("{\"root\":\"r\",\"folders\":{}}"), Is.Null, "A map without a token is unusable");
    }

    /// <summary>
    /// The folders below a folder at any depth, read from the serialized map. The map only
    /// exposes parent lookups, which is all the provider needs; the test reads the stored
    /// form to check the whole tree.
    /// </summary>
    /// <param name="map">The map, small enough not to be compressed when serialized</param>
    /// <param name="folderId">The folder id</param>
    /// <returns>The descendant folder ids</returns>
    private static List<string> Descendants(DriveFolderMap map, string folderId)
    {
        using var doc = JsonDocument.Parse(map.Serialize());
        var parents = doc.RootElement.GetProperty("folders").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!, StringComparer.Ordinal);

        var result = new List<string>();
        var work = new Stack<string>();
        work.Push(folderId);
        while (work.Count > 0)
        {
            var current = work.Pop();
            foreach (var child in parents.Where(x => x.Value == current).Select(x => x.Key))
            {
                result.Add(child);
                work.Push(child);
            }
        }

        return result;
    }

    [Test]
    [Category("ChangeTracking")]
    public void FolderMapPrunesFoldersBelowRemovedOnes()
    {
        var map = new DriveFolderMap { Token = "tok", RootId = "root" };
        map.SetParent("F1", "root");
        map.SetParent("F2", "F1");
        map.SetParent("F3", "F2");
        map.SetParent("G1", "root");
        map.SetParent("L1", "L2");
        map.SetParent("L2", "L1");

        Assert.That(Descendants(map, "F1"), Is.EquivalentTo(new[] { "F2", "F3" }));

        Assert.That(map.Remove("F1"), Is.True);
        Assert.That(map.PruneUnreachable(), Is.EqualTo(4), "The descendants of the removed folder and the cycle are dropped");
        Assert.That(map.Contains("G1"), Is.True);
        Assert.That(map.Contains("F2"), Is.False);
        Assert.That(map.Contains("L1"), Is.False);
    }
}
