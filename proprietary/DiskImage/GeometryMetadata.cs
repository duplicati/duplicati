// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duplicati.Proprietary.DiskImage;

/// <summary>
/// Represents the consolidated geometry metadata for a disk image.
/// This metadata is stored as a single file during backup and used during restore
/// to reconstruct the disk, partition table, partition, and filesystem structures.
/// </summary>
public class GeometryMetadata
{
    /// <summary>
    /// The version of the metadata format.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Disk-level geometry information.
    /// </summary>
    public DiskGeometry? Disk { get; set; }

    /// <summary>
    /// Partition table-level geometry information.
    /// </summary>
    public PartitionTableGeometry? PartitionTable { get; set; }

    /// <summary>
    /// Array of partition geometries.
    /// </summary>
    public List<PartitionGeometry>? Partitions { get; set; }

    /// <summary>
    /// Array of filesystem geometries, keyed by partition number.
    /// </summary>
    public List<FilesystemGeometry>? Filesystems { get; set; }

    /// <summary>
    /// Serializes this metadata to a JSON string.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        });
    }

    /// <summary>
    /// Deserializes a JSON string to a GeometryMetadata instance.
    /// </summary>
    public static GeometryMetadata? FromJson(string json)
    {
        return JsonSerializer.Deserialize<GeometryMetadata>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}

/// <summary>
/// Disk-level geometry information.
/// </summary>
public class DiskGeometry
{
    /// <summary>
    /// The device path of the source disk.
    /// </summary>
    public string? DevicePath { get; set; }

    /// <summary>
    /// The total size of the disk in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The sector size in bytes.
    /// </summary>
    public int SectorSize { get; set; }

    /// <summary>
    /// The number of sectors on the disk.
    /// </summary>
    public int Sectors { get; set; }

    /// <summary>
    /// The type of partition table on the disk.
    /// </summary>
    public PartitionTableType TableType { get; set; }
}

/// <summary>
/// Partition table-level geometry information.
/// </summary>
public class PartitionTableGeometry
{
    /// <summary>
    /// The type of partition table (MBR, GPT, etc.).
    /// </summary>
    public PartitionTableType Type { get; set; }

    /// <summary>
    /// The size of the partition table data in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The sector size of the disk.
    /// </summary>
    public int SectorSize { get; set; }

    /// <summary>
    /// For GPT: whether the disk has a protective MBR.
    /// </summary>
    public bool HasProtectiveMbr { get; set; }

    /// <summary>
    /// For GPT: the size of the GPT header in bytes.
    /// </summary>
    public int HeaderSize { get; set; }

    /// <summary>
    /// For MBR: the size of the MBR in bytes (typically 512).
    /// </summary>
    public int MbrSize { get; set; }
}

/// <summary>
/// Partition-level geometry information.
/// </summary>
public class PartitionGeometry
{
    /// <summary>
    /// The partition number (1-based).
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// The partition type.
    /// </summary>
    public PartitionType Type { get; set; }

    /// <summary>
    /// The starting offset of the partition in bytes.
    /// </summary>
    public long StartOffset { get; set; }

    /// <summary>
    /// The size of the partition in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The partition name (if available).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The filesystem type detected for this partition.
    /// </summary>
    public FileSystemType FilesystemType { get; set; }

    /// <summary>
    /// The volume GUID for GPT partitions.
    /// </summary>
    public Guid? VolumeGuid { get; set; }

    /// <summary>
    /// The partition table type this partition belongs to.
    /// </summary>
    public PartitionTableType TableType { get; set; }
}

/// <summary>
/// Filesystem-level geometry information.
/// </summary>
public class FilesystemGeometry
{
    /// <summary>
    /// The partition number this filesystem resides on.
    /// </summary>
    public int PartitionNumber { get; set; }

    /// <summary>
    /// The type of filesystem.
    /// </summary>
    public FileSystemType Type { get; set; }

    /// <summary>
    /// The starting offset of the partition in bytes.
    /// </summary>
    public long PartitionStartOffset { get; set; }

    /// <summary>
    /// The block size used by the filesystem.
    /// </summary>
    public int BlockSize { get; set; }

    /// <summary>
    /// Filesystem-specific metadata as a JSON string.
    /// </summary>
    public string? Metadata { get; set; }
}
