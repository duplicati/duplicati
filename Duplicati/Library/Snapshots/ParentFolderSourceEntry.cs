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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots;

/// <summary>
/// The folder of a file that is chosen as a source. It is recorded so that a restore can give it
/// its metadata when it has to create it, but the other entries of the folder are not part of the
/// backup, so it lists nothing. It is not a root entry, so the filters apply to it.
/// </summary>
/// <param name="service">The snapshot service</param>
/// <param name="path">The path of the folder, with a trailing directory separator</param>
public sealed class ParentFolderSourceEntry(ISnapshotService service, string path)
    : SnapshotSourceFileEntry(service, path, true, false, false)
{
    /// <inheritdoc/>
    public override IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => AsyncEnumerable.Empty<ISourceProviderEntry>();

    /// <summary>
    /// Adds the folder of each source that is a file, right after the file, unless a source folder
    /// already covers it. Without it, a restore that has to create the folder has no metadata for
    /// it, so the owner and the times of the folder are not restored (#6863). The folder comes
    /// after the file because the sources are taken from a stack, so the folder is processed first
    /// and is not seen as an empty folder.
    /// </summary>
    /// <param name="service">The snapshot service of the sources</param>
    /// <param name="sources">The source entries</param>
    /// <returns>The source entries with the folders of the file sources</returns>
    public static IEnumerable<ISourceProviderEntry> WithFoldersOfFileSources(ISnapshotService service, IEnumerable<ISourceProviderEntry> sources)
    {
        var comparison = Utility.Utility.ClientFilenameStringComparison;
        var sourceFolders = service.SourceEntries
            .Where(x => x.EndsWith(System.IO.Path.DirectorySeparatorChar) || service.DirectoryExists(x))
            .Select(Util.AppendDirSeparator)
            .ToList();
        var added = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);

        foreach (var source in sources)
        {
            yield return source;

            if (source.IsFolder || source.IsAlternateStream)
                continue;

            var folder = System.IO.Path.GetDirectoryName(source.Path);
            if (string.IsNullOrEmpty(folder))
                continue;

            folder = Util.AppendDirSeparator(folder);
            if (sourceFolders.Any(x => folder.StartsWith(x, comparison)) || !added.Add(folder))
                continue;

            if (service.DirectoryExists(folder))
                yield return new ParentFolderSourceEntry(service, folder);
        }
    }
}
