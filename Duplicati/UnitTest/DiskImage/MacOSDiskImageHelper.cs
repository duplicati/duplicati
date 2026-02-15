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
using System.IO;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// macOS implementation of IDiskImageHelper using hdiutil and standard macOS tools.
    /// This is a stub implementation that throws NotImplementedException for all operations.
    /// </summary>
    internal class MacOSDiskImageHelper : IDiskImageHelper
    {
        /// <inheritdoc />
        public string CreateAndAttachDisk(string imagePath, long sizeMB)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public int GetDiskNumber(string imagePath)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public void InitializeDisk(int diskNumber, string tableType)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public char CreateAndFormatPartition(int diskNumber, string fsType, long sizeMB = 0)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public void FlushVolume(char driveLetter)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public void PopulateTestData(char driveLetter, int fileCount = 10, int fileSizeKB = 10)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public void DetachDisk(string imagePath)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public void UnmountForWriting(string imagePath, char? driveLetter = null)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public void BringOnline(string imagePath)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public char MountForReading(string imagePath, char? driveLetter = null)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public void CleanupDisk(string imagePath)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public string GetDiskDetails(int diskNumber)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public string GetVolumeInfo(char driveLetter)
        {
            throw new NotImplementedException("macOS disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        public bool HasRequiredPrivileges()
        {
            // On macOS, check if running as root (UID 0)
            try
            {
                return Environment.UserName == "root";
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose in the stub implementation
            GC.SuppressFinalize(this);
        }
    }
}
