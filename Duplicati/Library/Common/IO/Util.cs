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
using System.Linq;
using Duplicati.Library.Logging;

#nullable enable

namespace Duplicati.Library.Common.IO
{
    public static class Util
    {
        /// <summary>
        /// The log tag for messages
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType(typeof(Util));

        /// <summary>
        /// The command-line option (without the leading <c>--</c>) that allows using an
        /// insecure data folder without restricting its permissions.
        /// </summary>
        public const string AllowInsecureDatafolderOption = "allow-insecure-datafolder";

        /// <summary>
        /// The environment variable that, when set to a truthy value, allows using an insecure
        /// data folder. This is equivalent to passing <c>--allow-insecure-datafolder</c> on the
        /// command line.
        /// </summary>
        public const string AllowInsecureDatafolderEnvVar = "DUPLICATI__ALLOW_INSECURE_DATAFOLDER";

        /// <summary>
        /// The insecure permission marker file
        /// </summary>
        public const string InsecurePermissionsMarkerFile = "insecure-permissions.txt";

        /// <summary>
        /// Returns <c>true</c> if the user has opted in to using an insecure data folder, either
        /// via the <c>--allow-insecure-datafolder</c> command-line argument or the
        /// <c>DUPLICATI__ALLOW_INSECURE_DATAFOLDER</c> environment variable.
        ///
        /// This is read directly from the process command line and environment so it can be
        /// consulted from low-level components (such as the SQLite loader) without threading the
        /// value through every call. When the option is present without a value it is treated as
        /// enabled; values <c>false</c>, <c>0</c>, <c>no</c> and <c>off</c> disable it.
        ///
        /// In <c>DEBUG</c> builds the option defaults to <c>true</c> (after all explicit sources
        /// are checked), so permission checks do not block local development. An explicit
        /// <c>--allow-insecure-datafolder=false</c> still overrides this default.
        /// </summary>
        /// <returns><c>true</c> if the insecure data folder opt-in is set; otherwise <c>false</c>.</returns>
        public static bool AllowInsecureDataFolder()
        {
            // Command-line argument takes precedence over the environment variable.
            var opt = $"--{AllowInsecureDatafolderOption}";
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            var match = args
                .Select((token, index) => new { token, index })
                .LastOrDefault(x =>
                    string.Equals(x.token, opt, StringComparison.OrdinalIgnoreCase)
                    || x.token.StartsWith(opt + "=", StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                // Found in the form --allow-insecure-datafolder=value
                if (match.token.StartsWith(opt + "=", StringComparison.OrdinalIgnoreCase))
                    return ParseBool(match.token.Substring(opt.Length + 1).Trim('"'));

                // Found in the form --allow-insecure-datafolder value
                if (match.index + 1 < args.Length)
                {
                    var value = args[match.index + 1];
                    if (!value.StartsWith("--"))
                        return ParseBool(value);
                }

                // Found as a bare flag with no value
                return true;
            }

            // Fall back to the environment variable.
            var envValue = Environment.GetEnvironmentVariable(AllowInsecureDatafolderEnvVar);
            if (envValue != null)
                return ParseBool(envValue);

            // Finally, check if the override file exists
            var installfolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(installfolder))
            {
                var path = Path.Combine(installfolder, InsecurePermissionsMarkerFile);
                if (File.Exists(path))
                    return true;
            }

#if DEBUG
            // In debug builds the permission checks are relaxed by default to make local
            // development easier. Users can still opt out explicitly via
            // --allow-insecure-datafolder=false.
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Parses a truthy string value. An empty or whitespace value is treated as enabled
        /// (the option was present); <c>false</c>, <c>0</c>, <c>no</c> and <c>off</c> disable it.
        /// </summary>
        /// <param name="value">The value to parse.</param>
        /// <returns><c>true</c> if the value is truthy; otherwise <c>false</c>.</returns>
        private static bool ParseBool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            return !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                   && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
                   && !value.Equals("no", StringComparison.OrdinalIgnoreCase)
                   && !value.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A cached instance of the directory separator as a string
        /// </summary>
        public static readonly string DirectorySeparatorString = Path.DirectorySeparatorChar.ToString();

        /// <summary>
        /// A cached instance of the alternate directory separator as a string
        /// </summary>
        public static readonly string AltDirectorySeparatorString = Path.AltDirectorySeparatorChar.ToString();

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
        /// Expands a bare Windows drive specification ("C:") to the root of that drive ("C:\").
        /// A bare drive specification is drive-relative, so Path.GetFullPath answers with the
        /// process' current directory on that drive rather than with the root of it.
        /// </summary>
        /// <param name="path">The path to expand</param>
        /// <returns>The root of the drive when the path is a bare drive specification, otherwise the path unchanged</returns>
        public static string ExpandBareDriveRoot(string path)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
                return path;

            return path.Length == 2 && path[1] == ':' && char.IsLetter(path[0])
                ? path + DirectorySeparatorString
                : path;
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
        /// Checks if the path is a Windows drive root (e.g., "C:\" or "D:\").
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns><c>true</c> if the path is a drive root, <c>false</c> otherwise.</returns>
        private static bool IsDriveRoot(string path)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            // GetPathRoot returns "C:\" for "C:\" or "C:\Users"
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return false;

            // Check if the input is exactly the root
            return string.Equals(path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                   && root.EndsWith(Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Get the final resolved path, accounting for symlinks in existing segments.
        /// </summary>
        /// <param name="path">The path to resolve.</param>
        /// <returns>The fully resolved path, with all symlinks resolved.</returns>
        private static string GetFinalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // 1. Separate the Alternate Data Stream suffix if it exists
            var basePath = SystemIO.IO_OS.GetAlternateDataStreamParent(path);
            var streamSuffix = path.Substring(basePath.Length);

            var current = Path.GetFullPath(basePath);
            var ghostSegments = new Stack<string>();

            // 2. Walk up the tree until we find a segment that exists on disk
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

            // 3. Resolve symlinks for the part of the path that actually exists
            // Skip symlink resolution for Windows drive roots (e.g., "C:\") as
            // ResolveLinkTarget can throw DirectoryNotFoundException on some systems
            var resolvedPath = current;
            if (!IsDriveRoot(current))
            {
                FileSystemInfo info = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);

                try
                {
                    resolvedPath = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "ResolveLinkTargetFailed", ex, "Failed to resolve link target for {0}, assuming it is not a link", current);
                }
            }

            // 4. Re-attach the non-existent segments to the resolved base path
            while (ghostSegments.Count > 0)
                resolvedPath = Path.Combine(resolvedPath, ghostSegments.Pop());

            // 5. Re-attach the alternate data stream suffix at the very end
            return resolvedPath + streamSuffix;
        }

        /// <summary>
        /// The number of links followed before a path is refused, which stops on links that point at each other
        /// </summary>
        private const int MaxLinksFollowed = 10;

        /// <summary>
        /// Resolves every symbolic link and junction in a path, so the result is the path that the operating
        /// system ends up in. The part of the path that does not exist is kept as it is, as nothing in it is a link.
        /// </summary>
        /// <remarks>
        /// This is the thorough counterpart of <see cref="GetFinalPath"/>, which looks at the last existing
        /// segment only and is what <see cref="IsPathInsideTarget"/> uses for each path of a restore.
        /// A folder that is reached through a link is not a link itself, so looking at the last segment finds
        /// no link in such a path. Here every existing segment is examined, at the cost of a file system call
        /// for each, and a path is refused when a segment cannot be examined: that suits a single path that
        /// is checked against a limit somebody else has set, and not every file of a restore.
        /// </remarks>
        /// <param name="path">The path to resolve.</param>
        /// <returns>The path with all links resolved.</returns>
        /// <exception cref="IOException">A segment of the path cannot be examined, or the path has more links than are followed.</exception>
        public static string ResolveAllLinks(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // 1. Separate the Alternate Data Stream suffix if it exists
            var basePath = SystemIO.IO_OS.GetAlternateDataStreamParent(path);
            var streamSuffix = path.Substring(basePath.Length);

            // 2. Make the path absolute. This removes the relative segments as text, which is also
            // what the file operations do with a path before they hand it to the operating system.
            var fullPath = Path.GetFullPath(basePath);

            // 3. Walk down from the root, replacing each link that is found with its target.
            // The root is not examined, as resolving a Windows drive root can fail on some systems.
            var resolved = Path.GetPathRoot(fullPath) ?? string.Empty;
            var pending = new Stack<string>(SplitSegments(fullPath.Substring(resolved.Length)).Reverse());
            var followed = 0;

            while (pending.TryPop(out var segment))
            {
                if (segment == ".")
                    continue;

                if (segment == "..")
                {
                    // Only the target of a link has these left. The operating system goes to the parent of the folder
                    // that was reached, which after a link is not the folder that the text before the segment names.
                    resolved = Path.GetDirectoryName(resolved) ?? resolved;
                    continue;
                }

                var parent = resolved;
                resolved = Path.Combine(parent, segment);

                string? linkTarget;
                try
                {
                    // The attributes say if this is a link without reading it. They are -1 for a path that does not exist.
                    var info = new DirectoryInfo(resolved);
                    var attributes = info.Attributes;
                    if ((int)attributes == -1 || !attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    linkTarget = OperatingSystem.IsWindows()
                        ? GetWindowsLinkTarget(info, resolved, path, ref followed)
                        : info.LinkTarget ?? throw new IOException($"The symbolic link cannot be read: {resolved}");
                }
                catch (Exception ex) when (ex is not IOException)
                {
                    // A segment that cannot be examined is not known to be a folder, so where the path leads is not known
                    throw new IOException($"Failed to examine {resolved}: {ex.Message}", ex);
                }

                // On Windows this is a reparse point that leads nowhere else
                if (linkTarget == null)
                    continue;

                if (++followed > MaxLinksFollowed)
                    throw new IOException($"Too many levels of symbolic links in the path: {path}");

                // A relative target continues from the folder the link is in; any other starts over from its root.
                // The segments of the target are followed like the rest, as they can be links and relative segments.
                resolved = parent;
                if (Path.IsPathRooted(linkTarget))
                {
                    resolved = Path.GetPathRoot(linkTarget) ?? string.Empty;
                    linkTarget = linkTarget.Substring(resolved.Length);
                }

                foreach (var targetSegment in SplitSegments(linkTarget).Reverse())
                    pending.Push(targetSegment);
            }

            // 4. Re-attach the alternate data stream suffix at the very end
            return resolved + streamSuffix;
        }

        /// <summary>
        /// Gets where a link on Windows leads, as an absolute path. Windows is asked for the end of the chain of links,
        /// as the target that a link holds can be a name that only Windows can follow, such as the name of a volume.
        /// </summary>
        /// <param name="info">The link</param>
        /// <param name="linkPath">The path of the link</param>
        /// <param name="path">The path that is being resolved, for the error message</param>
        /// <param name="followed">The number of links followed so far, which is increased by the links the chain has after the first</param>
        /// <returns>The absolute path the link leads to, or <c>null</c> if it leads nowhere else</returns>
        private static string? GetWindowsLinkTarget(DirectoryInfo info, string linkPath, string path, ref int followed)
        {
            // The links of the chain are counted one step at a time, before Windows is asked to follow them all.
            // That keeps the limit on the number of links, and stops on links that point at each other.
            FileSystemInfo step = info;
            string? firstStep = null;
            var steps = 0;
            while (step.ResolveLinkTarget(returnFinalTarget: false) is { } next)
            {
                firstStep ??= next.ToString();
                if (followed + ++steps > MaxLinksFollowed)
                    throw new IOException($"Too many levels of symbolic links in the path: {path}");

                // The chain ends on something that is not a link, or is not there, which cannot be asked for its target
                var attributes = next.Attributes;
                if ((int)attributes == -1 || !attributes.HasFlag(FileAttributes.ReparsePoint))
                    break;

                step = next;
            }

            // The caller counts the first link
            followed += Math.Max(0, steps - 1);

            // The text a link holds does not say if it is relative, but the target of one step does:
            // it is the text itself for an absolute target, and joined to the folder of the link for a relative one
            var ownTarget = info.LinkTarget;
            var isRelative = !string.Equals(ownTarget, firstStep, StringComparison.Ordinal);

            // A folder that a volume is mounted on is the root of that volume, and leads nowhere else. Following it
            // would give the drive letter of the volume, if it has one, which is another spelling of the same folder.
            if (!isRelative && IsWindowsVolumeName(ownTarget))
                return null;

            // The text is used as it was computed: as a full name, a path that is not rooted is placed in the current folder
            var target = ToWindowsPath(info.ResolveLinkTarget(returnFinalTarget: true)?.ToString());
            if (target == null)
                return null;

            if (!Path.Exists(target) && !string.IsNullOrEmpty(ownTarget))
            {
                // Windows does not follow a link without a target, and the text is then put together from the last link
                // of the chain, which is wrong for a target that starts at the root of a drive. One step is followed
                // instead, with the rules for a path that is relative to a folder, and the caller follows the next.
                target = isRelative
                    ? Path.GetFullPath(ownTarget, Path.GetDirectoryName(linkPath) ?? linkPath)
                    : ToWindowsPath(ownTarget);
                if (target == null)
                    return null;
            }

            // A link that the server of a share follows leads to itself
            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            return string.Equals(target.TrimEnd(separators), linkPath.TrimEnd(separators), StringComparison.OrdinalIgnoreCase)
                ? null
                : target;
        }

        /// <summary>
        /// Checks if a link target on Windows is the name of a volume and nothing more
        /// </summary>
        /// <param name="target">The target</param>
        /// <returns><c>true</c> if the target is the name of a volume</returns>
        private static bool IsWindowsVolumeName(string? target)
        {
            if (string.IsNullOrEmpty(target))
                return false;

            var name = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return name.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith('}')
                && name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
        }

        /// <summary>
        /// Makes the absolute target of a link on Windows a path that can be compared and followed
        /// </summary>
        /// <param name="target">The target, as Windows or the link gives it</param>
        /// <returns>The absolute path, or <c>null</c> if the target leads nowhere else</returns>
        private static string? ToWindowsPath(string? target)
        {
            if (string.IsNullOrEmpty(target))
                return null;

            // The path of a network share is given without its first characters, and others can have the prefix for long paths
            const string LongPathPrefix = @"\\?\";
            if (target.StartsWith(LongPathPrefix + @"UNC\", StringComparison.OrdinalIgnoreCase))
                target = @"\\" + target.Substring(LongPathPrefix.Length + 4);
            else if (target.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
                target = @"\\" + target.Substring(4);
            else if (target.StartsWith(LongPathPrefix, StringComparison.Ordinal) && target.Length > LongPathPrefix.Length + 1 && target[LongPathPrefix.Length + 1] == ':')
                target = target.Substring(LongPathPrefix.Length);

            if (!Path.IsPathFullyQualified(target))
            {
                // What is left is a name that is not a path. The name of a volume alone is the root of a volume that
                // cannot be reached. Any other name is kept in a form that can be followed, which has another root
                // than a path with a drive letter.
                if (IsWindowsVolumeName(target))
                    return null;

                target = LongPathPrefix + target;
            }

            // A link without a target is given with the relative segments of the target left in
            return Path.GetFullPath(target);
        }

        /// <summary>
        /// Splits a path without its root into segments
        /// </summary>
        /// <param name="path">The path</param>
        /// <returns>The segments</returns>
        private static string[] SplitSegments(string path)
            => path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    }
}
