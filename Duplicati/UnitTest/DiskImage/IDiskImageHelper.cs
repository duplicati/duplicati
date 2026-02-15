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

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Interface for disk image helper operations that work across different operating systems.
    /// Provides methods for creating, attaching, formatting, and managing virtual disk images
    /// for testing disk image backup and restore operations.
    /// </summary>
    public interface IDiskImageHelper : IDisposable
    {
        /// <summary>
        /// Creates a virtual disk file, attaches it to the system, and returns the physical drive path.
        /// </summary>
        /// <param name="imagePath">The path where the disk image file will be created.</param>
        /// <param name="sizeMB">The size of the disk image in megabytes.</param>
        /// <returns>The physical drive path (e.g., \\.\PhysicalDriveN on Windows, /dev/loopN on Linux).</returns>
        string CreateAndAttachDisk(string imagePath, long sizeMB);

        /// <summary>
        /// Gets the disk number or identifier for an attached disk image.
        /// </summary>
        /// <param name="imagePath">The path to the disk image file.</param>
        /// <returns>The disk number/index, or -1 if not found.</returns>
        int GetDiskNumber(string imagePath);

        /// <summary>
        /// Initializes a disk with the specified partition table type (GPT or MBR).
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="tableType">The partition table type ("gpt" or "mbr").</param>
        void InitializeDisk(int diskNumber, string tableType);

        /// <summary>
        /// Creates a partition on the specified disk, formats it with the specified filesystem,
        /// and assigns a drive letter or mount point.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="fsType">The filesystem type ("ntfs", "fat32", "ext4", etc.).</param>
        /// <param name="sizeMB">The size of the partition in MB (0 for all available space).</param>
        /// <returns>The assigned drive letter (Windows) or mount point identifier.</returns>
        char CreateAndFormatPartition(int diskNumber, string fsType, long sizeMB = 0);

        /// <summary>
        /// Flushes the volume cache to ensure all data is written to disk.
        /// </summary>
        /// <param name="driveLetter">The drive letter or mount point identifier.</param>
        void FlushVolume(char driveLetter);

        /// <summary>
        /// Populates a drive with test data including files of various sizes and directory structures.
        /// </summary>
        /// <param name="driveLetter">The drive letter to populate.</param>
        /// <param name="fileCount">The number of files to create.</param>
        /// <param name="fileSizeKB">The size of each file in KB.</param>
        void PopulateTestData(char driveLetter, int fileCount = 10, int fileSizeKB = 10);

        /// <summary>
        /// Detaches a disk image file from the system.
        /// </summary>
        /// <param name="imagePath">The path to the disk image file.</param>
        void DetachDisk(string imagePath);

        /// <summary>
        /// Unmounts a disk image for writing operations by removing drive letters/mount points
        /// and setting the disk offline.
        /// </summary>
        /// <param name="imagePath">The path to the disk image file.</param>
        /// <param name="driveLetter">Optional drive letter to unmount. If null, will attempt to detect.</param>
        void UnmountForWriting(string imagePath, char? driveLetter = null);

        /// <summary>
        /// Brings a disk online for read operations.
        /// </summary>
        /// <param name="imagePath">The path to the disk image file.</param>
        void BringOnline(string imagePath);

        /// <summary>
        /// Mounts a disk image for reading and returns the assigned drive letter.
        /// </summary>
        /// <param name="imagePath">The path to the disk image file.</param>
        /// <param name="driveLetter">Optional preferred drive letter. If null, one will be assigned.</param>
        /// <returns>The assigned drive letter.</returns>
        char MountForReading(string imagePath, char? driveLetter = null);

        /// <summary>
        /// Cleans up and deletes a disk image file, ensuring it is detached first.
        /// </summary>
        /// <param name="imagePath">The path to the disk image file.</param>
        void CleanupDisk(string imagePath);

        /// <summary>
        /// Gets detailed information about a disk.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <returns>Detailed information about the disk.</returns>
        string GetDiskDetails(int diskNumber);

        /// <summary>
        /// Gets the volume information for a drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter.</param>
        /// <returns>The volume information.</returns>
        string GetVolumeInfo(char driveLetter);

        /// <summary>
        /// Checks if the current process has the necessary privileges to perform disk operations.
        /// </summary>
        /// <returns><c>true</c> if running with sufficient privileges; otherwise, <c>false</c>.</returns>
        bool HasRequiredPrivileges();
    }
}
