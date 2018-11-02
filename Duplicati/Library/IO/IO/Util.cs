//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
        public static string GuessDirSeparator(string path)
        {
            return string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ? "/" : "\\";
        }
    }
}
