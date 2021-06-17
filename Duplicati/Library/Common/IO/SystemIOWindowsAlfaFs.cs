//  Copyright (C) 2021, The Duplicati Team

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
using Duplicati.Library.Interface;
using System.IO;
using System.Linq;

using AlphaFsFile = Alphaleonis.Win32.Filesystem.File;
using AlphaFsDirectory = Alphaleonis.Win32.Filesystem.Directory;
using AlphaFsPath = Alphaleonis.Win32.Filesystem.Path;
using System.Security.Principal;
using System.Security.AccessControl;
using Alphaleonis.Win32.Filesystem;
using Duplicati.Library.IO.WindowsFileMetadata;

namespace Duplicati.Library.Common.IO
{
    public class SystemIOWindowsAlfaFs : SystemIOWindowsBase, ISystemIO
    {

        #region Ntfs permissions
        private FileSystemSecurity GetAccessControlDir(string path)
        {
            return AlphaFsDirectory.GetAccessControl(PrefixWithUNC(path));
        }

        private FileSystemSecurity GetAccessControlFile(string path)
        {
            return AlphaFsFile.GetAccessControl(PrefixWithUNC(path));
        }

        private void SetAccessControlFile(string path, FileSecurity rules)
        {
            AlphaFsFile.SetAccessControl(PrefixWithUNC(path), rules);
        }

        private void SetAccessControlDir(string path, DirectorySecurity rules)
        {
            AlphaFsDirectory.SetAccessControl(PrefixWithUNC(path), rules);
        }

        #endregion


        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            if (FileExists(symlinkfile) || DirectoryExists(symlinkfile))
                throw new System.IO.IOException(string.Format("File already exists: {0}", symlinkfile));

            if (asDir)
            {
                AlphaFsDirectory.CreateSymbolicLink(PrefixWithUNC(symlinkfile), target, PathFormat.LongFullPath);
            }
            else
            {
                AlphaFsFile.CreateSymbolicLink(PrefixWithUNC(symlinkfile), target, PathFormat.LongFullPath);
            }

            //Sadly we do not get a notification if the creation fails :(
            System.IO.FileAttributes attr = 0;
            if ((!asDir && FileExists(symlinkfile)) || (asDir && DirectoryExists(symlinkfile)))
                try { attr = GetFileAttributes(symlinkfile); }
                catch { }

            if ((attr & System.IO.FileAttributes.ReparsePoint) == 0)
                throw new System.IO.IOException(string.Format("Unable to create symlink, check account permissions: {0}", symlinkfile));
        }

        public void DirectoryCreate(string path)
        {
            AlphaFsDirectory.CreateDirectory(PrefixWithUNC(path));
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            AlphaFsDirectory.Delete(PrefixWithUNC(path), recursive);
        }

        public IFileEntry DirectoryEntry(string path)
        {
            var dInfo = AlphaFsDirectory.GetFileSystemEntryInfo(PrefixWithUNC(path));
            return new FileEntry(dInfo.FileName, 0, dInfo.LastAccessTime, dInfo.LastWriteTime)
            {
                IsFolder = true
            };
        }

        public bool DirectoryExists(string path)
        {
            return AlphaFsDirectory.Exists(PrefixWithUNC(path));
        }

        public void DirectoryMove(string sourceDirName, string destDirName)
        {
            AlphaFsDirectory.Move(PrefixWithUNC(sourceDirName), PrefixWithUNC(destDirName));
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            AlphaFsDirectory.SetCreationTimeUtc(PrefixWithUNC(path), time);
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            AlphaFsDirectory.SetLastWriteTimeUtc(PrefixWithUNC(path), time);
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return AlphaFsDirectory.EnumerateDirectories(path);
            }
            else
            {
                return AlphaFsDirectory.EnumerateDirectories(PrefixWithUNC(path)).Select(StripUNCPrefix);
            }
        }

        public IEnumerable<string> EnumerateFiles(string path)
        {
            return AlphaFsDirectory.EnumerateFiles(PrefixWithUNC(path)).Select(StripUNCPrefix);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return AlphaFsDirectory.EnumerateFiles(PrefixWithUNC(path), searchPattern, searchOption).Select(StripUNCPrefix);
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            return AlphaFsDirectory.EnumerateFileSystemEntries(PrefixWithUNC(path)).Select(StripUNCPrefix);
        }

        public void FileCopy(string source, string target, bool overwrite)
        {
            AlphaFsFile.Copy(PrefixWithUNC(source), PrefixWithUNC(target), overwrite);
        }

        public FileStream FileCreate(string path)
        {
            return AlphaFsFile.Create(PrefixWithUNC(path));
        }

        public void FileDelete(string path)
        {
            AlphaFsFile.Delete(PrefixWithUNC(path));
        }

        public IFileEntry FileEntry(string path)
        {
            var fileInfo = AlphaFsFile.GetFileSystemEntryInfo(PrefixWithUNC(path));
            return new FileEntry(fileInfo.FileName, fileInfo.FileSize, fileInfo.LastAccessTime, fileInfo.LastWriteTime);
        }

        public bool FileExists(string path)
        {
            return AlphaFsFile.Exists(PrefixWithUNC(path));
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            return AlphaFsFile.GetCreationTimeUtc(PrefixWithUNC(path));
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            return AlphaFsFile.GetLastWriteTimeUtc(PrefixWithUNC(path));
        }

        public long FileLength(string path)
        {
            var fileInfo = AlphaFsFile.GetFileSystemEntryInfo(PrefixWithUNC(path));
            return fileInfo.FileSize;
        }

        public void FileMove(string source, string target)
        {
            AlphaFsFile.Move(PrefixWithUNC(source), PrefixWithUNC(target));
        }

        public FileStream FileOpenRead(string path)
        {
            return AlphaFsFile.Open(PrefixWithUNC(path), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        }

        public FileStream FileOpenWrite(string path)
        {
            return !FileExists(path)
                ? FileCreate(path)
                : AlphaFsFile.OpenWrite(PrefixWithUNC(path));
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            AlphaFsFile.SetCreationTimeUtc(PrefixWithUNC(path), time);
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            AlphaFsFile.SetLastWriteTimeUtc(PrefixWithUNC(path), time);
        }

        public DateTime GetCreationTimeUtc(string path)
        {
            return AlphaFsFile.GetCreationTimeUtc(PrefixWithUNC(path));
        }

        public string[] GetDirectories(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return AlphaFsDirectory.GetDirectories(path);
            }
            else
            {
                return AlphaFsDirectory.GetDirectories(PrefixWithUNC(path)).Select(StripUNCPrefix).ToArray();
            }
        }

        public FileAttributes GetFileAttributes(string path)
        {
            return AlphaFsFile.GetAttributes(PrefixWithUNC(path));
        }

        public string[] GetFiles(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return AlphaFsDirectory.GetFiles(path);
            }
            else
            {
                return AlphaFsDirectory.GetFiles(PrefixWithUNC(path)).Select(StripUNCPrefix).ToArray();
            }
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            if (IsPrefixedWithUNC(path))
            {
                return AlphaFsDirectory.GetFiles(path, searchPattern);
            }
            else
            {
                return AlphaFsDirectory.GetFiles(PrefixWithUNC(path), searchPattern).Select(StripUNCPrefix).ToArray();
            }
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return AlphaFsDirectory.GetLastWriteTimeUtc(PrefixWithUNC(path));
        }


        public Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;
            var dict = new Dictionary<string, string>();

            FileSystemSecurity rules = isDirTarget ? GetAccessControlDir(targetpath) : GetAccessControlFile(targetpath);
            var objs = new List<FileSystemAccessModel>();
            foreach (var f in rules.GetAccessRules(true, false, typeof(SecurityIdentifier)))
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

            try
            {
                SecurityIdentifier ownerSid = (SecurityIdentifier)rules.GetOwner(typeof(SecurityIdentifier));
                var ownerModel = new OwnerModel(ownerSid);
                dict[MetadataOwnerKey] = SerializeObject(ownerModel);
            }
            catch { }

            return dict;
        }

        public string GetPathRoot(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return AlphaFsPath.GetPathRoot(path);
            }
            else
            {
                return StripUNCPrefix(AlphaFsPath.GetPathRoot(PrefixWithUNC(path)));
            }
        }

        public string GetSymlinkTarget(string path)
        {
            try
            {
                return AlphaFsFile.GetLinkTargetInfo(PrefixWithUNC(path)).PrintName;
            }
            catch (Alphaleonis.Win32.Filesystem.NotAReparsePointException) { }
            catch (Alphaleonis.Win32.Filesystem.UnrecognizedReparsePointException) { }

            // This path looks like it isn't actually a symlink
            // (Note that some reparse points aren't actually symlinks -
            // things like the OneDrive folder in the Windows 10 Fall Creator's Update for example)
            return null;
        }

        public string PathChangeExtension(string path, string extension)
        {
            return StripUNCPrefix(AlphaFsPath.ChangeExtension(PrefixWithUNC(path), extension));
        }

        public string PathCombine(params string[] paths)
        {
            return AlphaFsPath.Combine(paths);
        }

        public string PathGetDirectoryName(string path)
        {
            return StripUNCPrefix(AlphaFsPath.GetDirectoryName(PrefixWithUNC(path)));
        }

        public string PathGetExtension(string path)
        {
            return StripUNCPrefix(AlphaFsPath.GetExtension(PrefixWithUNC(path)));
        }

        public string PathGetFileName(string path)
        {
            return StripUNCPrefix(AlphaFsPath.GetFileName(PrefixWithUNC(path)));
        }

        public string PathGetFullPath(string path)
        {
            if (IsPrefixedWithUNC(path))
            {
                return AlphaFsPath.GetFullPath(ConvertSlashes(path));
            }
            else
            {
                return StripUNCPrefix(AlphaFsPath.GetFullPath(PrefixWithUNC(ConvertSlashes(path))));
            }
        }

        public void SetFileAttributes(string path, FileAttributes attributes)
        {
            AlphaFsFile.SetAttributes(PrefixWithUNC(path), attributes);
        }

        public void SetMetadata(string path, Dictionary<string, string> data, bool restorePermissions)
        {
            var isDirTarget = path.EndsWith(DIRSEP, StringComparison.Ordinal);
            var targetpath = isDirTarget ? path.Substring(0, path.Length - 1) : path;

            if (restorePermissions)
            {
                FileSystemSecurity rules = isDirTarget
                    ? GetAccessControlDir(targetpath)
                    : GetAccessControlFile(targetpath);

                if (data.TryGetValue(MetadataAccessRulesIsProtectedKey, out string isProtectedString) &&
                    bool.TryParse(isProtectedString, out bool isProtected))
                {
                    if (rules.AreAccessRulesProtected != isProtected)
                    {
                        rules.SetAccessRuleProtection(isProtected, false);
                    }
                }

                if (data.TryGetValue(MetadataAccessRulesKey, out string accessRulesSerialised))
                {
                    var content = DeserializeObject<FileSystemAccessModel[]>(accessRulesSerialised);
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

                if (data.TryGetValue(MetadataOwnerKey, out string ownerSerialised))
                {
                    var ownerModel = DeserializeObject<OwnerModel>(ownerSerialised);

                    IdentityReference ownerReference = ownerModel.NTAccountName != null
                        ? (IdentityReference)new NTAccount(ownerModel.NTAccountName)
                        : new SecurityIdentifier(ownerModel.SID);

                    if (isDirTarget)
                        ((DirectorySecurity)rules).SetOwner(ownerReference);
                    else
                        ((FileSecurity)rules).SetOwner(ownerReference);
                }

                if (isDirTarget)
                    SetAccessControlDir(targetpath, (DirectorySecurity)rules);
                else
                    SetAccessControlFile(targetpath, (FileSecurity)rules);
            }
        }
    }
}

