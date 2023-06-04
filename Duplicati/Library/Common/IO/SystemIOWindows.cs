//  Copyright (C) 2015, The Duplicati Team

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
using System.Collections.Generic;
using System.Security.AccessControl;
using System.IO;
using System.Linq;

using AlphaFS = Alphaleonis.Win32.Filesystem;
using Duplicati.Library.Interface;
using Newtonsoft.Json;

namespace Duplicati.Library.Common.IO
{
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

            public FileSystemAccessRule Create(System.Security.AccessControl.FileSystemSecurity owner)
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

        private static Newtonsoft.Json.JsonSerializer _cachedSerializer;

        private Newtonsoft.Json.JsonSerializer Serializer
        {
            get
            {
                if (_cachedSerializer != null)
                {
                    return _cachedSerializer;
                }

                _cachedSerializer = Newtonsoft.Json.JsonSerializer.Create(
                    new Newtonsoft.Json.JsonSerializerSettings { Culture = System.Globalization.CultureInfo.InvariantCulture });

                return _cachedSerializer;
            }
        }

        private string SerializeObject<T>(T o)
        {
            using (var tw = new System.IO.StringWriter())
            {
                Serializer.Serialize(tw, o);
                tw.Flush();
                return tw.ToString();
            }
        }

        private T DeserializeObject<T>(string data)
        {
            using (var tr = new System.IO.StringReader(data))
            {
                return (T)Serializer.Deserialize(tr, typeof(T));
            }
        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlDir(string path)
        {
            return System.IO.Directory.GetAccessControl(AddExtendedDevicePathPrefix(path));
        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlFile(string path)
        {
            return System.IO.File.GetAccessControl(AddExtendedDevicePathPrefix(path));
        }

        private void SetAccessControlFile(string path, FileSecurity rules)
        {
            System.IO.File.SetAccessControl(AddExtendedDevicePathPrefix(path), rules);
        }

        private void SetAccessControlDir(string path, DirectorySecurity rules)
        {
            System.IO.Directory.SetAccessControl(AddExtendedDevicePathPrefix(path), rules);
        }

        #region ISystemIO implementation
        public void DirectoryCreate(string path)
        {
            System.IO.Directory.CreateDirectory(AddExtendedDevicePathPrefix(path));
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            System.IO.Directory.Delete(AddExtendedDevicePathPrefix(path), recursive);
        }

        public bool DirectoryExists(string path)
        {
            return System.IO.Directory.Exists(AddExtendedDevicePathPrefix(path));
        }

        public void DirectoryMove(string sourceDirName, string destDirName)
        {
            System.IO.Directory.Move(AddExtendedDevicePathPrefix(sourceDirName), AddExtendedDevicePathPrefix(destDirName));
        }

        public void FileDelete(string path)
        {
            System.IO.File.Delete(AddExtendedDevicePathPrefix(path));
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            System.IO.File.SetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            System.IO.File.SetCreationTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            return System.IO.File.GetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            return System.IO.File.GetCreationTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public bool FileExists(string path)
        {
            return System.IO.File.Exists(AddExtendedDevicePathPrefix(path));
        }

        public System.IO.FileStream FileOpenRead(string path)
        {
            return System.IO.File.Open(AddExtendedDevicePathPrefix(path), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        }

        public System.IO.FileStream FileOpenWrite(string path)
        {
            return !FileExists(path)
                ? FileCreate(path)
                : System.IO.File.OpenWrite(AddExtendedDevicePathPrefix(path));
        }

        public System.IO.FileStream FileCreate(string path)
        {
            return System.IO.File.Create(AddExtendedDevicePathPrefix(path));
        }

        public System.IO.FileAttributes GetFileAttributes(string path)
        {
            return System.IO.File.GetAttributes(AddExtendedDevicePathPrefix(path));
        }

        public void SetFileAttributes(string path, System.IO.FileAttributes attributes)
        {
            System.IO.File.SetAttributes(AddExtendedDevicePathPrefix(path), attributes);
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="file">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public string GetSymlinkTarget(string file)
        {
            try
            {
                return AlphaFS.File.GetLinkTargetInfo(AddExtendedDevicePathPrefix(file)).PrintName;
            }
            catch (AlphaFS.NotAReparsePointException) { }
            catch (AlphaFS.UnrecognizedReparsePointException) { }

            // This path looks like it isn't actually a symlink
            // (Note that some reparse points aren't actually symlinks -
            // things like the OneDrive folder in the Windows 10 Fall Creator's Update for example)
            return null;
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            return System.IO.Directory.EnumerateFileSystemEntries(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);
        }

        public IEnumerable<string> EnumerateFiles(string path)
        {
            return System.IO.Directory.EnumerateFiles(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return System.IO.Directory.EnumerateFiles(AddExtendedDevicePathPrefix(path), searchPattern,  searchOption).Select(RemoveExtendedDevicePathPrefix);
        }

        public string PathGetFileName(string path)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.GetFileName(AddExtendedDevicePathPrefix(path)));
        }

        public string PathGetFileNameWithoutExtension(string path)
        {
            return RemoveExtendedDevicePathPrefix(Path.GetFileNameWithoutExtension(AddExtendedDevicePathPrefix(path)));
        }

        public string PathGetDirectoryName(string path)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.GetDirectoryName(AddExtendedDevicePathPrefix(path)));
        }

        public string PathGetExtension(string path)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.GetExtension(AddExtendedDevicePathPrefix(path)));
        }

        public string PathChangeExtension(string path, string extension)
        {
            return RemoveExtendedDevicePathPrefix(System.IO.Path.ChangeExtension(AddExtendedDevicePathPrefix(path), extension));
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            System.IO.Directory.SetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            System.IO.Directory.SetCreationTimeUtc(AddExtendedDevicePathPrefix(path), time);
        }

        public void FileMove(string source, string target)
        {
            System.IO.File.Move(AddExtendedDevicePathPrefix(source), AddExtendedDevicePathPrefix(target));
        }

        public long FileLength(string path)
        {
            return new System.IO.FileInfo(AddExtendedDevicePathPrefix(path)).Length;
        }

        public string GetPathRoot(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Path.GetPathRoot(path);
            }
            else
            {
                return RemoveExtendedDevicePathPrefix(Path.GetPathRoot(AddExtendedDevicePathPrefix(path)));
            }
        }

        public string[] GetDirectories(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.GetDirectories(path);
            }
            else
            {
                return Directory.GetDirectories(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix).ToArray();
            }
        }

        public string[] GetFiles(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.GetFiles(path);
            }
            else
            {
                return Directory.GetFiles(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix).ToArray();
            }
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.GetFiles(path, searchPattern);
            }
            else
            {
                return Directory.GetFiles(AddExtendedDevicePathPrefix(path), searchPattern).Select(RemoveExtendedDevicePathPrefix).ToArray();
            }
        }

        public DateTime GetCreationTimeUtc(string path)
        {
            return Directory.GetCreationTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return Directory.GetLastWriteTimeUtc(AddExtendedDevicePathPrefix(path));
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return Directory.EnumerateDirectories(path);
            }
            else
            {
                return Directory.EnumerateDirectories(AddExtendedDevicePathPrefix(path)).Select(RemoveExtendedDevicePathPrefix);
            }
        }

        public void FileCopy(string source, string target, bool overwrite)
        {
            File.Copy(AddExtendedDevicePathPrefix(source), AddExtendedDevicePathPrefix(target), overwrite);
        }

        public string PathGetFullPath(string path)
        {
            // Desired behavior:
            // 1. If path is already prefixed with \\?\, it should be left untouched
            // 2. If path is not already prefixed with \\?\, the return value should also not be prefixed
            // 3. If path is relative or has relative components, that should be resolved by calling Path.GetFullPath()
            // 4. If path is not relative and has no relative components, prefix with \\?\ to prevent normalization from munging "problematic Windows paths"
            if (IsPrefixedWithExtendedDevicePathPrefix(path))
            {
                return path;
            }
            else
            {
                return RemoveExtendedDevicePathPrefix(Path.GetFullPath(AddExtendedDevicePathPrefix(path)));
            }
        }

        public IFileEntry DirectoryEntry(string path)
        {
            var dInfo = new DirectoryInfo(AddExtendedDevicePathPrefix(path));
            return new FileEntry(dInfo.Name, 0, dInfo.LastAccessTime, dInfo.LastWriteTime)
            {
                IsFolder = true
            };
        }

        public IFileEntry FileEntry(string path)
        {
            var fileInfo = new FileInfo(AddExtendedDevicePathPrefix(path));
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

        public void SetMetadata(string path, Dictionary<string, string> data, bool restorePermissions)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

            if (restorePermissions)
            {
                FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);

                if (data.ContainsKey("win-ext:accessrulesprotected"))
                {
                    bool isProtected = bool.Parse(data["win-ext:accessrulesprotected"]);
                    if (rules.AreAccessRulesProtected != isProtected)
                    {
                        rules.SetAccessRuleProtection(isProtected, false);
                    }
                }

                if (data.ContainsKey("win-ext:accessrules"))
                {
                    var content = DeserializeObject<FileSystemAccess[]>(data["win-ext:accessrules"]);
                    var c = rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier));
                    for (var i = c.Count - 1; i >= 0; i--)
                        rules.RemoveAccessRule((System.Security.AccessControl.FileSystemAccessRule)c[i]);

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
        {
            return Path.Combine(paths);
        }

        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            if (FileExists(symlinkfile) || DirectoryExists(symlinkfile))
                throw new System.IO.IOException(string.Format("File already exists: {0}", symlinkfile));

            if (asDir)
            {
                Alphaleonis.Win32.Filesystem.Directory.CreateSymbolicLink(AddExtendedDevicePathPrefix(symlinkfile), target, AlphaFS.PathFormat.LongFullPath);
            }
            else
            {
                Alphaleonis.Win32.Filesystem.File.CreateSymbolicLink(AddExtendedDevicePathPrefix(symlinkfile), target, AlphaFS.PathFormat.LongFullPath);
            }

            //Sadly we do not get a notification if the creation fails :(
            System.IO.FileAttributes attr = 0;
            if ((!asDir && FileExists(symlinkfile)) || (asDir && DirectoryExists(symlinkfile)))
                try { attr = GetFileAttributes(symlinkfile); }
                catch { }

            if ((attr & System.IO.FileAttributes.ReparsePoint) == 0)
                throw new System.IO.IOException(string.Format("Unable to create symlink, check account permissions: {0}", symlinkfile));
        }
        #endregion
    }
}

