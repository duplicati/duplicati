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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Proprietary.DiskImage;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Linux implementation of <see cref="IDiskImageHelper"/> using loop devices and standard Linux tools.
    /// </summary>
    internal class LinuxDiskImageHelper : IDiskImageHelper
    {
        /// <summary>
        /// Tracks loop devices created by this helper for cleanup.
        /// </summary>
        private readonly List<string> _trackedLoopDevices = new();

        /// <summary>
        /// Maps image paths to their associated loop devices.
        /// </summary>
        private readonly Dictionary<string, string> _imageToLoopDevice = new();

        /// <inheritdoc />
        public string CreateDisk(string imagePath, long sizeB)
        {
            // Ensure the directory exists
            var directory = Path.GetDirectoryName(imagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Delete existing image if it exists
            if (File.Exists(imagePath))
            {
                try
                {
                    CleanupDisk(imagePath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            // Create a sparse disk image file using truncate
            RunProcess("truncate", $"-s {sizeB} \"{imagePath}\"");

            // Attach it as a loop device
            var loopDevice = RunProcess("losetup", $"--find --show --partscan \"{imagePath}\"").Trim();

            if (string.IsNullOrEmpty(loopDevice) || !loopDevice.StartsWith("/dev/loop"))
                throw new InvalidOperationException($"Failed to attach loop device for {imagePath}");

            // Track the loop device
            _trackedLoopDevices.Add(loopDevice);
            _imageToLoopDevice[imagePath] = loopDevice;

            return loopDevice;
        }

        /// <inheritdoc />
        public string[] InitializeDisk(string diskIdentifier, PartitionTableType tableType, (FileSystemType, long)[] partitions)
        {
            if (tableType == PartitionTableType.Unknown)
                return Array.Empty<string>();

            // Create partition table using parted
            var label = tableType == PartitionTableType.GPT ? "gpt" : "msdos";
            RunProcess("parted", $"-s {diskIdentifier} mklabel {label}");

            // Get disk size for calculating partition sizes
            var diskSize = GetDiskSize(diskIdentifier);
            var sectorSize = GetSectorSize(diskIdentifier);
            long nextFreeSector = tableType == PartitionTableType.GPT ? 34 * sectorSize : 1 * sectorSize; // GPT reserves first sector for MBR followed by 33 sectors. MBR reserves 1 sector.

            // Create partitions
            for (int i = 0; i < partitions.Length; i++)
            {
                var (fsType, size) = partitions[i];
                var partitionNumber = i + 1;

                // Calculate partition size
                long endOffset;
                if (size <= 0 || i == partitions.Length - 1)
                {
                    // Use remaining space, but reserve space for GPT backup header if needed
                    endOffset = diskSize;
                    if (tableType == PartitionTableType.GPT)
                    {
                        // GPT requires at least 33 sectors at the end for backup header
                        var gptReservedSpace = 34 * sectorSize;
                        endOffset = diskSize - gptReservedSpace;
                        if (endOffset < nextFreeSector)
                            endOffset = nextFreeSector;
                    }
                }
                else
                {
                    endOffset = nextFreeSector + size;
                    if (endOffset > diskSize)
                        endOffset = diskSize;
                }

                // Create partition
                RunProcess("parted", $"-s {diskIdentifier} mkpart primary {nextFreeSector}B {endOffset}B");

                // For MBR, set the boot flag on the first partition (some filesystems need this)
                if (tableType == PartitionTableType.MBR && i == 0)
                {
                    try
                    {
                        RunProcess("parted", $"-s {diskIdentifier} set {partitionNumber} boot on");
                    }
                    catch
                    {
                        // Ignore errors setting boot flag
                    }
                }

                nextFreeSector = endOffset;
            }

            // Re-read partition table
            RunProcess("partprobe", $"{diskIdentifier}");

            // Small delay to allow kernel to create partition devices
            System.Threading.Thread.Sleep(200);

            // Format each partition
            var mountPoints = new List<string>();
            for (int i = 0; i < partitions.Length; i++)
            {
                var (fsType, _) = partitions[i];
                var partitionNumber = i + 1;
                var partitionDevice = GetPartitionDevice(diskIdentifier, partitionNumber);

                // Wait for partition device to exist
                var retryCount = 0;
                while (!File.Exists(partitionDevice) && retryCount < 10)
                {
                    System.Threading.Thread.Sleep(100);
                    retryCount++;
                }

                if (!File.Exists(partitionDevice))
                    throw new InvalidOperationException($"Partition device {partitionDevice} did not appear after creation");

                // Format the partition
                FormatPartition(partitionDevice, fsType);
            }

            // Return empty array - partitions need to be mounted separately
            return Array.Empty<string>();
        }

        /// <inheritdoc />
        public string[] Mount(string diskIdentifier, string? baseMountPath = null, bool readOnly = false)
        {
            var mountPoints = new List<string>();
            var partitions = GetPartitionNames(diskIdentifier);

            foreach (var partitionDevice in partitions)
            {
                // Get filesystem type to handle special cases
                var fsType = GetFilesystemType(partitionDevice);

                // Skip swap and LVM partitions
                if (fsType == "swap" || partitionDevice.Contains("-part"))
                    continue;

                // Create mount point
                string mountPoint;
                if (!string.IsNullOrEmpty(baseMountPath))
                {
                    var partitionName = Path.GetFileName(partitionDevice);
                    mountPoint = Path.Combine(baseMountPath, partitionName);
                }
                else
                {
                    mountPoint = $"/mnt/duplicati_{Path.GetFileName(partitionDevice)}_{Guid.NewGuid():N}";
                }

                Directory.CreateDirectory(mountPoint);

                // Mount the partition
                var readOnlyArg = readOnly ? "-o ro" : "";
                var mountArgs = $"{readOnlyArg} {partitionDevice} \"{mountPoint}\"".Trim();
                RunProcess("mount", mountArgs);

                mountPoints.Add(mountPoint);
            }

            return mountPoints.ToArray();
        }

        /// <inheritdoc />
        public void Unmount(string diskIdentifier)
        {
            // Find all mounted partitions of this device
            var partitions = GetPartitionNames(diskIdentifier);

            foreach (var partitionDevice in partitions)
            {
                // Check if mounted
                var mountInfo = GetMountPoint(partitionDevice);
                if (!string.IsNullOrEmpty(mountInfo))
                {
                    try
                    {
                        RunProcess("umount", partitionDevice);
                    }
                    catch (Exception ex)
                    {
                        TestContext.Progress.WriteLine($"Warning: Failed to unmount {partitionDevice}: {ex.Message}");
                    }
                }
            }

            // Also try lazy unmount for any remaining mounts
            System.Threading.Thread.Sleep(100);
        }

        /// <inheritdoc />
        public void CleanupDisk(string imagePath, string? diskIdentifier = null)
        {
            // Determine the loop device if not provided
            if (string.IsNullOrEmpty(diskIdentifier))
            {
                if (_imageToLoopDevice.TryGetValue(imagePath, out var trackedDevice))
                {
                    diskIdentifier = trackedDevice;
                }
                else
                {
                    // Try to find the loop device using losetup -j
                    try
                    {
                        var output = RunProcess("losetup", $"-j \"{imagePath}\"");
                        if (!string.IsNullOrEmpty(output))
                        {
                            // Output format: /dev/loop0: [0005]:123456 (/path/to/image)
                            var match = Regex.Match(output, @"^(\S+):");
                            if (match.Success)
                                diskIdentifier = match.Groups[1].Value;
                        }
                    }
                    catch
                    {
                        // Ignore errors
                    }
                }
            }

            // Unmount partitions
            if (!string.IsNullOrEmpty(diskIdentifier))
            {
                try
                {
                    Unmount(diskIdentifier);
                }
                catch
                {
                    // Ignore errors
                }

                // Detach loop device
                try
                {
                    RunProcess("losetup", $"-d {diskIdentifier}");
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Warning: Failed to detach loop device {diskIdentifier}: {ex.Message}");
                }

                _trackedLoopDevices.Remove(diskIdentifier);
            }

            // Delete the image file
            if (File.Exists(imagePath))
            {
                try
                {
                    File.Delete(imagePath);
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Warning: Failed to delete image file {imagePath}: {ex.Message}");
                }
            }

            _imageToLoopDevice.Remove(imagePath);
        }

        /// <inheritdoc />
        public bool HasRequiredPrivileges()
        {
            // Check if running as root (uid 0)
            try
            {
                var uid = RunProcess("id", "-u").Trim();
                return uid == "0";
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public PartitionTableGeometry GetPartitionTable(string diskIdentifier)
        {
            var deviceName = Path.GetFileName(diskIdentifier);
            var tableType = PartitionTableType.Unknown;

            // Use blkid to get partition table type
            try
            {
                var output = RunProcess("blkid", $"-p -o value -s PTTYPE {diskIdentifier}").Trim();
                tableType = output.ToLower() switch
                {
                    "gpt" => PartitionTableType.GPT,
                    "dos" => PartitionTableType.MBR,
                    "msdos" => PartitionTableType.MBR,
                    _ => PartitionTableType.Unknown
                };
            }
            catch
            {
                // Ignore errors
            }

            var size = GetDiskSize(diskIdentifier);
            var sectorSize = GetSectorSize(diskIdentifier);

            return new PartitionTableGeometry
            {
                Type = tableType,
                Size = size,
                SectorSize = sectorSize
            };
        }

        /// <inheritdoc />
        public PartitionGeometry[] GetPartitions(string diskIdentifier)
        {
            var partitions = new List<PartitionGeometry>();
            var partitionNames = GetPartitionNames(diskIdentifier);

            for (int i = 0; i < partitionNames.Count; i++)
            {
                var partitionDevice = partitionNames[i];
                var partitionNumber = i + 1;

                try
                {
                    // Get partition info using blockdev
                    var start = 0L;
                    var size = 0L;

                    try
                    {
                        var startStr = RunProcess("blockdev", $"--getstart {partitionDevice}").Trim();
                        long.TryParse(startStr, out start);
                    }
                    catch { }

                    try
                    {
                        size = GetDiskSize(partitionDevice);
                    }
                    catch { }

                    // Get filesystem type
                    var fsType = FileSystemType.Unknown;
                    try
                    {
                        var fsStr = RunProcess("blkid", $"-o value -s TYPE {partitionDevice}").Trim();
                        fsType = ParseFilesystemType(fsStr);
                    }
                    catch { }

                    partitions.Add(new PartitionGeometry
                    {
                        Number = partitionNumber,
                        Type = PartitionType.Unknown,
                        StartOffset = start,
                        Size = size,
                        FilesystemType = fsType,
                        TableType = PartitionTableType.Unknown
                    });
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Warning: Failed to get info for partition {partitionDevice}: {ex.Message}");
                }
            }

            return partitions.ToArray();
        }

        /// <inheritdoc />
        public void FlushDisk(string diskIdentifier)
        {
            // Sync all filesystems
            RunProcess("sync", "");

            // Flush buffers for the specific device
            try
            {
                RunProcess("blockdev", $"--flushbufs {diskIdentifier}");
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <inheritdoc />
        public string ReAttach(string imagePath, string diskIdentifier, PartitionTableType tableType, bool readOnly = false)
        {
            // Unmount and detach
            try
            {
                Unmount(diskIdentifier);
            }
            catch { }

            try
            {
                RunProcess("losetup", $"-d {diskIdentifier}");
            }
            catch { }

            // Re-attach the loop device
            var readOnlyArg = readOnly ? "--read-only" : "";
            var newLoopDevice = RunProcess("losetup", $"--find --show --partscan {readOnlyArg} \"{imagePath}\"").Trim();

            if (string.IsNullOrEmpty(newLoopDevice) || !newLoopDevice.StartsWith("/dev/loop"))
                throw new InvalidOperationException($"Failed to re-attach loop device for {imagePath}");

            // Update tracking
            _trackedLoopDevices.Remove(diskIdentifier);
            _trackedLoopDevices.Add(newLoopDevice);
            _imageToLoopDevice[imagePath] = newLoopDevice;

            // Allow time for partition discovery
            System.Threading.Thread.Sleep(200);

            return newLoopDevice;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Clean up any remaining tracked loop devices
            foreach (var loopDevice in _trackedLoopDevices.ToList())
            {
                try
                {
                    // Find associated image
                    var imagePath = _imageToLoopDevice.FirstOrDefault(x => x.Value == loopDevice).Key;
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        CleanupDisk(imagePath, loopDevice);
                    }
                    else
                    {
                        // Just try to detach the loop device
                        RunProcess("losetup", $"-d {loopDevice}");
                    }
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Warning: Failed to dispose loop device {loopDevice}: {ex.Message}");
                }
            }

            _trackedLoopDevices.Clear();
            _imageToLoopDevice.Clear();
            GC.SuppressFinalize(this);
        }

        #region Helper Methods

        /// <summary>
        /// Runs a process and returns the output.
        /// </summary>
        private static string RunProcess(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Process {fileName} exited with code {process.ExitCode}: {error}");
            }

            return output;
        }

        /// <summary>
        /// Gets the partition device path for a given disk and partition number.
        /// </summary>
        private static string GetPartitionDevice(string diskIdentifier, int partitionNumber)
        {
            // For loop devices, partitions are named /dev/loop0p1, /dev/loop0p2, etc.
            if (diskIdentifier.StartsWith("/dev/loop"))
            {
                return $"{diskIdentifier}p{partitionNumber}";
            }

            // For NVMe devices, partitions are named /dev/nvme0n1p1, etc.
            if (diskIdentifier.Contains("nvme"))
            {
                return $"{diskIdentifier}p{partitionNumber}";
            }

            // For standard SCSI/SATA devices, partitions are named /dev/sda1, /dev/sda2, etc.
            return $"{diskIdentifier}{partitionNumber}";
        }

        /// <summary>
        /// Gets the list of partition device paths for a disk.
        /// </summary>
        private static List<string> GetPartitionNames(string diskIdentifier)
        {
            var partitions = new List<string>();
            var deviceName = Path.GetFileName(diskIdentifier);

            // Check for partition devices
            if (diskIdentifier.StartsWith("/dev/loop"))
            {
                // Loop device partitions: /dev/loop0p1, /dev/loop0p2, etc.
                for (int i = 1; i <= 16; i++)
                {
                    var partitionDevice = $"{diskIdentifier}p{i}";
                    if (File.Exists(partitionDevice))
                        partitions.Add(partitionDevice);
                    else if (i > 1)
                        break; // Stop after first missing partition
                }
            }
            else if (diskIdentifier.Contains("nvme"))
            {
                // NVMe partitions: /dev/nvme0n1p1, etc.
                for (int i = 1; i <= 16; i++)
                {
                    var partitionDevice = $"{diskIdentifier}p{i}";
                    if (File.Exists(partitionDevice))
                        partitions.Add(partitionDevice);
                    else if (i > 1)
                        break;
                }
            }
            else
            {
                // Standard partitions: /dev/sda1, /dev/sda2, etc.
                for (int i = 1; i <= 16; i++)
                {
                    var partitionDevice = $"{diskIdentifier}{i}";
                    if (File.Exists(partitionDevice))
                        partitions.Add(partitionDevice);
                    else if (i > 1)
                        break;
                }
            }

            return partitions;
        }

        /// <summary>
        /// Gets the mount point for a partition device.
        /// </summary>
        private static string? GetMountPoint(string partitionDevice)
        {
            try
            {
                var output = RunProcess("findmnt", $"--raw --noheadings -o TARGET {partitionDevice}").Trim();
                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the filesystem type of a partition.
        /// </summary>
        private static string GetFilesystemType(string partitionDevice)
        {
            try
            {
                return RunProcess("blkid", $"-o value -s TYPE {partitionDevice}").Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Formats a partition with the specified filesystem type.
        /// </summary>
        private static void FormatPartition(string partitionDevice, FileSystemType fsType)
        {
            var (command, args) = fsType switch
            {
                FileSystemType.FAT12 => ("mkfs.vfat", $"-F 12 {partitionDevice}"),
                FileSystemType.FAT16 => ("mkfs.vfat", $"-F 16 {partitionDevice}"),
                FileSystemType.FAT32 => ("mkfs.vfat", $"-F 32 {partitionDevice}"),
                FileSystemType.ExFAT => ("mkfs.exfat", $"{partitionDevice}"),
                FileSystemType.Ext2 => ("mkfs.ext2", $"-F {partitionDevice}"),
                FileSystemType.Ext3 => ("mkfs.ext3", $"-F {partitionDevice}"),
                FileSystemType.Ext4 => ("mkfs.ext4", $"-F {partitionDevice}"),
                FileSystemType.XFS => ("mkfs.xfs", $"-f {partitionDevice}"),
                FileSystemType.Btrfs => ("mkfs.btrfs", $"-f {partitionDevice}"),
                FileSystemType.NTFS => ("mkfs.ntfs", $"-F {partitionDevice}"),
                FileSystemType.ZFS => throw new NotSupportedException("ZFS is not supported for testing. ZFS requires pool creation which is incompatible with standard partition-based testing."),
                _ => throw new NotSupportedException($"Unsupported filesystem type on Linux: {fsType}")
            };

            RunProcess(command, args);
        }

        /// <summary>
        /// Gets the size of a disk or partition in bytes.
        /// </summary>
        private static long GetDiskSize(string devicePath)
        {
            var output = RunProcess("blockdev", $"--getsize64 {devicePath}").Trim();
            return long.TryParse(output, out var size) ? size : 0;
        }

        /// <summary>
        /// Gets the sector size of a disk.
        /// </summary>
        private static int GetSectorSize(string devicePath)
        {
            var output = RunProcess("blockdev", $"--getss {devicePath}").Trim();
            return int.TryParse(output, out var size) ? size : 512;
        }

        /// <summary>
        /// Parses a filesystem type string to the FileSystemType enum.
        /// </summary>
        private static FileSystemType ParseFilesystemType(string fsType)
        {
            return fsType.ToLower() switch
            {
                "ntfs" => FileSystemType.NTFS,
                "vfat" or "fat" => FileSystemType.FAT32, // Could be FAT12/16/32
                "exfat" => FileSystemType.ExFAT,
                "hfsplus" or "hfs+" => FileSystemType.HFSPlus,
                "apfs" => FileSystemType.APFS,
                "ext2" => FileSystemType.Ext2,
                "ext3" => FileSystemType.Ext3,
                "ext4" => FileSystemType.Ext4,
                "xfs" => FileSystemType.XFS,
                "btrfs" => FileSystemType.Btrfs,
                "zfs" => FileSystemType.ZFS,
                "refs" => FileSystemType.ReFS,
                _ => FileSystemType.Unknown
            };
        }

        #endregion
    }
}
