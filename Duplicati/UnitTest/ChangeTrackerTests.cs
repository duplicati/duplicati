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
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.ChangeTracking;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The carry-forward decisions and state handling of the change tracker shared by the
/// providers that walk their changes, and the folder map a drive keeps between backups
/// </summary>
public class ChangeTrackerTests
{
    private static string D(params string[] parts) => Util.AppendDirSeparator(Path.Combine(parts));
    private static string F(params string[] parts) => Path.Combine(parts);

    private static readonly string Mount = D(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "changetracker")));

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

        tracker.MarkIdTouched(drive, "F2");
        Assert.That(tracker.WasPathEnumerated(D(drive, "F1", "F2")), Is.True, "A reported folder's own entry is examined");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "F2", "C")), Is.False, "Its unreported children are carried forward");

        tracker.MarkIdTouched(drive, "B");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "B")), Is.True, "A reported file is examined wherever it was");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "B2")), Is.False);

        tracker.MarkIdSubtreeExamined(drive, "F1");
        Assert.That(tracker.WasPathEnumerated(F(drive, "F1", "F2", "C")), Is.True, "Everything below a removed folder is examined");
        Assert.That(tracker.WasPathEnumerated(D(drive, "F1")), Is.True);
        Assert.That(tracker.WasPathEnumerated(F(drive, "F10", "X")), Is.False, "An id match needs a whole segment");
        Assert.That(tracker.WasPathEnumerated(F(Mount, "sites", "s1", "d2", "F1", "X")), Is.True, "Ids only apply below incremental folders");

        // A feed may mention items of another tree, such as a My Drive feed reporting activity
        // on a shared drive item as a removal; the ids of one tree never decide paths in another
        var other = D(Mount, "sites", "s1", "d2");
        tracker.MarkIncremental(other);
        Assert.That(tracker.WasPathEnumerated(F(other, "F1", "X")), Is.False, "An id reported by one tree is not applied below another");
        Assert.That(tracker.WasPathEnumerated(F(other, "B")), Is.False);
        tracker.MarkIdSubtreeExamined(other, "F1");
        Assert.That(tracker.WasPathEnumerated(F(other, "F1", "X")), Is.True, "Each tree applies its own ids");

        // A scope may be an ancestor of the incremental folder, as a mailbox is of its label folders
        var mailbox = D(Mount, "users", "u1", "mail");
        tracker.MarkIncremental(D(mailbox, "INBOX"));
        tracker.MarkIdTouched(mailbox, "m1.eml");
        Assert.That(tracker.WasPathEnumerated(F(mailbox, "INBOX", "m1.eml")), Is.True, "An id reported for the mailbox applies below its label folders");
        Assert.That(tracker.WasPathEnumerated(F(mailbox, "INBOX", "m2.eml")), Is.False);
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
        Assert.That(export, Is.EqualTo(new Dictionary<string, string> { ["a"] = "1", ["failures:a"] = "1", ["c"] = "3", ["d"] = "40", ["e"] = "", ["f"] = "6", ["failures:f"] = "1" }), "A failed key keeps its previous value whether or not it was set again and counts the failure, a failed key without a previous value is stored empty, a key that is not re-exported is dropped");
    }

    [Test]
    [Category("ChangeTracking")]
    public void RepeatedFailuresResetTheKey()
    {
        var tracker = ActiveTracker(new Dictionary<string, string> { ["a"] = "1", ["failures:a"] = "1", ["b"] = "2", ["failures:b"] = "1" });
        tracker.MarkStateFailed("a");
        tracker.SetState("b", "20");

        var export = tracker.Export();
        Assert.That(export["a"], Is.EqualTo("1"), "The value is held back while the failures are below the limit");
        Assert.That(export["failures:a"], Is.EqualTo("2"), "The consecutive failures are counted");
        Assert.That(export["b"], Is.EqualTo("20"));
        Assert.That(export.ContainsKey("failures:b"), Is.False, "A key that advanced starts over");

        tracker = ActiveTracker(new Dictionary<string, string> { ["a"] = "1", ["failures:a"] = (ChangeTracker.MAX_CONSECUTIVE_FAILURES - 1).ToString() });
        tracker.SetState("a", "10");
        tracker.MarkStateFailed("a");

        var messages = new List<Library.Logging.LogEntry>();
        using (Library.Logging.Log.StartIsolatingScope(true))
        using (Library.Logging.Log.StartScope(messages.Add))
            export = tracker.Export();

        Assert.That(export, Is.EqualTo(new Dictionary<string, string> { ["a"] = "" }), "After the limit the key is reset so the next backup lists it in full, and the counter is dropped");
        Assert.That(messages.Any(x => x.Id == "ChangeTrackingStateReset" && x.Level == Library.Logging.LogMessageType.Warning), Is.True);
    }

    [Test]
    [Category("ChangeTracking")]
    public void InvalidatedStateIsNotRestoredByAFailure()
    {
        var tracker = ActiveTracker(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        tracker.MarkLoadedStateInvalid("a");
        tracker.MarkLoadedStateInvalid("b");
        tracker.SetState("a", "10");
        tracker.SetState("b", "20");
        tracker.MarkStateFailed("b");

        Assert.That(tracker.Export(), Is.EqualTo(new Dictionary<string, string> { ["a"] = "10", ["b"] = "" }), "A key that advanced stores the new value; a failed key does not store the value this run found unusable");
    }

    [Test]
    [Category("ChangeTracking")]
    public void EmptyValueIsAKnownKeyWithoutState()
    {
        var tracker = ActiveTracker(new Dictionary<string, string> { ["mail:u1:p1:a"] = "", ["mail:u1:p1:b"] = "2" });
        Assert.That(tracker.GetState("mail:u1:p1:a"), Is.Null, "An empty value is reported as no state");
        Assert.That(tracker.GetState("mail:u1:p1:b"), Is.EqualTo("2"));
        Assert.That(tracker.GetLoadedKeysWithPrefix("mail:u1:p1:"), Is.EquivalentTo(new[] { "mail:u1:p1:a", "mail:u1:p1:b" }), "The key is still known to the parent");

        tracker.MarkStateFailed("mail:u1:p1:a");
        Assert.That(tracker.Export(), Is.EqualTo(new Dictionary<string, string> { ["mail:u1:p1:a"] = "" }), "A failure on a key without state stores it empty again");
    }

    [Test]
    [Category("ChangeTracking")]
    public void ItemsThatKeepFailingAreGivenUpAfterTheLimit()
    {
        // Run 1: one item below the key fails, the key is held back and the item is remembered
        var tracker = ActiveTracker(new Dictionary<string, string> { ["a"] = "1" });
        tracker.SetState("a", "10");
        tracker.MarkItemFailed("a", "x");
        tracker.MarkItemFailed("a", "x");
        var export = tracker.Export();
        Assert.That(export["a"], Is.EqualTo("1"), "A failed item holds the key back like a failed walk");
        Assert.That(export["failures:a"], Is.EqualTo("1"));
        var items = JsonSerializer.Deserialize<JsonElement>(export["failed-items:a"]);
        Assert.That(items.GetProperty("runs").GetInt32(), Is.EqualTo(1));
        Assert.That(items.GetProperty("items").EnumerateArray().Count(), Is.EqualTo(1), "The item is remembered once");
        Assert.That(items.GetProperty("items")[0].GetString(), Is.Not.EqualTo("x").And.Length.LessThanOrEqualTo(16), "The item is stored as a short hash, not as what identifies it");

        // Run 2: the same item fails again
        var state = new Dictionary<string, string>(export);
        tracker = ActiveTracker(state);
        tracker.SetState("a", "20");
        tracker.MarkItemFailed("a", "x");
        export = tracker.Export();
        Assert.That(export["a"], Is.EqualTo("1"), "The key is still held back below the limit");
        Assert.That(export["failures:a"], Is.EqualTo("2"));
        Assert.That(JsonSerializer.Deserialize<JsonElement>(export["failed-items:a"]).GetProperty("runs").GetInt32(), Is.EqualTo(2), "The runs with the same items are counted");

        // Run 3: the same item fails for the third time, so the key advances past it instead of
        // being reset to a full walk that would fail on the item again
        tracker = ActiveTracker(new Dictionary<string, string>(export));
        tracker.SetState("a", "30");
        tracker.MarkItemFailed("a", "x");
        var messages = new List<Library.Logging.LogEntry>();
        using (Library.Logging.Log.StartIsolatingScope(true))
        using (Library.Logging.Log.StartScope(messages.Add))
            export = tracker.Export();

        Assert.That(export, Is.EqualTo(new Dictionary<string, string> { ["a"] = "30" }), "After the limit the key stores the value of this run and drops the counters");
        Assert.That(messages.Any(x => x.Id == "ChangeTrackingItemsGivenUp" && x.Level == Library.Logging.LogMessageType.Warning), Is.True);
    }

    [Test]
    [Category("ChangeTracking")]
    public void OnlyTheSameItemsFailingCountsTowardsGivingUp()
    {
        // Runs in which x and y failed, one short of the limit
        var previous = new Dictionary<string, string> { ["a"] = "1" };
        for (var run = 1; run < ChangeTracker.MAX_CONSECUTIVE_FAILURES; run++)
        {
            var seed = ActiveTracker(new Dictionary<string, string>(previous));
            seed.SetState("a", "10");
            seed.MarkItemFailed("a", "x");
            seed.MarkItemFailed("a", "y");
            previous = new Dictionary<string, string>(seed.Export());
        }
        Assert.That(previous["a"], Is.EqualTo("1"));
        Assert.That(previous["failures:a"], Is.EqualTo((ChangeTracker.MAX_CONSECUTIVE_FAILURES - 1).ToString()));
        Assert.That(JsonSerializer.Deserialize<JsonElement>(previous["failed-items:a"]).GetProperty("runs").GetInt32(), Is.EqualTo(ChangeTracker.MAX_CONSECUTIVE_FAILURES - 1));

        // A subset of the previous items is the same failure
        var tracker = ActiveTracker(new Dictionary<string, string>(previous));
        tracker.SetState("a", "10");
        tracker.MarkItemFailed("a", "y");
        Assert.That(tracker.Export(), Is.EqualTo(new Dictionary<string, string> { ["a"] = "10" }), "Fewer of the same items failing still counts as the same failure");

        // A new item starts the item tracking over, and the key is reset like a failed walk
        tracker = ActiveTracker(new Dictionary<string, string>(previous));
        tracker.SetState("a", "10");
        tracker.MarkItemFailed("a", "x");
        tracker.MarkItemFailed("a", "z");
        var export = tracker.Export();
        Assert.That(export["a"], Is.EqualTo(""), "A different item failing reaches the limit of held-back runs and resets the key");
        Assert.That(JsonSerializer.Deserialize<JsonElement>(export["failed-items:a"]).GetProperty("runs").GetInt32(), Is.EqualTo(1), "The item tracking starts over with the new set");

        // A failed walk is not an item failure and forgets the items
        tracker = ActiveTracker(new Dictionary<string, string>(previous));
        tracker.SetState("a", "10");
        tracker.MarkItemFailed("a", "x");
        tracker.MarkStateFailed("a");
        Assert.That(tracker.Export(), Is.EqualTo(new Dictionary<string, string> { ["a"] = "" }), "A failed walk resets the key after the limit and records no items");

        // Without a value recorded in this run there is nothing to advance to
        tracker = ActiveTracker(new Dictionary<string, string>(previous));
        tracker.MarkItemFailed("a", "x");
        Assert.That(tracker.Export()["a"], Is.EqualTo(""), "Giving up needs a value from this run");
    }

    [Test]
    [Category("ChangeTracking")]
    public void ItemsThatKeepFailingAreGivenUpWithoutAPreviousValue()
    {
        // A first walk that fails on one item stores the key empty, so the next walk is in full
        var tracker = ActiveTracker();
        tracker.SetState("a", "10");
        tracker.MarkItemFailed("a", "x");
        var export = tracker.Export();
        Assert.That(export["a"], Is.EqualTo(""));
        Assert.That(export.ContainsKey("failures:a"), Is.False, "There is no previous value to count held-back runs for");
        Assert.That(export.ContainsKey("failed-items:a"), Is.True, "The failed item is still remembered");

        // The full walks fail on the same item, and after the limit the key advances
        tracker = ActiveTracker(new Dictionary<string, string>(export));
        tracker.SetState("a", "20");
        tracker.MarkItemFailed("a", "x");
        export = tracker.Export();
        Assert.That(export["a"], Is.EqualTo(""));

        tracker = ActiveTracker(new Dictionary<string, string>(export));
        tracker.SetState("a", "30");
        tracker.MarkItemFailed("a", "x");
        Assert.That(tracker.Export(), Is.EqualTo(new Dictionary<string, string> { ["a"] = "30" }), "A permanently unreadable item does not keep the key from ever storing a value");
    }

    [Test]
    [Category("ChangeTracking")]
    public void IncrementalFoldersAreToldApartFromFullyListedOnes()
    {
        var tracker = ActiveTracker();
        var drive = D(Mount, "sites", "s1", "d1");
        var mailbox = D(Mount, "users", "u1", "mailbox");

        Assert.That(tracker.IsIncrementallyEnumeratedFolder(drive), Is.False, "Nothing is incremental until marked");

        tracker.MarkIncremental(drive);
        tracker.MarkExaminedSubtree(D(drive, "Moved"));
        tracker.MarkIncremental(mailbox);
        tracker.MarkExaminedSubtree(D(mailbox, "Full"));
        tracker.MarkIncremental(D(mailbox, "Full", "Inner"));
        tracker.MarkExamined(D(mailbox, "Child"));

        Assert.That(tracker.IsIncrementallyEnumeratedFolder(drive), Is.True, "The drive lists only its changes");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(drive.TrimEnd(Path.DirectorySeparatorChar)), Is.True, "A path without the trailing separator is the same folder");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(D(drive, "Changed")), Is.True, "A changed folder below the drive lists only its changed children");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(D(drive, "Moved")), Is.False, "A moved folder is listed in full");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(D(drive, "Moved", "Sub")), Is.False, "And so is everything below it");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(D(mailbox, "Full")), Is.False, "A mail folder without a token is listed in full");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(D(mailbox, "Full", "Inner")), Is.True, "A folder with a token below it is incremental again");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(D(mailbox, "Child")), Is.True, "A child folder entry that was only listed is decided by its incremental parent");
        Assert.That(tracker.IsIncrementallyEnumeratedFolder(D(Mount, "sites", "s2", "d2")), Is.False, "A drive without marks was listed in full");
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
    public void FolderMapRoundTripsAndCompressesLargeMaps()
    {
        var map = new FolderMap { Token = "tok", RootId = "root" };
        map.SetParent("F1", "root");
        map.SetParent("F2", "F1");

        var small = map.Serialize();
        Assert.That(small, Does.Not.StartWith("gz:"));
        var restored = FolderMap.Deserialize(small)!;
        Assert.That(restored.Token, Is.EqualTo("tok"));
        Assert.That(restored.RootId, Is.EqualTo("root"));
        Assert.That(restored.TryGetParent("F2", out var parent) && parent == "F1", Is.True);
        Assert.That(restored.Count, Is.EqualTo(2));

        for (var i = 0; i < 2000; i++)
            map.SetParent($"01BYE5RZ6QN3ZWBTUFOFD3GSPGOHDJD{i:D5}", i % 2 == 0 ? "F1" : "F2");

        var large = map.Serialize();
        Assert.That(large, Does.StartWith("gz:"));
        Assert.That(large.Length, Is.LessThan(map.Count * 40), "Ids compress well");
        restored = FolderMap.Deserialize(large)!;
        Assert.That(restored.Count, Is.EqualTo(map.Count));

        Assert.That(FolderMap.Deserialize("not json"), Is.Null);
        Assert.That(FolderMap.Deserialize("gz:not base64!"), Is.Null);
        Assert.That(FolderMap.Deserialize("{\"token\":\"t\",\"folders\":{}}"), Is.Null, "A map without a root is unusable");
        Assert.That(FolderMap.Deserialize("{\"root\":\"r\",\"folders\":{}}"), Is.Null, "A map without a token is unusable");
    }

    /// <summary>
    /// The folders below a folder at any depth, read from the serialized map. The map only
    /// exposes parent lookups, which is all the provider needs; the test reads the stored
    /// form to check the whole tree.
    /// </summary>
    /// <param name="map">The map, small enough not to be compressed when serialized</param>
    /// <param name="folderId">The folder id</param>
    /// <returns>The descendant folder ids</returns>
    private static List<string> Descendants(FolderMap map, string folderId)
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
        var map = new FolderMap { Token = "tok", RootId = "root" };
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

    [Test]
    [Category("ChangeTracking")]
    public void FolderWalkObserverSeesOnlyChangesInHowAFolderIsWalked()
    {
        var tracker = ActiveTracker();
        var reports = new List<(string Path, FolderWalkMode Mode)>();
        tracker.SetFolderWalkObserver((path, mode) => reports.Add((path, mode)));

        var drive = D(Mount, "users", "u1", "drive");
        tracker.MarkIncremental(drive);
        tracker.MarkIncremental(drive);
        Assert.That(reports, Is.EqualTo(new[] { (drive, FolderWalkMode.Incremental) }), "A repeated mark is not reported");

        tracker.MarkExaminedSubtree(D(Mount, "users", "u1", "drive", "changed"));
        Assert.That(reports, Has.Count.EqualTo(1), "A folder listed in full that was never incremental is not reported");

        tracker.MarkExaminedSubtree(drive);
        Assert.That(reports, Is.EqualTo(new[] { (drive, FolderWalkMode.Incremental), (drive, FolderWalkMode.Full) }), "A full listing overriding the incremental one is reported");

        tracker.MarkIncremental(drive);
        var mailbox = D(Mount, "users", "u1", "mailbox");
        tracker.MarkExaminedSubtree(mailbox);
        tracker.MarkIncremental(mailbox);
        Assert.That(reports, Has.Count.EqualTo(2), "The full mark wins, so an incremental mark next to it changes nothing and is not reported");
        Assert.That(tracker.WasPathEnumerated(F(Mount, "users", "u1", "mailbox", "f1", "m1.eml")), Is.True);
    }
}
