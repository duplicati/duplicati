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

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Helper class for managing VHD (Virtual Hard Disk) files using PowerShell.
    /// This class provides methods to create, attach, format, and detach VHD files
    /// for testing disk image backup and restore operations.
    /// </summary>
    /// <remarks>
    /// This class is now a thin wrapper around <see cref="DiskImage.WindowsDiskImageHelper"/>
    /// for backward compatibility. New code should use the <see cref="DiskImage.IDiskImageHelper"/>
    /// interface and <see cref="DiskImage.DiskImageHelperFactory"/> instead.
    /// </remarks>
    internal static class DiskImageVhdHelper
    {
        private static readonly DiskImage.WindowsDiskImageHelper _helper = new();

        /// <summary>
        /// Runs a PowerShell script using the persistent session.
        /// </summary>
        /// <param name="script">The PowerShell script to execute.</param>
        /// <returns>The output from PowerShell.</returns>
        public static string RunPowerShell(string script)
        {
            return DiskImage.WindowsDiskImageHelper.RunPowerShell(script);
        }

        /// <summary>
        /// Creates a VHD file, attaches it to the system, and returns the physical drive path.
        /// </summary>
        /// <param name="vhdPath">The path where the VHD file will be created.</param>
        /// <param name="sizeMB">The size of the VHD in megabytes.</param>
        /// <returns>The physical drive path (e.g., \\.\PhysicalDriveN).</returns>
        public static string CreateAndAttachVhd(string vhdPath, long sizeMB)
        {
            return _helper.CreateAndAttachDisk(vhdPath, sizeMB);
        }

        /// <summary>
        /// Initializes a disk with the specified partition table type (GPT or MBR).
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="tableType">The partition table type ("gpt" or "mbr").</param>
        public static void InitializeDisk(int diskNumber, string tableType)
        {
            _helper.InitializeDisk(diskNumber, tableType);
        }

        /// <summary>
        /// Creates a partition on the specified disk, formats it with the specified filesystem,
        /// and assigns a drive letter.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <param name="fsType">The filesystem type ("ntfs" or "fat32").</param>
        /// <param name="sizeMB">The size of the partition in MB (0 for all available space).</param>
        /// <returns>The assigned drive letter.</returns>
        public static char CreateAndFormatPartition(int diskNumber, string fsType, long sizeMB = 0)
        {
            return _helper.CreateAndFormatPartition(diskNumber, fsType, sizeMB);
        }

        /// <summary>
        /// Flushes the volume cache to ensure all data is written to disk.
        /// </summary>
        /// <param name="driveLetter">The drive letter.</param>
        public static void FlushVolume(char driveLetter)
        {
            _helper.FlushVolume(driveLetter);
        }

        /// <summary>
        /// Populates a drive with test data including files of various sizes and directory structures.
        /// </summary>
        /// <param name="driveLetter">The drive letter to populate.</param>
        /// <param name="fileCount">The number of files to create.</param>
        /// <param name="fileSizeKB">The size of each file in KB.</param>
        public static void PopulateTestData(char driveLetter, int fileCount = 10, int fileSizeKB = 10)
        {
            _helper.PopulateTestData(driveLetter, fileCount, fileSizeKB);
        }

        /// <summary>
        /// Detaches a VHD file from the system.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        public static void DetachVhd(string vhdPath)
        {
            _helper.DetachDisk(vhdPath);
        }

        /// <summary>
        /// Unmounts a VHD for writing operations.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        /// <param name="driveLetter">Optional drive letter to unmount.</param>
        public static void UnmountForWriting(string vhdPath, char? driveLetter = null)
        {
            _helper.UnmountForWriting(vhdPath, driveLetter);
        }

        /// <summary>
        /// Brings a VHD online for read operations.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        public static void BringOnline(string vhdPath)
        {
            _helper.BringOnline(vhdPath);
        }

        /// <summary>
        /// Mounts a VHD for reading and returns the assigned drive letter.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        /// <param name="driveLetter">Optional preferred drive letter.</param>
        /// <returns>The assigned drive letter.</returns>
        public static char MountForReading(string vhdPath, char? driveLetter = null)
        {
            return _helper.MountForReading(vhdPath, driveLetter);
        }

        /// <summary>
        /// Gets the disk number for an attached VHD.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        /// <returns>The disk number, or -1 if not found.</returns>
        public static int GetDiskNumber(string vhdPath)
        {
            return _helper.GetDiskNumber(vhdPath);
        }

        /// <summary>
        /// Checks if the current process is running with administrator privileges.
        /// </summary>
        /// <returns><c>true</c> if running as administrator; otherwise, <c>false</c>.</returns>
        public static bool IsAdministrator()
        {
            return _helper.HasRequiredPrivileges();
        }

        /// <summary>
        /// Cleans up and deletes a VHD file, ensuring it is detached first.
        /// </summary>
        /// <param name="vhdPath">The path to the VHD file.</param>
        public static void CleanupVhd(string vhdPath)
        {
            _helper.CleanupDisk(vhdPath);
        }

        /// <summary>
        /// Gets detailed information about a disk.
        /// </summary>
        /// <param name="diskNumber">The disk number.</param>
        /// <returns>The PowerShell output for the disk details.</returns>
        public static string GetDiskDetails(int diskNumber)
        {
            return _helper.GetDiskDetails(diskNumber);
        }

        /// <summary>
        /// Gets the volume information for a drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter.</param>
        /// <returns>The volume information.</returns>
        public static string GetVolumeInfo(char driveLetter)
        {
            return _helper.GetVolumeInfo(driveLetter);
        }

        /// <summary>
        /// Disposes the shared PowerShell session.
        /// Call this when done with all VHD operations.
        /// </summary>
        public static void DisposeSession()
        {
            _helper.Dispose();
        }
    }
}
