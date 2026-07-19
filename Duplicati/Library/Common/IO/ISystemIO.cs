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
        IFileEntry DirectoryEntry(DirectoryInfo dirInfo);
        void DirectoryCreate(string path);
        void DirectoryDelete(string path, bool recursive);
        bool DirectoryExists(string path);
        void DirectoryMove(string sourceDirName, string destDirName);
        void DirectorySetLastWriteTimeUtc(string path, DateTime time);
        void DirectorySetCreationTimeUtc(string path, DateTime time);

        IFileEntry FileEntry(string path);
        IFileEntry FileEntry(FileInfo fileInfo);
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
        FileStream FileOpenReadWrite(string path);
        FileStream FileOpenWrite(string path);
        FileStream FileCreate(string path);
        FileAttributes GetFileAttributes(string path);
        void SetFileAttributes(string path, FileAttributes attributes);
        void CreateSymlink(string symlinkfile, string target, bool asDir);
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
        IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
        IEnumerable<string> EnumerateDirectories(string path);

        // Enumerate FileEntries of files and directories
        // This is more efficient than enumerating file names when metadata is needed
        IEnumerable<IFileEntry> EnumerateFileEntries(string path);

        void SetMetadata(string path, Dictionary<string, string> metdata, bool restorePermissions);
        Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink);
        /// <summary>
        /// Restricts access to the file so only the current user and privileged system principals retain access
        /// (on Windows: full control for the current user, SYSTEM and Administrators with inheritance disabled;
        /// on POSIX: owner-only 0600 permissions).
        /// </summary>
        /// <param name="path">The file to set permissions on.</param>
        void FileSetPermissionUserRWOnly(string path);
        /// <summary>
        /// Restricts access to the directory so only the current user and privileged system principals retain access
        /// (on Windows: full control for the current user, SYSTEM and Administrators with inheritance disabled;
        /// on POSIX: owner-only 0700 permissions).
        /// </summary>
        /// <param name="path">The directory to set permissions on.</param>
        void DirectorySetPermissionUserRWOnly(string path);

        /// <summary>
        /// Checks whether the directory has the restricted permissions for the current user,
        /// i.e. the same permissions that <see cref="DirectorySetPermissionUserRWOnly"/> would apply.
        /// </summary>
        /// <param name="path">The directory to check permissions on.</param>
        /// <param name="detail">A human-readable description of why the check failed, if it did; otherwise <see cref="string.Empty"/>.</param>
        /// <returns><c>true</c> if the directory has the expected permissions; otherwise <c>false</c>.</returns>
        bool DirectoryHasPermissionUserRWOnly(string path, out string detail);

        /// <summary>
        /// Gets a value indicating whether the operating system supports alternate data streams.
        /// </summary>
        bool SupportsAlternateDataStreams { get; }

        /// <summary>
        /// Enumerates the alternate data streams for a file (Windows-only).
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <returns>An enumerable of stream names (e.g., ":streamname").</returns>
        IEnumerable<string> EnumerateAlternateDataStreams(string path);

        /// <summary>
        /// Determines whether the specified path is an alternate data stream.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the path is an alternate data stream; otherwise, false.</returns>
        bool IsAlternateDataStream(string path);

        /// <summary>
        /// Returns the parent file or folder path of an alternate data stream path.
        /// For a path such as <c>C:\file.txt:stream</c> this returns <c>C:\file.txt</c>.
        /// If the path is not an alternate data stream, the path is returned unchanged.
        /// </summary>
        /// <param name="path">The alternate data stream path.</param>
        /// <returns>The parent file or folder path.</returns>
        string GetAlternateDataStreamParent(string path);
    }

}

