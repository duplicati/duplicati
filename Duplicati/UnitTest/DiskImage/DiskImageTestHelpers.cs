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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Helper methods for disk image unit tests.
    /// Provides common operations for creating, initializing, and managing disk images.
    /// </summary>
    internal static class DiskImageTestHelpers
    {
        private const long MiB = 1024 * 1024;

        /// <summary>
        /// Creates a raw disk interface for the specified disk identifier based on the current platform.
        /// </summary>
        /// <param name="diskIdentifier">The disk identifier (e.g., \\.\PhysicalDriveN on Windows, /dev/loopN on Linux).</param>
        /// <returns>An initialized IRawDisk instance for the specified platform.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when the current OS is not supported.</exception>
        internal static IRawDisk CreateRawDiskForIdentifier(string diskIdentifier)
        {
            if (OperatingSystem.IsWindows())
                return new Duplicati.Proprietary.DiskImage.Disk.Windows(diskIdentifier);
            else if (OperatingSystem.IsLinux())
                return new Duplicati.Proprietary.DiskImage.Disk.Linux(diskIdentifier);
            else if (OperatingSystem.IsMacOS())
                return new Duplicati.Proprietary.DiskImage.Disk.Mac(diskIdentifier);
            else
                throw new PlatformNotSupportedException("Unsupported operating system.");
        }

        /// <summary>
        /// Creates and initializes a raw disk interface for the specified disk identifier.
        /// </summary>
        /// <param name="diskIdentifier">The disk identifier.</param>
        /// <param name="readOnly">Whether to open the disk in read-only mode.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An initialized IRawDisk instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the disk fails to initialize.</exception>
        internal static async Task CreateAndInitializeRawDiskAsync(
            IRawDisk rawDisk,
            bool readOnly = true,
            CancellationToken cancellationToken = default)
        {
            if (!await rawDisk.InitializeAsync(readOnly, cancellationToken))
                throw new InvalidOperationException($"Failed to initialize raw disk");
        }

        /// <summary>
        /// Creates a disk image file with the specified parameters.
        /// </summary>
        /// <param name="diskHelper">The disk image helper.</param>
        /// <param name="imagePath">The path where the disk image will be created.</param>
        /// <param name="size">The size of the disk in bytes.</param>
        /// <param name="tableType">The partition table type (GPT, MBR, etc.).</param>
        /// <param name="partitions">Array of tuples specifying filesystem type and size for each partition.</param>
        /// <returns>The disk identifier for the created disk.</returns>
        internal static string CreateDiskWithPartitions(
            IDiskImageHelper diskHelper,
            string imagePath,
            long size,
            PartitionTableType tableType,
            (FileSystemType, long)[] partitions)
        {
            var diskIdentifier = diskHelper.CreateDisk(imagePath, size);
            diskHelper.InitializeDisk(diskIdentifier, tableType, partitions);
            diskHelper.FlushDisk(diskIdentifier);
            diskHelper.Unmount(diskIdentifier);
            return diskIdentifier;
        }

        /// <summary>
        /// Creates a disk with two FAT32 partitions for testing.
        /// </summary>
        /// <param name="diskHelper">The disk image helper.</param>
        /// <param name="imagePath">The path where the disk image will be created.</param>
        /// <param name="tableType">The partition table type (GPT or MBR).</param>
        /// <param name="partitionSize">The size of each partition in bytes (0 for remaining space on second partition).</param>
        /// <returns>The disk identifier for the created disk.</returns>
        internal static string CreateDiskWithTwoFat32Partitions(
            IDiskImageHelper diskHelper,
            string imagePath,
            PartitionTableType tableType,
            long partitionSize = 50 * MiB)
        {
            var totalSize = partitionSize > 0 ? partitionSize * 2 + (10 * MiB) : 110 * MiB; // Extra space for partition table
            return CreateDiskWithPartitions(
                diskHelper,
                imagePath,
                totalSize,
                tableType,
                [(FileSystemType.FAT32, partitionSize), (FileSystemType.FAT32, 0)]);
        }

        /// <summary>
        /// Fills a partition with well-known test data (repeating pattern of 0x00-0xFF).
        /// </summary>
        /// <param name="rawDisk">The raw disk interface.</param>
        /// <param name="partitionOffset">The starting offset of the partition in bytes.</param>
        /// <param name="partitionSize">The size of the partition in bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        internal static async Task FillPartitionWithTestDataAsync(
            IRawDisk rawDisk,
            long partitionOffset,
            long partitionSize,
            CancellationToken cancellationToken = default)
        {
            const int bufferSize = 64 * 1024; // 64KB chunks
            var buffer = new byte[bufferSize];

            long bytesWritten = 0;
            long currentOffset = partitionOffset;

            while (bytesWritten < partitionSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Fill buffer with well-known pattern (repeating 0x00-0xFF)
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = (byte)((bytesWritten + i) & 0xFF);
                }

                var remaining = partitionSize - bytesWritten;
                var toWrite = (int)Math.Min(bufferSize, remaining);

                await rawDisk.WriteBytesAsync(currentOffset, buffer.AsMemory(0, toWrite), cancellationToken);

                bytesWritten += toWrite;
                currentOffset += toWrite;
            }
        }

        /// <summary>
        /// Verifies that a partition contains the expected well-known test data pattern.
        /// </summary>
        /// <param name="rawDisk">The raw disk interface.</param>
        /// <param name="partitionOffset">The starting offset of the partition in bytes.</param>
        /// <param name="partitionSize">The size of the partition in bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the data matches the expected pattern; otherwise, false.</returns>
        internal static async Task<bool> VerifyPartitionTestDataAsync(
            IRawDisk rawDisk,
            long partitionOffset,
            long partitionSize,
            CancellationToken cancellationToken = default)
        {
            const int bufferSize = 64 * 1024; // 64KB chunks
            var buffer = new byte[bufferSize];

            long bytesRead = 0;
            long currentOffset = partitionOffset;

            while (bytesRead < partitionSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = partitionSize - bytesRead;
                var toRead = (int)Math.Min(bufferSize, remaining);

                var actualRead = await rawDisk.ReadBytesAsync(currentOffset, buffer.AsMemory(0, toRead), cancellationToken);

                if (actualRead != toRead)
                    return false;

                // Verify the pattern
                for (int i = 0; i < toRead; i++)
                {
                    var expected = (byte)((bytesRead + i) & 0xFF);
                    if (buffer[i] != expected)
                        return false;
                }

                bytesRead += toRead;
                currentOffset += toRead;
            }

            return true;
        }

        /// <summary>
        /// Gets the file extension for disk images based on the current platform.
        /// </summary>
        /// <returns>The platform-specific disk image extension (vhdx, img, or dmg).</returns>
        internal static string GetPlatformDiskImageExtension()
        {
            if (OperatingSystem.IsWindows())
                return "vhdx";
            else if (OperatingSystem.IsLinux())
                return "img";
            else if (OperatingSystem.IsMacOS())
                return "dmg";
            else
                throw new PlatformNotSupportedException("Unsupported operating system.");
        }

        /// <summary>
        /// Safely unmounts a disk, catching any exceptions during cleanup.
        /// </summary>
        /// <param name="diskHelper">The disk image helper.</param>
        /// <param name="diskIdentifier">The disk identifier to unmount.</param>
        internal static void SafeUnmount(IDiskImageHelper diskHelper, string? diskIdentifier)
        {
            if (string.IsNullOrEmpty(diskIdentifier) || diskHelper == null)
                return;

            try
            {
                diskHelper.Unmount(diskIdentifier);
            }
            catch (Exception ex)
            {
                // Log but don't throw - this is cleanup code
                Console.WriteLine($"Warning: Failed to unmount disk {diskIdentifier}: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely disposes a raw disk and unmounts the underlying disk identifier.
        /// </summary>
        /// <param name="rawDisk">The raw disk to dispose (can be null).</param>
        /// <param name="diskHelper">The disk image helper.</param>
        /// <param name="diskIdentifier">The disk identifier to unmount.</param>
        internal static void SafeDisposeRawDisk(IRawDisk? rawDisk, IDiskImageHelper diskHelper, string? diskIdentifier)
        {
            if (rawDisk != null)
                try
                {
                    rawDisk.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to dispose raw disk: {ex.Message}");
                }

            SafeUnmount(diskHelper, diskIdentifier);
        }

        /// <summary>
        /// Deletes a file if it exists, catching any exceptions.
        /// </summary>
        /// <param name="filePath">The path to the file to delete.</param>
        internal static void SafeDeleteFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to delete file {filePath}: {ex.Message}");
            }
        }
    }

}