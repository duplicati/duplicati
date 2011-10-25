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
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{
    public static class Utility
    {
        /// <summary>
        /// Gets the hash algorithm used for calculating a hash
        /// </summary>
        public static string HashAlgorithm { get { return "SHA256"; } }

        /// <summary>
        /// An enumeration used to identify file types when performing file listings
        /// </summary>
        public enum EnumeratedFileStatus
        {
            File,
            Folder,
            Error
        };

        /// <summary>
        /// The callback delegate type used to collecting file information
        /// </summary>
        /// <param name="rootpath">The path that the file enumeration started at</param>
        /// <param name="path">The current element</param>
        /// <param name="status">The type of the element</param>
        public delegate void EnumerationCallbackDelegate(string rootpath, string path, EnumeratedFileStatus status);

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
        /// A callback delegate used for applying alternate enumeration of filesystems
        /// </summary>
        /// <param name="path">The path to return data from</param>
        /// <returns>A list of paths</returns>
        public delegate string[] FileSystemInteraction(string path);

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="rootpath">The folder to look in</param>
        /// <param name="filter">An optional filter to apply to the filenames</param>
        /// <param name="callback">The function to call with the filenames</param>
        /// <returns>A list of the full filenames</returns>
        public static void EnumerateFileSystemEntries(string rootpath, FilenameFilter filter, EnumerationCallbackDelegate callback)
        {
            EnumerateFileSystemEntries(rootpath, filter, callback, new FileSystemInteraction(System.IO.Directory.GetDirectories), new FileSystemInteraction(System.IO.Directory.GetFiles));
        }

        /// <summary>
        /// Returns a list of all files found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="rootpath">The folder to look in</param>
        /// <param name="filter">An optional filter to apply to the filenames</param>
        /// <param name="callback">The function to call with the filenames</param>
        /// <param name="folderList">A function to call that lists all folders in the supplied folder</param>
        /// <param name="fileList">A function to call that lists all files in the supplied folder</param>
        /// <returns>A list of the full filenames</returns>
        public static void EnumerateFileSystemEntries(string rootpath, FilenameFilter filter, EnumerationCallbackDelegate callback, FileSystemInteraction folderList, FileSystemInteraction fileList)
        {
            if (!System.IO.Directory.Exists(rootpath))
                return;

            Queue<string> lst = new Queue<string>();
            lst.Enqueue(rootpath);

            while (lst.Count > 0)
            {
                string f = AppendDirSeparator(lst.Dequeue());
                try
                {
                    foreach (string s in folderList(f))
                        if (filter == null || filter.ShouldInclude(rootpath, AppendDirSeparator(s)))
                            lst.Enqueue(s);

                     callback(rootpath, f, EnumeratedFileStatus.Folder);
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception)
                {
                    callback(rootpath, f, EnumeratedFileStatus.Error);
                }

                try
                {
                    foreach (string s in fileList(f))
                        if (filter == null || filter.ShouldInclude(rootpath, s))
                            callback(rootpath, s, EnumeratedFileStatus.File);
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception)
                {
                    callback(rootpath, f, EnumeratedFileStatus.Error);
                }

            }
        }

        /// <summary>
        /// An internal helper class to collect filenames from the enumeration callback
        /// </summary>
        private class PathCollector
        {
            private List<string> m_files;
            private bool m_includeFolders;
            private bool m_includeFiles;

            public PathCollector(bool includeFolders, bool includeFiles)
            {
                m_files = new List<string>();
                m_includeFolders = includeFolders;
                m_includeFiles = includeFiles;
            }

            public void Callback(string rootpath, string path, EnumeratedFileStatus status)
            {
                if (m_includeFolders && status == EnumeratedFileStatus.Folder)
                    m_files.Add(path);
                else if (m_includeFiles && status == EnumeratedFileStatus.File)
                    m_files.Add(path);
            }

            public List<string> Files { get { return m_files; } }
        }

        /// <summary>
        /// An internal helper class to calculate the size of a folders files
        /// </summary>
        private class PathSizeCalculator
        {
            private long m_size = 0;

            public void Callback(string rootpath, string path, EnumeratedFileStatus status)
            {
                if (status == EnumeratedFileStatus.File)
                    try { m_size += new System.IO.FileInfo(path).Length; }
                    catch { }
            }

            public long Size { get { return m_size; } }
        }

        /// <summary>
        /// Calculates the size of files in a given folder
        /// </summary>
        /// <param name="folder">The folder to examine</param>
        /// <param name="filter">A filter to apply</param>
        /// <returns>The combined size of all files that match the filter</returns>
        public static long GetDirectorySize(string folder, FilenameFilter filter)
        {
            PathSizeCalculator c = new PathSizeCalculator();
            EnumerateFileSystemEntries(folder, filter, new EnumerationCallbackDelegate(c.Callback));
            return c.Size;
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
            PathCollector c = new PathCollector(false, true);
            EnumerateFileSystemEntries(basepath, filter, new EnumerationCallbackDelegate(c.Callback));
            return c.Files;
        }

        /// <summary>
        /// Returns a list of all files and subfolders found in the given folder.
        /// The search is recursive.
        /// </summary>
        /// <param name="basepath">The folder to look in.</param>
        /// <returns>A list of the full filenames and foldernames. Foldernames ends with the directoryseparator char</returns>
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
        /// <returns>A list of the full filenames and foldernames. Foldernames ends with the directoryseparator char</returns>
        public static List<string> EnumerateFileSystemEntries(string basepath, FilenameFilter filter)
        {
            PathCollector c = new PathCollector(true, true);
            EnumerateFileSystemEntries(basepath, filter, new EnumerationCallbackDelegate(c.Callback));
            return c.Files;
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
            PathCollector c = new PathCollector(true, false);
            EnumerateFileSystemEntries(basepath, filter, new EnumerationCallbackDelegate(c.Callback));
            return c.Files;
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
        /// Calculates the hash of a given file, and returns the results as an base64 encoded string
        /// </summary>
        /// <param name="path">The path to the file to calculate the hash for</param>
        /// <returns>The base64 encoded hash</returns>
        public static string CalculateHash(string path)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
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
        /// Formats a size into a human readable format, eg. 2048 becomes &quot;2 KB&quot;.
        /// </summary>
        /// <param name="size">The size to format</param>
        /// <returns>A human readable string representing the size</returns>
        public static string FormatSizeString(long size)
        {
            if (size >= 1024 * 1024 * 1024)
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
            foreach (byte b in data)
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
        /// <returns>The byte array</returns>
        public static byte[] HexStringAsByteArray(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return new byte[0];

            hex = hex.Trim().ToUpper();

            if (hex.Length % 2 != 0)
                throw new Exception(Strings.Utility.InvalidHexStringLengthError);
            
            byte[] data = new byte[hex.Length];
            for (int i = 0; i < hex.Length; i+= 2)
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
	            //TODO: This should probably be determined by filesystem rather than OS
    	        //In case MS decides to support case sensitive filesystems (yeah right :))
				return IsClientLinux;
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
                    Type t = Type.GetType("Mono.Runtime");
                    if (t != null)
                    {
                        System.Reflection.MethodInfo mi = t.GetMethod("GetDisplayName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                        if (mi != null)
                        {
                            string version = (string)mi.Invoke(null, null);
                            return new Version(version.Substring(version.Trim().LastIndexOf(' ')));
                        }
                    }
                }
                catch
                {
                }

                return new Version();
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
        private static void DummyMethod() { }

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

                try { filename = System.IO.Path.GetFileName(filename); }
                catch { }

                string homedir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + System.IO.Path.PathSeparator.ToString();

                //Look in application base folder and all system path folders
                foreach (string s in (homedir + Environment.GetEnvironmentVariable("PATH")).Split(System.IO.Path.PathSeparator))
                    if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0)
                        try
                        {
                            foreach (string sx in System.IO.Directory.GetFiles(Environment.ExpandEnvironmentVariables(s), filename))
                                return sx;
                        }
                        catch 
                        { }
            }
            catch 
            { }

            return null;
        }


        /// <summary>
        /// Gets a stream using the GetRequestStream method, but with a timeout.
        /// This is a workaround for the fact that GetRequestStream() hangs if
        /// Timeout is set to infinity, but setting it to something else than
        /// inifinity may abort the request if it is slow. Setting it to something
        /// large is just as bad, as it would hang for a long time.
        /// </summary>
        /// <param name="req">The WebRequest to invoke GetRequestStream() on</param>
        /// <returns>The request stream</returns>
        public static System.IO.Stream SafeGetRequestStream(System.Net.WebRequest req)
        {
            return (System.IO.Stream)SafeGetRequestOrResponseStream(req, true);
        }

        /// <summary>
        /// Gets a response using the GetResponse method, but with a timeout.
        /// This is a workaround for the fact that GetResponse() hangs if
        /// Timeout is set to infinity, but setting it to something else than
        /// inifinity may abort the request if it is slow. Setting it to something
        /// large is just as bad, as it would hang for a long time.
        /// </summary>
        /// <param name="req">The WebRequest to invoke GetResponse() on</param>
        /// <returns>The WebResponse</returns>
        public static System.Net.WebResponse SafeGetResponse(System.Net.WebRequest req)
        {
            return (System.Net.WebResponse)SafeGetRequestOrResponseStream(req, false);
        }

        /// <summary>
        /// The default timeout for a connection response, either for creating the connection or for starting to deliver the results
        /// </summary>
        private static readonly int DEFAULT_RESPONSE_TIMEOUT = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;

        /// <summary>
        /// Helper function that invokes a thread and the GetRequestStream() or GetResponse() method
        /// </summary>
        /// <param name="req">The request to invoke the method on</param>
        /// <param name="getRequest">A value indicating if the invoked method should be GetRequestStream() or GetResponse()</param>
        /// <returns>Either a System.IO.Stream or a System.Net.WebResponse object</returns>
        private static object SafeGetRequestOrResponseStream(System.Net.WebRequest req, bool getRequest)
        {
            object[] args = new object[] { req, getRequest, null };
            System.Threading.Thread t = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(RunSafeGetRequest));
            try
            {
                //We use the timeout to determine how long we should wait
                int waitTime = req.Timeout == System.Threading.Timeout.Infinite ? DEFAULT_RESPONSE_TIMEOUT : req.Timeout;
                t.Start(args);
                if (t.Join(waitTime))
                {
                    if (args[2] == null)
                        throw new Exception(string.Format(Strings.Utility.UnexpectedRequestResultError, "null", ""));
                    else if (args[2] is Exception)
                        throw (Exception)args[2];

                    if (getRequest && args[2] is System.IO.Stream)
                        return (System.IO.Stream)args[2];
                    else if (!getRequest && args[2] is System.Net.WebResponse)
                        return (System.Net.WebResponse)args[2];

                    throw new Exception(string.Format(Strings.Utility.UnexpectedRequestResultError, args[2].GetType(), args[2].ToString()));
                }
                else
                {
                    t.Abort();
                    throw new System.Net.WebException(Strings.Utility.TimeoutException, null, System.Net.WebExceptionStatus.Timeout, null);
                }

            }
            catch
            {
                try { t.Abort(); }
                catch { }

                throw;
            }
        }

        /// <summary>
        /// The thread method that is invoked to perform the actual invocation,
        /// this ensures that we can abort the thread should we want to
        /// </summary>
        /// <param name="data">An object array with the required parameters. Index 0 is the System.Net.WebRequest, index 1 is the flag indicating if it should use GetRequestStream() or GetResponse(), index 2 is an empty slot for returning the result or an exception</param>
        private static void RunSafeGetRequest(object data)
        {
            object[] args = (object[])data;
            try
            {
                System.Net.WebRequest req = (System.Net.WebRequest)args[0];
                req.Timeout = System.Threading.Timeout.Infinite;

                bool getRequest = (bool)args[1];
                if (getRequest)
                {
                    args[2] = req.GetRequestStream();
                }
                else
                {
                    args[2] = req.GetResponse();
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
            }
            catch (Exception ex)
            {
                args[2] = ex;
            }
        }

        private class ThreadWorker
        {
            public System.IO.Stream stream = null;
            public void Run(object data)
            {
                System.Net.WebRequest req = (System.Net.WebRequest)data;
                req.Timeout = System.Threading.Timeout.Infinite;
                stream = req.GetRequestStream();
            }
        }
    }
}
