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
using System.Threading;
using System.Xml.Linq;
using Duplicati.Proprietary.DiskImage;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// macOS implementation of <see cref="IDiskImageHelper"/> using hdiutil and standard macOS tools.
    /// </summary>
    internal class MacOSDiskImageHelper : IDiskImageHelper
    {
        /// <summary>
        /// Tracks the last attached disk device path for cleanup.
        /// </summary>
        private string? _lastDiskDevice;

        /// <inheritdoc />
        public string CreateDisk(string imagePath, long sizeB)
        {
            // Ensure the directory exists
            var directory = Path.GetDirectoryName(imagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Delete existing image if it exists
            if (File.Exists(imagePath))
                try
                {
                    CleanupDisk(imagePath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }

            // Create a raw disk image using hdiutil
            // Use UDIF format with "Free Space" filesystem (unformatted)
            var psi = new ProcessStartInfo
            {
                FileName = "hdiutil",
                Arguments = $"create -size {sizeB}b -type UDIF -layout NONE -o \"{imagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Failed to create disk image '{imagePath}': {error}");
            }

            // Attach the disk image without mounting
            psi = new ProcessStartInfo
            {
                FileName = "hdiutil",
                Arguments = $"attach -nomount \"{imagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            string diskDevice;
            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Failed to attach disk image: {error}");

                // Parse the disk device from output (e.g., "/dev/disk4")
                diskDevice = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                    ?? throw new InvalidOperationException("Failed to get disk device from hdiutil attach output");

                _lastDiskDevice = diskDevice;
            }

            // Return the raw device path for direct I/O
            return diskDevice.Replace("/dev/disk", "/dev/rdisk");
        }

        private string[] GetPartitionNames(string diskIdentifier)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "diskutil",
                Arguments = $"list {diskIdentifier}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Very hardcoded to disks with < 10 partitions and no identifiers with spaces. Should be sufficient for test purposes.
            return output
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 1 && x[1] == ':')
                .Skip(2)
                .Select(x => x.Split([' '], StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();
        }

        private string[] GetMountPoints(string diskIdentifier)
        {
            var partitions = GetPartitionNames(diskIdentifier);
            var mountPoints = new System.Collections.Generic.List<string>();

            foreach (var partition in partitions)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "diskutil",
                    Arguments = $"info /dev/{partition}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var mountLine = output
                    .Split('\n')
                    .FirstOrDefault(l => l.Trim().StartsWith("Mount Point:"))
                    ?? throw new InvalidOperationException($"Failed to get mount point for partition {partition} of disk {diskIdentifier}");

                var mountPoint = mountLine.Split([':'], 2)[1].Trim();
                if (!string.IsNullOrEmpty(mountPoint) && mountPoint != "Not mounted")
                    mountPoints.Add(mountPoint);
            }

            return [.. mountPoints];
        }

        /// <inheritdoc />
        public string[] InitializeDisk(string diskIdentifier, Proprietary.DiskImage.PartitionTableType tableType, (Proprietary.DiskImage.FileSystemType, long)[] partitions)
        {
            if (tableType == Proprietary.DiskImage.PartitionTableType.Unknown)
                throw new ArgumentException("Invalid partition table type", nameof(tableType));

            // Use diskutil to partition the disk with the specified scheme
            // "Free Space" means no filesystem, just the partition table
            var partitionStrings = partitions.Length > 0 ? partitions.Select(p =>
            {
                var fsType = p.Item1 switch
                {
                    Proprietary.DiskImage.FileSystemType.NTFS => throw new NotSupportedException("NTFS is not natively supported on macOS for creating partitions. Use ExFAT or FAT32 instead."),
                    Proprietary.DiskImage.FileSystemType.FAT32 => "MS-DOS FAT32",
                    Proprietary.DiskImage.FileSystemType.ExFAT => "ExFAT",
                    Proprietary.DiskImage.FileSystemType.HFSPlus => "HFS+",
                    Proprietary.DiskImage.FileSystemType.APFS => "APFS",
                    _ => throw new ArgumentException($"Unsupported filesystem type on macOS: {p.Item1}", nameof(partitions))
                };

                var sizeArg = p.Item2 > 0 ? $"{p.Item2}" : "0";
                return $"{fsType} \"Partition\" {sizeArg}";
            })
            : ["Free Space \"\" 0"];
            var partitionArgs = string.Join(" ", partitionStrings);

            var psi = new ProcessStartInfo
            {
                FileName = "diskutil",
                Arguments = $"partitionDisk {diskIdentifier} {tableType.ToString().ToUpperInvariant()} {partitionArgs}",
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
                throw new InvalidOperationException($"Failed to initialize disk: {error}");

            if (partitions.Length == 0)
                return [];

            return GetMountPoints(diskIdentifier);
        }

        /// <inheritdoc />
        public string[] Mount(string diskIdentifier)
        {
            // Check if already mounted
            var mountPoints = GetMountPoints(diskIdentifier);
            if (mountPoints.Length > 0)
                return mountPoints;

            // Mount all volumes on the disk
            var psi = new ProcessStartInfo
            {
                FileName = "diskutil",
                Arguments = $"mountDisk {diskIdentifier}",
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
                throw new InvalidOperationException($"Failed to mount disk: {error}");

            // Find the mount points for the mounted partitions
            return GetMountPoints(diskIdentifier);
        }

        public void Unmount(string diskIdentifier)
        {
            // Unmount all volumes on the disk
            var psi = new ProcessStartInfo
            {
                FileName = "diskutil",
                Arguments = $"unmountDisk {diskIdentifier}",
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
                throw new InvalidOperationException($"Failed to mount disk: {error}");
        }

        /// <inheritdoc />
        public void FlushDisk(string diskIdentifier)
        {
            // On macOS, use sync to flush all filesystem buffers
            var psi = new ProcessStartInfo
            {
                FileName = "sync",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            process.WaitForExit();
        }

        /// <inheritdoc />
        public void CleanupDisk(string imagePath, string? diskIdentifier = null)
        {
            if (diskIdentifier is null)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "hdiutil",
                    Arguments = $"info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to get hdiutil info: {process.StandardError.ReadToEnd()}");
                }

                diskIdentifier = output
                    .Split("================================================")
                    .Skip(1) // Skip the header
                    .Select(d =>
                    {
                        var lines = d
                            .Split('\n')
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .ToList();
                        var imagePathLine = lines.FirstOrDefault(l => l.StartsWith("image-path"));
                        if (imagePathLine == null)
                            return null;

                        var path = imagePathLine.Split([':'], 2)[1].Trim();
                        if (!string.Equals(path, imagePath, StringComparison.OrdinalIgnoreCase))
                            return null;

                        var deviceLine = lines.FirstOrDefault(l => l.StartsWith("/dev/"));
                        if (deviceLine == null)
                            return null;

                        var device = deviceLine
                            .Split(['\t'], 2)[0]
                            .Trim();

                        return device;
                    })
                    .FirstOrDefault(d => d != null);
            }

            if (diskIdentifier is null)
                throw new InvalidOperationException($"Failed to determine disk identifier for {imagePath} during cleanup");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "diskutil",
                    Arguments = $"eject {diskIdentifier}",
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
                    TestContext.Progress.WriteLine($"Warning: Failed to eject disk {diskIdentifier}: {error}");
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to detach disk image: {ex.Message}");
            }

            try
            {
                if (File.Exists(imagePath))
                    File.Delete(imagePath);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Failed to delete disk image file: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public bool HasRequiredPrivileges()
        {
            // On macOS, check if running as root (UID 0)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "id",
                    Arguments = "-u",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (int.TryParse(output, out int uid))
                {
                    return uid == 0;
                }
            }
            catch
            {
                // Ignore errors
            }

            return false;
        }

        /// <inheritdoc />
        public Proprietary.DiskImage.PartitionTableGeometry GetPartitionTable(string diskIdentifier)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "diskutil",
                Arguments = $"info {diskIdentifier}",
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
                throw new InvalidOperationException($"Process diskutil exited with code {process.ExitCode}: {error}");

            var lines = output
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            PartitionTableType tableType = PartitionTableType.Unknown;
            long size = -1;
            int sectorSize = -1;
            foreach (var line in lines)
            {
                var parts = line.Split([':'], 2);
                switch (parts[0].Trim())
                {
                    case "Device Node":
                        if (!string.Equals(parts[1].Trim(), diskIdentifier, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Disk identifier mismatch in diskutil info output: expected {diskIdentifier}, got {parts[1].Trim()}");
                        break;
                    case "Content (IOContent)":
                        tableType = parts[1].Trim() switch
                        {
                            "GUID_partition_scheme" => PartitionTableType.GPT,
                            "MBR_partition_scheme" => PartitionTableType.MBR,
                            _ => PartitionTableType.Unknown
                        };
                        break;
                    case "Disk Size":
                        var sizePart = parts[1].Trim().Split(' ').FirstOrDefault();
                        var unitPart = parts[1].Trim().Split(' ').Skip(1).FirstOrDefault();
                        if (sizePart != null && unitPart != null)
                        {
                            if (long.TryParse(sizePart, out long parsedSize))
                            {
                                size = unitPart switch
                                {
                                    "TB" => parsedSize * 1_000_000_000_000,
                                    "GB" => parsedSize * 1_000_000_000,
                                    "MB" => parsedSize * 1_000_000,
                                    "KB" => parsedSize * 1_000,
                                    "B" => parsedSize,
                                    _ => -1
                                };
                            }
                        }
                        break;
                    case "Device Block Size":
                        var sectorSizePart = parts[1].Trim().Split(' ').FirstOrDefault();
                        if (sectorSizePart != null && int.TryParse(sectorSizePart, out int parsedSectorSize))
                            sectorSize = parsedSectorSize;
                        break;
                }
            }

            if (tableType == PartitionTableType.Unknown || size == -1 || sectorSize == -1)
                throw new InvalidOperationException($"Failed to retrieve partition table information for disk {diskIdentifier}");

            return new Proprietary.DiskImage.PartitionTableGeometry
            {
                Type = tableType,
                Size = size,
                SectorSize = sectorSize
                // Ignore the rest for now.
            };
        }

        /// <inheritdoc />
        public PartitionGeometry[] GetPartitions(string diskIdentifier)
        {
            var names = GetPartitionNames(diskIdentifier);

            var partitions = new List<PartitionGeometry>();
            foreach (var name in names)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "diskutil",
                    Arguments = $"info /dev/{name}",
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
                    throw new InvalidOperationException($"Process diskutil exited with code {process.ExitCode}: {error}");

                var lines = output
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList();

                PartitionGeometry partition = new()
                {
                    Number = int.TryParse(name.AsSpan(name.Length - 1), out int num) ? num : -1,
                    Type = PartitionType.Unknown,
                    StartOffset = -1,
                    Size = -1,
                    Name = null,
                    FilesystemType = Proprietary.DiskImage.FileSystemType.Unknown,
                    VolumeGuid = null,
                    TableType = PartitionTableType.Unknown
                };

                foreach (var line in lines)
                {
                    var parts = line.Split([':'], 2);
                    switch (parts[0].Trim())
                    {
                        case "Device Node":
                            if (!string.Equals(parts[1].Trim(), $"/dev/{name}", StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException($"Partition identifier mismatch in diskutil info output: expected /dev/{name}, got {parts[1].Trim()}");
                            break;
                        case "Partition Offset":
                            var offsetPart = parts[1].Trim().Split(' ').FirstOrDefault();
                            if (offsetPart != null && long.TryParse(offsetPart, out long parsedOffset))
                                partition.StartOffset = parsedOffset;
                            break;
                        case "Disk Size":
                            var sizePart = parts[1].Trim().Split(' ').FirstOrDefault();
                            if (sizePart != null && long.TryParse(sizePart, out long parsedSize))
                                partition.Size = parsedSize;
                            break;
                        case "Volume Name":
                            partition.Name = parts[1].Trim();
                            break;
                        case "File System Personality":
                            partition.FilesystemType = parts[1].Trim() switch
                            {
                                "MS-DOS FAT32" => Proprietary.DiskImage.FileSystemType.FAT32,
                                "ExFAT" => Proprietary.DiskImage.FileSystemType.ExFAT,
                                "HFS+" => Proprietary.DiskImage.FileSystemType.HFSPlus,
                                "APFS" => Proprietary.DiskImage.FileSystemType.APFS,
                                _ => Proprietary.DiskImage.FileSystemType.Unknown
                            };
                            break;
                        case "Volume UUID":
                            if (Guid.TryParse(parts[1].Trim(), out Guid parsedGuid))
                                partition.VolumeGuid = parsedGuid;
                            break;
                        default:
                            // Ignore other lines for now
                            break;
                    }
                }

                partitions.Add(partition);
            }

            return partitions.ToArray();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose
            GC.SuppressFinalize(this);
        }
    }
}
