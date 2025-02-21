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
using System.IO;

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
        public static string GuessDirSeparator(string path)
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
    }
}
