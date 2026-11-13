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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// Thrown when a path cannot be written to a change list
/// </summary>
public sealed class ChangeListPathException : Exception
{
    /// <summary>
    /// Creates a new exception
    /// </summary>
    /// <param name="path">The path that cannot be written</param>
    public ChangeListPathException(string path)
        : base($"The path cannot be written to a change list: {path}")
    {
        Path = path;
    }

    /// <summary>
    /// The path that cannot be written
    /// </summary>
    public string Path { get; }
}

/// <summary>
/// Writes change list files in the format the backup reads: one entry per line,
/// a marker character followed by the full path.
/// </summary>
public static class ChangeListWriter
{
    /// <summary>
    /// The marker for an entry that is rescanned with everything below it
    /// </summary>
    public const char RECURSE_MARKER = 'R';
    /// <summary>
    /// The marker for an entry that is examined by itself
    /// </summary>
    public const char EXAMINE_MARKER = 'E';

    /// <summary>
    /// Decides whether everything below a path must be rescanned. A deleted or renamed-away
    /// path may have been a folder, and a created or renamed-in folder may have received
    /// children before its watch was attached. A folder that only changed by itself is a
    /// parent whose timestamp was bumped by a child, and must not trigger a subtree scan.
    /// </summary>
    /// <param name="kind">The observed kinds</param>
    /// <returns>True to rescan the subtree</returns>
    public static bool RequiresRecursion(ChangeKind kind)
    {
        if ((kind & (ChangeKind.Deleted | ChangeKind.RenamedFrom)) != 0)
            return true;
        if ((kind & (ChangeKind.Created | ChangeKind.RenamedTo)) != 0 && (kind & ChangeKind.IsDirectoryHint) != 0)
            return true;
        return false;
    }

    /// <summary>
    /// Checks whether a path can be written as a line
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <returns>True if the path can be written</returns>
    public static bool IsRepresentable(string path)
    {
        if (path.IndexOfAny(['\n', '\r']) >= 0)
            return false;

        // An unpaired surrogate, which Windows allows in names, does not survive UTF-8
        for (var i = 0; i < path.Length; i++)
        {
            if (char.IsHighSurrogate(path[i]))
            {
                if (i + 1 >= path.Length || !char.IsLowSurrogate(path[i + 1]))
                    return false;
                i++;
            }
            else if (char.IsLowSurrogate(path[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the union of two sets of changes. The recent set is read first and kept in
    /// memory, so it must be the small one; the closed set is streamed.
    /// </summary>
    /// <param name="filePath">The file to write</param>
    /// <param name="closed">The changes of the closed session</param>
    /// <param name="recent">The changes of the new session so far</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of entries written</returns>
    /// <exception cref="ChangeListPathException">A path cannot be written</exception>
    public static async Task<long> WriteAsync(string filePath, IAsyncEnumerable<RecordedChange> closed, IAsyncEnumerable<RecordedChange> recent, CancellationToken cancellationToken)
    {
        // The database compares paths exactly, so the merge does too
        var pending = new Dictionary<string, ChangeKind>(StringComparer.Ordinal);
        await foreach (var change in recent.WithCancellation(cancellationToken).ConfigureAwait(false))
            pending[change.Path] = pending.TryGetValue(change.Path, out var existing) ? existing | change.Kind : change.Kind;

        var count = 0L;
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };

        await foreach (var change in closed.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var kind = change.Kind;
            if (pending.Remove(change.Path, out var recentKind))
                kind |= recentKind;

            await WriteEntryAsync(writer, change.Path, kind).ConfigureAwait(false);
            count++;
        }

        foreach (var (path, kind) in pending)
        {
            await WriteEntryAsync(writer, path, kind).ConfigureAwait(false);
            count++;
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    private static async Task WriteEntryAsync(StreamWriter writer, string path, ChangeKind kind)
    {
        if (!IsRepresentable(path))
            throw new ChangeListPathException(path);

        await writer.WriteAsync(RequiresRecursion(kind) ? RECURSE_MARKER : EXAMINE_MARKER).ConfigureAwait(false);
        await writer.WriteLineAsync(path).ConfigureAwait(false);
    }
}
