#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
using System.Text.RegularExpressions;
using System.Linq;


#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{
    public static class Utility
    {
        /// <summary>
        /// Size of buffers for copying stream
        /// </summary>
        public static long DEFAULT_BUFFER_SIZE = 64 * 1024;

        /// <summary>
        /// A value indicating if the current process is running in 64bit mode
        /// </summary>
        public static readonly bool Is64BitProcess = IntPtr.Size == 8;

        /// <summary>
        /// Gets the hash algorithm used for calculating a hash
        /// </summary>
        public static string HashAlgorithm { get { return "SHA256"; } }

        /// <summary>
        /// The EPOCH offset (unix style)
        /// </summary>
        public static readonly DateTime EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The attribute value used to indicate error
        /// </summary>
        public const System.IO.FileAttributes ATTRIBUTE_ERROR = (System.IO.FileAttributes)(1 << 30);

        /// <summary>
        /// The callback delegate type used to collecting file information
        /// </summary>
        /// <param name="rootpath">The path that the file enumeration started at</param>
        /// <param name="path">The current element</param>
        /// <param name="attributes">The attributes of the element</param>
        /// <returns>A value indicating if the folder should be recursed, ignored for other types</returns>
        public delegate bool EnumerationFilterDelegate(string rootpath, string path, System.IO.FileAttributes attributes);

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

            byte[] buf = new byte[DEFAULT_BUFFER_SIZE];
            int read;

            while ((read = source.Read(buf, 0, buf.Length)) != 0)
                target.Write(buf, 0, read);
        }

        /// <summary>
        /// These are characters that must be escaped when using a globbing expression
        /// </summary>
        private static readonly string BADCHARS = "\\" + string.Join("|\\", new string[] {
                "\\",
                "+",
                "|",
                "{",
                "[",
                "(",
                ")",
                "]",
                "}",
                "^",
                "$",
                "#",
                "."
            });

        /// <summary>
        /// Most people will probably want to use fileglobbing, but RegExp's are more flexible.
        /// By converting from the weak globbing to the stronger regexp, we support both.
        /// </summary>
        /// <param name="globexp"></param>
        /// <returns></returns>
        public static string ConvertGlobbingToRegExp(string globexp)
        {
            //First escape all special characters
            globexp = Regex.Replace(globexp, BADCHARS, "\\$&");

            //Replace the globbing expressions with the corresponding regular expressions
            globexp = globexp.Replace('?', '.').Replace("*", ".*");
            return globexp;
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <param name="filter">The filter to apply.</param>
        /// <returns>A list of the full filenames</returns>
        public static IEnumerable<string> EnumerateFiles(string basepath)
        {
            return EnumerateFiles(basepath, null);
        }

        /// <summary>
        /// Returns a list of folder names found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <returns>A list of the full paths</returns>
        public static IEnumerable<string> EnumerateFolders(string basepath)
        {
            return EnumerateFolders(basepath, null);
        }

        /// <summary>
        /// Returns a list of all files and subfolders found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in.</param>
        /// <returns>A list of the full filenames and foldernames. Foldernames ends with the directoryseparator char</returns>
        public static IEnumerable<string> EnumerateFileSystemEntries(string basepath)
        {
            return EnumerateFileSystemEntries(basepath, (IFilter)null);
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <param name="filter">The filter to apply.</param>
        /// <returns>A list of the full filenames</returns>
        public static IEnumerable<string> EnumerateFiles(string basepath, IFilter filter)
        {
            return EnumerateFileSystemEntries(basepath, filter).Where(x => !x.EndsWith(DirectorySeparatorString));
        }

        /// <summary>
        /// Returns a list of folder names found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in</param>
        /// <param name="filter">The filter to apply.</param>
        /// <returns>A list of the full paths</returns>
        public static IEnumerable<string> EnumerateFolders(string basepath, IFilter filter)
        {
            return EnumerateFileSystemEntries(basepath, filter).Where(x => x.EndsWith(DirectorySeparatorString));
        }

        /// <summary>
        /// Returns a list of all files and subfolders found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in.</param>
        /// <param name="filter">The filter to apply.</param>
        /// <returns>A list of the full filenames and foldernames. Foldernames ends with the directoryseparator char</returns>
        public static IEnumerable<string> EnumerateFileSystemEntries(string basepath, IFilter filter)
        {
            IFilter match;
            filter = filter ?? new FilterExpression();
            return EnumerateFileSystemEntries(basepath, (rootpath, path, attributes) => { 
                bool result;
                if (!filter.Matches(path, out result, out match))
                    result = true;

                return result;
            });
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
        public delegate System.IO.FileAttributes ExtractFileAttributes(string path);

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="rootpath">The folder to look in</param>
        /// <param name="filter">An optional filter to apply to the filenames</param>
        /// <param name="callback">The function to call with the filenames</param>
        /// <returns>A list of the full filenames</returns>
        public static IEnumerable<string> EnumerateFileSystemEntries(string rootpath, EnumerationFilterDelegate callback)
        {
            return EnumerateFileSystemEntries(rootpath, callback, new FileSystemInteraction(System.IO.Directory.GetDirectories), new FileSystemInteraction(System.IO.Directory.GetFiles));
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="rootpath">The folder to look in</param>
        /// <param name="callback">The function to call with the filenames</param>
        /// <param name="folderList">A function to call that lists all folders in the supplied folder</param>
        /// <param name="fileList">A function to call that lists all files in the supplied folder</param>
        /// <returns>A list of the full filenames</returns>
        public static IEnumerable<string> EnumerateFileSystemEntries(string rootpath, EnumerationFilterDelegate callback, FileSystemInteraction folderList, FileSystemInteraction fileList)
        {
            return EnumerateFileSystemEntries(rootpath, callback, folderList, fileList, null);
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="rootpath">The folder to look in</param>
        /// <param name="callback">The function to call with the filenames</param>
        /// <param name="folderList">A function to call that lists all folders in the supplied folder</param>
        /// <param name="fileList">A function to call that lists all files in the supplied folder</param>
        /// <param name="attributeReader">A function to call that obtains the attributes for an element, set to null to avoid reading attributes</param>
        /// <returns>A list of the full filenames</returns>
        public static IEnumerable<string> EnumerateFileSystemEntries(string rootpath, EnumerationFilterDelegate callback, FileSystemInteraction folderList, FileSystemInteraction fileList, ExtractFileAttributes attributeReader)
        {
            Stack<string> lst = new Stack<string>();
        
            var isFolder = false;
            try
            {
                if (attributeReader == null)
                    isFolder = true;
                else
                    isFolder = (attributeReader(rootpath) & System.IO.FileAttributes.Directory) == System.IO.FileAttributes.Directory;
            }
            catch
            {
            }
        
            if (isFolder)
            {
                rootpath = AppendDirSeparator(rootpath);
                try
                {
                    
                    System.IO.FileAttributes attr = attributeReader == null ? System.IO.FileAttributes.Directory : attributeReader(rootpath);
                    if (callback(rootpath, rootpath, attr))
                        lst.Push(rootpath);
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception)
                {
                    callback(rootpath, rootpath, ATTRIBUTE_ERROR | System.IO.FileAttributes.Directory);
                }

                while (lst.Count > 0)
                {
                    string f = AppendDirSeparator(lst.Pop());
            
                    yield return f;
                                
                    try
                    {
                        foreach(string s in folderList(f))
                        {
                            var sf = AppendDirSeparator(s);
                            System.IO.FileAttributes attr = attributeReader == null ? System.IO.FileAttributes.Directory : attributeReader(sf);
                            if (callback(rootpath, sf, attr))
                                lst.Push(sf);
                        }
                    }
                    catch (System.Threading.ThreadAbortException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        callback(rootpath, f, ATTRIBUTE_ERROR | System.IO.FileAttributes.Directory);
                    }

                    string[] files = null;
                    if (fileList != null)
                        try
                        {
                            files = fileList(f);
                        }
                        catch (System.Threading.ThreadAbortException)
                        {
                            throw;
                        }
                        catch (Exception)
                        {
                            callback(rootpath, f, ATTRIBUTE_ERROR);
                        }
		
                    if (files != null)
                        foreach(var s in files)
                        {
                            try
                            {
                                System.IO.FileAttributes attr = attributeReader == null ? System.IO.FileAttributes.Normal : attributeReader(s);
                                if (!callback(rootpath, s, attr))
                                    continue;
                            }
                            catch (System.Threading.ThreadAbortException)
                            {
                                throw;
                            }
                            catch (Exception)
                            {
                                callback(rootpath, s, ATTRIBUTE_ERROR);
                                continue;
                            }
                            yield return s;
                        }
                }
            }
            else
            {
                try
                {
                    System.IO.FileAttributes attr = attributeReader == null ? System.IO.FileAttributes.Normal : attributeReader(rootpath);
                    if (!callback(rootpath, rootpath, attr))
                        yield break;
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception)
                {
                    callback(rootpath, rootpath, ATTRIBUTE_ERROR);
                    yield break;
                }
                
                yield return rootpath;
            }
        }

        /// <summary>
        /// Calculates the size of files in a given folder
        /// </summary>
        /// <param name="folder">The folder to examine</param>
        /// <param name="filter">A filter to apply</param>
        /// <returns>The combined size of all files that match the filter</returns>
        public static long GetDirectorySize(string folder, IFilter filter)
        {
            return EnumerateFolders(folder, filter).Sum((path) => new System.IO.FileInfo(path).Length);
        }

        /// <summary>
        /// A cached instance of the directory separator as a string
        /// </summary>
        public static readonly string DirectorySeparatorString = System.IO.Path.DirectorySeparatorChar.ToString();

        /// <summary>
        /// Appends the appropriate directory separator to paths, depending on OS.
        /// Does not append the separator if the path already ends with it.
        /// </summary>
        /// <param name="path">The path to append to</param>
        /// <returns>The path with the directory separator appended</returns>
        public static string AppendDirSeparator(string path)
        {
            if (!path.EndsWith(DirectorySeparatorString))
                return path += DirectorySeparatorString;
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
                for(int i = 0; i < a1 / longSize; i++)
                    if (BitConverter.ToUInt64(buf1, ix) != BitConverter.ToUInt64(buf2, ix))
                        return false;
                    else
                        ix += longSize;

                for(int i = 0; i < a1 % longSize; i++)
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
        /// Calculates the hash of a given file, and returns the results as an base64 encoded string
        /// </summary>
        /// <param name="path">The path to the file to calculate the hash for</param>
        /// <returns>The base64 encoded hash</returns>
        public static string CalculateHash(string path)
        {
            using(System.IO.FileStream fs = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                return CalculateHash(fs);
        }

        /// <summary>
        /// Calculates the hash of a given stream, and returns the results as an base64 encoded string
        /// </summary>
        /// <param name="path">The stream to calculate the hash for</param>
        /// <returns>The base64 encoded hash</returns>
        public static string CalculateHash(System.IO.Stream stream)
        {
            return Convert.ToBase64String(System.Security.Cryptography.HashAlgorithm.Create(HashAlgorithm).ComputeHash(stream));
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
            byte[] buffer = new byte[4096];
            using(System.IO.FileStream file = new System.IO.FileStream(filename, System.IO.FileMode.Open))
                file.Read(buffer, 0, 4096);

            Encoding enc = Encoding.UTF8;
            try
            {
                // this will throw an error if not really UTF8
                new UTF8Encoding(false, true).GetString(buffer); 
            }
            catch (Exception)
            {
                enc = Encoding.Default;
            }

            // This will load the text using the BOM, or the detected encoding if no BOM.
            using(System.IO.StreamReader reader = new System.IO.StreamReader(filename, enc, true))
            {
                // Remove all \r from the file and split on \n, then pass directly to ExtractOptions
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Formats a size into a human readable format, eg. 2048 becomes &quot;2 KB&quot;.
        /// </summary>
        /// <param name="size">The size to format</param>
        /// <returns>A human readable string representing the size</returns>
        public static string FormatSizeString(long size)
        {
            if (size >= 1024 * 1024 * 1024 * 1024L)
                return string.Format(Strings.Utility.FormatStringTB, (double)size / (1024 * 1024 * 1024 * 1024L));
            else if (size >= 1024 * 1024 * 1024)
                return string.Format(Strings.Utility.FormatStringGB, (double)size / (1024 * 1024 * 1024));
            else if (size >= 1024 * 1024)
                return string.Format(Strings.Utility.FormatStringMB, (double)size / (1024 * 1024));
            else if (size >= 1024)
                return string.Format(Strings.Utility.FormatStringKB, (double)size / 1024);
            else
                return string.Format(Strings.Utility.FormatStringB, size);
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

        /// <summary>
        /// Parses a string into a boolean value
        /// </summary>
        /// <param name="value">The value to parse</param>
        /// <param name="default">The default value, in case the string is not a valid boolean value</param>
        /// <returns>The parsed value or the default value</returns>
        public static bool ParseBool(string value, bool @default)
        {
            if (value == null)
                value = "";

            switch (value.Trim().ToLower())
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
                    return @default;
            }
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
        /// A helper for converting byte arrays to hex, vice versa
        /// </summary>
        private const string HEX_DIGITS_UPPER = "0123456789ABCDEF";

        /// <summary>
        /// Converts the byte array to hex digits
        /// </summary>
        /// <param name="data">The data to convert</param>
        /// <returns>The data as a string of hex digits</returns>
        public static string ByteArrayAsHexString(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "";
    
            StringBuilder sb = new StringBuilder();
            foreach(byte b in data)
            {
                sb.Append(HEX_DIGITS_UPPER[(b >> 4) & 0xF]);
                sb.Append(HEX_DIGITS_UPPER[b & 0xF]);
            }

            return sb.ToString();
        }

        
        /// <summary>
        /// Converts the given hex string into a byte array
        /// </summary>
        /// <param name="hex">The hex string</param>
        /// <param name="data">A pre-allocated output data array, or null</param>
        /// <returns>The byte array</returns>
        public static byte[] HexStringAsByteArray(string hex, byte[] data = null)
        {
            if (string.IsNullOrEmpty(hex))
                return new byte[0];

            hex = hex.Trim().ToUpper();

            if (hex.Length % 2 != 0)
                throw new Exception(Strings.Utility.InvalidHexStringLengthError);
                
            if (data == null)
                data = new byte[hex.Length / 2];
            else if (data.Length < hex.Length / 2)
                throw new ArgumentOutOfRangeException("data");
                
            for(int i = 0; i < hex.Length; i+= 2)
            {
                int upper = HEX_DIGITS_UPPER.IndexOf(hex[i]);
                int lower = HEX_DIGITS_UPPER.IndexOf(hex[i + 1]);

                if (upper < 0)
                    throw new Exception(string.Format(Strings.Utility.InvalidHexDigitError, hex[i]));
                if (lower < 0)
                    throw new Exception(string.Format(Strings.Utility.InvalidHexDigitError, hex[i + 1]));

                data[i % 2] = (byte)((upper << 4) | lower);
            }

            return data;
        }        

        private static string UNAME;

        /// <value>
        /// Gets or sets a value indicating if the client is running OSX
        /// </value>
        public static bool IsClientOSX
        {
            get
            {
                if (!IsClientLinux)
                    return false;
        
                try
                {
                    if (UNAME == null)
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("uname");
                        psi.RedirectStandardOutput = true;
                        psi.UseShellExecute = false;
                
                        var pi = System.Diagnostics.Process.Start(psi);
                        pi.WaitForExit(5000);
                        if (pi.HasExited)
                            UNAME = pi.StandardOutput.ReadToEnd().Trim();
                    }
                }
                catch
                {
                }
        
                return "Darwin".Equals(UNAME);
            
            }
        }
        /// <value>
        /// Gets the output of "uname -a" on Linux, or null on Windows
        /// </value>
        public static string UnameAll
        {
            get
            {
                if (!IsClientLinux)
                    return null;
        
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("uname", "-a");
                    psi.RedirectStandardOutput = true;
                    psi.UseShellExecute = false;
            
                    var pi = System.Diagnostics.Process.Start(psi);
                    pi.WaitForExit(5000);
                    if (pi.HasExited)
                        return pi.StandardOutput.ReadToEnd().Trim();
                }
                catch
                {
                }
        
                return null;            
            }
        }
        /// <value>
        /// Gets or sets a value indicating if the client is Linux/Unix based
        /// </value>
        public static bool IsClientLinux
        {
            get
            {
#if __MonoCS__
                if (Environment.OSVersion.Platform == PlatformID.Unix || (int)Environment.OSVersion.Platform == 6)
                    return true;
#else
        if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
			return true;
#endif
                return false;
		
            }
        }

        /// <value>
        /// Returns a value indicating if the filesystem, is case sensitive 
        /// </value>
        public static bool IsFSCaseSensitive
        {
            get
            {
                //TODO: This should probably be determined by filesystem rather than OS,
                // OSX can actually have the disks formated as Case Sensitive, but insensitive is default
                return IsClientLinux && !IsClientOSX;
            }
        }

        /// <summary>
        /// Returns a value indicating if the app is running under Mono
        /// </summary>
        public static bool IsMono
        {
            get
            {
                return Type.GetType("Mono.Runtime") != null;
            }
        }

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
                        var regex = new System.Text.RegularExpressions.Regex(@"\d+\.\d+(\.\d+)?(\.\d+)?");
                        var match = regex.Match(v);
                        if (match.Success)
                            return new Version(match.Value);
                    }   
                }
                catch
                {
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
                    Type t = Type.GetType("Mono.Runtime");
                    if (t != null)
                    {
                        System.Reflection.MethodInfo mi = t.GetMethod("GetDisplayName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                        if (mi != null)
                            return (string)mi.Invoke(null, null);
                    }
                }
                catch
                {
                }

                return null;            
            }
        }

        /// <summary>
        /// Gets the users default UI language
        /// </summary>
        public static System.Globalization.CultureInfo DefaultCulture
        {
            get
            {
                System.Threading.Thread t = new System.Threading.Thread(new System.Threading.ThreadStart(DummyMethod));
                return t.CurrentUICulture;
            }
        }
        //Unused function, used to create a dummy thread
        private static void DummyMethod()
        {
        }

        /// <summary>
        /// Gets a string comparer that matches the client filesystems case sensitivity
        /// </summary>
        public static StringComparer ClientFilenameStringComparer { get { return Utility.IsFSCaseSensitive ? StringComparer.CurrentCulture : StringComparer.CurrentCultureIgnoreCase; } }

        /// <summary>
        /// Gets the string comparision that matches the client filesystems case sensitivity
        /// </summary>
        public static StringComparison ClientFilenameStringComparision { get { return Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase; } }

        /// <summary>
        /// Searches the system paths for the file specified
        /// </summary>
        /// <param name="filename">The file to locate</param>
        /// <returns>The full path to the file, or null if the file was not found</returns>
        public static string LocateFileInSystemPath(string filename)
        {
            try
            {
                if (System.IO.Path.IsPathRooted(filename))
                    return System.IO.File.Exists(filename) ? filename : null;

                try
                {
                    filename = System.IO.Path.GetFileName(filename);
                }
                catch
                {
                }

                string homedir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + System.IO.Path.PathSeparator.ToString();

                //Look in application base folder and all system path folders
                foreach(string s in (homedir + Environment.GetEnvironmentVariable("PATH")).Split(System.IO.Path.PathSeparator))
                    if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0)
                        try
                        {
                            foreach(string sx in System.IO.Directory.GetFiles(Environment.ExpandEnvironmentVariables(s), filename))
                                return sx;
                        }
                        catch
                        {
                        }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Checks that a hostname is valid
        /// </summary>
        /// <param name="hostname">The hostname to verify</param>
        /// <returns>True if the hostname is valid, false otherwise</returns>
        public static bool IsValidHostname(string hostname)
        {
            try
            {
                return System.Uri.CheckHostName(hostname) != UriHostNameType.Unknown;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a string representation of a <see cref="System.DateTime"/> in UTC format
        /// </summary>
        /// <param name="dt">The <see cref="System.DateTime"/> instance</param>
        /// <returns>A string representing the time</returns>
        public static string SerializeDateTime(DateTime dt)
        {
            //Note: Actually the K should be Z which is more correct as it is forced to be Z, but Z as a format specifier is fairly undocumented
            return dt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssK");
        }

        /// <summary>
        /// Parses a serialized <see cref="System.DateTime"/> instance
        /// </summary>
        /// <param name="str">The string to parse</param>
        /// <returns>The parsed <see cref="System.DateTime"/> instance</returns>
        public static DateTime DeserializeDateTime(string str)
        {
            DateTime dt;
            if (!DateTime.TryParseExact(str, "yyyyMMdd'T'HHmmssK", null, System.Globalization.DateTimeStyles.AssumeUniversal, out dt))
                throw new Exception(string.Format(Strings.Utility.InvalidDateError, str));

            return dt;
        }

        /// <summary>
        /// Helper method that replaces one file with another
        /// </summary>
        /// <param name="target">The file to replace</param>
        /// <param name="sourcefile">The file to replace with</param>
        public static void ReplaceFile(string target, string sourcefile)
        {
            if (System.IO.File.Exists(target))
                System.IO.File.Delete(target);

            //Nasty workaround for the fact that a recently deleted file occasionally blocks a new write
            long i = 5;
            do
            {
                try
                {
                    System.IO.File.Move(sourcefile, target);
                    break;
                }
                catch (Exception ex)
                {
                    if (i == 0)
                        throw new Exception(string.Format("Failed to replace the file \"{0}\" volume with the \"{1}\", error: {2}", target, sourcefile, ex.Message));
                    System.Threading.Thread.Sleep(250);
                }
            } while (i-- > 0);
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
        /// Decodes a &quot;base64 for url&quot; encoded string into the raw byte array.
        /// See https://en.wikipedia.org/wiki/Base64#URL_applications
        /// </summary>
        /// <param name="data">The data to decode</param>
        /// <returns>The raw data</returns>
        public static byte[] Base64UrlDecode(string data)
        {
            return Convert.FromBase64String(Base64UrlToBase64Plain(data));
        }

        /// <summary>
        /// Prints the object to a stream, which can be used for display or logging
        /// </summary>
        /// <returns>The serialized object</returns>
        /// <param name="item">The object to serialize</param>
        public static void PrintSerializeObject(object item, System.IO.TextWriter writer, Func<System.Reflection.PropertyInfo, bool> filter = null)
        {
            foreach(var p in item.GetType().GetProperties())
            {
                if (filter != null && !filter(p))
                    continue;
                
                if (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string))
                {
                    writer.WriteLine("{0}: {1}", p.Name, p.GetValue(item, null));
                }
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType))
                {
                    var enumerable = (System.Collections.IEnumerable)p.GetValue(item, null);
                    if (enumerable != null)
                    {
                        var enumerator = enumerable.GetEnumerator();
                        if (enumerator != null)
                        {
                            writer.Write("{0}: [", p.Name);
                            if (enumerator.MoveNext())
                            {
                                writer.Write(enumerator.Current);
                                while (enumerator.MoveNext())
                                {
                                    writer.Write(", ");
                                    writer.Write(enumerator.Current);
                                }
                            }
                            writer.WriteLine("]");
                        }
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
        public static StringBuilder PrintSerializeObject(object item, StringBuilder sb = null, Func<System.Reflection.PropertyInfo, bool> filter = null)
        {
            sb = sb ?? new StringBuilder();
            using(var sw = new System.IO.StringWriter(sb))
                PrintSerializeObject(item, sw);
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
                System.Text.Encoding.UTF8.GetBytes(data ?? ""),
                System.Text.Encoding.UTF8.GetBytes(salt ?? ""),
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
            using(var h = System.Security.Cryptography.SHA256.Create())
            {
                h.Initialize();
                h.TransformBlock(salt, 0, salt.Length, salt, 0);
                h.TransformFinalBlock(data, 0, data.Length);
                var buf = h.Hash;
            
                for(var i = 0; i < repeats; i++)
                {
                    h.Initialize();
                    h.TransformBlock(salt, 0, salt.Length, salt, 0);
                    h.TransformFinalBlock(buf, 0, buf.Length);
                    buf = h.Hash;
                }
                
                return buf;
            }
        }
    }
}