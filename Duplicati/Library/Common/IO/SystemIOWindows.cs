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
using Duplicati.Library.IO.WindowsFileMetadata;

namespace Duplicati.Library.Common.IO
{
    public class SystemIOWindows : SystemIOWindowsBase, ISystemIO
    {
        private System.Security.AccessControl.FileSystemSecurity GetAccessControlDir(string path)
        {
            return System.IO.Directory.GetAccessControl(PrefixWithUNC(path));
        }

        private System.Security.AccessControl.FileSystemSecurity GetAccessControlFile(string path)
        {
            return System.IO.File.GetAccessControl(PrefixWithUNC(path));
        }

        private void SetAccessControlFile(string path, FileSecurity rules)
        {
            System.IO.File.SetAccessControl(PrefixWithUNC(path), rules);
        }

        private void SetAccessControlDir(string path, DirectorySecurity rules)
        {
            System.IO.Directory.SetAccessControl(PrefixWithUNC(path), rules);
        }

        #region ISystemIO implementation
        public void DirectoryCreate(string path)
        {
            System.IO.Directory.CreateDirectory(PrefixWithUNC(path));
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            System.IO.Directory.Delete(PrefixWithUNC(path), recursive);
        }

        public bool DirectoryExists(string path)
        {
            return System.IO.Directory.Exists(PrefixWithUNC(path));
        }

        public void DirectoryMove(string sourceDirName, string destDirName)
        {
            System.IO.Directory.Move(PrefixWithUNC(sourceDirName), PrefixWithUNC(destDirName));
        }

        public void FileDelete(string path)
        {
            System.IO.File.Delete(PrefixWithUNC(path));
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            System.IO.File.SetLastWriteTimeUtc(PrefixWithUNC(path), time);
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            System.IO.File.SetCreationTimeUtc(PrefixWithUNC(path), time);
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            return System.IO.File.GetLastWriteTimeUtc(PrefixWithUNC(path));
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            return System.IO.File.GetCreationTimeUtc(PrefixWithUNC(path));
        }

        public bool FileExists(string path)
        {
            return System.IO.File.Exists(PrefixWithUNC(path));
        }

        public System.IO.FileStream FileOpenRead(string path)
        {
            return System.IO.File.Open(PrefixWithUNC(path), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        }

        public System.IO.FileStream FileOpenWrite(string path)
        {
            return !FileExists(path)
                ? FileCreate(path)
                : System.IO.File.OpenWrite(PrefixWithUNC(path));
        }

        public System.IO.FileStream FileCreate(string path)
        {
            return System.IO.File.Create(PrefixWithUNC(path));
        }

        public System.IO.FileAttributes GetFileAttributes(string path)
        {
            return System.IO.File.GetAttributes(PrefixWithUNC(path));
        }

        public void SetFileAttributes(string path, System.IO.FileAttributes attributes)
        {
            System.IO.File.SetAttributes(PrefixWithUNC(path), attributes);
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
                return AlphaFS.File.GetLinkTargetInfo(PrefixWithUNC(file)).PrintName;
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
            return System.IO.Directory.EnumerateFileSystemEntries(PrefixWithUNC(path)).Select(StripUNCPrefix);
        }

        public IEnumerable<string> EnumerateFiles(string path)
        {
            return System.IO.Directory.EnumerateFiles(PrefixWithUNC(path)).Select(StripUNCPrefix);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return System.IO.Directory.EnumerateFiles(PrefixWithUNC(path), searchPattern, searchOption).Select(StripUNCPrefix);
        }

        public string PathGetFileName(string path)
        {
            return StripUNCPrefix(System.IO.Path.GetFileName(PrefixWithUNC(path)));
        }

        public string PathGetDirectoryName(string path)
        {
            return StripUNCPrefix(System.IO.Path.GetDirectoryName(PrefixWithUNC(path)));
        }

        public string PathGetExtension(string path)
        {
            return StripUNCPrefix(System.IO.Path.GetExtension(PrefixWithUNC(path)));
        }

        public string PathChangeExtension(string path, string extension)
        {
            return StripUNCPrefix(System.IO.Path.ChangeExtension(PrefixWithUNC(path), extension));
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            System.IO.Directory.SetLastWriteTimeUtc(PrefixWithUNC(path), time);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            System.IO.Directory.SetCreationTimeUtc(PrefixWithUNC(path), time);
        }

        public void FileMove(string source, string target)
        {
            System.IO.File.Move(PrefixWithUNC(source), PrefixWithUNC(target));
        }

        public long FileLength(string path)
        {
            return new System.IO.FileInfo(PrefixWithUNC(path)).Length;
        }

        public string GetPathRoot(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return Path.GetPathRoot(path);
            }
            else
            {
                return StripUNCPrefix(Path.GetPathRoot(PrefixWithUNC(path)));
            }
        }

        public string[] GetDirectories(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return Directory.GetDirectories(path);
            }
            else
            {
                return Directory.GetDirectories(PrefixWithUNC(path)).Select(StripUNCPrefix).ToArray();
            }
        }

        public string[] GetFiles(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return Directory.GetFiles(path);
            }
            else
            {
                return Directory.GetFiles(PrefixWithUNC(path)).Select(StripUNCPrefix).ToArray();
            }
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            if (IsPrefixedWithUNC(path))
            {
                return Directory.GetFiles(path, searchPattern);
            }
            else
            {
                return Directory.GetFiles(PrefixWithUNC(path), searchPattern).Select(StripUNCPrefix).ToArray();
            }
        }

        public DateTime GetCreationTimeUtc(string path)
        {
            return Directory.GetCreationTimeUtc(PrefixWithUNC(path));
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return Directory.GetLastWriteTimeUtc(PrefixWithUNC(path));
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return Directory.EnumerateDirectories(path);
            }
            else
            {
                return Directory.EnumerateDirectories(PrefixWithUNC(path)).Select(StripUNCPrefix);
            }
        }

        public void FileCopy(string source, string target, bool overwrite)
        {
            File.Copy(PrefixWithUNC(source), PrefixWithUNC(target), overwrite);
        }

        public string PathGetFullPath(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return System.IO.Path.GetFullPath(ConvertSlashes(path));
            }
            else
            {
                return StripUNCPrefix(System.IO.Path.GetFullPath(PrefixWithUNC(ConvertSlashes(path))));
            }
        }

        public IFileEntry DirectoryEntry(string path)
        {
            var dInfo = new DirectoryInfo(PrefixWithUNC(path));
            return new FileEntry(dInfo.Name, 0, dInfo.LastAccessTime, dInfo.LastWriteTime)
            {
                IsFolder = true
            };
        }

        public IFileEntry FileEntry(string path)
        {
            var fileInfo = new FileInfo(PrefixWithUNC(path));
            return new FileEntry(fileInfo.Name, fileInfo.Length, fileInfo.LastAccessTime, fileInfo.LastWriteTime);
        }

        public Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;
            var dict = new Dictionary<string, string>();

            FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);
            var objs = new List<FileSystemAccessModel>();
            foreach (var f in rules.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
                objs.Add(new FileSystemAccessModel((FileSystemAccessRule)f));

            dict[MetadataAccessRulesKey] = SerializeObject(objs);

            // Only include the following key when its value is True.
            // This prevents unnecessary 'metadata change' detections when upgrading from
            // older versions (pre-2.0.5.101) that didn't store this value at all.
            // When key is not present, its value is presumed False by the restore code.
            if (rules.AreAccessRulesProtected)
            {
                dict[MetadataAccessRulesIsProtectedKey] = "True";
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

                if (data.ContainsKey(MetadataAccessRulesIsProtectedKey))
                {
                    bool isProtected = bool.Parse(data[MetadataAccessRulesIsProtectedKey]);
                    if (rules.AreAccessRulesProtected != isProtected)
                    {
                        rules.SetAccessRuleProtection(isProtected, false);
                    }
                }

                if (data.ContainsKey(MetadataAccessRulesKey))
                {
                    var content = DeserializeObject<FileSystemAccessModel[]>(data[MetadataAccessRulesKey]);
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

