// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Common;
using System.Globalization;
using System.Threading;

namespace Duplicati.Library.Utility
{
    public static class Utility
    {
        /// <summary>
        /// Size of buffers for copying stream
        /// </summary>
        public static long DEFAULT_BUFFER_SIZE => SystemContextSettings.Buffersize;

        /// <summary>
        /// A cache of the FileSystemCaseSensitive property, which is computed upon the first access.
        /// </summary>
        private static bool? CachedIsFSCaseSensitive;

        /// <summary>
        /// Gets the hash algorithm used for calculating a hash
        /// </summary>
        public static string HashAlgorithm => "SHA256";

        /// <summary>
        /// The EPOCH offset (unix style)
        /// </summary>
        public static readonly DateTime EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The attribute value used to indicate error
        /// </summary>
        public const FileAttributes ATTRIBUTE_ERROR = (FileAttributes)(1 << 30);

        /// <summary>
        /// The callback delegate type used to collecting file information
        /// </summary>
        /// <param name="rootpath">The path that the file enumeration started at</param>
        /// <param name="path">The current element</param>
        /// <param name="attributes">The attributes of the element</param>
        /// <returns>A value indicating if the folder should be recursed, ignored for other types</returns>
        public delegate bool EnumerationFilterDelegate(string rootpath, string path, FileAttributes attributes);

        /// <summary>
        /// Copies the content of one stream into another
        /// </summary>
        /// <param name="source">The stream to read from</param>
        /// <param name="target">The stream to write to</param>
        public static long CopyStream(Stream source, Stream target)
        {
            return CopyStream(source, target, true);
        }

        /// <summary>
        /// Copies the content of one stream into another
        /// </summary>
        /// <param name="source">The stream to read from</param>
        /// <param name="target">The stream to write to</param>
        /// <param name="tryRewindSource">True if an attempt should be made to rewind the source stream, false otherwise</param>
        /// <param name="buf">Temporary buffer to use (optional)</param>
        public static long CopyStream(Stream source, Stream target, bool tryRewindSource, byte[] buf = null)
        {
            if (tryRewindSource && source.CanSeek)
                try { source.Position = 0; }
                catch
                {
                    // ignored
                }

            buf = buf ?? new byte[DEFAULT_BUFFER_SIZE];

            int read;
			long total = 0;
			while ((read = source.Read(buf, 0, buf.Length)) != 0)
			{
				target.Write(buf, 0, read);
				total += read;
			}

			return total;
        }

        /// <summary>
        /// Copies the content of one stream into another
        /// </summary>
        /// <param name="source">The stream to read from</param>
        /// <param name="target">The stream to write to</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        public static async Task<long> CopyStreamAsync(Stream source, Stream target, CancellationToken cancelToken)
        {
            return await CopyStreamAsync(source, target, tryRewindSource: true, cancelToken: cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies the content of one stream into another
        /// </summary>
        /// <param name="source">The stream to read from</param>
        /// <param name="target">The stream to write to</param>
        /// <param name="tryRewindSource">True if an attempt should be made to rewind the source stream, false otherwise</param>
        /// <param name="cancelToken">Token to cancel the operation.</param>
        /// <param name="buf">Temporary buffer to use (optional)</param>
        public static async Task<long> CopyStreamAsync(Stream source, Stream target, bool tryRewindSource, CancellationToken cancelToken, byte[] buf = null)
        {
            if (tryRewindSource && source.CanSeek)
                try { source.Position = 0; }
                catch {}

            buf = buf ?? new byte[DEFAULT_BUFFER_SIZE];

            int read;
            long total = 0;
            while (true)
            {
                read = await source.ReadAsync(buf, 0, buf.Length, cancelToken).ConfigureAwait(false);
                if (read == 0) break;
                await target.WriteAsync(buf, 0, read, cancelToken).ConfigureAwait(false);
                total += read;
            }

            return total;
        }

        /// <summary>
        /// These are characters that must be escaped when using a globbing expression
        /// </summary>
        private static readonly string BADCHARS = @"\\|\+|\||\{|\[|\(|\)|\]|\}|\^|\$|\#|\.";

        /// <summary>
        /// Most people will probably want to use fileglobbing, but RegExp's are more flexible.
        /// By converting from the weak globbing to the stronger regexp, we support both.
        /// </summary>
        /// <param name="globexp"></param>
        /// <returns></returns>
        public static string ConvertGlobbingToRegExp(string globexp)
        {
            //First escape all special characters
            globexp = Regex.Replace(globexp, BADCHARS, @"\$&");

            //Replace the globbing expressions with the corresponding regular expressions
            globexp = globexp.Replace('?', '.').Replace("*", ".*");
            return globexp;
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <returns>A list of the full filenames</returns>
        public static IEnumerable<string> EnumerateFiles(string basepath)
        {
            return EnumerateFileSystemEntries(basepath).Where(x => !x.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));
        }

        /// <summary>
        /// Returns a list of folder names found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <returns>A list of the full paths</returns>
        public static IEnumerable<string> EnumerateFolders(string basepath)
        {
            return EnumerateFileSystemEntries(basepath).Where(x => x.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal));
        }

        /// <summary>
        /// Returns a list of all files and subfolders found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in.</param>
        /// <returns>A list of the full filenames and foldernames. Foldernames ends with the directoryseparator char</returns>
        public static IEnumerable<string> EnumerateFileSystemEntries(string basepath)
        {
            return EnumerateFileSystemEntries(basepath, (rootpath, path, attributes) => true, SystemIO.IO_OS.GetDirectories, Directory.GetFiles, null);
        }

        /// <summary>
        /// A callback delegate used for applying alternate enumeration of filesystems
        /// </summary>
        /// <param name="path">The path to return data from</param>
        /// <returns>A list of paths</returns>
        public delegate string[] FileSystemInteraction(string path);

        /// <summary>
        /// A callback delegate used for extracting attributes from a file or folder
        /// </summary>
        /// <param name="path">The path to return data from</param>
        /// <returns>Attributes for the file or folder</returns>
        public delegate FileAttributes ExtractFileAttributes(string path);

        /// <summary>
        /// A callback delegate used for extracting attributes from a file or folder
        /// </summary>
        /// <param name="rootpath">The root folder where the path was found</param>
        /// <param name="path">The path that produced the error</param>
        /// <param name="ex">The exception for the error</param>
        public delegate void ReportAccessError(string rootpath, string path, Exception ex);

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="rootpath">The folder to look in</param>
        /// <param name="callback">The function to call with the filenames</param>
        /// <param name="folderList">A function to call that lists all folders in the supplied folder</param>
        /// <param name="fileList">A function to call that lists all files in the supplied folder</param>
        /// <param name="attributeReader">A function to call that obtains the attributes for an element, set to null to avoid reading attributes</param>
        /// <param name="errorCallback">An optional function to call with error messages.</param>
        /// <returns>A list of the full filenames</returns>
        public static IEnumerable<string> EnumerateFileSystemEntries(string rootpath, EnumerationFilterDelegate callback, FileSystemInteraction folderList, FileSystemInteraction fileList, ExtractFileAttributes attributeReader, ReportAccessError errorCallback = null)
        {
            var lst = new Stack<string>();

            if (IsFolder(rootpath, attributeReader))
            {
                rootpath = Util.AppendDirSeparator(rootpath);
                try
                {
                    var attr = attributeReader?.Invoke(rootpath) ?? FileAttributes.Directory;
                    if (callback(rootpath, rootpath, attr))
                        lst.Push(rootpath);
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errorCallback?.Invoke(rootpath, rootpath, ex);
                    callback(rootpath, rootpath, FileAttributes.Directory | ATTRIBUTE_ERROR);
                }

                while (lst.Count > 0)
                {
                    var f = lst.Pop();

                    yield return f;

                    try
                    {
                        foreach (var s in folderList(f))
                        {
                            var sf = Util.AppendDirSeparator(s);
                            try
                            {
                                var attr = attributeReader?.Invoke(sf) ?? FileAttributes.Directory;
                                if (callback(rootpath, sf, attr))
                                    lst.Push(sf);
                            }
                            catch (System.Threading.ThreadAbortException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                errorCallback?.Invoke(rootpath, sf, ex);
                                callback(rootpath, sf, FileAttributes.Directory | ATTRIBUTE_ERROR);
                            }
                        }
                    }
                    catch (System.Threading.ThreadAbortException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        errorCallback?.Invoke(rootpath, f, ex);
                        callback(rootpath, f, FileAttributes.Directory | ATTRIBUTE_ERROR);
                    }

                    string[] files = null;
                    if (fileList != null)
                    {
                        try
                        {
                            files = fileList(f);
                        }
                        catch (System.Threading.ThreadAbortException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            errorCallback?.Invoke(rootpath, f, ex);
                            callback(rootpath, f, FileAttributes.Directory | ATTRIBUTE_ERROR);
                        }
                    }

                    if (files != null)
                    {
                        foreach (var s in files)
                        {
                            try
                            {
                                var attr = attributeReader?.Invoke(s) ?? FileAttributes.Normal;
                                if (!callback(rootpath, s, attr))
                                    continue;
                            }
                            catch (System.Threading.ThreadAbortException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                errorCallback?.Invoke(rootpath, s, ex);
                                callback(rootpath, s, ATTRIBUTE_ERROR);
                                continue;
                            }
                            yield return s;
                        }
                    }
                }
            }
            else
            {
                try
                {
                    var attr = attributeReader?.Invoke(rootpath) ?? FileAttributes.Normal;
                    if (!callback(rootpath, rootpath, attr))
                        yield break;
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errorCallback?.Invoke(rootpath, rootpath, ex);
                    callback(rootpath, rootpath, ATTRIBUTE_ERROR);
                    yield break;
                }

                yield return rootpath;
            }
        }

        /// <summary>
        /// Test if specified path is a folder
        /// </summary>
        /// <param name="path">Path to test</param>
        /// <param name="attributeReader">Function to use for testing path</param>
        /// <returns>True if path is refers to a folder</returns>
        public static bool IsFolder(string path, ExtractFileAttributes attributeReader)
        {
            if (attributeReader == null)
                return true;

            try
            {
                return attributeReader(path).HasFlag(FileAttributes.Directory);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tests if path refers to a file, or folder, <b>below</b> the parent folder
        /// </summary>
        /// <param name="fileOrFolderPath">File or folder to test</param>
        /// <param name="parentFolder">Candidate parent folder</param>
        /// <returns>True if below parent folder, false otherwise
        /// (note that this returns false if the two argument paths are identical!)</returns>
        public static bool IsPathBelowFolder(string fileOrFolderPath, string parentFolder)
        {
            var sanitizedParentFolder = Util.AppendDirSeparator(parentFolder);
            return fileOrFolderPath.StartsWith(sanitizedParentFolder, ClientFilenameStringComparison) && 
                   !fileOrFolderPath.Equals(sanitizedParentFolder, ClientFilenameStringComparison);
        }

        /// <summary>
        /// Returns parent folder of path
        /// </summary>
        /// <param name="path">Full file or folder path</param>
        /// <param name="forceTrailingDirectorySeparator">If true, return value always has trailing separator</param>
        /// <returns>Parent folder of path (containing folder for file paths, parent folder for folder paths)</returns>
        public static string GetParent(string path, bool forceTrailingDirectorySeparator)
        {
            var len = path.Length - 1;
            if (len > 1 && path[len] == Path.DirectorySeparatorChar)
            {
                len--;
            }

            var last = path.LastIndexOf(Path.DirectorySeparatorChar, len);
            if (last == -1 || last == 0 && len == 0)
                return null;
            
            if (last == 0 && !Platform.IsClientWindows)
                return Util.DirectorySeparatorString;

            var parent = path.Substring(0, last);

            if (forceTrailingDirectorySeparator ||
                Platform.IsClientWindows && parent.Length == 2 && parent[1] == ':' && char.IsLetter(parent[0]))
            {
                parent += Path.DirectorySeparatorChar;
            }

            return parent;
        }

        

        /// <summary>
        /// Given a collection of unique folders, returns only parent-most folders
        /// </summary>
        /// <param name="folders">Collection of unique folders</param>
        /// <returns>Parent-most folders of input collection</returns>
        public static IEnumerable<string> SimplifyFolderList(ICollection<string> folders)
        {
            if (!folders.Any())
                return folders;

            var result = new LinkedList<string>();
            result.AddFirst(folders.First());

            foreach (var folder1 in folders)
            {
                bool addFolder = true;
                LinkedListNode<string> next;
                for (var node = result.First; node != null; node = next)
                {
                    next = node.Next;
                    var folder2 = node.Value;

                    if (IsPathBelowFolder(folder1, folder2))
                    {
                        // higher-level folder already present
                        addFolder = false;
                        break;
                    }

                    if (IsPathBelowFolder(folder2, folder1))
                    {
                        // retain folder1
                        result.Remove(node);
                    }
                }

                if (addFolder)
                {
                    result.AddFirst(folder1);
                }
            }

            return result.Distinct();
        }
        
        /// <summary>
        /// Given a collection of file paths, return those NOT contained within specified collection of folders
        /// </summary>
        /// <param name="files">Collection of files to filter</param>
        /// <param name="folders">Collection of folders to use as filter</param>
        /// <returns>Files not in any of specified <c>folders</c></returns>
        public static IEnumerable<string> GetFilesNotInFolders(IEnumerable<string> files, IEnumerable<string> folders)
        {
            return files.Where(x => folders.All(folder => !IsPathBelowFolder(x, folder)));
        }

        /// <summary>
        /// Calculates the size of files in a given folder
        /// </summary>
        /// <param name="folder">The folder to examine</param>
        /// <returns>The combined size of all files that match the filter</returns>
        public static long GetDirectorySize(string folder)
        {
            return EnumerateFolders(folder).Sum((path) => new FileInfo(path).Length);
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
        public static int ForceStreamRead(Stream stream, byte[] buf, int count)
        {
            int a;
            int index = 0;
            do
            {
                a = stream.Read(buf, index, count);
                index += a;
                count -= a;
            } while (a != 0 && count > 0);

            return index;
        }

        /// <summary>
        /// Some streams can return a number that is less than the requested number of bytes.
        /// This is usually due to fragmentation, and is solved by issuing a new read.
        /// This function wraps that functionality.
        /// </summary>
        /// <param name="stream">The stream to read.</param>
        /// <param name="buf">The buffer to read into.</param>
        /// <param name="count">The amout of bytes to read.</param>
        /// <returns>The number of bytes read</returns>
        public static async Task<int> ForceStreamReadAsync(this System.IO.Stream stream, byte[] buf, int count)
        {
            int a;
            int index = 0;
            do
            {
                a = await stream.ReadAsync(buf, index, count).ConfigureAwait(false);
                index += a;
                count -= a;
            } while (a != 0 && count > 0);

            return index;
        }

        /// <summary>
        /// Compares two streams to see if they are binary equals
        /// </summary>
        /// <param name="stream1">One stream</param>
        /// <param name="stream2">Another stream</param>
        /// <param name="checkLength">True if the length of the two streams should be compared</param>
        /// <returns>True if they are equal, false otherwise</returns>
        public static bool CompareStreams(Stream stream1, Stream stream2, bool checkLength)
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
        /// Calculates the hash of a given stream, and returns the results as an base64 encoded string
        /// </summary>
        /// <param name="stream">The stream to calculate the hash for</param>
        /// <returns>The base64 encoded hash</returns>
        public static string CalculateHash(Stream stream)
        {
            return Convert.ToBase64String(HashAlgorithmHelper.Create(HashAlgorithm).ComputeHash(stream));
        }

        /// <summary>
        /// Reads a file, attempts to detect encoding
        /// </summary>
        /// <param name="filename">The path to the file to read</param>
        /// <returns>The file contents</returns>
        public static string ReadFileWithDefaultEncoding(string filename)
        {
            // Since StreamReader defaults to UTF8 and most text files will NOT be UTF8 without BOM,
            // we need to detect the encoding (at least that it's not UTF8).
            // So we read the first 4096 bytes and try to decode them as UTF8. 
            var buffer = new byte[4096];
            using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Utility.ForceStreamRead(file, buffer, 4096);
            }

            var enc = Encoding.UTF8;
            try
            {
                // this will throw an error if not really UTF8
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                new UTF8Encoding(false, true).GetString(buffer);
            }
            catch (Exception)
            {
                enc = Encoding.Default;
            }

            // This will load the text using the BOM, or the detected encoding if no BOM.
            using (var reader = new StreamReader(filename, enc, true))
            {
                // Remove all \r from the file and split on \n, then pass directly to ExtractOptions
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Formats a size into a human readable format, eg. 2048 becomes &quot;2 KB&quot; or -2283 becomes &quot;-2.23 KB%quot.
        /// </summary>
        /// <param name="size">The size to format</param>
        /// <returns>A human readable string representing the size</returns>
        public static string FormatSizeString(double size)
        {
            double sizeAbs = Math.Abs(size);  // Allow formatting of negative sizes
            if (sizeAbs >= 1024 * 1024 * 1024 * 1024L)
                return Strings.Utility.FormatStringTB(size / (1024 * 1024 * 1024 * 1024L));
            else if (sizeAbs >= 1024 * 1024 * 1024)
                return Strings.Utility.FormatStringGB(size / (1024 * 1024 * 1024));
            else if (sizeAbs >= 1024 * 1024)
                return Strings.Utility.FormatStringMB(size / (1024 * 1024));
            else if (sizeAbs >= 1024)
                return Strings.Utility.FormatStringKB(size / 1024);
            else
                return Strings.Utility.FormatStringB((long) size); // safe to cast because lower than 1024 and thus well within range of long
        }

        public static System.Threading.ThreadPriority ParsePriority(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
                return System.Threading.ThreadPriority.Normal;

            switch (value.ToLower(CultureInfo.InvariantCulture).Trim())
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

        /// <summary>
        /// Parses a string into a boolean value.
        /// </summary>
        /// <param name="value">The value to parse.</param>
        /// <param name="defaultFunc">A delegate that returns the default value if <paramref name="value"/> is not a valid boolean value.</param>
        /// <returns>The parsed value, or the value returned by <paramref name="defaultFunc"/>.</returns>
        public static bool ParseBool(string value, Func<bool> defaultFunc)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return defaultFunc();
            }

            switch (value.Trim().ToLower(CultureInfo.InvariantCulture))
            {
                case "1":
                case "on":
                case "true":
                case "yes":
                    return true;
                case "0":
                case "off":
                case "false":
                case "no":
                    return false;
                default:
                    return defaultFunc();
            }
        }

        /// <summary>
        /// Parses a string into a boolean value.
        /// </summary>
        /// <param name="value">The value to parse.</param>
        /// <param name="default">The default value, in case <paramref name="value"/> is not a valid boolean value.</param>
        /// <returns>The parsed value, or the default value.</returns>
        public static bool ParseBool(string value, bool @default)
        {
            return ParseBool(value, () => @default);
        }

        /// <summary>
        /// Parses an option from the option set, using the convention that if the option is set, it is true unless it parses to false, and false otherwise
        /// </summary>
        /// <param name="options">The set of options to look for the setting in</param>
        /// <param name="value">The value to look for in the settings</param>
        /// <returns></returns>
        public static bool ParseBoolOption(IDictionary<string, string> options, string value)
        {
            string opt;
            if (options.TryGetValue(value, out opt))
                return ParseBool(opt, true);
            else
                return false;
        }

        /// <summary>
        /// Parses an enum found in the options dictionary
        /// </summary>
        /// <returns>The parsed or default enum value.</returns>
        /// <param name="options">The set of options to look for the setting in</param>
        /// <param name="value">The value to look for in the settings</param>
        /// <param name="default">The default value to return if there are no matches.</param>
        /// <typeparam name="T">The enum type parameter.</typeparam>
        public static T ParseEnumOption<T>(IDictionary<string, string> options, string value, T @default)
        {
            return options.TryGetValue(value, out var opt) ? ParseEnum(opt, @default) : @default;
        }

        /// <summary>
        /// Attempts to parse an enum with case-insensitive lookup, returning the default value if there was no match
        /// </summary>
        /// <returns>The parsed or default enum value.</returns>
        /// <param name="value">The string to parse.</param>
        /// <param name="default">The default value to return if there are no matches.</param>
        /// <typeparam name="T">The enum type parameter.</typeparam>
        public static T ParseEnum<T>(string value, T @default)
        {
            foreach (var s in Enum.GetNames(typeof(T)))
                if (s.Equals(value, StringComparison.OrdinalIgnoreCase))
                    return (T)Enum.Parse(typeof(T), s);

            return @default;
        }

        /// <summary>
        /// Converts a sequence of bytes to a hex string
        /// </summary>
        /// <returns>The array as hex string.</returns>
        /// <param name="data">The data to convert</param>
        public static string ByteArrayAsHexString(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", string.Empty);
        }

        /// <summary>
        /// Converts a hex string to a byte array
        /// </summary>
        /// <returns>The string as byte array.</returns>
        /// <param name="hex">The hex string</param>
        /// <param name="data">The parsed data</param>
        public static void HexStringAsByteArray(string hex, byte[] data)
        {
            for (var i = 0; i < hex.Length; i += 2)
                data[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }

        public static bool Which(string appname)
        {
            if (!Platform.IsClientPosix)
                return false;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("which", appname)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    UseShellExecute = false
                };

                var pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(5000);
                if (pi.HasExited)
                    return pi.ExitCode == 0;
                else
                    return false;
            }
            catch
            {
            }

            return false;
        }


        /// <value>
        /// Returns a value indicating if the filesystem, is case sensitive 
        /// </value>
        public static bool IsFSCaseSensitive
        {
            get
            {
                if (!CachedIsFSCaseSensitive.HasValue)
                {
                    var str = Environment.GetEnvironmentVariable("FILESYSTEM_CASE_SENSITIVE");

                    // TODO: This should probably be determined by filesystem rather than OS,
                    // OSX can actually have the disks formated as Case Sensitive, but insensitive is default
                    CachedIsFSCaseSensitive = ParseBool(str, () => Platform.IsClientPosix && !Platform.IsClientOSX);
                }

                return CachedIsFSCaseSensitive.Value;
            }
        }

        /// <summary>
        /// Returns a value indicating if the app is running under Mono
        /// </summary>
        public static bool IsMono => Type.GetType("Mono.Runtime") != null;

        /// <summary>
        /// Gets the current Mono runtime version, will return 0.0 if not running Mono
        /// </summary>
        public static Version MonoVersion
        {
            get
            {
                try
                {
                    var v = MonoDisplayVersion;
                    if (v != null)
                    {
                        var regex = new Regex(@"\d+\.\d+(\.\d+)?(\.\d+)?");
                        var match = regex.Match(v);
                        if (match.Success)
                            return new Version(match.Value);
                    }
                }
                catch
                {
                    // ignored
                }

                return new Version();
            }
        }

        /// <summary>
        /// Gets the Mono display version, or null if not running Mono
        /// </summary>
        public static string MonoDisplayVersion
        {
            get
            {
                try
                {
                    var t = Type.GetType("Mono.Runtime");
                    if (t != null)
                    {
                        var mi = t.GetMethod("GetDisplayName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                        if (mi != null)
                            return (string)mi.Invoke(null, null);
                    }
                }
                catch
                {
                    // ignored
                }

                return null;
            }
        }

        /// <summary>
        /// Gets a string comparer that matches the client filesystems case sensitivity
        /// </summary>
        public static StringComparer ClientFilenameStringComparer => IsFSCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// Gets the string comparision that matches the client filesystems case sensitivity
        /// </summary>
        public static StringComparison ClientFilenameStringComparison => IsFSCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        /// <summary>
        /// The path to the users home directory
        /// </summary>
        public static readonly string HOME_PATH = Environment.GetFolderPath(Platform.IsClientPosix ? Environment.SpecialFolder.Personal : Environment.SpecialFolder.UserProfile);

        /// <summary>
        /// Regexp for matching environment variables on Windows (%VAR%)
        /// </summary>
        private static readonly Regex ENVIRONMENT_VARIABLE_MATCHER_WINDOWS = new Regex(@"\%(?<name>\w+)\%");

        /// <summary>
        /// Expands environment variables in a RegExp safe format
        /// </summary>
        /// <returns>The expanded string.</returns>
        /// <param name="str">The string to expand.</param>
        /// <param name="lookup">A lookup method that converts an environment key to an expanded string</param>
        public static string ExpandEnvironmentVariablesRegexp(string str, Func<string, string> lookup = null)
        {
            if (lookup == null)
                lookup = Environment.GetEnvironmentVariable;

            return

                // TODO: Should we switch to using the native format ($VAR or ${VAR}), instead of following the Windows scheme?
                // IsClientLinux ? new Regex(@"\$(?<name>\w+)|(\{(?<name>[^\}]+)\})") : ENVIRONMENT_VARIABLE_MATCHER_WINDOWS

                ENVIRONMENT_VARIABLE_MATCHER_WINDOWS.Replace(str, m => Regex.Escape(lookup(m.Groups["name"].Value)));
        }
        
        /// <summary>
        /// Normalizes a DateTime instance by converting to UTC and flooring to seconds.
        /// </summary>
        /// <returns>The normalized date time</returns>
        /// <param name="input">The input time</param>
        public static DateTime NormalizeDateTime(DateTime input)
        {
            var ticks = input.ToUniversalTime().Ticks;
            ticks -= ticks % TimeSpan.TicksPerSecond;
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        
        public static long NormalizeDateTimeToEpochSeconds(DateTime input)
        {
            return (long) Math.Floor((NormalizeDateTime(input) - EPOCH).TotalSeconds);
        }
        
        /// <summary>
        /// The format string for a DateTime
        /// </summary>
        //Note: Actually the K should be Z which is more correct as it is forced to be Z, but Z as a format specifier is fairly undocumented
        public static string SERIALIZED_DATE_TIME_FORMAT = "yyyyMMdd'T'HHmmssK";

        /// <summary>
        /// Returns a string representation of a <see cref="System.DateTime"/> in UTC format
        /// </summary>
        /// <param name="dt">The <see cref="System.DateTime"/> instance</param>
        /// <returns>A string representing the time</returns>
        public static string SerializeDateTime(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(SERIALIZED_DATE_TIME_FORMAT, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses a serialized <see cref="System.DateTime"/> instance
        /// </summary>
        /// <param name="str">The string to parse</param>
        /// <returns>The parsed <see cref="System.DateTime"/> instance</returns>
        public static bool TryDeserializeDateTime(string str, out DateTime dt)
        {
            return DateTime.TryParseExact(str, SERIALIZED_DATE_TIME_FORMAT, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out dt);
        }

        /// <summary>
        /// Parses a serialized <see cref="System.DateTime"/> instance
        /// </summary>
        /// <param name="str">The string to parse</param>
        /// <returns>The parsed <see cref="System.DateTime"/> instance</returns>
        public static DateTime DeserializeDateTime(string str)
        {
            if (!TryDeserializeDateTime(str, out var dt))
                throw new Exception(Strings.Utility.InvalidDateError(str));

            return dt;
        }

        /// <summary>
        /// Gets the unique items from a collection.
        /// </summary>
        /// <typeparam name="T">The type of the elements in <paramref name="collection"/>.</typeparam>
        /// <param name="collection">The collection to remove duplicate items from.</param>
        /// <param name="duplicateItems">The duplicate items in <paramref name="collection"/>.</param>
        /// <returns>The unique items from <paramref name="collection"/>.</returns>
        public static ISet<T> GetUniqueItems<T>(IEnumerable<T> collection, out ISet<T> duplicateItems)
        {
            return GetUniqueItems(collection, EqualityComparer<T>.Default, out duplicateItems);
        }

        /// <summary>
        /// Gets the unique items from a collection.
        /// </summary>
        /// <typeparam name="T">The type of the elements in <paramref name="collection"/>.</typeparam>
        /// <param name="collection">The collection to remove duplicate items from.</param>
        /// <param name="comparer">The <see cref="System.Collections.Generic.IEqualityComparer{T}"/> implementation to use when comparing values in the collection.</param>
        /// <param name="duplicateItems">The duplicate items in <paramref name="collection"/>.</param>
        /// <returns>The unique items from <paramref name="collection"/>.</returns>
        public static ISet<T> GetUniqueItems<T>(IEnumerable<T> collection, IEqualityComparer<T> comparer, out ISet<T> duplicateItems)
        {
            var uniqueItems = new HashSet<T>(comparer);
            duplicateItems = new HashSet<T>(comparer);

            foreach (var item in collection)
            {
                if (!uniqueItems.Add(item))
                    duplicateItems.Add(item);
            }

            return uniqueItems;
        }

        // <summary>
        // Returns the entry assembly or reasonable approximation if no entry assembly is available.
        // This is the case in NUnit tests.  The following approach does not work w/ Mono due to unimplemented members:
        // http://social.msdn.microsoft.com/Forums/nb-NO/clr/thread/db44fe1a-3bb4-41d4-a0e0-f3021f30e56f
        // so this layer of indirection is necessary
        // </summary>
        // <returns>entry assembly or reasonable approximation</returns>
        public static System.Reflection.Assembly getEntryAssembly()
        {
            return System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly();
        }

        /// <summary>
        /// Converts a Base64 encoded string to &quot;base64 for url&quot;
        /// See https://en.wikipedia.org/wiki/Base64#URL_applications
        /// </summary>
        /// <param name="data">The base64 encoded string</param>
        /// <returns>The base64 for url encoded string</returns>
        public static string Base64PlainToBase64Url(string data)
        {
            return data.Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Converts a &quot;base64 for url&quot; encoded string to a Base64 encoded string.
        /// See https://en.wikipedia.org/wiki/Base64#URL_applications
        /// </summary>
        /// <param name="data">The base64 for url encoded string</param>
        /// <returns>The base64 encoded string</returns>
        public static string Base64UrlToBase64Plain(string data)
        {
            return data.Replace('-', '+').Replace('_', '/');
        }

        /// <summary>
        /// Encodes a byte array into a &quot;base64 for url&quot; encoded string.
        /// See https://en.wikipedia.org/wiki/Base64#URL_applications
        /// </summary>
        /// <param name="data">The data to encode</param>
        /// <returns>The base64 for url encoded string</returns>
        public static string Base64UrlEncode(byte[] data)
        {
            return Base64PlainToBase64Url(Convert.ToBase64String(data));
        }

        /// <summary>
        /// Converts a DateTime instance to a Unix timestamp
        /// </summary>
        /// <returns>The Unix timestamp.</returns>
        /// <param name="input">The DateTime instance to convert.</param>
        public static long ToUnixTimestamp(DateTime input)
        {
            var ticks = input.ToUniversalTime().Ticks;
            ticks -= ticks % TimeSpan.TicksPerSecond;
            input = new DateTime(ticks, DateTimeKind.Utc);

            return (long)Math.Floor((input - EPOCH).TotalSeconds);
        }

        /// <summary>
        /// Returns a value indicating if the given type should be treated as a primitive
        /// </summary>
        /// <returns><c>true</c>, if type is primitive for serialization, <c>false</c> otherwise.</returns>
        /// <param name="t">The type to check.</param>
        private static bool IsPrimitiveTypeForSerialization(Type t)
        {
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(DateTime) || t == typeof(TimeSpan);
        }

        /// <summary>
        /// Writes a primitive to the output, or returns false if the input is not primitive
        /// </summary>
        /// <returns><c>true</c>, the item was printed, <c>false</c> otherwise.</returns>
        /// <param name="item">The item to write.</param>
        /// <param name="writer">The target writer.</param>
        private static bool PrintSerializeIfPrimitive(object item, TextWriter writer)
        {
            if (item == null)
            {
                writer.Write("null");
                return true;
            }

            if (IsPrimitiveTypeForSerialization(item.GetType()))
            {
                if (item is DateTime)
                {
                    writer.Write(((DateTime)item).ToLocalTime());
                    writer.Write(" (");
                    writer.Write(ToUnixTimestamp((DateTime)item));
                    writer.Write(")");
                }
                else
                    writer.Write(item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Prints the object to a stream, which can be used for display or logging
        /// </summary>
        /// <returns>The serialized object</returns>
        /// <param name="item">The object to serialize</param>
        /// <param name="writer">The writer to write the results to</param>
        /// <param name="filter">A filter applied to properties to decide if they are omitted or not</param>
        /// <param name="recurseobjects">A value indicating if non-primitive values are recursed</param>
        /// <param name="indentation">The string indentation</param>
        /// <param name="visited">A lookup table with visited objects, used to avoid inifinite recursion</param>
        /// <param name="collectionlimit">The maximum number of items to report from an IEnumerable instance</param>
        public static void PrintSerializeObject(object item, TextWriter writer, Func<System.Reflection.PropertyInfo, object, bool> filter = null, bool recurseobjects = false, int indentation = 0, int collectionlimit = 0, Dictionary<object, object> visited = null)
        {
            visited = visited ?? new Dictionary<object, object>();
            var indentstring = new string(' ', indentation);

            var first = true;


            if (item == null || IsPrimitiveTypeForSerialization(item.GetType()))
            {
                writer.Write(indentstring);
                if (PrintSerializeIfPrimitive(item, writer))
                    return;
            }

            foreach (var p in item.GetType().GetProperties())
            {
                if (filter != null && !filter(p, item))
                    continue;

                if (IsPrimitiveTypeForSerialization(p.PropertyType))
                {
                    if (first)
                        first = false;
                    else
                        writer.WriteLine();

                    writer.Write("{0}{1}: ", indentstring, p.Name);
                    PrintSerializeIfPrimitive(p.GetValue(item, null), writer);
                }
                else if (typeof(Task).IsAssignableFrom(p.PropertyType) || p.Name == "TaskReader")
                {
                    // Ignore Task items
                    continue;
                }
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType))
                {
                    var enumerable = (System.Collections.IEnumerable)p.GetValue(item, null);
                    var any = false;
                    if (enumerable != null)
                    {
                        var enumerator = enumerable.GetEnumerator();
                        if (enumerator != null)
                        {
                            var remain = collectionlimit;

                            if (first)
                                first = false;
                            else
                                writer.WriteLine();

                            writer.Write("{0}{1}: [", indentstring, p.Name);
                            if (enumerator.MoveNext())
                            {
                                any = true;
                                writer.WriteLine();
                                PrintSerializeObject(enumerator.Current, writer, filter, recurseobjects, indentation + 4, collectionlimit, visited);

                                remain--;

                                while (enumerator.MoveNext())
                                {
                                    writer.WriteLine(",");

                                    if (remain == 0)
                                    {
                                        writer.Write("...");
                                        break;
                                    }

                                    PrintSerializeObject(enumerator.Current, writer, filter, recurseobjects, indentation + 4, collectionlimit, visited);

                                    remain--;
                                }

                            }

                            if (any)
                            {
                                writer.WriteLine();
                                writer.Write(indentstring);
                            }
                            writer.Write("]");
                        }
                    }
                }
                else if (recurseobjects)
                {
                    var value = p.GetValue(item, null);
                    if (value == null)
                    {
                        if (first)
                            first = false;
                        else
                            writer.WriteLine();
                        writer.Write("{0}{1}: null", indentstring, p.Name);
                    }
                    else if (!visited.ContainsKey(value))
                    {
                        if (first)
                            first = false;
                        else
                            writer.WriteLine();
                        writer.WriteLine("{0}{1}:", indentstring, p.Name);
                        visited[value] = null;
                        PrintSerializeObject(value, writer, filter, recurseobjects, indentation + 4, collectionlimit, visited);
                    }
                }
            }
            writer.Flush();
        }

        /// <summary>
        /// Returns a string representing the object, which can be used for display or logging
        /// </summary>
        /// <returns>The serialized object</returns>
        /// <param name="item">The object to serialize</param>
        /// <param name="filter">A filter applied to properties to decide if they are omitted or not</param>
        /// <param name="recurseobjects">A value indicating if non-primitive values are recursed</param>
        /// <param name="indentation">The string indentation</param>
        /// <param name="collectionlimit">The maximum number of items to report from an IEnumerable instance, set to zero or less for reporting all</param>
        public static StringBuilder PrintSerializeObject(object item, StringBuilder sb = null, Func<System.Reflection.PropertyInfo, object, bool> filter = null, bool recurseobjects = false, int indentation = 0, int collectionlimit = 10)
        {
            sb = sb ?? new StringBuilder();
            using (var sw = new StringWriter(sb))
                PrintSerializeObject(item, sw, filter, recurseobjects, indentation, collectionlimit);
            return sb;
        }

        /// <summary>
        /// Repeatedly hash a value with a salt.
        /// This effectively masks the original value,
        /// and destroys lookup methods, like rainbow tables
        /// </summary>
        /// <param name="data">The data to hash</param>
        /// <param name="salt">The salt to apply</param>
        /// <param name="repeats">The number of times to repeat the hashing</param>
        /// <returns>The salted hash</returns>
        public static byte[] RepeatedHashWithSalt(string data, string salt, int repeats = 1200)
        {
            return RepeatedHashWithSalt(
                Encoding.UTF8.GetBytes(data ?? ""),
                Encoding.UTF8.GetBytes(salt ?? ""),
                repeats);
        }

        /// <summary>
        /// Repeatedly hash a value with a salt.
        /// This effectively masks the original value,
        /// and destroys lookup methods, like rainbow tables
        /// </summary>
        /// <param name="data">The data to hash</param>
        /// <param name="salt">The salt to apply</param>
        /// <returns>The salted hash</returns>
        public static byte[] RepeatedHashWithSalt(byte[] data, byte[] salt, int repeats = 1200)
        {
            // We avoid storing the passphrase directly, 
            // instead we salt and rehash repeatedly
            using (var h = System.Security.Cryptography.SHA256.Create())
            {
                h.Initialize();
                h.TransformBlock(salt, 0, salt.Length, salt, 0);
                h.TransformFinalBlock(data, 0, data.Length);
                var buf = h.Hash;

                for (var i = 0; i < repeats; i++)
                {
                    h.Initialize();
                    h.TransformBlock(salt, 0, salt.Length, salt, 0);
                    h.TransformFinalBlock(buf, 0, buf.Length);
                    buf = h.Hash;
                }

                return buf;
            }
        }

        /// <summary>
        /// Gets the drive letter from the given volume guid.
        /// This method cannot be inlined since the System.Management types are not implemented in Mono
        /// </summary>
        /// <param name="volumeGuid">Volume guid</param>
        /// <returns>Drive letter, as a single character, or null if the volume wasn't found</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static string GetDriveLetterFromVolumeGuid(Guid volumeGuid)
        {
            // Based on this answer:
            // https://stackoverflow.com/questions/10186277/how-to-get-drive-information-by-volume-id
            using (System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher("Select * from Win32_Volume"))
            {
                string targetId = string.Format(@"\\?\Volume{{{0}}}\", volumeGuid);
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    if (string.Equals(obj["DeviceID"].ToString(), targetId, StringComparison.OrdinalIgnoreCase))
                    {
                        object driveLetter = obj["DriveLetter"];
                        if (driveLetter != null)
                        {
                            return obj["DriveLetter"].ToString();
                        }
                        else
                        {
                            // The volume was found, but doesn't have a drive letter associated with it.
                            break;
                        }
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets all volume guids and their associated drive letters.
        /// This method cannot be inlined since the System.Management types are not implemented in Mono
        /// </summary>
        /// <returns>Pairs of drive letter to volume guids</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static IEnumerable<KeyValuePair<string, string>> GetVolumeGuidsAndDriveLetters()
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("Select * from Win32_Volume"))
            {
                foreach (var obj in searcher.Get())
                {
                    var deviceIdObj = obj["DeviceID"];
                    var driveLetterObj = obj["DriveLetter"];
                    if (deviceIdObj != null && driveLetterObj != null)
                    {
                        var deviceId = deviceIdObj.ToString();
                        var driveLetter = driveLetterObj.ToString();
                        if (!string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(driveLetter))
                        {
                            yield return new KeyValuePair<string, string>(driveLetter + @"\", deviceId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The regular expression matching all know non-quoted commandline characters
        /// </summary>
        private static readonly Regex COMMANDLINE_SAFE = new Regex(@"[A-Za-z0-9\-_/:\.]*");
        /// <summary>
        /// Special characters that needs to be escaped on Linux
        /// </summary>
        private static readonly Regex COMMANDLINE_ESCAPED_LINUX = new Regex(@"[""|$|`|\\|!]");

        /// <summary>
        /// Wraps a single argument in quotes suitable for the passing on the commandline
        /// </summary>
        /// <returns>The wrapped commandline element.</returns>
        /// <param name="arg">The argument to wrap.</param>
        /// <param name="allowEnvExpansion">A flag indicating if environment variables are allowed to be expanded</param>
        public static string WrapCommandLineElement(string arg, bool allowEnvExpansion)
        {
            if (string.IsNullOrWhiteSpace(arg))
                return arg;

            if (!Platform.IsClientWindows)
            {
                // We could consider using single quotes that prevents all expansions
                //if (!allowEnvExpansion)
                //    return "'" + arg.Replace("'", "\\'") + "'";

                // Linux is using backslash to escape, except for !
                arg = COMMANDLINE_ESCAPED_LINUX.Replace(arg, (match) =>
                {
                    if (match.Value == "!")
                        return @"""'!'""";

                    if (match.Value == "$" && allowEnvExpansion)
                        return match.Value;

                    return @"\" + match.Value;
                });
            }
            else
            {
                // Windows needs only needs " replaced with "",
                // but is prone to %var% expansion when used in 
                // immediate mode (i.e. from command prompt)
                // Fortunately it does not expand when processes
                // are started from within .Net

                // TODO: I have not found a way to avoid escaping %varname%,
                // and sadly it expands only if the variable exists
                // making it even rarer and harder to diagnose when
                // it happens
                arg = arg.Replace(@"""", @"""""");

                // Also fix the case where the argument ends with a slash
                if (arg[arg.Length - 1] == '\\')
                    arg += @"\";
            }

            // Check that all characters are in the safe set
            if (COMMANDLINE_SAFE.Match(arg).Length != arg.Length)
                return @"""" + arg + @"""";
            else
                return arg;
        }

        /// <summary>
        /// Wrap a set of commandline arguments suitable for the commandline
        /// </summary>
        /// <returns>A commandline string.</returns>
        /// <param name="args">The arguments to create into a commandline.</param>
        /// <param name="allowEnvExpansion">A flag indicating if environment variables are allowed to be expanded</param>
        public static string WrapAsCommandLine(IEnumerable<string> args, bool allowEnvExpansion = false)
        {
            return string.Join(" ", args.Select(x => WrapCommandLineElement(x, allowEnvExpansion)));
        }

        /// <summary>
        /// Utility method that emulates C#'s built in await keyword without requiring the calling method to be async.
        /// This method should be preferred over using Task.Result, as it doesn't wrap singular exceptions in AggregateExceptions.
        /// (It uses Task.GetAwaiter().GetResult(), which is the same thing that await uses under the covers.)
        /// https://stackoverflow.com/questions/17284517/is-task-result-the-same-as-getawaiter-getresult
        /// </summary>
        /// <param name="task">Task to await</param>
        public static void Await(this Task task)
        {
            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Utility method that emulates C#'s built in await keyword without requiring the calling method to be async.
        /// This method should be preferred over using Task.Result, as it doesn't wrap singular exceptions in AggregateExceptions.
        /// (It uses Task.GetAwaiter().GetResult(), which is the same thing that await uses under the covers.)
        /// https://stackoverflow.com/questions/17284517/is-task-result-the-same-as-getawaiter-getresult
        /// </summary>
        /// <typeparam name="T">Result type</typeparam>
        /// <param name="task">Task to await</param>
        /// <returns>Task result</returns>
        public static T Await<T>(this Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
    }
}
