// Copyright (C) 2025, The Duplicati Team
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

#nullable enable

namespace Duplicati.Library.Common.IO
{
    public static class Util
    {
        /// <summary>
        /// A cached instance of the directory separator as a string
        /// </summary>
        public static readonly string DirectorySeparatorString = Path.DirectorySeparatorChar.ToString();

        /// <summary>
        /// A cached instance of the alternate directory separator as a string
        /// </summary>
        public static readonly string AltDirectorySeparatorString = Path.AltDirectorySeparatorChar.ToString();

        /// <summary>
        /// Filename of a marker file that can be put inside the data folder to prevent Duplicati from fixing lax permissions
        /// </summary>
        public const string InsecurePermissionsMarkerFile = "insecure-permissions.txt";

        /// <summary>
        /// Appends the appropriate directory separator to paths, depending on OS.
        /// Does not append the separator if the path already ends with it.
        /// </summary>
        /// <param name="path">The path to append to</param>
        /// <returns>The path with the directory separator appended</returns>
        public static string AppendDirSeparator(string path)
        {
            return AppendDirSeparator(path, DirectorySeparatorString);
        }

        /// <summary>
        /// Appends the specified directory separator to paths.
        /// Does not append the separator if the path already ends with it.
        /// </summary>
        /// <param name="path">The path to append to</param>
        /// <param name="separator">The directory separator to use</param>
        /// <returns>The path with the directory separator appended</returns>
        public static string AppendDirSeparator(string path, string separator)
        {
            return !path.EndsWith(separator, StringComparison.Ordinal) ? path + separator : path;
        }


        /// <summary>
        /// Guesses the directory separator from the path
        /// </summary>
        /// <param name="path">The path to guess the separator from</param>
        /// <returns>The guessed directory separator</returns>
        public static string GuessDirSeparator(string? path)
        {
            return string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ? "/" : "\\";
        }

        /// <summary>
        /// Checks if the path is inside the Windows folder
        /// </summary>
        /// <param name="path">The path to check</param>
        /// <returns><c>true</c> if the path is inside the Windows folder, <c>false</c> otherwise</returns>
        public static bool IsPathUnderWindowsFolder(string path)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            return path.StartsWith(AppendDirSeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verify that the given path is within the target destination.
        /// </summary>
        /// <param name="path">The path to verify.</param>
        /// <param name="targetDestination">The target destination to verify against.</param>
        /// <returns><c>true</c> if the path is inside the target destination, <c>false</c> otherwise.</returns>
        public static bool IsPathInsideTarget(string path, string targetDestination)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(targetDestination))
                throw new ArgumentNullException(nameof(targetDestination));

            var fullPath = Path.GetFullPath(path);
            var fullTarget = Path.GetFullPath(targetDestination);

            // Resolve the target destination once
            var realTarget = GetFinalPath(fullTarget);

            // Resolve the path of the file/folder we are about to create
            var realPath = GetFinalPath(fullPath);

            // Normalize both paths to ensure consistent comparison
            // This handles cases where paths differ only by trailing separators
            var normalizedTarget = realTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = realPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // If the paths are equal after normalization, it's valid
            if (string.Equals(normalizedPath, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                return true;

            var relative = Path.GetRelativePath(realTarget, realPath);

            if (relative.StartsWith("..") || Path.IsPathRooted(relative))
                return false;

            return true;
        }

        /// <summary>
        /// Get the final resolved path, accounting for symlinks in existing segments.
        /// </summary>
        /// <param name="path">The path to resolve.</param>
        /// <returns>The fully resolved path, with all symlinks resolved.</returns>
        private static string GetFinalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            var current = Path.GetFullPath(path);
            var ghostSegments = new Stack<string>();

            // 1. Walk up the tree until we find a segment that exists on disk
            while (!Path.Exists(current))
            {
                var parent = Path.GetDirectoryName(current);

                // If we've reached the root and nothing exists (rare/impossible for absolute paths),
                // we have to stop and return the original path.
                if (string.IsNullOrEmpty(parent) || parent == current)
                    return path;

                // Store the part that doesn't exist so we can put it back later
                ghostSegments.Push(Path.GetFileName(current));
                current = parent;
            }

            // 2. Resolve symlinks for the part of the path that actually exists
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            var resolvedPath = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;

            // 3. Re-attach the non-existent segments to the resolved base path
            while (ghostSegments.Count > 0)
                resolvedPath = Path.Combine(resolvedPath, ghostSegments.Pop());

            return resolvedPath;
        }
    }
}
