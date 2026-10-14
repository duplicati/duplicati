// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using Duplicati.Library.Logging;

namespace Duplicati.Proprietary.Office365;

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
internal sealed class ChangeTracker
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<ChangeTracker>();

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
    /// Ids of items whose own entry was examined; matched against the last segment of a path
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _touchedIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Ids of folders whose whole subtree left its path; matched against any segment of a path
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _goneSubtreeIds = new(StringComparer.Ordinal);

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
    /// Whether the provider is used for a backup that stores state. Listing for the user
    /// interface and restores never load state, and then no tokens are requested or kept.
    /// </summary>
    public bool IsActive { get; private set; }

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
    /// Gets a value stored by the previous backup
    /// </summary>
    /// <param name="key">The state key</param>
    /// <returns>The stored value, or <c>null</c> if none is stored or the tracker is not active</returns>
    public string? GetState(string key)
        => IsActive
            ? _loaded.GetValueOrDefault(key)
            : null;

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
    /// Records that a listing or download below the key failed, so the stored value stays at
    /// what the previous backup recorded and the failed items are reported again. A key without
    /// a previous value is not stored at all, so the next backup walks it in full.
    /// </summary>
    /// <param name="key">The state key</param>
    public void MarkStateFailed(string key)
    {
        if (IsActive && _failedKeys.TryAdd(key, 0))
            Log.WriteVerboseMessage(LOGTAG, "ChangeTrackingKeyHeldBack", "A listing or download below \"{0}\" failed, so its change tracking state is not advanced", key);
    }

    /// <summary>
    /// Gets the state to store with this backup. A failed key keeps the value the previous
    /// backup stored, whether or not a value was recorded for it in this run.
    /// </summary>
    /// <returns>The state rows</returns>
    public IReadOnlyDictionary<string, string> Export()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in _exported)
            if (!_failedKeys.ContainsKey(kv.Key))
                result[kv.Key] = kv.Value();

        foreach (var key in _failedKeys.Keys)
            if (_loaded.TryGetValue(key, out var previous))
                result[key] = previous;

        return result;
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
    /// Marks an item id as examined: a path below an incremental folder whose last
    /// segment is the id counts as examined
    /// </summary>
    /// <param name="id">The item id</param>
    public void MarkIdTouched(string id)
        => _touchedIds.TryAdd(id, 0);

    /// <summary>
    /// Marks a folder id as gone from its path: any path below an incremental folder
    /// that has the id as a segment counts as examined
    /// </summary>
    /// <param name="id">The folder id</param>
    public void MarkIdSubtreeGone(string id)
        => _goneSubtreeIds.TryAdd(id, 0);

    /// <summary>
    /// Adds a mark to a path
    /// </summary>
    /// <param name="path">The path</param>
    /// <param name="mark">The mark to add</param>
    private void AddMark(string path, Mark mark)
        => _marks.AddOrUpdate(path, mark, (_, existing) => existing | mark);

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
                return IsTouchedByIds(path.AsSpan(i + 1, end - (i + 1)));
        }

        return true;
    }

    /// <summary>
    /// Whether a path relative to an incremental folder has an id segment that was reported
    /// </summary>
    /// <param name="relative">The path below the incremental folder, without a trailing separator</param>
    /// <returns><c>true</c> if a segment matches a reported id</returns>
    private bool IsTouchedByIds(ReadOnlySpan<char> relative)
    {
        if (_touchedIds.IsEmpty && _goneSubtreeIds.IsEmpty)
            return false;

        var gone = _goneSubtreeIds.GetAlternateLookup<ReadOnlySpan<char>>();
        var touched = _touchedIds.GetAlternateLookup<ReadOnlySpan<char>>();

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
