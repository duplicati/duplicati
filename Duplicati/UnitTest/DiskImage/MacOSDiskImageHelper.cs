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
            RunProcess("hdiutil", $"create -size {sizeB} -type UDIF -layout NONE -o \"{imagePath}\"");

            var output = RunProcess("hdiutil", $"attach -nomount \"{imagePath}\"");

            // Parse the disk device from output (e.g., "/dev/disk4")
            var diskDevice = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                ?? throw new InvalidOperationException("Failed to get disk device from hdiutil attach output");

            // Return the raw device path for direct I/O
            return diskDevice.Replace("/dev/disk", "/dev/rdisk");
        }

        private string[] GetPartitionNames(string diskIdentifier)
        {
            var output = RunProcess("diskutil", $"list {diskIdentifier}");

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
                var output = RunProcess("diskutil", $"info /dev/{partition}");

                // Check if this is an APFS Physical Store
                var isApfsPhysicalStore = output.Contains("APFS Physical Store") ||
                    output.Split('\n').Any(l => l.Trim().StartsWith("Partition Type:") && l.Contains("Apple_APFS"));

                if (isApfsPhysicalStore)
                {
                    // For APFS, get the container and then get volumes from it
                    var containerLine = output
                        .Split('\n')
                        .FirstOrDefault(l => l.Trim().StartsWith("APFS Container:"));

                    if (containerLine != null)
                    {
                        var containerId = containerLine.Split([':'], 2)[1].Trim();
                        // Get volumes from the container (they are like disk5s1, disk5s2, etc.)
                        var containerVolumes = GetApfsContainerVolumes(containerId);
                        foreach (var volume in containerVolumes)
                        {
                            var volumeOutput = RunProcess("diskutil", $"info {volume}");
                            var mountPoint = ExtractMountPoint(volumeOutput);
                            if (!string.IsNullOrEmpty(mountPoint))
                                mountPoints.Add(mountPoint);
                        }
                    }
                }
                else
                {
                    // Regular partition - extract mount point directly
                    var mountPoint = ExtractMountPoint(output);
                    if (!string.IsNullOrEmpty(mountPoint))
                        mountPoints.Add(mountPoint);
                }
            }

            return [.. mountPoints];
        }

        private string[] GetApfsContainerVolumes(string containerId)
        {
            var output = RunProcess("diskutil", $"list {containerId}");

            // Parse volumes from container listing (format: disk5s1, disk5s2, etc.)
            return output
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.StartsWith(containerId + "s", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Split([' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();
        }

        private string? ExtractMountPoint(string diskutilInfoOutput)
        {
            // Look for "Mount Point:" line
            var mountLine = diskutilInfoOutput
                .Split('\n')
                .FirstOrDefault(l => l.Trim().StartsWith("Mount Point:"));

            if (mountLine == null)
                return null;

            var mountPoint = mountLine.Split([':'], 2)[1].Trim();

            // Return null if not mounted or not applicable
            if (string.IsNullOrEmpty(mountPoint) ||
                mountPoint == "Not mounted" ||
                mountPoint == "Not applicable (no file system)")
                return null;

            return mountPoint;
        }

        /// <inheritdoc />
        public string[] InitializeDisk(string diskIdentifier, Proprietary.DiskImage.PartitionTableType tableType, (Proprietary.DiskImage.FileSystemType, long)[] partitions)
        {
            if (tableType == Proprietary.DiskImage.PartitionTableType.Unknown)
                return []; // No partition table, so nothing to initialize

            // Use diskutil to partition the disk with the specified scheme
            // "Free Space" means no filesystem, just the partition table
            var partitionStrings = partitions.Length > 0 ? partitions.Select(p =>
            {
                var fsType = p.Item1 switch
                {
                    Proprietary.DiskImage.FileSystemType.NTFS => throw new NotSupportedException("NTFS is not natively supported on macOS for creating partitions. Use ExFAT or FAT32 instead."),
                    Proprietary.DiskImage.FileSystemType.FAT32 => "\"MS-DOS FAT32\"",
                    Proprietary.DiskImage.FileSystemType.ExFAT => "ExFAT",
                    Proprietary.DiskImage.FileSystemType.HFSPlus => "HFS+",
                    Proprietary.DiskImage.FileSystemType.APFS => "APFS",
                    _ => throw new ArgumentException($"Unsupported filesystem type on macOS: {p.Item1}", nameof(partitions))
                };

                var sizeArg = p.Item2 > 0 ? $"{p.Item2}" : "0";
                return $"{fsType} \"Partition\" {sizeArg}";
            })
            : ["\"Free Space\" \"\" 0"];
            var partitionArgs = string.Join(" ", partitionStrings);

            RunProcess("diskutil", $"partitionDisk {diskIdentifier} {tableType.ToString().ToUpperInvariant()} {partitionArgs}");

            if (partitions.Length == 0)
                return [];

            return GetMountPoints(diskIdentifier);
        }

        /// <inheritdoc />
        public string[] Mount(string diskIdentifier)
        {
            // Check if already mounted
            try
            {
                var mountPoints = GetMountPoints(diskIdentifier);
                if (mountPoints.Length > 0)
                    return mountPoints;
            }
            catch
            {
                // Ignore errors and try mounting
            }

            // Mount all volumes on the disk
            RunProcess("diskutil", $"mountDisk {diskIdentifier}");

            // Find the mount points for the mounted partitions
            return GetMountPoints(diskIdentifier);
        }

        public void Unmount(string diskIdentifier)
        {
            // Unmount all volumes on the disk
            RunProcess("diskutil", $"unmountDisk {diskIdentifier}");
        }

        /// <inheritdoc />
        public void FlushDisk(string diskIdentifier)
        {
            // On macOS, use sync to flush all filesystem buffers
            RunProcess("sync", "");
        }

        /// <inheritdoc />
        public void CleanupDisk(string imagePath, string? diskIdentifier = null)
        {
            if (diskIdentifier is null)
            {
                var output = RunProcess("hdiutil", $"info");

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
                RunProcess("diskutil", $"eject {diskIdentifier}");
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
            // Root is not required for virtual disks.
            return true;
        }

        /// <inheritdoc />
        public Proprietary.DiskImage.PartitionTableGeometry GetPartitionTable(string diskIdentifier)
        {
            var output = RunProcess("diskutil", $"info {diskIdentifier}");

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
                    case "Device Identifier":
                        if (!diskIdentifier.EndsWith(parts[1].Trim(), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Disk identifier mismatch in diskutil info output: {diskIdentifier} doesn't end with {parts[1].Trim()}");
                        break;
                    case "Content (IOContent)":
                        tableType = parts[1].Trim() switch
                        {
                            "GUID_partition_scheme" => PartitionTableType.GPT,
                            "FDisk_partition_scheme" => PartitionTableType.MBR,
                            _ => PartitionTableType.Unknown
                        };
                        break;
                    case "Disk Size":
                        var totalbytesPart = parts[1].Trim().Split(' ').Skip(2).FirstOrDefault()?.TrimStart('(').TrimEnd(')');
                        if (totalbytesPart is not null)
                            size = long.TryParse(totalbytesPart, out long parsedSize) ? parsedSize : -1;
                        break;
                    case "Device Block Size":
                        var sectorSizePart = parts[1].Trim().Split(' ').FirstOrDefault();
                        if (sectorSizePart != null && int.TryParse(sectorSizePart, out int parsedSectorSize))
                            sectorSize = parsedSectorSize;
                        break;
                }
            }

            if (size == -1 || sectorSize == -1)
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
                var output = RunProcess("diskutil", $"info /dev/{name}");

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

        private string RunProcess(string fileName, string arguments)
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
                Console.WriteLine($"Error running process {fileName} {arguments}");
                Console.WriteLine($"Error: {error}");
                Console.WriteLine($"Output: {output}");
                throw new InvalidOperationException($"Process {fileName} exited with code {process.ExitCode}: {error}");
            }

            return output;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose
            GC.SuppressFinalize(this);
        }
    }
}
