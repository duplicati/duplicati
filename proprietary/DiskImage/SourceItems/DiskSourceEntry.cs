// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

/// <summary>
/// Represents a disk as a source entry for backup operations.
/// This is the root entry in the disk image hierarchy.
/// </summary>
internal class DiskSourceEntry(SourceProvider provider, IRawDisk disk, string subpath)
    : DiskImageEntryBase(CalculatePath(provider.MountedPath, disk.DevicePath))
{
    /// <summary>
    /// Helper to construct the slightly odd path needed.
    /// </summary>
    /// <param name="mountPoint">The mount point used</param>
    /// <param name="devicePath">The disk device path</param>
    /// <returns>The mounted full path</returns>
    private static string CalculatePath(string mountPoint, string devicePath)
    {
        // Example: X:\Test\ + \\.\\PHYSICAL_DRIVE0 => X:\Test\\.\PHYSICAL_DRIVE0
        devicePath = Util.AppendDirSeparator(devicePath);
        if (string.IsNullOrWhiteSpace(mountPoint))
            return devicePath;

        if (SourceProvider.GetDevicePrefix().StartsWith(System.IO.Path.DirectorySeparatorChar) && mountPoint.EndsWith(System.IO.Path.DirectorySeparatorChar))
            mountPoint = mountPoint.Substring(0, mountPoint.Length - 1);

        return mountPoint + devicePath;

    }

    /// <inheritdoc />
    public override bool IsFolder => true;

    /// <inheritdoc />
    public override bool IsRootEntry => true;

    /// <inheritdoc />
    public override long Size => disk.Size;

    /// <inheritdoc />
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Create a consolidated geometry metadata object
        var geometryMetadata = new GeometryMetadata
        {
            Version = 1,
            Disk = new DiskGeometry
            {
                DevicePath = disk.DevicePath,
                Size = disk.Size,
                SectorSize = disk.SectorSize,
                Sectors = disk.Sectors,
                TableType = PartitionTableType.Unknown // Will be updated if partition table is found
            },
            Partitions = new List<PartitionGeometry>(),
            Filesystems = new List<FilesystemGeometry>()
        };

        var table = await PartitionTableFactory.CreateAsync(disk, cancellationToken);
        if (table != null)
        {
            // Update disk geometry with partition table type
            geometryMetadata.Disk!.TableType = table.TableType;

            // Add partition table geometry
            geometryMetadata.PartitionTable = new PartitionTableGeometry
            {
                Type = table.TableType,
                SectorSize = disk.SectorSize,
                Size = 0, // Will be updated below
                HasProtectiveMbr = table.TableType == PartitionTableType.GPT,
                HeaderSize = table.TableType == PartitionTableType.GPT ? 92 : 0,
                MbrSize = table.TableType == PartitionTableType.MBR ? 512 : 0
            };

            // Get the actual partition table data size
            try
            {
                using var tableData = await table.GetPartitionTableDataAsync(cancellationToken);
                geometryMetadata.PartitionTable.Size = tableData.Length;
            }
            catch
            {
                // Use estimated sizes if we can't get the actual data
                geometryMetadata.PartitionTable.Size = table.TableType switch
                {
                    PartitionTableType.MBR => 512,
                    PartitionTableType.GPT => 512 + 512 + (128 * 4),
                    _ => 0
                };
            }

            // First, enumerate partitions and collect their geometry
            var partitions = new List<IPartition>();
            await foreach (var partition in table.EnumeratePartitions(cancellationToken))
            {
                partitions.Add(partition);

                // Add partition geometry to the consolidated metadata
                geometryMetadata.Partitions.Add(new PartitionGeometry
                {
                    Number = partition.PartitionNumber,
                    Type = partition.Type,
                    StartOffset = partition.StartOffset,
                    Size = partition.Size,
                    Name = partition.Name,
                    FilesystemType = partition.FilesystemType,
                    VolumeGuid = partition.VolumeGuid,
                    TableType = partition.PartitionTable.TableType
                });

                // Create partition source entry which will add filesystem geometry
                var partitionEntry = new PartitionSourceEntry(this.Path, partition, provider.TreatFilesystemAsUnknown);

                // Pre-enumerate to collect filesystem geometry
                await foreach (var child in partitionEntry.Enumerate(cancellationToken))
                {
                    if (child is FilesystemSourceEntry fsEntry)
                    {
                        // Collect filesystem geometry
                        var fsGeometry = await fsEntry.GetFilesystemGeometry(cancellationToken);
                        if (fsGeometry != null)
                        {
                            geometryMetadata.Filesystems.Add(fsGeometry);
                        }
                    }
                }
            }

            // Now yield the consolidated geometry metadata file with all geometry collected
            yield return new GeometrySourceEntry(this.Path, geometryMetadata);

            // If the source pointed to a specific partition, only emit that one now
            var subpathmarker = string.IsNullOrWhiteSpace(subpath) || subpath.StartsWith(System.IO.Path.DirectorySeparatorChar)
                ? subpath
                : System.IO.Path.DirectorySeparatorChar + subpath;


            // Then yield the actual partition and filesystem entries
            foreach (var partition in partitions)
            {
                var pse = new PartitionSourceEntry(this.Path, partition, provider.TreatFilesystemAsUnknown);
                if (string.IsNullOrWhiteSpace(subpathmarker) || pse.Path.EndsWith(subpathmarker))
                    yield return pse;
            }
        }
        else
        {
            // No partition table found, still yield the disk geometry
            yield return new GeometrySourceEntry(this.Path, geometryMetadata);
        }
    }

    /// <inheritdoc />
    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add disk-level metadata
        metadata["diskimage:Type"] = "disk";
        metadata["disk:DevicePath"] = disk.DevicePath;
        metadata["disk:Size"] = disk.Size.ToString();
        metadata["disk:SectorSize"] = disk.SectorSize.ToString();
        metadata["disk:Sectors"] = disk.Sectors.ToString();

        // Add partition table metadata if available
        var table = await PartitionTableFactory.CreateAsync(disk, cancellationToken);
        if (table != null)
        {
            metadata["disk:PartitionTableType"] = table.TableType.ToString();

            // Get the full partition table data for restore
            try
            {
                using var tableData = await table.GetPartitionTableDataAsync(cancellationToken);
                metadata["disk:PartitionTableDataSize"] = tableData.Length.ToString();

                // For GPT disks, also record specific info
                if (table.TableType == PartitionTableType.GPT)
                {
                    metadata["disk:HasProtectiveMbr"] = "true";
                }
            }
            catch
            {
                // Partition table data not available
            }
        }

        return metadata;
    }
}
