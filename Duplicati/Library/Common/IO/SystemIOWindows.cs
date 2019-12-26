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
        private const string UNCPREFIX = @"\\?\";
        private const string UNCPREFIX_SERVER = @"\\?\UNC\";
        private const string PATHPREFIX_SERVER = @"\\";
        private static readonly string DIRSEP = Util.DirectorySeparatorString;

        private static bool IsPathTooLong(string path)
        {
            // Use 258 for length check instead of 260 (MAX_PATH) - we need to leave room for the 16-bit (wide) null terminator
            return path.StartsWith(UNCPREFIX, StringComparison.Ordinal) || path.StartsWith(UNCPREFIX_SERVER, StringComparison.Ordinal) || path.Length > 258;
        }

        public static string PrefixWithUNC(string path)
        {
            if (path.StartsWith(UNCPREFIX_SERVER, StringComparison.Ordinal))
                return path;

            if (path.StartsWith(UNCPREFIX, StringComparison.Ordinal))
                return path;

            return path.StartsWith(PATHPREFIX_SERVER, StringComparison.Ordinal)
                ? UNCPREFIX_SERVER + path.Remove(0, PATHPREFIX_SERVER.Length)
                : UNCPREFIX + path;
        }

        public static string StripUNCPrefix(string path)
        {
            return path.StartsWith(UNCPREFIX, StringComparison.Ordinal) ? path.Substring(UNCPREFIX.Length) : path;
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

        private class FileSystemAccessProtected
        {
            [JsonProperty]
            public readonly bool IsProtected;

            public FileSystemAccessProtected(bool Protected)
            {
                IsProtected = Protected;
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
            return PathTooLongFuncWrapper(System.IO.Directory.GetAccessControl,
                                          Alphaleonis.Win32.Filesystem.Directory.GetAccessControl, path, true);
        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlFile(string path)
        {
            return PathTooLongFuncWrapper(System.IO.File.GetAccessControl,
                                          Alphaleonis.Win32.Filesystem.File.GetAccessControl, path, true);
        }

        private void SetAccessControlFile(string path, FileSecurity rules)
        {
            PathTooLongActionWrapper(p => System.IO.File.SetAccessControl(p, rules),
                                     p => Alphaleonis.Win32.Filesystem.File.SetAccessControl(p, rules, AccessControlSections.All),
                                     path, true);
        }

        private void SetAccessControlDir(string path, DirectorySecurity rules)
        {
            PathTooLongActionWrapper(p => System.IO.Directory.SetAccessControl(p, rules),
                                     p => Alphaleonis.Win32.Filesystem.Directory.SetAccessControl(p, rules, AccessControlSections.All),
                                     path, true);
        }

        private static void PathTooLongVoidFuncWrapper<U, T>(Func<string, T> nativeIOFunc,
                                                    Func<string, U> alternativeIOFunc,
                                                    string path, bool prefixWithUnc = false)
        {
            // Wrap void into bool return type to avoid code duplication. Code at anytime available for replacement.
            PathTooLongFuncWrapper(p => { nativeIOFunc(p); return true; }, p => { alternativeIOFunc(p); return true; } , path, prefixWithUnc);
        }

        private static void PathTooLongActionWrapper(Action<string> nativeIOFunc,
                                                     Action<string> alternativeIOFunc,
                                                     string path, bool prefixWithUnc = false)
        {
            // Wrap void into bool return type to avoid code duplication. Code at anytime available for replacement.
            PathTooLongFuncWrapper(p => { nativeIOFunc(p); return true; }, p => { alternativeIOFunc(p); return true; }, path, prefixWithUnc);
        }

        private static T PathTooLongFuncWrapper<T>(Func<string, T> nativeIOFunc,
                                                   Func<string, T> alternativeIOFunc,
                                                   string path, bool prefixWithUnc = false)
        {
            if (!IsPathTooLong(path))
                try { return nativeIOFunc(path); }
                catch (System.IO.PathTooLongException) { }
                catch (System.ArgumentException) { }

            return !prefixWithUnc ? alternativeIOFunc(path) : alternativeIOFunc(PrefixWithUNC(path));
        }

        #region ISystemIO implementation
        public void DirectoryDelete(string path)
        {
            PathTooLongActionWrapper(System.IO.Directory.Delete,
                                     Alphaleonis.Win32.Filesystem.Directory.Delete, path, true);
        }

        public void DirectoryCreate(string path)
        {
            PathTooLongVoidFuncWrapper(System.IO.Directory.CreateDirectory,
                                       Alphaleonis.Win32.Filesystem.Directory.CreateDirectory, path, true);
        }

        public bool DirectoryExists(string path)
        {
            return PathTooLongFuncWrapper(System.IO.Directory.Exists,
                                          Alphaleonis.Win32.Filesystem.Directory.Exists, path, true);
        }

        public void FileDelete(string path)
        {
            PathTooLongActionWrapper(System.IO.File.Delete,
                                     Alphaleonis.Win32.Filesystem.File.Delete, path, true);
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            PathTooLongActionWrapper(p => System.IO.File.SetLastWriteTimeUtc(p, time),
                                     p => Alphaleonis.Win32.Filesystem.File.SetLastWriteTimeUtc(p, time), path, true);
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            PathTooLongActionWrapper(p => System.IO.File.SetCreationTimeUtc(p, time),
                                     p => Alphaleonis.Win32.Filesystem.File.SetCreationTimeUtc(p, time), path, true);
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            return PathTooLongFuncWrapper(System.IO.File.GetLastWriteTimeUtc,
                                          Alphaleonis.Win32.Filesystem.File.GetLastWriteTimeUtc, path, true);
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            return PathTooLongFuncWrapper(System.IO.File.GetCreationTimeUtc,
                                          Alphaleonis.Win32.Filesystem.File.GetCreationTimeUtc, path, true);
        }

        public bool FileExists(string path)
        {
            return PathTooLongFuncWrapper(System.IO.File.Exists,
                                          Alphaleonis.Win32.Filesystem.File.Exists, path, true);
        }

        public System.IO.FileStream FileOpenRead(string path)
        {
            return PathTooLongFuncWrapper(p => System.IO.File.Open(p, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite),
                                   p => Alphaleonis.Win32.Filesystem.File.Open(p, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite),
                                   path, true);
        }

        public System.IO.FileStream FileOpenWrite(string path)
        {
            return !FileExists(path)
                ? FileCreate(path)
                : PathTooLongFuncWrapper(System.IO.File.OpenWrite, Alphaleonis.Win32.Filesystem.File.OpenWrite, path, true);
        }

        public System.IO.FileStream FileOpenReadWrite(string path)
        {
            return PathTooLongFuncWrapper(p => System.IO.File.Open(p, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.Read),
                                   p => Alphaleonis.Win32.Filesystem.File.Open(p, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read), path, true);
        }

        public System.IO.FileStream FileCreate(string path)
        {
            return PathTooLongFuncWrapper(System.IO.File.Create, Alphaleonis.Win32.Filesystem.File.Create, path, true);
        }

        public System.IO.FileAttributes GetFileAttributes(string path)
        {
            return PathTooLongFuncWrapper(System.IO.File.GetAttributes, AlphaFS.File.GetAttributes, path, true);
        }

        public void SetFileAttributes(string path, System.IO.FileAttributes attributes)
        {
            PathTooLongActionWrapper(p => System.IO.File.SetAttributes(p, attributes),
                                      p => Alphaleonis.Win32.Filesystem.File.SetAttributes(p, attributes), path, true);
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
                return PathTooLongFuncWrapper(AlphaFS.File.GetLinkTargetInfo, AlphaFS.File.GetLinkTargetInfo, file, true).PrintName;
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
            return PathTooLongFuncWrapper(System.IO.Directory.EnumerateFileSystemEntries,
                                          Alphaleonis.Win32.Filesystem.Directory.GetFileSystemEntries,
                                          path, true).Select(StripUNCPrefix).AsEnumerable();
        }

        public IEnumerable<string> EnumerateFiles(string path)
        {
            return PathTooLongFuncWrapper(System.IO.Directory.EnumerateFiles,
                                          Alphaleonis.Win32.Filesystem.Directory.GetFiles, path,
                                          true).Select(StripUNCPrefix).AsEnumerable();
        }

        public string PathGetFileName(string path)
        {
            return StripUNCPrefix(PathTooLongFuncWrapper(System.IO.Path.GetFileName,
                                                         Alphaleonis.Win32.Filesystem.Path.GetFileName,
                                                         path, true));
        }

        public string PathGetDirectoryName(string path)
        {
            return StripUNCPrefix(PathTooLongFuncWrapper(System.IO.Path.GetDirectoryName,
                                                         Alphaleonis.Win32.Filesystem.Path.GetDirectoryName,
                                                         path, true));
        }

        public string PathGetExtension(string path)
        {
            return StripUNCPrefix(PathTooLongFuncWrapper(System.IO.Path.GetExtension,
                                                         Alphaleonis.Win32.Filesystem.Path.GetExtension,
                                                         path, true));
        }

        public string PathChangeExtension(string path, string extension)
        {
            return StripUNCPrefix(PathTooLongFuncWrapper(p => System.IO.Path.ChangeExtension(p, extension),
                                                         p => Alphaleonis.Win32.Filesystem.Path.ChangeExtension(p, extension),
                                                         path, true));
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            PathTooLongActionWrapper(p => System.IO.Directory.SetLastWriteTimeUtc(p, time),
                                     p => Alphaleonis.Win32.Filesystem.File.SetLastWriteTimeUtc(p, time), path, true);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            PathTooLongActionWrapper(p => System.IO.Directory.SetCreationTimeUtc(p, time),
                                     p => Alphaleonis.Win32.Filesystem.File.SetCreationTimeUtc(p, time), path, true);
        }

        public DateTime DirectoryGetLastWriteTimeUtc(string path)
        {
            return PathTooLongFuncWrapper(System.IO.Directory.GetLastWriteTimeUtc,
                                          Alphaleonis.Win32.Filesystem.Directory.GetLastWriteTimeUtc, path, true);
        }

        public DateTime DirectoryGetCreationTimeUtc(string path)
        {
            return PathTooLongFuncWrapper(System.IO.Directory.GetCreationTimeUtc,
                                          Alphaleonis.Win32.Filesystem.Directory.GetCreationTimeUtc, path, true);
        }

        public void FileMove(string source, string target)
        {
            // We do not check if path is too long on target. If so, then we catch an exception.
            PathTooLongActionWrapper(p => System.IO.File.Move(p, target),
                                     p => Alphaleonis.Win32.Filesystem.File.Move(p, PrefixWithUNC(target)),
                                     source, true);
        }

        public long FileLength(string path)
        {
            return PathTooLongFuncWrapper(p => new System.IO.FileInfo(p).Length,
                                          p => new Alphaleonis.Win32.Filesystem.FileInfo(p).Length,
                                          path, true);
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            PathTooLongActionWrapper(p => System.IO.Directory.Delete(p, recursive),
                                     p => Alphaleonis.Win32.Filesystem.Directory.Delete(p, recursive),
                                     path, true);
        }

        public string GetPathRoot(string path)
        {
            return PathTooLongFuncWrapper(Path.GetPathRoot, AlphaFS.Path.GetPathRoot, path, false);
        }

        public string[] GetDirectories(string path)
        {
            return PathTooLongFuncWrapper(Directory.GetDirectories, AlphaFS.Directory.GetDirectories, path, false);
        }

        public string[] GetFiles(string path)
        {
            return PathTooLongFuncWrapper(Directory.GetFiles, AlphaFS.Directory.GetFiles, path, false);
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            return PathTooLongFuncWrapper(p => Directory.GetFiles(p, searchPattern), p => AlphaFS.Directory.GetFiles(p, searchPattern), path, false);
        }

        public DateTime GetCreationTimeUtc(string path)
        {
            return PathTooLongFuncWrapper(Directory.GetCreationTimeUtc, AlphaFS.File.GetCreationTimeUtc, path, false);
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return PathTooLongFuncWrapper(Directory.GetLastWriteTimeUtc, AlphaFS.File.GetLastWriteTimeUtc, path, false);
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            return PathTooLongFuncWrapper(Directory.EnumerateDirectories, AlphaFS.Directory.EnumerateDirectories, path, false);
        }

        public void FileCopy(string source, string target, bool overwrite)
        {
            // We do not check if path is too long on target. If so, then we catch an exception.
            PathTooLongActionWrapper(p => File.Copy(p, target, overwrite), p => AlphaFS.File.Copy(p, target, overwrite), source, false);
        }

        public string PathGetFullPath(string path)
        {
            return PathTooLongFuncWrapper(System.IO.Path.GetFullPath, AlphaFS.Path.GetFullPath, path, false);
        }

        public IFileEntry DirectoryEntry(string path)
        {
            IFileEntry DirectoryEntryNative(string p) {
                var dInfo = new DirectoryInfo(p);
                return new FileEntry(dInfo.Name, 0, dInfo.LastAccessTime, dInfo.LastWriteTime)
                {
                    IsFolder = true
                };
            }

            IFileEntry DirectoryEntryAlphaFS(string p)
            {
                var dInfoAlphaFS = new AlphaFS.DirectoryInfo(p);
                return new FileEntry(dInfoAlphaFS.Name, 0, dInfoAlphaFS.LastAccessTime, dInfoAlphaFS.LastWriteTime)
                {
                    IsFolder = true
                };
            }

            return PathTooLongFuncWrapper(DirectoryEntryNative, DirectoryEntryAlphaFS, path, false);
        }

        public IFileEntry FileEntry(string path)
        {
            IFileEntry FileEntryNative(string p)
            {
                var fileInfo = new FileInfo(p);
                return new FileEntry(fileInfo.Name, fileInfo.Length, fileInfo.LastAccessTime, fileInfo.LastWriteTime);
            }

            IFileEntry FileEntryAlphaFS(string p)
            {
                var fInfoAlphaFS = new AlphaFS.FileInfo(p);
                return new FileEntry(fInfoAlphaFS.Name, fInfoAlphaFS.Length, fInfoAlphaFS.LastAccessTime, fInfoAlphaFS.LastWriteTime);
            }

            return PathTooLongFuncWrapper(FileEntryNative, FileEntryAlphaFS, path, false);
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
            dict["win-ext:accessrulesprotected"] = SerializeObject(new FileSystemAccessProtected(rules.AreAccessRulesProtected));

            return dict;
        }

        public void SetMetadata(string path, Dictionary<string, string> data, bool restorePermissions)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

            FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);
            if (restorePermissions && data.ContainsKey("win-ext:accessrulesprotected"))
            {
                var content = DeserializeObject<FileSystemAccessProtected>(data["win-ext:accessrulesprotected"]);
                if (rules.AreAccessRulesProtected != content.IsProtected)
                {
                    rules.SetAccessRuleProtection(content.IsProtected, false);
                }
            }

            if (restorePermissions && data.ContainsKey("win-ext:accessrules"))
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

                if (isDirTarget)
                    SetAccessControlDir(targetpath, (DirectorySecurity)rules);
                else
                    SetAccessControlFile(targetpath, (FileSecurity)rules);
            }
        }

        public string PathCombine(params string[] paths)
        {
            var combinedPath = "";
            for (int i = 0; i < paths.Length; i++)
            {
                if (i == 0)
                {
                    combinedPath = paths[i];
                }
                else
                {
                    if (!IsPathTooLong(combinedPath + "\\" + paths[i]))
                    {
                        try
                        {
                            combinedPath = Path.Combine(combinedPath, paths[i]);
                        }
                        catch (Exception ex) when (ex is System.IO.PathTooLongException || ex is System.ArgumentException)
                        {
                            //TODO: Explain why we need to keep prefixing and stripping UNC's.
                            combinedPath = StripUNCPrefix(Alphaleonis.Win32.Filesystem.Path.Combine(PrefixWithUNC(combinedPath), paths[i]));
                        }
                    }
                }
            }

            return combinedPath;
        }

        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            if (FileExists(symlinkfile) || DirectoryExists(symlinkfile))
                throw new System.IO.IOException(string.Format("File already exists: {0}", symlinkfile));

            if (asDir)
            {
                Alphaleonis.Win32.Filesystem.Directory.CreateSymbolicLink(PrefixWithUNC(symlinkfile), target, AlphaFS.PathFormat.LongFullPath);
            }
            else
            {
                Alphaleonis.Win32.Filesystem.File.CreateSymbolicLink(PrefixWithUNC(symlinkfile), target, AlphaFS.PathFormat.LongFullPath);
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

