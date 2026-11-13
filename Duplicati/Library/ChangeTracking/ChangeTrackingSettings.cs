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
/// The settings of the change tracking service
/// </summary>
/// <param name="Enabled">Whether backups may use the watcher at all</param>
/// <param name="DatabasePath">The path to the tracking database; it is recreated on start</param>
/// <param name="TempFolder">The folder for the change list files handed to backups</param>
/// <param name="FlushInterval">How often buffered changes are written to the database</param>
/// <param name="HighWaterMark">The number of buffered changes that triggers a write</param>
/// <param name="MaxChanges">The number of recorded paths above which a full scan is cheaper than the list</param>
/// <param name="GraceDelay">How long the handoff waits for late events after closing a session</param>
public sealed record ChangeTrackingSettings(
    bool Enabled,
    string DatabasePath,
    string TempFolder,
    TimeSpan FlushInterval,
    int HighWaterMark,
    long MaxChanges,
    TimeSpan GraceDelay)
{
    /// <summary>
    /// The default flush interval
    /// </summary>
    public const string DEFAULT_FLUSH_INTERVAL = "10s";
    /// <summary>
    /// The default high-water mark
    /// </summary>
    public const int DEFAULT_HIGH_WATER_MARK = 5000;
    /// <summary>
    /// The default limit on recorded paths per session
    /// </summary>
    public const long DEFAULT_MAX_CHANGES = 1000000;
    /// <summary>
    /// The default grace delay
    /// </summary>
    public const string DEFAULT_GRACE_DELAY = "2s";
    /// <summary>
    /// The file name of the tracking database inside the data folder
    /// </summary>
    public const string DATABASE_FILENAME = "change-tracking.sqlite";
}
