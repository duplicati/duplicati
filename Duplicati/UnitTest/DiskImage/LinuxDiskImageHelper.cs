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
    /// Linux implementation of <see cref="IDiskImageHelper"/> using loop devices and standard Linux tools.
    /// This is a stub implementation that throws <see cref="NotImplementedException"/> for all operations.
    /// </summary>
    internal class LinuxDiskImageHelper : IDiskImageHelper
    {
        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public string CreateAndAttachDisk(string imagePath, long sizeMB)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public int GetDiskNumber(string imagePath)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void InitializeDisk(int diskNumber, Duplicati.Proprietary.DiskImage.PartitionTableType tableType)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public char CreateAndFormatPartition(int diskNumber, Duplicati.Proprietary.DiskImage.FileSystemType fsType, long sizeMB = 0)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void FlushVolume(char driveLetter)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void PopulateTestData(char driveLetter, int fileCount = 10, int fileSizeKB = 10)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void DetachDisk(string imagePath)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void UnmountForWriting(string imagePath, char? driveLetter = null)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void BringOnline(string imagePath)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public char MountForReading(string imagePath, char? driveLetter = null)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public void CleanupDisk(string imagePath)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public string GetDiskDetails(int diskNumber)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <exception cref="NotImplementedException">Always thrown as Linux support is not yet implemented.</exception>
        public string GetVolumeInfo(char driveLetter)
        {
            throw new NotImplementedException("Linux disk image operations are not yet implemented.");
        }

        /// <inheritdoc />
        /// <remarks>
        /// On Linux, checks if running as root (UID 0).
        /// </remarks>
        public bool HasRequiredPrivileges()
        {
            // On Linux, check if running as root (UID 0)
            try
            {
                return Environment.UserName == "root" || System.Diagnostics.Process.GetCurrentProcess().Id == 0;
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
