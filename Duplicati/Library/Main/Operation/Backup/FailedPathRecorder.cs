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
using System.Threading;

namespace Duplicati.Library.Main.Operation.Backup;

/// <summary>
/// Collects the paths a backup failed to process, such as a locked file or a folder that
/// could not be listed. A full scan looks at such a path again on the next run by itself;
/// a run driven by a change list only looks at listed paths, so the paths are stored with
/// the fileset and added to the next run's list. The recorder is ambient for the run, so
/// the processes started by the backup reach it without being handed it.
/// </summary>
internal sealed class FailedPathRecorder
{
    private static readonly AsyncLocal<FailedPathRecorder?> _current = new();

    private readonly object _lock = new();
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly int _limit;

    /// <summary>
    /// Creates a new recorder
    /// </summary>
    /// <param name="limit">The number of paths above which the recorder gives up</param>
    private FailedPathRecorder(int limit)
    {
        _limit = limit;
    }

    /// <summary>
    /// The recorder of the current run, or null
    /// </summary>
    public static FailedPathRecorder? Current => _current.Value;

    /// <summary>
    /// Makes a new recorder current for the calling flow and everything it starts
    /// </summary>
    /// <param name="limit">The number of paths above which the recorder gives up</param>
    /// <returns>The recorder</returns>
    public static FailedPathRecorder Start(int limit)
    {
        var recorder = new FailedPathRecorder(limit);
        _current.Value = recorder;
        return recorder;
    }

    /// <summary>
    /// Records a failed path with the current recorder, if any
    /// </summary>
    /// <param name="path">The path</param>
    public static void Record(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            _current.Value?.Add(path);
    }

    /// <summary>
    /// Whether more paths failed than the recorder keeps, so the run cannot be repaired by a list
    /// </summary>
    public bool Overflowed { get; private set; }

    private void Add(string path)
    {
        lock (_lock)
        {
            if (Overflowed)
                return;

            _paths.Add(path);
            if (_paths.Count > _limit)
            {
                Overflowed = true;
                _paths.Clear();
            }
        }
    }

    /// <summary>
    /// The recorded paths
    /// </summary>
    /// <returns>A copy of the recorded paths</returns>
    public List<string> GetPaths()
    {
        lock (_lock)
            return new List<string>(_paths);
    }
}
