//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Library.Snapshots
{
    public struct SystemIOLinux : ISystemIO
    {
        #region ISystemIO implementation
        public void DirectoryDelete(string path)
        {
            Directory.Delete(path);
        }

        public void DirectoryCreate(string path)
        {
            Directory.CreateDirectory(path);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public void FileDelete(string path)
        {
            File.Delete(path);
        }

        public void FileSetLastWriteTimeUtc(string path, DateTime time)
        {
            File.SetLastWriteTimeUtc(path, time);
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

        public Stream FileCreate(string path)
        {
            return File.Create(path);
        }

        public FileAttributes GetFileAttributes(string path)
        {
            return File.GetAttributes(path);
        }

        public void CreateSymlink(string symlinkfile, string target, bool asDir)
        {
            UnixSupport.File.CreateSymlink(symlinkfile, target);
        }
        public string PathGetDirectoryName(string path)
        {
            return Path.GetDirectoryName(path);
        }

        public void DirectorySetLastWriteTimeUtc(string path, DateTime time)
        {
            Directory.SetLastWriteTimeUtc(path, time);
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
            Directory.Delete(path, recursive);
        }
        #endregion


    }

}

