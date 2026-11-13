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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation.Backup;

/// <summary>
/// One entry of a change list
/// </summary>
/// <param name="Path">The full path, without a trailing directory separator unless it is a root</param>
/// <param name="Recurse">True if the path and everything below it must be rescanned, false if only the entry itself is examined</param>
internal sealed record ChangeListEntry(string Path, bool Recurse);

/// <summary>
/// A normalized change list file and the local file source roots it applies to.
/// The file has one entry per line: the character <c>R</c> (rescan the path and
/// everything below it) or <c>E</c> (examine the entry itself only), followed by the
/// full path. Lines are separated by <c>\n</c>; a trailing <c>\r</c> is ignored.
/// A normalized list has one line per path, and every entry that must be rescanned
/// with its subtree is marked <c>R</c> and also held in <see cref="RecursedPaths"/>.
/// </summary>
/// <param name="FilePath">The path to the normalized list file</param>
/// <param name="FileSourceRoots">The local file source roots of the backup; folders end with a directory separator</param>
/// <param name="RecursedPaths">The paths marked <c>R</c>, compared as the file system compares names</param>
/// <param name="GonePaths">The paths found to no longer exist, which must not be resolved again under another spelling</param>
internal sealed record ChangeList(string FilePath, IReadOnlyList<string> FileSourceRoots, IReadOnlySet<string> RecursedPaths, IReadOnlySet<string> GonePaths)
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
    /// Reads the entries of the list, without keeping the list in memory
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    public IAsyncEnumerable<ChangeListEntry> ReadAsync(CancellationToken cancellationToken)
        => ReadFileAsync(FilePath, cancellationToken);

    /// <summary>
    /// Reads the entries of a change list file, without keeping the list in memory
    /// </summary>
    /// <param name="filePath">The path to the list file</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    public static async IAsyncEnumerable<ChangeListEntry> ReadFileAsync(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        var lineNumber = 0L;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                yield break;

            lineNumber++;
            if (line.Length == 0)
                continue;

            var marker = line[0];
            if (marker != RECURSE_MARKER && marker != EXAMINE_MARKER)
                throw new UserInformationException($"Line {lineNumber} of the change list \"{filePath}\" does not start with '{RECURSE_MARKER}' or '{EXAMINE_MARKER}'", "InvalidChangeListEntry");

            var path = NormalizePath(line.Substring(1));
            if (string.IsNullOrWhiteSpace(path))
                throw new UserInformationException($"Line {lineNumber} of the change list \"{filePath}\" has no path", "InvalidChangeListEntry");

            yield return new ChangeListEntry(path, marker == RECURSE_MARKER);
        }
    }

    /// <summary>
    /// Removes trailing directory separators, unless the path is a root
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The normalized path</returns>
    public static string NormalizePath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
            return path;

        // A root such as "C:\\" or "/" keeps its separator
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && trimmed.Length < root.Length)
            return root;

        return trimmed;
    }
}
