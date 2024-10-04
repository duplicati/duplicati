// Copyright (C) 2024, The Duplicati Team
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
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
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

        [DllImport("libc", EntryPoint = "fopen", SetLastError = true)]
        private static extern IntPtr fopen(string path, string mode);

        [DllImport("libc", EntryPoint = "fclose", SetLastError = true)]
        private static extern int fclose(IntPtr handle);

        [DllImport("libc", EntryPoint = "fileno", SetLastError = true)]
        private static extern int fileno(IntPtr stream);

        [DllImport("libc", EntryPoint = "strerror", SetLastError = true)]
        private static extern IntPtr strerror(int errnum);

        private static string GetErrorMessage(int errno)
        {
            IntPtr strPtr = strerror(errno);
            return Marshal.PtrToStringAnsi(strPtr) ?? $"Unknown error: {errno}";
        }

        public class UnixFileHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private readonly IntPtr _filePointer;

            public UnixFileHandle(IntPtr filePointer) : base(true)
            {
                _filePointer = filePointer;
                SetHandle(filePointer);
            }

            protected override bool ReleaseHandle()
            {
                if (_filePointer != IntPtr.Zero)
                    return fclose(_filePointer) == 0;
                else
                    return true;
            }

            public override bool IsInvalid => _filePointer == IntPtr.Zero || base.IsInvalid;

            public int GetFileDescriptor()
            {
                return fileno(_filePointer);
            }

            protected override void Dispose(bool disposing)
            {
                ReleaseHandle();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
        {
            return SystemIO.IO_SYS.GetSymlinkTarget(localPath);
        }

        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="localPath">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        public override Dictionary<string, string> GetMetadata(string localPath, bool isSymlink, bool followSymlink)
        {
            return SystemIO.IO_SYS.GetMetadata(localPath, isSymlink, followSymlink);
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
            IntPtr filePtr = fopen(localPath, "r");
            var errorNo = Marshal.GetLastWin32Error(); // Surprisingly, this is to be used on linux/macos
            if (filePtr == IntPtr.Zero)
            {
                throw new IOException($"Unable to open file: {localPath}. Error: {errorNo} - {GetErrorMessage(errorNo)}");
            }

            UnixFileHandle unixHandle = new(filePtr);

            try
            {
                // Do not attempt to apply any using pattern here as it will cause the file to be closed
                // FileStream class takes the handle and is responsible for closing the handle and it
                // has been verified to do that.
                SafeFileHandle safeFileHandle = new(unixHandle.GetFileDescriptor(), true);

                // This is important, means that the file handle is now owned by the SafeFileHandle
                // and no longer by unixHandle
                unixHandle.SetHandleAsInvalid();

                return new UnixFileStream(safeFileHandle, unixHandle, FileAccess.Read);
            }
            catch // Catch all exceptions and rethrow
            {
                unixHandle.Dispose();
                throw;
            }
        }
    }
    public class UnixFileStream : FileStream
    {
        NoSnapshotLinux.UnixFileHandle _unixHandle;
        SafeFileHandle _handle;
        public UnixFileStream(SafeFileHandle handle, NoSnapshotLinux.UnixFileHandle unixHandle, FileAccess access) : base(handle, access)
        {
            _unixHandle = unixHandle;
            _handle = handle;
        }

        protected override void Dispose(bool disposing)
        {
            _unixHandle.Dispose();
            _handle.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            _unixHandle.Dispose();
            _handle.Dispose();
            return base.DisposeAsync();
        }
    }
}

