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
    /// <summary>
    /// Interface for wrapping System.IO operations.
    /// </summary>
    public interface ISystemIO
    {
        void DirectoryDelete(string path);
        void DirectoryDelete(string path, bool recursive);
        void DirectoryCreate(string path);
        bool DirectoryExists(string path);
        void DirectorySetLastWriteTimeUtc(string path, DateTime time);

        void FileMove(string source, string target);
        void FileDelete(string path);
        void FileSetLastWriteTimeUtc(string path, DateTime time);
        bool FileExists(string path);
        long FileLength(string path);
        Stream FileOpenRead(string path);
        Stream FileOpenWrite(string path);
        Stream FileCreate(string path);
        FileAttributes GetFileAttributes(string path);
        void CreateSymlink(string symlinkfile, string target, bool asDir);
        string PathGetDirectoryName(string path);
    }
}

