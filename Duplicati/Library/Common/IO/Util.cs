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

            return false;
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
    }
}
