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

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// The in-memory set of observed paths, deduplicated by path. Event handlers
/// only add to it, so they return quickly; a flush drains it in one swap.
/// </summary>
public sealed class ChangeBuffer
{
    private readonly object _lock = new();
    private readonly int _highWaterMark;
    private Dictionary<string, BufferedChange> _changes;

    /// <summary>
    /// Creates a new buffer
    /// </summary>
    /// <param name="highWaterMark">The number of entries at which the owner should flush</param>
    public ChangeBuffer(int highWaterMark)
    {
        if (highWaterMark <= 0)
            throw new ArgumentOutOfRangeException(nameof(highWaterMark), "The high-water mark must be positive");
        _highWaterMark = highWaterMark;
        _changes = CreateDictionary();
    }

    // Paths are kept as reported, so a case-only rename keeps both spellings and the
    // backup can tell them apart; it resolves the spelling on disk
    private static Dictionary<string, BufferedChange> CreateDictionary()
        => new(StringComparer.Ordinal);

    /// <summary>
    /// The number of buffered paths
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _changes.Count;
        }
    }

    /// <summary>
    /// Records an observation. The kind is merged into an existing entry for the path
    /// and the observation time is raised to the newer value.
    /// </summary>
    /// <param name="path">The full path</param>
    /// <param name="kind">The observed kind</param>
    /// <param name="observedUtcTicks">The UTC ticks of the observation</param>
    /// <returns>True if the buffer has reached the high-water mark and should be flushed</returns>
    public bool Add(string path, ChangeKind kind, long observedUtcTicks)
    {
        lock (_lock)
        {
            if (_changes.TryGetValue(path, out var existing))
                _changes[path] = new BufferedChange(existing.Kind | kind, Math.Max(existing.ObservedUtcTicks, observedUtcTicks));
            else
                _changes[path] = new BufferedChange(kind, observedUtcTicks);

            return _changes.Count >= _highWaterMark;
        }
    }

    /// <summary>
    /// Takes every buffered change out of the buffer in one swap
    /// </summary>
    /// <returns>The drained changes</returns>
    public Dictionary<string, BufferedChange> Drain()
    {
        lock (_lock)
        {
            var drained = _changes;
            _changes = CreateDictionary();
            return drained;
        }
    }

    /// <summary>
    /// Removes the changes last observed before a time
    /// </summary>
    /// <param name="utcTicks">The earliest observation time to keep</param>
    /// <returns>The number of removed changes</returns>
    public int PruneOlderThan(long utcTicks)
    {
        lock (_lock)
        {
            var old = _changes.Where(x => x.Value.ObservedUtcTicks < utcTicks).Select(x => x.Key).ToList();
            foreach (var path in old)
                _changes.Remove(path);
            return old.Count;
        }
    }

    /// <summary>
    /// Puts changes back, as after a failed flush. Entries for the same path are merged.
    /// </summary>
    /// <param name="changes">The changes to put back</param>
    public void Merge(IReadOnlyDictionary<string, BufferedChange> changes)
    {
        lock (_lock)
        {
            foreach (var (path, change) in changes)
            {
                if (_changes.TryGetValue(path, out var existing))
                    _changes[path] = new BufferedChange(existing.Kind | change.Kind, Math.Max(existing.ObservedUtcTicks, change.ObservedUtcTicks));
                else
                    _changes[path] = change;
            }
        }
    }
}
