#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Core
{
    public static class Utility
    {
        /// <summary>
        /// Copies the content of one stream into another
        /// </summary>
        /// <param name="source">The stream to read from</param>
        /// <param name="target">The stream to write to</param>
        public static void CopyStream(System.IO.Stream source, System.IO.Stream target)
        {
            CopyStream(source, target, true);
        }

        /// <summary>
        /// Copies the content of one stream into another
        /// </summary>
        /// <param name="source">The stream to read from</param>
        /// <param name="target">The stream to write to</param>
        /// <param name="tryRewindSource">True if an attempt should be made to rewind the source stream, false otherwise</param>
        public static void CopyStream(System.IO.Stream source, System.IO.Stream target, bool tryRewindSource)
        {
            if (tryRewindSource && source.CanSeek)
                try { source.Position = 0; }
                catch { }

            byte[] buf = new byte[4096];
            int read;

            while ((read = source.Read(buf, 0, buf.Length)) != 0)
                target.Write(buf, 0, read);
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <returns>A list of the full filenames</returns>
        public static List<string> EnumerateFiles(string basepath)
        {
            return EnumerateFiles(basepath, null);
        }

        /// <summary>
        /// Returns a list of folder names found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <returns>A list of the full paths</returns>
        public static List<string> EnumerateFolders(string basepath)
        {
            return EnumerateFolders(basepath, null);
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <param name="filter">An optional filter to apply to the filenames</param>
        /// <returns>A list of the full filenames</returns>
        public static List<string> EnumerateFiles(string basepath, FilenameFilter filter)
        {
            List<string> files = new List<string>();

            if (!System.IO.Directory.Exists(basepath))
                return files;

            Queue<string> lst = new Queue<string>();

            lst.Enqueue(basepath);

            while (lst.Count > 0)
            {
                string f = lst.Dequeue();
                foreach (string s in System.IO.Directory.GetDirectories(f))
                    if (filter == null || filter.ShouldInclude(basepath, AppendDirSeperator(s)))
                        lst.Enqueue(s);

                foreach (string s in System.IO.Directory.GetFiles(f))
                    if (filter == null || filter.ShouldInclude(basepath, s))
                        files.Add(s);
            }

            return files;
        }

        /// <summary>
        /// Returns a list of all files and subfolders found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in.</param>
        /// <returns>A list of the full filenames and foldernames. Foldernames ends with the directoryseperator char</returns>
        public static List<string> EnumerateFileSystemEntries(string basepath)
        {
            return EnumerateFileSystemEntries(basepath, null);
        }

        /// <summary>
        /// Returns a list of all files and subfolders found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in.</param>
        /// <param name="filter">An optional filter to apply to the filenames</param>
        /// <returns>A list of the full filenames and foldernames. Foldernames ends with the directoryseperator char</returns>
        public static List<string> EnumerateFileSystemEntries(string basepath, FilenameFilter filter)
        {
            List<string> entries = new List<string>();

            if (!System.IO.Directory.Exists(basepath))
                return entries;

            Queue<string> lst = new Queue<string>();

            lst.Enqueue(basepath);

            while (lst.Count > 0)
            {
                string f = lst.Dequeue();
                foreach (string s in System.IO.Directory.GetDirectories(f))
                {
                    string folder = AppendDirSeperator(s);
                    if (filter == null || filter.ShouldInclude(basepath, folder))
                    {
                        lst.Enqueue(folder);
                        entries.Add(folder);
                    }
                }

                foreach (string s in System.IO.Directory.GetFiles(f))
                    if (filter == null || filter.ShouldInclude(basepath, s))
                        entries.Add(s);
            }

            return entries;
        }

        /// <summary>
        /// Returns a list of folder names found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <param name="filter">An optional filter to apply to the folder names</param>
        /// <returns>A list of the full paths</returns>
        public static List<string> EnumerateFolders(string basepath, FilenameFilter filter)
        {
            List<string> folders = new List<string>();

            if (!System.IO.Directory.Exists(basepath))
                return folders;

            Queue<string> lst = new Queue<string>();
            lst.Enqueue(basepath);

            while (lst.Count > 0)
            {
                string f = lst.Dequeue();
                foreach (string s in System.IO.Directory.GetDirectories(f))
                {
                    if (filter == null || filter.ShouldInclude(basepath, Core.Utility.AppendDirSeperator(s)))
                    {
                        folders.Add(s);
                        lst.Enqueue(s);
                    }
                }

            }

            return folders;
        }

        /// <summary>
        /// Appends the appropriate directory seperator to paths, depending on OS.
        /// Does not append the seperator if the path already ends with it.
        /// </summary>
        /// <param name="path">The path to append to</param>
        /// <returns>The path with the directory seperator appended</returns>
        public static string AppendDirSeperator(string path)
        {
            if (!path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                return path += System.IO.Path.DirectorySeparatorChar;
            else
                return path;
        }

        /// <summary>
        /// Some streams can return a number that is less than the requested number of bytes.
        /// This is usually due to fragmentation, and is solved by issuing a new read.
        /// This function wraps that functionality.
        /// </summary>
        /// <param name="stream">The stream to read</param>
        /// <param name="buf">The buffer to read into</param>
        /// <param name="count">The amout of bytes to read</param>
        /// <returns>The actual number of bytes read</returns>
        public static int ForceStreamRead(System.IO.Stream stream, byte[] buf, int count)
        {
            int a;
            int index = 0;
            do
            {
                a = stream.Read(buf, index, count);
                index += a;
                count -= a;
            }
            while (a != 0 && count > 0);

            return index;
        }

        /// <summary>
        /// Compares two streams to see if they are binary equals
        /// </summary>
        /// <param name="stream1">One stream</param>
        /// <param name="stream2">Another stream</param>
        /// <param name="checkLength">True if the length of the two streams should be compared</param>
        /// <returns>True if they are equal, false otherwise</returns>
        public static bool CompareStreams(System.IO.Stream stream1, System.IO.Stream stream2, bool checkLength)
        {
            if (checkLength)
            {
                try
                {
                    if (stream1.Length != stream2.Length)
                        return false;
                }
                catch
                {
                    //We must read along, trying to determine if they are equals
                }
            }

            int longSize = BitConverter.GetBytes((long)0).Length;
            byte[] buf1 = new byte[longSize * 512];
            byte[] buf2 = new byte[buf1.Length];

            int a1, a2;
            while ((a1 = ForceStreamRead(stream1, buf1, buf1.Length)) == (a2 = ForceStreamRead(stream2, buf2, buf2.Length)))
            {
                int ix = 0;
                for (int i = 0; i < a1 / longSize; i++)
                    if (BitConverter.ToUInt64(buf1, ix) != BitConverter.ToUInt64(buf2, ix))
                        return false;
                    else
                        ix += longSize;

                for (int i = 0; i < a1 % longSize; i++)
                    if (buf1[ix] != buf2[ix])
                        return false;
                    else
                        ix++;

                if (a1 == 0)
                    break;
            }

            return a1 == a2;
        }

        /// <summary>
        /// Removes an entire folder, and its contents.
        /// Equal to System.IO.Directory.Delete
        /// </summary>
        /// <param name="path">The folder to remove</param>
        public static void DeleteFolder(string path)
        {
            if (!System.IO.Directory.Exists(path))
                return;

            foreach (string s in EnumerateFiles(path))
            {
                System.IO.File.SetAttributes(s, System.IO.FileAttributes.Normal);
                System.IO.File.Delete(s);
            }
            
            List<string> folders = Utility.EnumerateFolders(path);
            folders.Sort();
            folders.Reverse();

            foreach (string s in folders)
                System.IO.Directory.Delete(s);

            System.IO.Directory.Delete(path);
        }

        /// <summary>
        /// Calculates the SHA256 hash of a given file, and returns the results as an base64 encoded string
        /// </summary>
        /// <param name="path">The path to the file to calculate the hash for</param>
        /// <returns>The base64 encoded SHA256 hash</returns>
        public static string CalculateHash(string path)
        {
            System.Security.Cryptography.HashAlgorithm sha = System.Security.Cryptography.HashAlgorithm.Create("SHA256");
            using (System.IO.FileStream fs = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                return Convert.ToBase64String(sha.ComputeHash(fs));
        }


        /// <summary>
        /// Formats a size into a human readable format, eg. 2048 becomes &quot;2 KB&quot;.
        /// </summary>
        /// <param name="size">The size to format</param>
        /// <returns>A human readable string representing the size</returns>
        public static string FormatSizeString(long size)
        {
            if (size > 1024 * 1024 * 1024)
                return string.Format("{0:N} GB", (double)size / (1024 * 1024 * 1024));
            else if (size > 1024 * 1024)
                return string.Format("{0:N} MB", (double)size / (1024 * 1024));
            else if (size > 1024)
                return string.Format("{0:N} KB", (double)size / 1024);
            else
                return string.Format("{0} bytes", size);
        }

        public static System.Threading.ThreadPriority ParsePriority(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
                return System.Threading.ThreadPriority.Normal;

            switch (value.ToLower().Trim())
            {
                case "+2":
                case "high":
                case "highest":
                    return System.Threading.ThreadPriority.Highest;
                case "+1":
                case "abovenormal":
                case "above normal":
                    return System.Threading.ThreadPriority.AboveNormal;

                case "-1":
                case "belownormal":
                case "below normal":
                    return System.Threading.ThreadPriority.BelowNormal;
                case "-2":
                case "low":
                case "lowest":
                case "idle":
                    return System.Threading.ThreadPriority.Lowest;

                default:
                    return System.Threading.ThreadPriority.Normal;
            }
        }
    }
}
