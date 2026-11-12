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

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// What the watcher observed for a path. Several kinds accumulate for a path
/// that was reported more than once. The directory hint is what the watcher
/// could tell at event time and is only a hint; the backup re-checks the path.
/// </summary>
[Flags]
public enum ChangeKind
{
    /// <summary>Nothing observed</summary>
    None = 0,
    /// <summary>The path was created</summary>
    Created = 1,
    /// <summary>The path's contents or attributes changed</summary>
    Changed = 2,
    /// <summary>The path was deleted</summary>
    Deleted = 4,
    /// <summary>The path is the old name of a renamed entry</summary>
    RenamedFrom = 8,
    /// <summary>The path is the new name of a renamed entry</summary>
    RenamedTo = 16,
    /// <summary>The path was a directory when the event was observed</summary>
    IsDirectoryHint = 32,
}

/// <summary>
/// A change held in memory before it is written to the tracking database
/// </summary>
/// <param name="Kind">The accumulated kinds</param>
/// <param name="ObservedUtcTicks">The UTC ticks of the newest observation</param>
public readonly record struct BufferedChange(ChangeKind Kind, long ObservedUtcTicks);

/// <summary>
/// A change read from the tracking database
/// </summary>
/// <param name="Path">The full path</param>
/// <param name="Kind">The accumulated kinds</param>
/// <param name="ObservedUtcTicks">The UTC ticks of the newest observation</param>
public readonly record struct RecordedChange(string Path, ChangeKind Kind, long ObservedUtcTicks);
