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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin;

/// <summary>
/// Helpers for the virtual paths used by source providers that expose
/// non-filesystem content (e.g. Hyper-V machines or MSSQL databases).
/// <para>
/// The virtual content is mounted under a UNC-style root, such as
/// <c>\\duplicati\hyperv\</c>, so the stored paths are rooted and valid on Windows
/// without colliding with a local disk. Each file or folder the content is read
/// from is placed directly below its virtual folder under its own name
/// (<c>C:\VMs\disk.vhdx</c> becomes <c>&lt;vm&gt;\disk.vhdx</c>), numbered when
/// names clash (see <see cref="AssignNames"/>). The local path is kept in the
/// entry metadata, so it is not needed in the stored path.
/// </para>
/// </summary>
public static class VirtualSourcePath
{
    /// <summary>
    /// The host name used in the root of the virtual paths
    /// </summary>
    public const string VIRTUAL_HOST = "duplicati";

    /// <summary>
    /// The name used for a data path that is the root of a filesystem
    /// </summary>
    public const string ROOT_NAME = "root";

    /// <summary>
    /// Metadata key suffix that holds the local path an entry was read from
    /// </summary>
    public const string ORIGINAL_PATH_KEY = "orig-path";

    /// <summary>
    /// Builds the rooted mount path for a virtual share, e.g. <c>\\duplicati\hyperv\</c>
    /// </summary>
    /// <param name="share">The share name</param>
    /// <returns>The mount path, ending with a directory separator</returns>
    public static string GetMountedPath(string share)
    {
        var ds = Path.DirectorySeparatorChar;
        return $"{ds}{ds}{VIRTUAL_HOST}{ds}{share}{ds}";
    }

    /// <summary>
    /// Gets the name a data path is stored under: the last segment of the path,
    /// or the drive letter for the root of a drive
    /// </summary>
    /// <param name="localPath">The local path</param>
    /// <returns>The name</returns>
    public static string GetDataPathName(string localPath)
    {
        var name = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar)).TrimEnd(':');
        return string.IsNullOrEmpty(name) ? ROOT_NAME : name;
    }

    /// <summary>
    /// Assigns the names the data paths of a virtual item are stored under.
    /// Each data path is named by <see cref="GetDataPathName"/>. Paths that share
    /// a name (compared case-insensitively) are ordered by their full path and all
    /// get a <c>-N</c> suffix before the extension (<c>data-1.ndf</c>, <c>data-2.ndf</c>;
    /// a folder becomes <c>Snapshots-1</c>), skipping any number that would give
    /// a name already in use.
    /// </summary>
    /// <param name="dataPaths">The local paths and whether each is a folder</param>
    /// <returns>The names, in the same order as the data paths</returns>
    public static List<string> AssignNames(IReadOnlyList<(string LocalPath, bool IsFolder)> dataPaths)
    {
        var names = dataPaths.Select(x => GetDataPathName(x.LocalPath)).ToList();
        var taken = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        var clashes = Enumerable.Range(0, dataPaths.Count)
            .GroupBy(i => names[i], StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in clashes)
        {
            var number = 1;
            var ordered = group
                .OrderBy(i => dataPaths[i].LocalPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => dataPaths[i].LocalPath, StringComparer.Ordinal);

            foreach (var i in ordered)
            {
                string candidate;
                do
                    candidate = AddNumber(names[i], number++, dataPaths[i].IsFolder);
                while (taken.Contains(candidate));

                taken.Add(candidate);
                names[i] = candidate;
            }
        }

        return names;
    }

    /// <summary>
    /// Adds a <c>-N</c> suffix to a name, before the extension of a file
    /// </summary>
    /// <param name="name">The name</param>
    /// <param name="number">The number to add</param>
    /// <param name="isFolder">True if the name is a folder, which has no extension</param>
    /// <returns>The numbered name</returns>
    private static string AddNumber(string name, int number, bool isFolder)
    {
        var extension = isFolder ? string.Empty : Path.GetExtension(name);
        var baseName = name.Substring(0, name.Length - extension.Length);
        if (baseName.Length == 0)
            (baseName, extension) = (name, string.Empty);

        return $"{baseName}-{number}{extension}";
    }

    /// <summary>
    /// Removes duplicate paths, and paths that are inside another path in the list,
    /// so the content of a folder is not produced more than once
    /// </summary>
    /// <param name="paths">The local paths</param>
    /// <returns>The paths that are not contained in another path, in their original order</returns>
    public static List<string> RemoveNestedPaths(IEnumerable<string> paths)
    {
        var comparison = Library.Utility.Utility.ClientFilenameStringComparison;
        var list = paths.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var result = new List<string>();

        foreach (var path in list)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
            if (result.Any(x => x.TrimEnd(Path.DirectorySeparatorChar).Equals(trimmed, comparison)))
                continue;

            if (list.Any(x => path.StartsWith(Util.AppendDirSeparator(x), comparison) && !x.TrimEnd(Path.DirectorySeparatorChar).Equals(trimmed, comparison)))
                continue;

            result.Add(path);
        }

        return result;
    }

    /// <summary>
    /// Finds an entry in a virtual hierarchy by its path, descending into the child
    /// that is the path or contains it. The children are matched on their full path
    /// rather than on their name, so the lookup does not depend on how the
    /// entries name their path segments.
    /// </summary>
    /// <param name="root">The root entry of the hierarchy</param>
    /// <param name="path">The path to find</param>
    /// <param name="isFolder">True if the path is a folder</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entry, or null if it does not exist or is not of the requested kind</returns>
    public static async Task<ISourceProviderEntry?> FindEntryAsync(ISourceProviderEntry root, string path, bool isFolder, CancellationToken cancellationToken)
    {
        var target = Util.AppendDirSeparator(path);
        if (!target.StartsWith(Util.AppendDirSeparator(root.Path), StringComparison.OrdinalIgnoreCase))
            return null;

        var current = root;
        while (!Util.AppendDirSeparator(current.Path).Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            ISourceProviderEntry? next = null;
            await foreach (var child in current.Enumerate(cancellationToken).ConfigureAwait(false))
            {
                var childPath = Util.AppendDirSeparator(child.Path);
                if (childPath.Equals(target, StringComparison.OrdinalIgnoreCase)
                    || (child.IsFolder && target.StartsWith(childPath, StringComparison.OrdinalIgnoreCase)))
                {
                    next = child;
                    break;
                }
            }

            if (next == null)
                return null;

            current = next;
        }

        return current.IsFolder == isFolder ? current : null;
    }
}
