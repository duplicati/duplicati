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
using System.Linq;
using Duplicati.Library.Common.IO;

namespace Duplicati.Server;

/// <summary>
/// Locates the QTS shared folders when running as a QNAP package.
/// QTS keeps one symlink per shared folder in the share root (<c>/share</c>),
/// pointing at the folder on its data volume, such as
/// <c>/share/CACHEDEV1_DATA/Public</c>. The symlinks are the only list of
/// shares across all volumes that is visible inside the package chroot, but a
/// symlink selected as a backup source is stored as a link with nothing behind
/// it, so each share is exposed as a special folder resolving to the real path.
/// </summary>
public static class QnapSharedFolders
{
    /// <summary>
    /// The folder QTS keeps the shared folder symlinks in
    /// </summary>
    public const string DefaultShareRoot = "/share";

    /// <summary>
    /// The prefix of the special folder tokens for shared folders
    /// </summary>
    public const string TokenPrefix = "%QNAP_SHARE_";

    /// <summary>
    /// A QTS shared folder
    /// </summary>
    /// <param name="Name">The share name as shown in QTS</param>
    /// <param name="Path">The folder on the data volume</param>
    public sealed record SharedFolder(string Name, string Path);

    /// <summary>
    /// True when running inside the QNAP package, which the service script
    /// marks by exporting the package name
    /// </summary>
    public static bool IsQnapPackage
        => OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QNAP_QPKG"));

    /// <summary>
    /// Gets the special folder token for a shared folder
    /// </summary>
    /// <param name="shareName">The share name</param>
    /// <returns>The token, such as <c>%QNAP_SHARE_Public%</c></returns>
    public static string TokenFor(string shareName)
        => TokenPrefix + shareName + "%";

    /// <summary>
    /// Lists the shared folders: the symlinks in the share root that point
    /// to an existing folder, sorted by name
    /// </summary>
    /// <param name="shareRoot">The folder holding the shared folder symlinks</param>
    /// <returns>The shared folders</returns>
    public static IEnumerable<SharedFolder> Enumerate(string shareRoot)
    {
        if (OperatingSystem.IsWindows() || !Directory.Exists(shareRoot))
            return [];

        var result = new List<SharedFolder>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(shareRoot))
        {
            var name = Path.GetFileName(entry);

            // Hidden entries are QTS internals, and a name with the token
            // delimiter cannot be expressed as a special folder
            if (string.IsNullOrEmpty(name) || name.StartsWith('.') || name.Contains('%'))
                continue;

            var target = ResolveSymlinkTarget(shareRoot, entry);
            if (target != null)
                result.Add(new SharedFolder(name, target));
        }

        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Resolves a share root entry to the folder it links to
    /// </summary>
    /// <param name="shareRoot">The share root, which relative link targets are resolved against</param>
    /// <param name="entry">The entry to resolve</param>
    /// <returns>The linked folder, or null if the entry is not a symlink to an existing folder</returns>
    private static string? ResolveSymlinkTarget(string shareRoot, string entry)
    {
        try
        {
            if (!SystemIO.IO_OS.GetFileAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                return null;

            var target = SystemIO.IO_OS.GetSymlinkTarget(entry);
            if (string.IsNullOrWhiteSpace(target))
                return null;

            if (!Path.IsPathRooted(target))
                target = Path.Combine(shareRoot, target);

            target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
            if (target.Length == 0 || !Directory.Exists(target))
                return null;

            return target;
        }
        catch
        {
            // An entry that cannot be read is not offered as a share
            return null;
        }
    }
}
