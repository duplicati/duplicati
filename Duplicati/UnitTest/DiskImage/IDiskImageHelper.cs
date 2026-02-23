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
using Duplicati.Proprietary.DiskImage;

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
        /// <param name="sizeB">The size of the disk image in bytes.</param>
        /// <returns>The physical drive path (e.g., \\.\PhysicalDriveN on Windows, /dev/loopN on Linux).</returns>
        string CreateDisk(string imagePath, long sizeB);

        /// <summary>
        /// Initializes, partitions, and formats a disk based on the provided parameters
        /// </summary>
        /// <param name="diskIdentifier">The identifier of the disk to initialize.</param>
        /// <param name="tableType">The type of partition table to create (e.g., GPT, MBR).</param>
        /// <param name="partitions">An array of tuples specifying the file system type and size for each partition. If the size is 0, the partition will use the remaining available space.</param>
        /// <returns>An array of mount points for the created partitions.</returns>
        string[] InitializeDisk(string diskIdentifier, PartitionTableType tableType, (FileSystemType, long)[] partitions);

        /// <summary>
        /// Mounts all of the partitions on the specified disk and returns their mount points.
        /// </summary>
        /// <param name="diskIdentifier">The identifier of the disk to mount.</param>
        /// <returns>An array of mount points for the mounted partitions.</returns>
        string[] Mount(string diskIdentifier);

        /// <summary>
        /// Unmounts all of the partitions on the specified disk, ensuring they are safely detached from the system. The disk will still be attached, but will be pulled offline. After this call, the disk device can be written to.
        /// </summary>
        /// <param name="diskIdentifier">The identifier of the disk to unmount.</param>
        void Unmount(string diskIdentifier);

        /// <summary>
        /// Cleans up and deletes a disk image file, ensuring it is detached first.
        /// </summary>
        /// <param name="imagePath">The path to the disk image file.</param>
        /// <param name="diskIdentifier">The identifier of the disk to clean up. If null, the method will attempt to determine the disk based on the image path.</param>
        void CleanupDisk(string imagePath, string? diskIdentifier = null);

        /// <summary>
        /// Checks if the current process has the necessary privileges to perform disk operations.
        /// </summary>
        /// <returns><c>true</c> if running with sufficient privileges; otherwise, <c>false</c>.</returns>
        bool HasRequiredPrivileges();

        /// <summary>
        /// Retrieves the partition table information for a given disk identifier.
        /// </summary>
        /// <param name="diskIdentifier">The identifier of the disk.</param>
        /// <returns>The partition table geometry of the disk.</returns>
        PartitionTableGeometry GetPartitionTable(string diskIdentifier);

        /// <summary>
        /// Retrieves the partition information for a given disk identifier.
        /// </summary>
        /// <param name="diskIdentifier">The identifier of the disk.</param>
        /// <returns>An array of partition geometries for the disk.</returns>
        PartitionGeometry[] GetPartitions(string diskIdentifier);

        /// <summary>
        /// Flushes the specified disk. Important prior to reading the disk raw.
        /// </summary>
        /// <param name="diskIdentifier">The identifier of the disk to flush.</param>
        void FlushDisk(string diskIdentifier);
    }
}
