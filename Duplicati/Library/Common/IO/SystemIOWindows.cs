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
using System.Security.AccessControl;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Duplicati.Library.Interface;
using Newtonsoft.Json;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;

namespace Duplicati.Library.Common.IO
{
    [SupportedOSPlatform("windows")]
    public struct SystemIOWindows : ISystemIO
    {
        // Based on the constant names used in
        // https://github.com/dotnet/runtime/blob/v5.0.12/src/libraries/Common/src/System/IO/PathInternal.Windows.cs
        private const string ExtendedDevicePathPrefix = @"\\?\";
        private const string UncPathPrefix = @"\\";
        private const string AltUncPathPrefix = @"//";
        private const string UncExtendedPathPrefix = @"\\?\UNC\";

        private static readonly string DIRSEP = Util.DirectorySeparatorString;

        /// <summary>
        /// Encapsulation of Win32 calls
        /// </summary>
        private static class Win32API
        {
            /// <summary>
            /// The stream info levels for FindFirstStreamW
            /// </summary>
            public enum STREAM_INFO_LEVELS
            {
                FindStreamInfoStandard = 0,
                FindStreamInfoMaxInfoLevel = 1
            }

            /// <summary>
            /// The access mode for CreateFile to only read attributes
            /// </summary>
            public const uint FILE_READ_ATTRIBUTES = 0x0080;
            /// <summary>
            /// Open only an existing file
            /// </summary>
            public const uint OPEN_EXISTING = 3;
            /// <summary>
            /// The share mode for CreateFile to allow read access
            /// </summary>
            public const uint FILE_SHARE_READ = 0x00000001;
            /// <summary>
            /// The share mode for CreateFile to allow write access
            /// </summary>
            public const uint FILE_SHARE_WRITE = 0x00000002;
            /// <summary>
            /// The share mode for CreateFile to allow delete access
            /// </summary>
            public const uint FILE_SHARE_DELETE = 0x00000004;

            /// <summary>
            /// Creates a file handle
            /// </summary>
            /// <param name="lpFileName">The filename</param>
            /// <param name="dwDesiredAccess">The access mode</param>
            /// <param name="dwShareMode">The share mode</param>
            /// <param name="lpSecurityAttributes">Pointer to security attributes</param>
            /// <param name="dwCreationDisposition">Create mode flags</param>
            /// <param name="dwFlagsAndAttributes">Flags and attributes</param>
            /// <param name="hTemplateFile">Handle to a template file</param>
            /// <returns>A filehandle for the entry</returns>
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeFileHandle CreateFileW(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            /// <summary>
            /// Gets the file size
            /// </summary>
            /// <param name="hFile">The file handle</param>
            /// <param name="lpFileSize">The size of the file</param>
            /// <returns><c>true</c> if the call succeeds, <c>false</c> otherwise
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetFileSizeEx(SafeFileHandle hFile, out long lpFileSize);

            /// <summary>
            /// The WIN32_FIND_STREAM_DATA structure
            /// </summary>
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct WIN32_FIND_STREAM_DATA
            {
                public long StreamSize;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
                public string cStreamName;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr FindFirstStreamW(string lpFileName, STREAM_INFO_LEVELS InfoLevel, out WIN32_FIND_STREAM_DATA lpFindStreamData, uint dwFlags);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern bool FindNextStreamW(IntPtr hFindStream, out WIN32_FIND_STREAM_DATA lpFindStreamData);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool FindClose(IntPtr hFindFile);
        }

        /// <summary>
        /// The current user SID
        /// </summary>
        private static readonly SecurityIdentifier CURRENT_USER_SID = WindowsIdentity.GetCurrent().User;

        /// <summary>
        /// The LocalSystem user SID
        /// </summary>
        private static readonly SecurityIdentifier LOCAL_SYSTEM_SID = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        /// <summary>
        /// The Administrator user SID
        /// </summary>
        private static readonly SecurityIdentifier ADMINISTRATORS_SID = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        /// <summary>
        /// Prefix path with one of the extended device path prefixes
        /// (@"\\?\" or @"\\?\UNC\") but only if it's a fully qualified
        /// path with no relative components (i.e., with no "." or ".."
        /// as part of the path).
        /// </summary>
        public static string AddExtendedDevicePathPrefix(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                // For example: \\?\C:\Temp\foo.txt or \\?\UNC\example.com\share\foo.txt
                return path;
            }
            else
            {
                var hasRelativePathComponents = HasRelativePathComponents(path);
                if (IsPrefixedWithUncPathPrefix(path) && !hasRelativePathComponents)
                {
                    // For example: \\example.com\share\foo.txt or //example.com/share/foo.txt
                    return UncExtendedPathPrefix + ConvertSlashes(path.Substring(UncPathPrefix.Length));
                }
                else if (DotNetRuntimePathWindows.IsPathFullyQualified(path) && !hasRelativePathComponents)
                {
                    // For example: C:\Temp\foo.txt or C:/Temp/foo.txt
                    return ExtendedDevicePathPrefix + ConvertSlashes(path);
                }
                else
                {
                    // A relative path or a fully qualified path with relative
                    // path components so the extended device path prefixes
                    // cannot be applied.
                    //
                    // For example: foo.txt or C:\Temp\..\foo.txt
                    return path;
                }
            }
        }

        /// <summary>
        /// Returns true if prefixed with @"\\" or @"//".
        /// </summary>
        private static bool IsPrefixedWithUncPathPrefix(string path)
        {
            return path.StartsWith(UncPathPrefix, StringComparison.Ordinal) ||
                path.StartsWith(AltUncPathPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns true if prefixed with @"\\?\UNC\" or @"\\?\".
        /// </summary>
        private static bool IsPrefixedWithExtendedDevicePathPrefix(string path)
        {
            return path.StartsWith(UncExtendedPathPrefix, StringComparison.Ordinal) ||
                path.StartsWith(ExtendedDevicePathPrefix, StringComparison.Ordinal);
        }

        private static string[] relativePathComponents = new[] { ".", ".." };

        /// <summary>
        /// Returns true if <paramref name="path"/> contains relative path components; i.e., "." or "..".
        /// </summary>
        private static bool HasRelativePathComponents(string path)
        {
            return GetPathComponents(path).Any(pathComponent => relativePathComponents.Contains(pathComponent));
        }

        /// <summary>
        /// Returns a sequence representing the files and directories in <paramref name="path"/>.
        /// </summary>
        private static IEnumerable<string> GetPathComponents(string path)
        {
            while (!String.IsNullOrEmpty(path))
            {
                var pathComponent = Path.GetFileName(path);
                if (!String.IsNullOrEmpty(pathComponent))
                {
                    yield return pathComponent;
                }
                path = Path.GetDirectoryName(path);
            }
        }

        /// <summary>
        /// Removes either of the extended device path prefixes
        /// (@"\\?\" or @"\\?\UNC\") if <paramref name="path"/> is prefixed
        /// with one of them.
        /// </summary>
        public static string RemoveExtendedDevicePathPrefix(string path)
        {
            if (path.StartsWith(UncExtendedPathPrefix, StringComparison.Ordinal))
            {
                // @"\\?\UNC\example.com\share\file.txt" to @"\\example.com\share\file.txt"
                return UncPathPrefix + path.Substring(UncExtendedPathPrefix.Length);
            }
            else if (path.StartsWith(ExtendedDevicePathPrefix, StringComparison.Ordinal))
            {
                // @"\\?\C:\file.txt" to @"C:\file.txt"
                return path.Substring(ExtendedDevicePathPrefix.Length);
            }
            else
            {
                return path;
            }
        }

        /// <summary>
        /// Convert forward slashes to backslashes.
        /// </summary>
        /// <returns>Path with forward slashes replaced by backslashes.</returns>
        private static string ConvertSlashes(string path)
        {
            return path.Replace("/", Util.DirectorySeparatorString);
        }


        [SupportedOSPlatform("windows")]
        private class FileSystemAccess
        {
            // Use JsonProperty Attribute to allow readonly fields to be set by deserializer
            // https://github.com/duplicati/duplicati/issues/4028
            [JsonProperty]
            public readonly FileSystemRights Rights;
            [JsonProperty]
            public readonly AccessControlType ControlType;
            [JsonProperty]
            public readonly string SID;
            [JsonProperty]
            public readonly bool Inherited;
            [JsonProperty]
            public readonly InheritanceFlags Inheritance;
            [JsonProperty]
            public readonly PropagationFlags Propagation;

            public FileSystemAccess()
            {
            }

            public FileSystemAccess(FileSystemAccessRule rule)
            {
                Rights = rule.FileSystemRights;
                ControlType = rule.AccessControlType;
                SID = rule.IdentityReference.Value;
                Inherited = rule.IsInherited;
                Inheritance = rule.InheritanceFlags;
                Propagation = rule.PropagationFlags;
            }

            public FileSystemAccessRule Create(FileSystemSecurity owner)
            {
                return (FileSystemAccessRule)owner.AccessRuleFactory(
                    new System.Security.Principal.SecurityIdentifier(SID),
                    (int)Rights,
                    Inherited,
                    Inheritance,
                    Propagation,
                    ControlType);
            }
        }

        private static JsonSerializer _cachedSerializer;

        private JsonSerializer Serializer
        {
            get
            {
                if (_cachedSerializer != null)
                {
                    return _cachedSerializer;
                }

                _cachedSerializer = JsonSerializer.Create(
                    new JsonSerializerSettings { Culture = System.Globalization.CultureInfo.InvariantCulture });

                return _cachedSerializer;
            }
        }

        private string SerializeObject<T>(T o)
        {
            using (var tw = new StringWriter())
            {
                Serializer.Serialize(tw, o);
                tw.Flush();
                return tw.ToString();
            }
        }

        private T DeserializeObject<T>(string data)
        {
            using (var tr = new StringReader(data))
                return (T)Serializer.Deserialize(tr, typeof(T));
        }


        [SupportedOSPlatform("windows")]
        private FileSystemSecurity GetAccessControlDir(string path)
            => new DirectoryInfo(AddExtendedDevicePathPrefix(path)).GetAccessControl();


        [SupportedOSPlatform("windows")]
        private FileSystemSecurity GetAccessControlFile(string path)
            => new FileInfo(AddExtendedDevicePathPrefix(path)).GetAccessControl();


        [SupportedOSPlatform("windows")]
        private void SetAccessControlFile(string path, FileSecurity rules)
            => new FileInfo(AddExtendedDevicePathPrefix(path)).SetAccessControl(rules);

        [SupportedOSPlatform("windows")]
        private void SetAccessControlDir(string path, DirectorySecurity rules)
            => new DirectoryInfo(AddExtendedDevicePathPrefix(path)).SetAccessControl(rules);

        #region ISystemIO implementation
        public void DirectoryCreate(string path)
            => Directory.CreateDirectory(AddExtendedDevicePathPrefix(path));

        public void DirectoryDelete(string path, bool recursive)
            => Directory.Delete(AddExtendedDevicePathPrefix(path), recursive);

        public bool DirectoryExists(string path)
            => Directory.Exists(AddExtendedDevicePathPrefix(path));

        public void DirectoryMove(string sourceDirName, string destDirName)
            => Directory.Move(AddExtendedDevicePathPrefix(sourceDirName), AddExtendedDevicePathPrefix(destDirName));

        public void FileDelete(string path)
            => File.Delete(AddExtendedDevicePathPrefix(path));

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
            => File.SetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path), time);

        public void FileSetCreationTimeUtc(string path, DateTime time)
            => File.SetCreationTimeUtc(AddExtendedDevicePathPrefix(path), time);

        public DateTime FileGetLastWriteTimeUtc(string path)
            => File.GetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path));

        public DateTime FileGetCreationTimeUtc(string path)
            => File.GetCreationTimeUtc(AddExtendedDevicePathPrefix(path));

        public bool FileExists(string path)
            => File.Exists(AddExtendedDevicePathPrefix(path));

        public FileStream FileOpenRead(string path)
            => File.Open(AddExtendedDevicePathPrefix(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        public FileStream FileOpenReadWrite(string path)
            => !FileExists(path)
                ? FileCreate(path)
                : File.Open(AddExtendedDevicePathPrefix(path), FileMode.Open, FileAccess.ReadWrite);

        public FileStream FileOpenWrite(string path)
            => !FileExists(path)
                ? FileCreate(path)
                : File.OpenWrite(AddExtendedDevicePathPrefix(path));

        public FileStream FileCreate(string path)
            => File.Create(AddExtendedDevicePathPrefix(path));

        public FileAttributes GetFileAttributes(string path)
            => File.GetAttributes(AddExtendedDevicePathPrefix(path));

        public void SetFileAttributes(string path, FileAttributes attributes)
        {
            File.SetAttributes(AddExtendedDevicePathPrefix(path), attributes);
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="file">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public string GetSymlinkTarget(string file)
            => new FileInfo(AddExtendedDevicePathPrefix(file)).LinkTarget;

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
            => Directory.EnumerateFileSystemEntries(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);

        public IEnumerable<string> EnumerateFiles(string path)
            => Directory.EnumerateFiles(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
            => Directory.EnumerateFiles(AddExtendedDevicePathPrefix(path), searchPattern, searchOption).Select(RemoveExtendedDevicePathPrefix);

        public string PathGetFileName(string path)
            => RemoveExtendedDevicePathPrefix(Path.GetFileName(AddExtendedDevicePathPrefix(path)));

        public string PathGetDirectoryName(string path)
            => RemoveExtendedDevicePathPrefix(Path.GetDirectoryName(AddExtendedDevicePathPrefix(path)));

        public string PathGetExtension(string path)
            => RemoveExtendedDevicePathPrefix(Path.GetExtension(AddExtendedDevicePathPrefix(path)));

        public string PathChangeExtension(string path, string extension)
            => RemoveExtendedDevicePathPrefix(Path.ChangeExtension(AddExtendedDevicePathPrefix(path), extension));

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            Directory.SetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            Directory.SetCreationTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void FileMove(string source, string target)
        {
            File.Move(AddExtendedDevicePathPrefix(source), AddExtendedDevicePathPrefix(target));
        }

        public string GetPathRoot(string path)
            => IsPrefixedWithExtendedDevicePathPrefix(path)
                ? Path.GetPathRoot(path)
                : RemoveExtendedDevicePathPrefix(Path.GetPathRoot(AddExtendedDevicePathPrefix(path)));
        public string[] GetDirectories(string path)
            => IsPrefixedWithExtendedDevicePathPrefix(path)
                ? Directory.GetDirectories(path)
                : Directory.GetDirectories(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix).ToArray();

        public string[] GetFiles(string path)
            => IsPrefixedWithExtendedDevicePathPrefix(path)
                ? Directory.GetFiles(path)
                : Directory.GetFiles(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix).ToArray();

        public string[] GetFiles(string path, string searchPattern)
            => IsPrefixedWithExtendedDevicePathPrefix(path)
                ? Directory.GetFiles(path, searchPattern)
                : Directory.GetFiles(AddExtendedDevicePathPrefix(path), searchPattern).Select(RemoveExtendedDevicePathPrefix).ToArray();

        public DateTime GetCreationTimeUtc(string path)
            => Directory.GetCreationTimeUtc(AddExtendedDevicePathPrefix(path));

        public DateTime GetLastWriteTimeUtc(string path)
            => Directory.GetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path));

        public IEnumerable<string> EnumerateDirectories(string path)
            => IsPrefixedWithExtendedDevicePathPrefix(path)
                ? Directory.EnumerateDirectories(path)
                : Directory.EnumerateDirectories(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);

        public IEnumerable<IFileEntry> EnumerateFileEntries(string path)
        {
            // For consistency with previous implementation, enumerate files first and directories after
            var dir = IsPrefixedWithExtendedDevicePathPrefix(path)
                ? new DirectoryInfo(path)
                : new DirectoryInfo(AddExtendedDevicePathPrefix(path));

            foreach (FileInfo file in dir.EnumerateFiles())
                yield return FileEntry(file);

            foreach (DirectoryInfo d in dir.EnumerateDirectories())
                yield return DirectoryEntry(d);
        }

        public void FileCopy(string source, string target, bool overwrite)
            => File.Copy(AddExtendedDevicePathPrefix(source), AddExtendedDevicePathPrefix(target), overwrite);

        public string PathGetFullPath(string path)
        {
            // Desired behavior:
            // 1. If path is already prefixed with \\?\, it should be left untouched
            // 2. If path is not already prefixed with \\?\, the return value should also not be prefixed
            // 3. If path is relative or has relative components, that should be resolved by calling Path.GetFullPath()
            // 4. If path is not relative and has no relative components, prefix with \\?\ to prevent normalization from munging "problematic Windows paths"
            return IsPrefixedWithExtendedDevicePathPrefix(path)
                ? path
                : RemoveExtendedDevicePathPrefix(Path.GetFullPath(AddExtendedDevicePathPrefix(path)));
        }

        public IFileEntry DirectoryEntry(string path)
            => DirectoryEntry(new DirectoryInfo(AddExtendedDevicePathPrefix(path)));

        public IFileEntry DirectoryEntry(DirectoryInfo dInfo)
            => new FileEntry(dInfo.Name, 0, dInfo.LastAccessTime, dInfo.LastWriteTime)
            {
                IsFolder = true
            };

        public IFileEntry FileEntry(string path)
            => FileEntry(new FileInfo(AddExtendedDevicePathPrefix(path)));

        public IFileEntry FileEntry(FileInfo fileInfo)
        {
            var lastAccess = new DateTime();
            try
            {
                // Internally this will convert the FILETIME value from Windows API to a
                // DateTime. If the value represents a date after 12/31/9999 it will throw
                // ArgumentOutOfRangeException, because this is not supported by DateTime.
                // Some file systems seem to set strange access timestamps on files, which
                // may lead to this exception being thrown. Since the last accessed
                // timestamp is not important such exeptions are just silently ignored.
                lastAccess = fileInfo.LastAccessTime;
            }
            catch { }
            return new FileEntry(fileInfo.Name, fileInfo.Length, lastAccess, fileInfo.LastWriteTime);
        }

        [SupportedOSPlatform("windows")]
        public Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;
            var dict = new Dictionary<string, string>();

            FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);
            var objs = new List<FileSystemAccess>();
            foreach (var f in rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
                objs.Add(new FileSystemAccess((FileSystemAccessRule)f));

            dict["win-ext:accessrules"] = SerializeObject(objs);

            // Only include the following key when its value is True.
            // This prevents unnecessary 'metadata change' detections when upgrading from
            // older versions (pre-2.0.5.101) that didn't store this value at all.
            // When key is not present, its value is presumed False by the restore code.
            if (rules.AreAccessRulesProtected)
            {
                dict["win-ext:accessrulesprotected"] = "True";
            }

            return dict;
        }

        [SupportedOSPlatform("windows")]
        public void SetMetadata(string path, Dictionary<string, string> data, bool restorePermissions)
        {
            if (restorePermissions)
            {
                var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
                var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

                FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);

                if (data.ContainsKey("win-ext:accessrulesprotected"))
                {
                    bool isProtected = bool.Parse(data["win-ext:accessrulesprotected"]);
                    if (rules.AreAccessRulesProtected != isProtected)
                        rules.SetAccessRuleProtection(isProtected, false);
                }

                if (data.ContainsKey("win-ext:accessrules"))
                {
                    var content = DeserializeObject<FileSystemAccess[]>(data["win-ext:accessrules"]);
                    var c = rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier));
                    for (var i = c.Count - 1; i >= 0; i--)
                        rules.RemoveAccessRule((FileSystemAccessRule)c[i]);

                    Exception ex = null;

                    foreach (var r in content)
                    {
                        // Attempt to apply as many rules as we can
                        try
                        {
                            rules.AddAccessRule(r.Create(rules));
                        }
                        catch (Exception e)
                        {
                            ex = e;
                        }
                    }

                    if (ex != null)
                        throw ex;
                }

                if (isDirTarget)
                    SetAccessControlDir(targetpath, (DirectorySecurity)rules);
                else
                    SetAccessControlFile(targetpath, (FileSecurity)rules);
            }
        }

        public string PathCombine(params string[] paths)
            => Path.Combine(paths);

        /// <inheritdoc />
        public bool SupportsAlternateDataStreams => true;

        /// <inheritdoc />
        public bool IsAlternateDataStream(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Extracts the root component (e.g., "C:\", "C:", "\\server\share\", or "\\?\C:\")
            var root = Path.GetPathRoot(path) ?? string.Empty;

            // If a colon exists outside of the root structure, it's an alternate data stream
            return path.AsSpan(root.Length).Contains(':');
        }

        /// <inheritdoc />
        public string GetAlternateDataStreamParent(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            // Extracts the root component (e.g., "C:\", "C:", "\\server\share\", or "\\?\C:\")
            var root = Path.GetPathRoot(path) ?? string.Empty;
            var relativePath = path.AsSpan(root.Length);

            var idx = relativePath.IndexOf(':');
            if (idx < 0)
                return path;

            return root + relativePath.Slice(0, idx).ToString();
        }

        /// <inheritdoc />
        [SupportedOSPlatform("windows")]
        public IEnumerable<string> EnumerateAlternateDataStreams(string path)
        {
            var prefixed = AddExtendedDevicePathPrefix(path);
            var data = new Win32API.WIN32_FIND_STREAM_DATA();
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = Win32API.FindFirstStreamW(prefixed, Win32API.STREAM_INFO_LEVELS.FindStreamInfoStandard, out data, 0);
                if (handle == new IntPtr(-1))
                    yield break;

                do
                {
                    // Skip the main stream (::$DATA)
                    if (data.cStreamName == "::$DATA")
                        continue;

                    // Clean the stream name by removing the :$DATA suffix
                    var streamName = data.cStreamName;
                    if (streamName.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase))
                        streamName = streamName.Substring(0, streamName.Length - ":$DATA".Length);

                    yield return streamName;
                }
                while (Win32API.FindNextStreamW(handle, out data));
            }
            finally
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                    Win32API.FindClose(handle);
            }
        }

        /// <summary>
        /// Returns the length of a file or alternate data stream.
        /// </summary>
        public long FileLength(string path)
        {
            if (!IsAlternateDataStream(path))
                return new FileInfo(AddExtendedDevicePathPrefix(path)).Length;

            using SafeFileHandle handle = Win32API.CreateFileW(
                        AddExtendedDevicePathPrefix(path),
                        Win32API.FILE_READ_ATTRIBUTES,
                        Win32API.FILE_SHARE_READ | Win32API.FILE_SHARE_WRITE | Win32API.FILE_SHARE_DELETE, // Broad sharing flags avoid collisions
                        IntPtr.Zero,
                        Win32API.OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open handle to alternative data stream ");

            // Retrieve the size directly from the stream handle
            if (!Win32API.GetFileSizeEx(handle, out long streamSize))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to retrieve alternate data stream size.");

            return streamSize;
        }

        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            if (FileExists(symlinkfile) || DirectoryExists(symlinkfile))
                throw new IOException(string.Format("File already exists: {0}", symlinkfile));


            if (asDir)
            {
                Directory.CreateSymbolicLink(AddExtendedDevicePathPrefix(symlinkfile), target);
            }
            else
            {
                File.CreateSymbolicLink(AddExtendedDevicePathPrefix(symlinkfile), target);
            }

            //Sadly we do not get a notification if the creation fails :(
            FileAttributes attr = 0;
            if ((!asDir && FileExists(symlinkfile)) || (asDir && DirectoryExists(symlinkfile)))
                try { attr = GetFileAttributes(symlinkfile); }
                catch { }

            if ((attr & FileAttributes.ReparsePoint) == 0)
                throw new IOException(string.Format("Unable to create symlink, check account permissions: {0}", symlinkfile));
        }

        /// <summary>
        /// Sets the file permissions to user read-write only.
        /// </summary>
        /// <param name="path">The file to set permissions on.</param>
        public void FileSetPermissionUserRWOnly(string path)
        {
            // Create directory security settings
            var security = new FileSecurity();

            // Remove inherited permissions to ensure only the current user has access
            security.SetAccessRuleProtection(true, false);

            var users = new[] {
                CURRENT_USER_SID,
                LOCAL_SYSTEM_SID,
                ADMINISTRATORS_SID
            }.ToHashSet();

            // Grant the users read access
            foreach (var user in users)
                security.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow
                ));

            // Explicitly set the owner to the current user so the applied state matches what
            // HasPermissionUserRWOnly verifies. Without this, the owner is whatever the
            // filesystem assigned at creation (e.g. the Administrators group or a different
            // account), which could cause the verification to reject a file we just locked down.
            TrySetOwner(security, CURRENT_USER_SID);

            // Adjust with the new security settings
            new FileInfo(path).SetAccessControl(security);
        }

        /// <summary>
        /// Attempts to set the owner of the security descriptor to the current user, so that a
        /// file or directory locked down by <see cref="FileSetPermissionUserRWOnly"/> or
        /// <see cref="DirectorySetPermissionUserRWOnly"/> has a trusted owner that
        /// <see cref="HasPermissionUserRWOnly"/> will accept. Setting the owner can require the
        /// SeRestorePrivilege in some scenarios, so failures are ignored: the existing owner may
        /// already be a trusted principal (SYSTEM or Administrators), which verification accepts.
        /// </summary>
        /// <param name="security">The security descriptor to update.</param>
        /// <param name="userId">The id of the user to set as owner</param>
        private static void TrySetOwner(FileSystemSecurity security, SecurityIdentifier userId)
        {
            try
            {
                security.SetOwner(userId);
            }
            catch
            {
                // Best-effort: the owner may already be a trusted principal, which is accepted
                // by the verification. Setting a new owner can require additional privileges.
            }
        }

        /// <summary>
        /// Sets the directory permissions to read-write only for the current user.
        /// </summary>
        /// <param name="path">The directory to set permissions on.</param>
        /// <param name="excludeCurrentUser">Do not include the current user.</param>
        public void DirectorySetPermissionUserRWOnly(string path, bool excludeCurrentUser)
        {
            // Create directory security settings
            var security = new DirectorySecurity();

            // Remove inherited permissions to ensure only the current user has access
            security.SetAccessRuleProtection(true, false);

            var self = excludeCurrentUser ? ADMINISTRATORS_SID : CURRENT_USER_SID;

            var users = new[] {
                self,
                LOCAL_SYSTEM_SID,
                ADMINISTRATORS_SID
            }.ToHashSet();

            // Grant the users full access
            foreach (var user in users)
                security.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl, // Using full-control to allow changing permissions as well
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, // Apply to subfolders & files
                    PropagationFlags.None, // Keeps inheritance settings intact
                    AccessControlType.Allow
                ));

            // Explicitly set the owner to the current user so the applied state matches what
            // HasPermissionUserRWOnly verifies. Without this, the owner is whatever the
            // filesystem assigned at creation (e.g. the Administrators group or a different
            // account), which could cause the verification to reject a folder we just locked down.
            TrySetOwner(security, self);

            // Adjust with the new security settings
            new DirectoryInfo(path).SetAccessControl(security);
        }


        /// <summary>
        /// Checks whether the directory has the read-write only permission for the current user,
        /// matching the permissions applied by <see cref="DirectorySetPermissionUserRWOnly"/>.
        /// </summary>
        /// <param name="path">The directory to check permissions on.</param>
        /// <param name="excludeCurrentUser">Do not accept the current user as part of the security check</param>
        /// <param name="detail">A human-readable description of why the check failed, if it did; otherwise <see cref="string.Empty"/>.</param>
        /// <returns><c>true</c> if the directory has the expected permissions; otherwise <c>false</c>.</returns>
        public bool DirectoryHasPermissionUserRWOnly(string path, bool excludeCurrentUser, out string detail)
            => HasPermissionUserRWOnly(new DirectoryInfo(path).GetAccessControl(), excludeCurrentUser, "folder", out detail);

        /// <summary>
        /// Verifies that a file or directory access control object grants full control to
        /// only the current user, SYSTEM and Administrators, has inheritance disabled, and is
        /// owned by one of those principals.
        /// </summary>
        /// <param name="security">The access control object for the file or directory.</param>
        /// <param name="excludeCurrentUser">Do not accept the current user as part of the security check</param>
        /// <param name="kind">A label such as "file" or "folder" used in detail messages.</param>
        /// <param name="detail">A human-readable description of why the check failed, if it did; otherwise <see cref="string.Empty"/>.</param>
        /// <returns><c>true</c> if the expected permissions are set; otherwise <c>false</c>.</returns>
        private static bool HasPermissionUserRWOnly(FileSystemSecurity security, bool excludeCurrentUser, string kind, out string detail)
        {
            detail = string.Empty;
            try
            {
                // The owner must be one of the trusted principals. An unexpected owner
                // can modify the DACL regardless of the explicit access rules, which
                // would allow them to grant themselves access later. Any of these principals
                // is privileged and cannot be impersonated by an unprivileged attacker, so all
                // three are accepted. DirectorySetPermissionUserRWOnly explicitly sets the owner
                // to the current user, so a folder we lock down always satisfies this check.
                var expected = new[] { excludeCurrentUser ? ADMINISTRATORS_SID : CURRENT_USER_SID, LOCAL_SYSTEM_SID, ADMINISTRATORS_SID }.ToHashSet();
                var ownerRef = security.GetOwner(typeof(SecurityIdentifier));
                var owner = ownerRef as SecurityIdentifier;
                if (owner == null || !expected.Contains(owner))
                {
                    detail = $"the {kind} owner is {(ownerRef == null ? "(unknown)" : ownerRef.Value)} but expected one of SYSTEM, Administrators or the current user";
                    return false;
                }

                // Inheritance must be disabled (protected from parent), matching SetAccessRuleProtection(true, false)
                if (!security.AreAccessRulesProtected)
                {
                    detail = $"the {kind} inherits permissions from its parent";
                    return false;
                }

                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
                var found = new HashSet<SecurityIdentifier>();

                foreach (var rule in rules.Cast<FileSystemAccessRule>())
                {
                    // Any deny rule means the entry is not in the expected secure state
                    if (rule.AccessControlType == AccessControlType.Deny)
                    {
                        detail = $"a deny access rule is present for {rule.IdentityReference}";
                        return false;
                    }

                    // Only allow rules for the expected SIDs are permitted
                    if (!expected.Contains(rule.IdentityReference))
                    {
                        detail = $"an unexpected access rule grants rights to {rule.IdentityReference}";
                        return false;
                    }

                    found.Add((SecurityIdentifier)rule.IdentityReference);
                }

                // All expected SIDs must be present
                if (found.Count != expected.Count)
                {
                    var missing = expected.Except(found).Select(x => x.ToString());
                    detail = $"missing access rules for: {string.Join(", ", missing)}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                detail = $"failed to read permissions: {ex.Message}";
                return false;
            }

            return true;
        }
        #endregion
    }
}

