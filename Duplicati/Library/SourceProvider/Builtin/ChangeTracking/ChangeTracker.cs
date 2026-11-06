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


using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.SourceProvider.ChangeTracking;

/// <summary>
/// Keeps what the provider learned during one enumeration about which paths it examined,
/// and the delta tokens it carries between backups. The backup copies every path from the
/// previous fileset that <see cref="WasPathEnumerated"/> does not claim.
/// </summary>
/// <remarks>
/// Everything is examined by default: the provider lists most content in full on every run.
/// A folder walked with a delta token is marked incremental, and below it only the paths
/// explicitly marked, or whose id segments were reported by the delta, count as examined.
/// The marks are set by the enumeration while download failures are reported from the file
/// processors that read the content, so the collections are safe for concurrent use.
/// </remarks>
public sealed class ChangeTracker
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<ChangeTracker>();

    /// <summary>
    /// The number of consecutive backups a key may be held back at its previous value before
    /// the value is reset and the next backup lists the key in full. A held-back token keeps
    /// the carried-forward content in place and reports the same changes again, which is right
    /// for a transient failure but would otherwise pin a folder to stale content forever if the
    /// failure is permanent.
    /// </summary>
    public const int MAX_CONSECUTIVE_FAILURES = 3;

    /// <summary>
    /// The prefix of the keys that count the consecutive failures of a state key
    /// </summary>
    private const string FAILURE_COUNT_PREFIX = "failures:";

    /// <summary>
    /// The prefix of the keys that record which items failed below a state key, so the next
    /// backup can tell whether the same items fail again
    /// </summary>
    private const string FAILED_ITEMS_PREFIX = "failed-items:";

    /// <summary>
    /// The most failed items remembered for a key. More failures than this are not the fault
    /// of single items, so the key is held back and reset like a key that failed as a whole
    /// </summary>
    public const int MAX_TRACKED_FAILED_ITEMS = 1000;

    /// <summary>
    /// The most items named in the warning that gives up on them
    /// </summary>
    private const int NAMED_FAILED_ITEMS = 5;

    /// <summary>
    /// The items that failed below a key, as stored between backups. The items are stored as
    /// short hashes, so the record stays small whatever the items are identified by.
    /// </summary>
    private sealed class FailedItems
    {
        /// <summary>The consecutive backups in which only these items, or a subset of them, failed</summary>
        [JsonPropertyName("runs")]
        public int Runs { get; set; }

        /// <summary>The hashes of what identifies the items</summary>
        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = [];
    }

    /// <summary>
    /// Hashes what identifies a failed item for storage. 12 bytes of SHA-256 tell a thousand
    /// items apart with no realistic chance of a collision, in 16 characters per item
    /// </summary>
    /// <param name="item">What identifies the item</param>
    /// <returns>The hash</returns>
    private static string HashFailedItem(string item)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(item)).AsSpan(0, 12));

    /// <summary>
    /// What is known about a path
    /// </summary>
    [Flags]
    private enum Mark
    {
        /// <summary>The folder was walked with a delta token, so unmarked paths below it were not examined</summary>
        Incremental = 1,
        /// <summary>The folder and everything below it was examined</summary>
        ExaminedSubtree = 2,
        /// <summary>The entry itself was examined, which says nothing about its contents</summary>
        ExaminedSelf = 4
    }

    /// <summary>
    /// The directory separator used in entry paths
    /// </summary>
    private static readonly char SEPARATOR = System.IO.Path.DirectorySeparatorChar;

    /// <summary>
    /// The marks by path. Folder paths end with the separator.
    /// </summary>
    private readonly ConcurrentDictionary<string, Mark> _marks = new(Library.Utility.Utility.ClientFilenameStringComparer);

    /// <summary>
    /// The ids a tree reported, matched against the segments of the paths below the tree
    /// </summary>
    private sealed class IdScope
    {
        /// <summary>Ids of items whose own entry was examined; matched against the last segment of a path</summary>
        public readonly ConcurrentDictionary<string, byte> Touched = new(StringComparer.Ordinal);
        /// <summary>Ids of items whose whole subtree counts as examined; matched against any segment of a path</summary>
        public readonly ConcurrentDictionary<string, byte> Subtrees = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The reported ids by the path of the tree that reported them. A change feed may mention
    /// items that live in another tree, such as a user's My Drive feed reporting activity on a
    /// shared drive item as a removal, so an id only counts below the tree it was reported for
    /// </summary>
    private readonly ConcurrentDictionary<string, IdScope> _idScopes = new(Library.Utility.Utility.ClientFilenameStringComparer);

    /// <summary>
    /// The state loaded from the previous backup
    /// </summary>
    private IReadOnlyDictionary<string, string> _loaded = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The loaded keys in ordinal order, so the keys with a common prefix form one range
    /// </summary>
    private string[] _loadedKeys = [];

    /// <summary>
    /// The state to store with this backup
    /// </summary>
    private readonly ConcurrentDictionary<string, Func<string>> _exported = new(StringComparer.Ordinal);

    /// <summary>
    /// Keys whose listing or content had failures in this run, so their token must not advance
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _failedKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// The items that failed below a key in this run, by key, so the next run can tell whether
    /// the same items keep failing
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _failedItems = new(StringComparer.Ordinal);

    /// <summary>
    /// Keys whose loaded value this run found unusable, so a failure must not restore it
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _invalidatedKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// The observer told how the folders are walked, if one is registered
    /// </summary>
    private volatile Action<string, FolderWalkMode>? _folderWalkObserver;

    /// <summary>
    /// Whether the provider is used for a backup that stores state. Listing for the user
    /// interface and restores never load state, and then no tokens are requested or kept.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Registers the observer told when a folder is first marked as walked with a delta token,
    /// and when such a folder is later marked as examined in full after all. The backup uses
    /// the reports to estimate how much of the previous fileset is carried forward. A mark
    /// that does not change how the paths below the folder are judged is not reported.
    /// </summary>
    /// <param name="observer">The observer, called with the folder path and how it is walked</param>
    public void SetFolderWalkObserver(Action<string, FolderWalkMode> observer)
        => _folderWalkObserver = observer;

    /// <summary>
    /// Hands the state stored by the previous backup to the tracker
    /// </summary>
    /// <param name="state">The stored state</param>
    public void Load(IReadOnlyDictionary<string, string> state)
    {
        _loaded = state;
        _loadedKeys = state.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        IsActive = true;
    }

    /// <summary>
    /// Gets a value stored by the previous backup. An empty value means that the key was known
    /// but had nothing usable to store, and is reported as no value.
    /// </summary>
    /// <param name="key">The state key</param>
    /// <returns>The stored value, or <c>null</c> if none is stored or the tracker is not active</returns>
    public string? GetState(string key)
    {
        if (!IsActive)
            return null;

        var value = _loaded.GetValueOrDefault(key);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Gets the keys stored by the previous backup that start with a prefix. The keys are
    /// sorted, so the matches are the range starting at the prefix's position.
    /// </summary>
    /// <param name="prefix">The key prefix</param>
    /// <returns>The matching keys</returns>
    public IEnumerable<string> GetLoadedKeysWithPrefix(string prefix)
    {
        if (!IsActive)
            return [];

        var index = Array.BinarySearch(_loadedKeys, prefix, StringComparer.Ordinal);
        if (index < 0)
            index = ~index;

        var result = new List<string>();
        for (var i = index; i < _loadedKeys.Length && _loadedKeys[i].StartsWith(prefix, StringComparison.Ordinal); i++)
            result.Add(_loadedKeys[i]);

        return result;
    }

    /// <summary>
    /// Records a value to store with this backup
    /// </summary>
    /// <param name="key">The state key</param>
    /// <param name="value">The value</param>
    public void SetState(string key, string value)
        => SetState(key, () => value);

    /// <summary>
    /// Records a value to store with this backup, computed when the state is exported. Used for
    /// state that keeps changing after the entry that owns it was listed, such as a drive's folder
    /// map, which the backup fills in as it recurses into the folders.
    /// </summary>
    /// <param name="key">The state key</param>
    /// <param name="valueFactory">Computes the value</param>
    public void SetState(string key, Func<string> valueFactory)
    {
        if (IsActive)
            _exported[key] = valueFactory;
    }

    /// <summary>
    /// Records that the walk of the key failed as a whole, so the stored value stays at what
    /// the previous backup recorded and the changes are reported again. A key without a usable
    /// previous value is stored with an empty value, so the key stays known to the provider and
    /// the next backup walks it in full. For a single item that could not be listed or read,
    /// use <see cref="MarkItemFailed"/>.
    /// </summary>
    /// <param name="key">The state key</param>
    public void MarkStateFailed(string key)
    {
        if (IsActive && _failedKeys.TryAdd(key, 0))
            Log.WriteVerboseMessage(LOGTAG, "ChangeTrackingKeyHeldBack", "A listing or download below \"{0}\" failed, so its change tracking state is not advanced", key);
    }

    /// <summary>
    /// Records that one item below the key could not be listed or read, so the stored value
    /// stays at what the previous backup recorded and the item is reported again. Unlike
    /// <see cref="MarkStateFailed"/>, the failed items are remembered between backups: when
    /// the same items, and no others, fail in <see cref="MAX_CONSECUTIVE_FAILURES"/>
    /// consecutive backups, the failure is taken as permanent and the key advances past them,
    /// as holding the key back or listing it in full would only fail on the same items again
    /// and would keep the whole key from ever being walked incrementally.
    /// </summary>
    /// <param name="key">The state key</param>
    /// <param name="item">What identifies the item below the key, such as its path or id</param>
    public void MarkItemFailed(string key, string item)
    {
        if (!IsActive)
            return;

        var items = _failedItems.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        if (items.TryAdd(item, 0))
            Log.WriteVerboseMessage(LOGTAG, "ChangeTrackingItemHeldBack", "The listing or download of \"{0}\" below \"{1}\" failed, so the change tracking state of the key is not advanced", item, key);
    }

    /// <summary>
    /// Records that the value loaded for the key cannot be used, because the service rejected
    /// it or it could not be read, so a failure later in this run must not store it again
    /// </summary>
    /// <param name="key">The state key</param>
    public void MarkLoadedStateInvalid(string key)
    {
        if (IsActive)
            _invalidatedKeys.TryAdd(key, 0);
    }

    /// <summary>
    /// Gets the state to store with this backup. A failed key keeps the value the previous
    /// backup stored, whether or not a value was recorded for it in this run, for up to
    /// <see cref="MAX_CONSECUTIVE_FAILURES"/> consecutive backups; after that, or when there is
    /// no usable previous value, the key is stored with an empty value so the next backup
    /// walks it in full. A key that failed only on single items remembers which, and once the
    /// same items have failed in <see cref="MAX_CONSECUTIVE_FAILURES"/> consecutive backups the
    /// key advances past them instead, so a permanently unreadable item does not keep its tree
    /// from being walked incrementally.
    /// </summary>
    /// <returns>The state rows</returns>
    public IReadOnlyDictionary<string, string> Export()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var heldBack = new HashSet<string>(_failedKeys.Keys, StringComparer.Ordinal);
        heldBack.UnionWith(_failedItems.Keys);

        foreach (var kv in _exported)
            if (!heldBack.Contains(kv.Key))
                result[kv.Key] = kv.Value();

        foreach (var key in heldBack)
        {
            // A key that failed as a whole, or on more items than are remembered, starts the
            // item tracking over; otherwise the items are compared with the previous run's
            var items = !_failedKeys.ContainsKey(key) && _failedItems.TryGetValue(key, out var failed) && failed.Count <= MAX_TRACKED_FAILED_ITEMS
                ? failed.Keys.ToList()
                : null;
            if (items != null)
            {
                var hashes = items.Select(HashFailedItem).ToList();
                var previousItems = GetPreviousFailedItems(key);
                var sameItems = previousItems != null && hashes.All(new HashSet<string>(previousItems.Items, StringComparer.Ordinal).Contains);
                var itemRuns = sameItems ? previousItems!.Runs + 1 : 1;
                if (sameItems && itemRuns >= MAX_CONSECUTIVE_FAILURES && _exported.TryGetValue(key, out var factory))
                {
                    var named = string.Join(", ", items.Take(NAMED_FAILED_ITEMS));
                    if (items.Count > NAMED_FAILED_ITEMS)
                        named += $" and {items.Count - NAMED_FAILED_ITEMS} more";
                    Log.WriteWarningMessage(LOGTAG, "ChangeTrackingItemsGivenUp", null, "The same {0} item(s) below \"{1}\" could not be read in {2} consecutive backups, so its change tracking state is advanced past them; they are backed up again when they change: {3}", items.Count, key, itemRuns, named);
                    result[key] = factory();
                    continue;
                }

                result[FAILED_ITEMS_PREFIX + key] = JsonSerializer.Serialize(new FailedItems { Runs = itemRuns, Items = hashes });
            }

            if (_invalidatedKeys.ContainsKey(key) || !_loaded.TryGetValue(key, out var previous) || string.IsNullOrEmpty(previous))
            {
                result[key] = string.Empty;
                continue;
            }

            var failures = GetFailureCount(key) + 1;
            if (failures >= MAX_CONSECUTIVE_FAILURES)
            {
                Log.WriteWarningMessage(LOGTAG, "ChangeTrackingStateReset", null, "The change tracking state for \"{0}\" could not be advanced in {1} consecutive backups, so it is reset and the next backup lists it in full", key, failures);
                result[key] = string.Empty;
                continue;
            }

            result[key] = previous;
            result[FAILURE_COUNT_PREFIX + key] = failures.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    /// <summary>
    /// Gets the number of consecutive backups the key was held back in before this one
    /// </summary>
    /// <param name="key">The state key</param>
    /// <returns>The failure count</returns>
    private int GetFailureCount(string key)
        => _loaded.TryGetValue(FAILURE_COUNT_PREFIX + key, out var count)
            && int.TryParse(count, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    /// <summary>
    /// Gets the items that failed below the key in the previous backup
    /// </summary>
    /// <param name="key">The state key</param>
    /// <returns>The failed items, or <c>null</c> if none were recorded or the record cannot be read</returns>
    private FailedItems? GetPreviousFailedItems(string key)
    {
        if (!_loaded.TryGetValue(FAILED_ITEMS_PREFIX + key, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var items = JsonSerializer.Deserialize<FailedItems>(value);
            return items == null || items.Runs < 1 ? null : items;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Marks a folder as walked with a delta token: only the paths below it that are
    /// explicitly marked, or whose ids were reported, count as examined
    /// </summary>
    /// <param name="folderPath">The folder path, ending with the separator</param>
    public void MarkIncremental(string folderPath)
        => AddMark(folderPath, Mark.Incremental);

    /// <summary>
    /// Marks an entry as examined, without saying anything about its contents
    /// </summary>
    /// <param name="path">The entry path</param>
    public void MarkExamined(string path)
        => AddMark(path, Mark.ExaminedSelf);

    /// <summary>
    /// Marks a folder and everything below it as examined
    /// </summary>
    /// <param name="folderPath">The folder path, ending with the separator</param>
    public void MarkExaminedSubtree(string folderPath)
        => AddMark(folderPath, Mark.ExaminedSubtree);

    /// <summary>
    /// Marks an item id as examined: a path below an incremental folder inside the scope whose
    /// last segment is the id counts as examined
    /// </summary>
    /// <param name="scopePath">The path of the tree that reported the id, ending with the separator; the id applies below it only</param>
    /// <param name="id">The item id</param>
    public void MarkIdTouched(string scopePath, string id)
        => _idScopes.GetOrAdd(scopePath, _ => new IdScope()).Touched.TryAdd(id, 0);

    /// <summary>
    /// Marks everything below an item id as examined: any path below an incremental folder
    /// inside the scope that has the id as a segment counts as examined. Used for a folder
    /// whose whole subtree left its path, and for an item that is a folder of derived entries
    /// which are all re-listed when the item is reported
    /// </summary>
    /// <param name="scopePath">The path of the tree that reported the id, ending with the separator; the id applies below it only</param>
    /// <param name="id">The item id</param>
    public void MarkIdSubtreeExamined(string scopePath, string id)
        => _idScopes.GetOrAdd(scopePath, _ => new IdScope()).Subtrees.TryAdd(id, 0);

    /// <summary>
    /// Adds a mark to a path
    /// </summary>
    /// <param name="path">The path</param>
    /// <param name="mark">The mark to add</param>
    private void AddMark(string path, Mark mark)
    {
        var previous = default(Mark);
        _marks.AddOrUpdate(path, mark, (_, existing) =>
        {
            previous = existing;
            return existing | mark;
        });

        var observer = _folderWalkObserver;
        if (observer == null)
            return;

        // A folder marked as examined in full is judged by that mark whatever else it carries,
        // so an incremental mark is only news without one, and a full mark only when it
        // overrides an incremental one
        if (mark == Mark.Incremental && (previous & (Mark.Incremental | Mark.ExaminedSubtree)) == 0)
            observer(path, FolderWalkMode.Incremental);
        else if (mark == Mark.ExaminedSubtree && (previous & Mark.Incremental) != 0 && (previous & Mark.ExaminedSubtree) == 0)
            observer(path, FolderWalkMode.Full);
    }

    /// <summary>
    /// Whether a path from the previous fileset was examined in this run
    /// </summary>
    /// <param name="path">The path</param>
    /// <returns><c>true</c> if the path was examined</returns>
    public bool WasPathEnumerated(string path)
    {
        if (_marks.IsEmpty)
            return true;

        if (_marks.TryGetValue(path, out var own) && (own & (Mark.ExaminedSelf | Mark.ExaminedSubtree)) != 0)
            return true;

        var lookup = _marks.GetAlternateLookup<ReadOnlySpan<char>>();

        // The longest matching ancestor decides, so a fully walked folder inside an
        // incremental one, or an incremental one inside a fully walked one, is honored
        var end = path.Length;
        if (end > 0 && path[end - 1] == SEPARATOR)
            end--;

        for (var i = end - 1; i >= 0; i--)
        {
            if (path[i] != SEPARATOR)
                continue;

            if (!lookup.TryGetValue(path.AsSpan(0, i + 1), out var mark))
                continue;

            if ((mark & Mark.ExaminedSubtree) != 0)
                return true;

            if ((mark & Mark.Incremental) != 0)
                return IsTouchedByIds(path, end);
        }

        return true;
    }

    /// <summary>
    /// Whether a folder was enumerated incrementally in this run, so that it lists only what
    /// changed and an empty listing does not mean the folder is empty. The folder's own mark
    /// decides, then the nearest marked ancestor: a folder below an incremental one is
    /// incremental unless it was listed in full itself.
    /// </summary>
    /// <param name="folderPath">The folder path</param>
    /// <returns><c>true</c> if the folder lists only its changes</returns>
    public bool IsIncrementallyEnumeratedFolder(string folderPath)
    {
        if (_marks.IsEmpty)
            return false;

        var path = folderPath.Length > 0 && folderPath[^1] == SEPARATOR ? folderPath : folderPath + SEPARATOR;
        var lookup = _marks.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var i = path.Length - 1; i >= 0; i--)
        {
            if (path[i] != SEPARATOR)
                continue;

            if (!lookup.TryGetValue(path.AsSpan(0, i + 1), out var mark))
                continue;

            if ((mark & Mark.ExaminedSubtree) != 0)
                return false;

            if ((mark & Mark.Incremental) != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a path below an incremental folder has an id segment that was reported by a
    /// tree the path is in. Only the scopes that contain the path are consulted, so an id
    /// reported by one tree never decides a path in another. A scope is the path of a tree,
    /// ending with the separator, so the scopes containing the path are among its ancestor
    /// prefixes and are looked up directly; this runs once per carried-forward path, so it
    /// must not depend on the number of trees
    /// </summary>
    /// <param name="path">The path</param>
    /// <param name="end">The length of the path without its trailing separator</param>
    /// <returns><c>true</c> if a segment matches a reported id</returns>
    private bool IsTouchedByIds(string path, int end)
    {
        if (_idScopes.IsEmpty)
            return false;

        var lookup = _idScopes.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var i = 0; i < end; i++)
        {
            if (path[i] != SEPARATOR)
                continue;

            if (lookup.TryGetValue(path.AsSpan(0, i + 1), out var scope) && IsTouchedByIds(scope, path.AsSpan(i + 1, end - (i + 1))))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a path relative to a scope has an id segment the scope reported
    /// </summary>
    /// <param name="scope">The reported ids</param>
    /// <param name="relative">The path below the scope, without a trailing separator</param>
    /// <returns><c>true</c> if a segment matches a reported id</returns>
    private static bool IsTouchedByIds(IdScope scope, ReadOnlySpan<char> relative)
    {
        if (scope.Touched.IsEmpty && scope.Subtrees.IsEmpty)
            return false;

        var gone = scope.Subtrees.GetAlternateLookup<ReadOnlySpan<char>>();
        var touched = scope.Touched.GetAlternateLookup<ReadOnlySpan<char>>();

        while (true)
        {
            var sep = relative.IndexOf(SEPARATOR);
            var segment = sep < 0 ? relative : relative.Slice(0, sep);

            if (segment.Length > 0 && gone.ContainsKey(segment))
                return true;

            if (sep < 0)
                return segment.Length > 0 && touched.ContainsKey(segment);

            relative = relative.Slice(sep + 1);
        }
    }
}
