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
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Common.IO
{
    /// <summary>
    /// Interface for wrapping System.IO operations.
    /// </summary>
    public interface ISystemIO
    {
        IFileEntry DirectoryEntry(string path);
        void DirectoryDelete(string path);
        void DirectoryDelete(string path, bool recursive);
        void DirectoryCreate(string path);
        bool DirectoryExists(string path);
        void DirectorySetLastWriteTimeUtc(string path, DateTime time);
        void DirectorySetCreationTimeUtc(string path, DateTime time);
        DateTime DirectoryGetLastWriteTimeUtc(string path);
        DateTime DirectoryGetCreationTimeUtc(string path);


        IFileEntry FileEntry(string path);
        void FileMove(string source, string target);
        void FileDelete(string path);
        void FileCopy(string source, string target, bool overwrite);
        void FileSetLastWriteTimeUtc(string path, DateTime time);
        void FileSetCreationTimeUtc(string path, DateTime time);
        DateTime FileGetLastWriteTimeUtc(string path);
        DateTime FileGetCreationTimeUtc(string path);
        bool FileExists(string path);
        long FileLength(string path);
        FileStream FileOpenRead(string path);
        FileStream FileOpenWrite(string path);
        FileStream FileOpenReadWrite(string path);
        FileStream FileCreate(string path);
        FileAttributes GetFileAttributes(string path);
        void SetFileAttributes(string path, FileAttributes attributes);
        void CreateSymlink(string symlinkfile, string target, bool overwrite, bool asDir);
        string GetSymlinkTarget(string path);
        string PathGetDirectoryName(string path);
        string PathGetFileName(string path);
        string PathGetExtension(string path);
        string PathChangeExtension(string path, string extension);
        string PathCombine(params string[] paths);
        string PathGetFullPath(string path);
        string GetPathRoot(string path);
        string[] GetDirectories(string path);
        string[] GetFiles(string path);
        string[] GetFiles(string path, string searchPattern);
        DateTime GetCreationTimeUtc(string path);
        DateTime GetLastWriteTimeUtc(string path);
        IEnumerable<string> EnumerateFileSystemEntries(string path);
        IEnumerable<string> EnumerateFiles(string path);
        IEnumerable<string> EnumerateDirectories(string path);

        void SetMetadata(string path, Dictionary<string, string> metdata, bool restorePermissions);
        Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink);
    }

}

