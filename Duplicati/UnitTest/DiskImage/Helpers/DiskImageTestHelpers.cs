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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Helpers;

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
            return new Proprietary.DiskImage.Disk.Windows(diskIdentifier);
        else if (OperatingSystem.IsLinux())
            return new Linux(diskIdentifier);
        else if (OperatingSystem.IsMacOS())
            return new Mac(diskIdentifier);
        else
            throw new PlatformNotSupportedException("Unsupported operating system.");
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