using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Raw;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Represents a single partition on a disk.
/// </summary>
public interface IPartition : IDisposable
{
    /// <summary>
    /// Gets the parent partition table.
    /// </summary>
    IPartitionTable PartitionTable { get; }

    /// <summary>
    /// Gets the partition number (1-based).
    /// </summary>
    int PartitionNumber { get; }

    /// <summary>
    /// Gets the partition type identifier.
    /// </summary>
    PartitionType Type { get; }

    /// <summary>
    /// Gets the starting offset in bytes.
    /// </summary>
    long StartOffset { get; }

    /// <summary>
    /// Gets the size in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Gets the partition name (if available).
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Gets the filesystem type (if detectable).
    /// </summary>
    FileSystemType FilesystemType { get; }

    /// <summary>
    /// Gets the volume GUID (for GPT).
    /// </summary>
    Guid? VolumeGuid { get; }

    /// <summary>
    /// Creates a stream to read the raw partition data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream for reading partition data.</returns>
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Partition table parsing interface.
/// </summary>
public interface IPartitionTable : IDisposable
{
    /// <summary>
    /// Gets the raw disk this table was parsed from.
    /// </summary>
    IRawDisk RawDisk { get; }

    /// <summary>
    /// Gets the partition table type.
    /// </summary>
    PartitionTableType TableType { get; }

    /// <summary>
    /// Enumerates all partitions on the disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of partitions.</returns>
    IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a specific partition by number.
    /// </summary>
    /// <param name="partitionNumber">The partition number.</param>
    /// <returns>The partition, or null if not found.</returns>
    Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the protective MBR (for GPT disks).
    /// </summary>
    Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken);
}
