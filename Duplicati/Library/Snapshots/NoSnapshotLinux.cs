// Copyright (C) 2025, The Duplicati Team
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
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Microsoft.Win32.SafeHandles;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Handler for providing a snapshot like access to files and folders
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macOS")]
    public sealed class NoSnapshotLinux : SnapshotBase
    {
        /// <summary>
        /// PInvoke methods
        /// </summary>
        private static class PInvoke
        {
            [DllImport("libc", EntryPoint = "fopen", SetLastError = true)]
            public static extern IntPtr fopen(string path, string mode);

            [DllImport("libc", EntryPoint = "fclose", SetLastError = true)]
            public static extern int fclose(IntPtr handle);

            [DllImport("libc", EntryPoint = "fileno", SetLastError = true)]
            public static extern int fileno(IntPtr stream);

            [DllImport("libc", EntryPoint = "strerror", SetLastError = true)]
            public static extern IntPtr strerror(int errnum);

            public static string GetErrorMessage(int errno)
            {
                IntPtr strPtr = PInvoke.strerror(errno);
                return Marshal.PtrToStringAnsi(strPtr) ?? $"Unknown error: {errno}";
            }
        }

        /// <summary>
        /// Flag indicating if advisory locks should be ignored
        /// </summary>
        private readonly bool m_ignoreAdvisoryLocks;

        /// <summary>
        /// The list of folders to create snapshots of
        /// </summary>
        private readonly IEnumerable<string> m_sources;

        /// <summary>
        /// Initializes a new instance of the <see cref="NoSnapshotLinux"/> class.
        /// </summary>
        /// <param name="sources">The list of sources to create snapshots of</param>
        /// <param name="ignoreAdvisoryLocks">A flag indicating if advisory locks should be ignored</param>
        /// <param name="followSymlinks">A flag indicating if symlinks should be followed</param>
        public NoSnapshotLinux(IEnumerable<string> sources, bool ignoreAdvisoryLocks, bool followSymlinks)
            : base(followSymlinks)
        {
            m_sources = sources;
            m_ignoreAdvisoryLocks = ignoreAdvisoryLocks;
        }

        /// <summary>
        /// Gets the source folders
        /// </summary>
        public override IEnumerable<string> SourceEntries => m_sources;

        /// <summary>
        /// Enumerates the root source files and folders
        /// </summary>
        /// <returns>The source files and folders</returns>
        public override IEnumerable<ISourceProviderEntry> EnumerateFilesystemEntries()
        {
            foreach (var folder in m_sources)
            {
                if (DirectoryExists(folder) || folder.EndsWith(Path.DirectorySeparatorChar))
                    yield return new SnapshotSourceFileEntry(this, Util.AppendDirSeparator(folder), true, true);
                else
                    yield return new SnapshotSourceFileEntry(this, folder, false, true);
            }
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
        {
            return SystemIO.IO_OS.GetSymlinkTarget(localPath);
        }

        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="localPath">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        public override Dictionary<string, string> GetMetadata(string localPath, bool isSymlink)
        {
            return SystemIO.IO_OS.GetMetadata(localPath, isSymlink, FollowSymlinks);
        }

        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override bool IsBlockDevice(string localPath)
        {
            var n = PosixFile.GetFileType(SystemIOLinux.NormalizePath(localPath));
            switch (n)
            {
                case PosixFile.FileType.Directory:
                case PosixFile.FileType.Symlink:
                case PosixFile.FileType.File:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override string HardlinkTargetID(string localPath)
        {
            var normalizePath = SystemIOLinux.NormalizePath(localPath);
            return PosixFile.GetHardlinkCount(normalizePath) <= 1
                ? null
                : PosixFile.GetInodeTargetID(normalizePath);
        }

        /// <inheritdoc />
        public override string ConvertToLocalPath(string snapshotPath)
        {
            return snapshotPath;
        }

        /// <inheritdoc />
        public override string ConvertToSnapshotPath(string localPath)
        {
            return SystemIOLinux.NormalizePath(localPath);
        }


        /// <summary>
        /// In case of Linux without snapshot support, we just open the file directly with fopen
        /// in order to avoid the issues related to file locks, which on linux are advisory, and
        /// since .net 6 when using FileStream, the framework is taking the advisory as mandatory
        /// therefore we can't open the file with FileShare.ReadWrite.
        /// </summary>
        /// <param name="localPath">file to be opened</param>
        /// <returns></returns>
        public override Stream OpenRead(string localPath)
        {
            if (m_ignoreAdvisoryLocks)
            {
                var filePtr = PInvoke.fopen(localPath, "r");
                var errorNo = Marshal.GetLastWin32Error(); // Surprisingly, this is to be used on linux/macos
                if (filePtr == IntPtr.Zero)
                {
                    throw new IOException($"Unable to open file: {localPath}. Error: {errorNo} - {PInvoke.GetErrorMessage(errorNo)}");
                }

                try
                {

                    SafeFileHandle safeFileHandle = new(PInvoke.fileno(filePtr), false);
                    return new UnixFileStream(safeFileHandle, filePtr, FileAccess.Read);
                }
                catch // Catch all exceptions and rethrow
                {
                    if (filePtr != IntPtr.Zero)
                        PInvoke.fclose(filePtr);

                    throw;
                }
            }
            else
            {
                return base.OpenRead(localPath);
            }
        }

        /// <summary>
        /// Stream wrapping a file handle
        /// </summary>
        private class UnixFileStream : FileStream
        {
            IntPtr _fileHandle;
            SafeFileHandle _handle;
            public UnixFileStream(SafeFileHandle handle, IntPtr fileHandle, FileAccess access) : base(handle, access)
            {
                _fileHandle = fileHandle;
                _handle = handle;
            }

            protected override void Dispose(bool disposing)
            {
                if (_fileHandle != IntPtr.Zero)
                {
                    PInvoke.fclose(_fileHandle);
                    _fileHandle = IntPtr.Zero;
                }
                _handle?.Dispose();
                base.Dispose(disposing);
            }

        }
    }
}

