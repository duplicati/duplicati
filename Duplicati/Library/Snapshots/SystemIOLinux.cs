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
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.Snapshots
{
    public struct SystemIOLinux : ISystemIO
    {
        #region ISystemIO implementation
        public void DirectoryDelete(string path)
        {
            Directory.Delete(NoSnapshot.NormalizePath(path));
        }

        public void DirectoryCreate(string path)
        {
            Directory.CreateDirectory(NoSnapshot.NormalizePath(path));
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(NoSnapshot.NormalizePath(path));
        }

        public void FileDelete(string path)
        {
            File.Delete(path);
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            File.SetLastWriteTimeUtc(path, time);
        }

        public void FileSetCreationTimeUtc(string path, DateTime time)
        {
            File.SetCreationTimeUtc(path, time);
        }

        public DateTime FileGetLastWriteTimeUtc(string path)
        {
            return File.GetLastWriteTimeUtc(path);
        }

        public DateTime FileGetCreationTimeUtc(string path)
        {
            return File.GetCreationTimeUtc(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public Stream FileOpenRead(string path)
        {
            return File.OpenRead(path);
        }

        public Stream FileOpenWrite(string path)
        {
            return File.OpenWrite(path);
        }

        public Stream FileOpenReadWrite(string path)
        {
            return File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }

        public Stream FileCreate(string path)
        {
            return File.Create(path);
        }

        public FileAttributes GetFileAttributes(string path)
        {
            return File.GetAttributes(NoSnapshot.NormalizePath(path));
        }

        public void SetFileAttributes(string path, FileAttributes attributes)
        {
            File.SetAttributes(path, attributes);
        }

        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            UnixSupport.File.CreateSymlink(symlinkfile, target);
        }
        
        public string PathGetDirectoryName(string path)
        {
            return Path.GetDirectoryName(NoSnapshot.NormalizePath(path));
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            return Directory.EnumerateFileSystemEntries(path);
        }

        public string PathGetFileName(string path)
        {
            return Path.GetFileName(path);
        }

        public string PathGetExtension(string path)
        {
            return Path.GetExtension(path);
        }
        
        public string PathChangeExtension(string path, string extension)
        {
            return Path.ChangeExtension(path, extension);
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            Directory.SetLastWriteTimeUtc(NoSnapshot.NormalizePath(path), time);
        }

        public void DirectorySetCreationTimeUtc(string path, DateTime time)
        {
            Directory.SetCreationTimeUtc(NoSnapshot.NormalizePath(path), time);
        }

        public DateTime DirectoryGetLastWriteTimeUtc(string path)
        {
            return Directory.GetLastWriteTimeUtc(NoSnapshot.NormalizePath(path));
        }

        public DateTime DirectoryGetCreationTimeUtc(string path)
        {
            return Directory.GetCreationTimeUtc(NoSnapshot.NormalizePath(path));
        }

        public void FileMove(string source, string target)
        {
            File.Move(source, target);
        }

        public long FileLength(string path)
        {
            return new System.IO.FileInfo(path).Length;
        }

        public void DirectoryDelete(string path, bool recursive)
        {
            Directory.Delete(NoSnapshot.NormalizePath(path), recursive);
        }

        public Dictionary<string, string> GetMetadata(string file, bool isSymlink, bool followSymlink)
        {
            var f = NoSnapshot.NormalizePath(file);
            var dict = new Dictionary<string, string>();

            var n = UnixSupport.File.GetExtendedAttributes(f, isSymlink, followSymlink);
            if (n != null)
                foreach(var x in n)
                    dict["unix-ext:" + x.Key] = Convert.ToBase64String(x.Value);

            var fse = UnixSupport.File.GetUserGroupAndPermissions(f);
            dict["unix:uid-gid-perm"] = string.Format("{0}-{1}-{2}", fse.UID, fse.GID, fse.Permissions);
            if (fse.OwnerName != null)
            {
                dict["unix:owner-name"] = fse.OwnerName;
            }
            if (fse.GroupName != null)
            {
                dict["unix:group-name"] = fse.GroupName;
            }

            return dict;
        }

        public void SetMetadata(string file, Dictionary<string, string> data, bool restorePermissions)
        {
            if (data == null)
                return;

            var f = NoSnapshot.NormalizePath(file);

            foreach(var x in data.Where(x => x.Key.StartsWith("unix-ext:")).Select(x => new KeyValuePair<string, byte[]>(x.Key.Substring("unix-ext:".Length), Convert.FromBase64String(x.Value))))
                UnixSupport.File.SetExtendedAttribute(f, x.Key, x.Value);

            if (restorePermissions && data.ContainsKey("unix:uid-gid-perm"))
            {
                var parts = data["unix:uid-gid-perm"].Split(new char[] { '-' });
                if (parts.Length == 3)
                {
                    long uid;
                    long gid;
                    long perm;

                    if (long.TryParse(parts[0], out uid) && long.TryParse(parts[1], out gid) && long.TryParse(parts[2], out perm))
                    {
                        if (data.ContainsKey("unix:owner-name"))
                            try { uid = UnixSupport.File.GetUserID(data["unix:owner-name"]); }
                            catch { }

                        if (data.ContainsKey("unix:group-name"))
                            try { gid = UnixSupport.File.GetGroupID(data["unix:group-name"]); }
                            catch { }

                        UnixSupport.File.SetUserGroupAndPermissions(f, uid, gid, perm);
                    }
                }
            }
        }
        #endregion


    }

}

