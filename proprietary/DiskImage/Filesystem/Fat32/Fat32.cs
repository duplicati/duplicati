// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem.Fat32;

/// <summary>
/// Metadata for FAT32 filesystems, storing cluster and block size information.
/// </summary>
public sealed record Fat32FilesystemMetadata
{
    /// <summary>
    /// Gets the block size used for reading/writing data.
    /// </summary>
    public int BlockSize { get; init; }

    /// <summary>
    /// Gets the size of a cluster in bytes.
    /// </summary>
    public int ClusterSize { get; init; }

    /// <summary>
    /// Gets the total number of clusters in the volume.
    /// </summary>
    public long TotalClusters { get; init; }

    /// <summary>
    /// Gets the number of allocated (in-use) clusters.
    /// </summary>
    public long AllocatedClusters { get; init; }

    /// <summary>
    /// Gets the number of free clusters.
    /// </summary>
    public long FreeClusters { get; init; }
}

/// <summary>
/// Represents a file (block) in a FAT32 filesystem.
/// </summary>
public class Fat32File : IFile
{
    /// <inheritdoc />
    public string? Path { get; init; }

    /// <inheritdoc />
    public long? Address { get; init; }

    /// <inheritdoc />
    public long Size { get; init; }

    /// <inheritdoc />
    public bool IsDirectory => false;

    /// <summary>
    /// Gets the last modification timestamp of the block.
    /// This is the maximum timestamp of any file whose clusters overlap this block.
    /// </summary>
    public DateTime? LastModified { get; init; }

    /// <summary>
    /// Gets a value indicating whether the block contains any allocated clusters.
    /// </summary>
    public bool IsAllocated { get; init; }
}

/// <summary>
/// Represents a FAT32 filesystem.
/// Provides filesystem-aware block-level access with cluster allocation tracking.
/// </summary>
internal class Fat32Filesystem : IFilesystem
{
    /// <summary>
    /// Cache of zero buffers keyed by block size.
    /// </summary>
    private static readonly ConcurrentDictionary<int, byte[]> s_zeroBuffers = new();

    /// <inheritdoc />
    public FileSystemType Type => FileSystemType.FAT32;

    /// <inheritdoc />
    public IPartition Partition { get; }

    /// <summary>
    /// Gets the block size used for reading/writing data.
    /// </summary>
    private readonly int m_blockSize;

    /// <summary>
    /// The parsed FAT32 boot sector.
    /// </summary>
    private readonly Fat32BootSector m_bootSector;

    /// <summary>
    /// The FAT table reader.
    /// </summary>
    private readonly Fat32Table m_fatTable;

    /// <summary>
    /// The directory walker that built the cluster-to-timestamp map.
    /// </summary>
    private readonly Fat32DirectoryWalker m_directoryWalker;

    /// <summary>
    /// Maps block index to block metadata (LastModified, IsAllocated).
    /// </summary>
    private readonly BlockMetadata[] m_blockMetadata;

    /// <summary>
    /// The total number of blocks in the partition.
    /// </summary>
    private readonly long m_blockCount;

    /// <summary>
    /// Flag to indicate whether the object has been disposed.
    /// </summary>
    private bool m_disposed;

    /// <summary>
    /// Metadata for a single block.
    /// </summary>
    private readonly struct BlockMetadata
    {
        /// <summary>
        /// The last modification timestamp for this block.
        /// </summary>
        public DateTime LastModified { get; init; }

        /// <summary>
        /// Whether this block contains any allocated clusters.
        /// </summary>
        public bool IsAllocated { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Fat32Filesystem"/> class.
    /// </summary>
    /// <param name="partition">The parent partition.</param>
    /// <param name="blockSize">The block size for reading/writing (default is 1MB).</param>
    /// <exception cref="ArgumentNullException">Thrown if partition is null.</exception>
    /// <exception cref="ArgumentException">Thrown if block size is invalid.</exception>
    internal Fat32Filesystem(IPartition partition, int blockSize = 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(partition);

        Partition = partition;

        var sectorSize = partition.PartitionTable.RawDisk?.SectorSize ?? 512;
        if (blockSize <= 0 || blockSize % sectorSize != 0)
            throw new ArgumentException($"Block size must be positive and a multiple of the sector size ({sectorSize} bytes).", nameof(blockSize));

        m_blockSize = blockSize;

        // Read and parse the boot sector
        var bootSectorData = new byte[512];
        using (var stream = partition.OpenReadAsync(CancellationToken.None).Result)
        {
            stream.ReadExactly(bootSectorData);
        }
        m_bootSector = new Fat32BootSector(bootSectorData);

        // Validate block size is a multiple of cluster size
        if (m_blockSize % m_bootSector.ClusterSize != 0)
            throw new ArgumentException($"Block size ({m_blockSize}) must be a multiple of the cluster size ({m_bootSector.ClusterSize}).", nameof(blockSize));

        // Read the FAT table
        m_fatTable = new Fat32Table(partition, m_bootSector, CancellationToken.None);

        // Walk the directory tree to build cluster-to-timestamp map
        m_directoryWalker = new Fat32DirectoryWalker(partition, m_bootSector, m_fatTable, CancellationToken.None);

        // Pre-compute block metadata
        m_blockCount = (partition.Size + m_blockSize - 1) / m_blockSize;
        m_blockMetadata = BuildBlockMetadata();
    }

    /// <summary>
    /// Builds the metadata array for all blocks.
    /// </summary>
    /// <returns>An array of BlockMetadata for each block.</returns>
    private BlockMetadata[] BuildBlockMetadata()
    {
        var metadata = new BlockMetadata[m_blockCount];
        var clusterToTimestampMap = m_directoryWalker.ClusterToTimestampMap;
        var dataRegionStart = m_bootSector.DataStartOffset;
        var dataRegionEnd = dataRegionStart + (long)m_bootSector.TotalDataClusters * m_bootSector.ClusterSize;
        var fatRegionEnd = dataRegionStart;
        var now = DateTime.UtcNow;

        for (long blockIndex = 0; blockIndex < m_blockCount; blockIndex++)
        {
            long blockStart = blockIndex * m_blockSize;
            long blockEnd = Math.Min(blockStart + m_blockSize, Partition.Size);

            // Check if block is in reserved/FAT region (before data region)
            if (blockStart < fatRegionEnd)
            {
                // Reserved sectors and FAT region are always allocated
                metadata[blockIndex] = new BlockMetadata
                {
                    LastModified = now,
                    IsAllocated = true
                };
                continue;
            }

            // Check if block is beyond the data region
            if (blockStart >= dataRegionEnd)
            {
                // Beyond the data region - treat as unallocated with epoch timestamp
                metadata[blockIndex] = new BlockMetadata
                {
                    LastModified = DateTime.UnixEpoch,
                    IsAllocated = false
                };
                continue;
            }

            // Block is in data region - check which clusters fall within it
            DateTime maxTimestamp = DateTime.UnixEpoch;
            bool hasAllocatedClusters = false;

            // Calculate which clusters are in this block
            long firstClusterInBlock = 2 + (blockStart - dataRegionStart) / m_bootSector.ClusterSize;
            long lastClusterInBlock = 2 + (blockEnd - 1 - dataRegionStart) / m_bootSector.ClusterSize;

            // Clamp to valid cluster range
            firstClusterInBlock = Math.Max(2, firstClusterInBlock);
            lastClusterInBlock = Math.Min(lastClusterInBlock, m_fatTable.TotalClusters - 1);

            for (long cluster = firstClusterInBlock; cluster <= lastClusterInBlock; cluster++)
            {
                if (m_fatTable.IsClusterAllocated((uint)cluster))
                {
                    hasAllocatedClusters = true;

                    // Get the timestamp for this cluster
                    if (clusterToTimestampMap.TryGetValue((uint)cluster, out var timestamp))
                    {
                        if (timestamp > maxTimestamp)
                        {
                            maxTimestamp = timestamp;
                        }
                    }
                    else
                    {
                        // Allocated cluster without a timestamp - use current time
                        maxTimestamp = now;
                    }
                }
            }

            metadata[blockIndex] = new BlockMetadata
            {
                LastModified = maxTimestamp,
                IsAllocated = hasAllocatedClusters
            };
        }

        return metadata;
    }

    /// <summary>
    /// Tries to detect whether a given partition has a FAT32 file system.
    /// </summary>
    /// <param name="disk">The raw disk to check for the filesystem.</param>
    /// <param name="offset">The offset on the disk where the partition starts.</param>
    /// <param name="size">The size of the partition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to true if a valid FAT32 filesystem is detected, otherwise false.</returns>
    public static async Task<bool> DetectAsync(IRawDisk disk, long offset, int size, CancellationToken cancellationToken)
    {
        try
        {
            var bootSectorData = new byte[512];
            using var stream = await disk.ReadBytesAsync(offset, size, cancellationToken);
            var bytesRead = await stream.ReadAsync(bootSectorData, cancellationToken);
            if (bytesRead < 512)
                return false;

            // Try FAT32: the constructor validates the boot sector signature and
            // the "FAT32" filesystem-type string at offset 0x52.
            _ = new Fat32BootSector(bootSectorData);
            return true;
        }
        catch
        {
            // Unable to read the partition
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
    }

    /// <inheritdoc />
    public Task<object?> GetFilesystemMetadataAsync(CancellationToken cancellationToken)
    {
        var metadata = new Fat32FilesystemMetadata
        {
            BlockSize = m_blockSize,
            ClusterSize = m_bootSector.ClusterSize,
            TotalClusters = m_fatTable.TotalClusters,
            AllocatedClusters = m_fatTable.AllocatedClusters,
            FreeClusters = m_fatTable.FreeClusters
        };

        return Task.FromResult<object?>(metadata);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFile> ListFilesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (long i = 0; i < m_blockCount; i++)
        {
            long address = i * m_blockSize;
            long size = Math.Min(m_blockSize, Partition.Size - i * m_blockSize);
            var blockMeta = m_blockMetadata[i];

            if (size > 0)
            {
                yield return new Fat32File()
                {
                    Path = $"block_{i:X016}",
                    Address = address,
                    Size = size,
                    LastModified = blockMeta.LastModified,
                    IsAllocated = blockMeta.IsAllocated
                };
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (directory is not Fat32File)
            throw new ArgumentException("The specified directory does not belong to this filesystem.", nameof(directory));
        if (!directory.IsDirectory)
            throw new ArgumentException("The specified file is not a directory.", nameof(directory));

        yield break;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = fat32File.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = fat32File.Size;

        BoundsCheck(address, size);

        if (fat32File.IsAllocated)
        {
            return new Fat32Stream(Partition.PartitionTable.RawDisk!, Partition.StartOffset + address, size, readEnabled: true, writeEnabled: false);
        }
        else
        {
            return new Fat32ZeroStream((int)size);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return await OpenReadStreamAsync(new Fat32File { Address = address, Size = size, IsAllocated = true }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = fat32File.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long requestedSize = fat32File.Size;

        BoundsCheck(address, requestedSize);

        // Calculate actual available size based on target partition
        // This ensures we fail fast if the target is too small
        long actualSize = Math.Min(requestedSize, Partition.Size - address);
        if (actualSize < requestedSize)
            throw new IOException($"Target partition is too small to write block at address 0x{address:X}. " +
                $"Requested size: {requestedSize} bytes, Available: {actualSize} bytes. " +
                $"The target disk may be smaller than the source disk.");

        // For write operations, always use the full stream
        return new Fat32Stream(Partition.PartitionTable.RawDisk!, Partition.StartOffset + address, actualSize, readEnabled: false, writeEnabled: true);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return await OpenWriteStreamAsync(new Fat32File { Address = address, Size = size, IsAllocated = true }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = fat32File.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long requestedSize = fat32File.Size;

        BoundsCheck(address, requestedSize);

        // Calculate actual available size based on target partition
        // This ensures we fail fast if the target is too small
        long actualSize = Math.Min(requestedSize, Partition.Size - address);
        if (actualSize < requestedSize)
            throw new IOException($"Target partition is too small to write block at address 0x{address:X}. " +
                $"Requested size: {requestedSize} bytes, Available: {actualSize} bytes. " +
                $"The target disk may be smaller than the source disk.");

        return new Fat32Stream(Partition.PartitionTable.RawDisk!, Partition.StartOffset + address, actualSize, readEnabled: true, writeEnabled: true);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return await OpenReadWriteStreamAsync(new Fat32File { Address = address, Size = size, IsAllocated = true }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> GetFileLengthAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not Fat32File fat32File)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        return Task.FromResult(fat32File.Size);
    }

    /// <inheritdoc />
    public Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, Partition.Size - address);

        return Task.FromResult(size);
    }

    /// <summary>
    /// Parses a file path to extract the corresponding block address.
    /// </summary>
    /// <param name="path">The file path to parse.</param>
    /// <returns>The block address corresponding to the file path.</returns>
    /// <exception cref="ArgumentException">Thrown if the path format is invalid.</exception>
    private static long ParsePathToAddress(string path)
    {
        // Path format: root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/{Address}
        // We need to extract the part after the last slash
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw new ArgumentException("Invalid path format.", nameof(path));

        return Convert.ToInt64(parts[^1], 16);
    }

    /// <summary>
    /// Checks if the specified range is valid for the partition.
    /// </summary>
    /// <param name="start">The starting address of the range.</param>
    /// <param name="size">The size of the range.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the start or size is negative or exceeds the partition size.</exception>
    /// <exception cref="ArgumentException">Thrown if the start or size is not a multiple of the sector size.</exception>
    public void BoundsCheck(long start, long size)
    {
        if (start < 0 || size < 0)
            throw new ArgumentOutOfRangeException("Start and size must be non-negative.");

        if (start + size > Partition.Size)
            throw new ArgumentOutOfRangeException("The specified range exceeds the partition size.");

        var sectorSize = Partition.PartitionTable.RawDisk?.SectorSize ?? 512;

        if (start % sectorSize != 0 || size % sectorSize != 0)
            throw new ArgumentException($"Start and size must be multiples of the sector size ({sectorSize} bytes).");
    }

    /// <summary>
    /// A stream that reads data from disk and buffers it in memory for writing back on dispose.
    /// Similar to UnknownFilesystemStream but specific to FAT32 allocated blocks.
    /// </summary>
    private class Fat32Stream : Stream
    {
        /// <summary>
        /// The raw disk to read from.
        /// </summary>
        private readonly IRawDisk _disk;

        /// <summary>
        /// The starting address on the disk for this stream.
        /// </summary>
        private readonly long _address;

        /// <summary>
        /// The size of the stream in bytes.
        /// </summary>
        private readonly long _size;

        /// <summary>
        /// The buffer used to store data before writing to disk.
        /// </summary>
        private readonly byte[] _buffer;

        /// <summary>
        /// Indicates whether the buffer was rented from the ArrayPool.
        /// </summary>
        private readonly bool _bufferRented;

        /// <summary>
        /// The current position within the buffer.
        /// </summary>
        private long _position;

        /// <summary>
        /// Indicates whether the data in the buffer is valid (has been read from disk).
        /// </summary>
        private bool _validData;

        /// <summary>
        /// Indicates whether the stream has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Indicates whether the stream supports reading.
        /// </summary>
        private readonly bool _readEnabled;

        /// <summary>
        /// Indicates whether the stream supports writing.
        /// </summary>
        private readonly bool _writeEnabled;

        /// <summary>
        /// Indicates whether the buffer has been modified (dirty).
        /// </summary>
        private bool _dirty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Fat32Stream"/> class.
        /// </summary>
        /// <param name="disk">The raw disk to read from/write to.</param>
        /// <param name="address">The starting address on the disk for this stream.</param>
        /// <param name="size">The size of the stream in bytes.</param>
        /// <param name="readEnabled">Whether reading is enabled.</param>
        /// <param name="writeEnabled">Whether writing is enabled.</param>
        public Fat32Stream(IRawDisk disk, long address, long size, bool readEnabled, bool writeEnabled)
        {
            _disk = disk;
            _address = address;
            _size = size;
            _readEnabled = readEnabled;
            _writeEnabled = writeEnabled;
            // Rent buffer from ArrayPool to avoid LOH allocations for large buffers (typically 1 MB)
            _buffer = ArrayPool<byte>.Shared.Rent((int)size);
            _bufferRented = true;
            _position = 0;
            _validData = false;
            _disposed = false;
            _dirty = false;
        }

        public override bool CanRead => _readEnabled;
        public override bool CanWrite => _writeEnabled;
        public override bool CanSeek => true;
        public override long Length => _size;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _size)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override void Flush()
        {
            // No-op - data is written on dispose
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _size + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0 || newPosition > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _position = newPosition;
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Cannot change the length of this stream.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_readEnabled)
                throw new NotSupportedException("Read is not supported on this stream.");

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset and count for buffer length");

            if (!_validData)
            {
                // Load the data from disk into the buffer on the first read
                _disk.ReadBytesAsync(_address, _buffer.AsMemory(0, (int)_size), CancellationToken.None).GetAwaiter().GetResult();
                _validData = true;
            }

            int bytesToRead = (int)Math.Min(count, _size - _position);
            if (bytesToRead <= 0)
                return 0;

            Buffer.BlockCopy(_buffer, (int)_position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_writeEnabled)
                throw new NotSupportedException("Write is not supported on this stream.");

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset and count for buffer length");

            if (_position >= _size)
                throw new IOException($"Cannot write at position {_position}: stream has ended (size: {_size}). The target partition may be smaller than the source.");

            Buffer.BlockCopy(buffer, offset, _buffer, (int)_position, count);
            _position += count;
            _validData = true;
            _dirty = true;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_readEnabled)
                throw new NotSupportedException("Read is not supported on this stream.");

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset and count for buffer length");

            if (!_validData)
            {
                // Load the data from disk into the buffer on the first read
                await _disk.ReadBytesAsync(_address, _buffer.AsMemory(0, (int)_size), cancellationToken);
                _validData = true;
            }

            int bytesToRead = (int)Math.Min(count, _size - _position);
            if (bytesToRead <= 0)
                return 0;

            Buffer.BlockCopy(_buffer, (int)_position, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_writeEnabled)
                throw new NotSupportedException("Write is not supported on this stream.");

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset and count for buffer length");

            if (_position >= _size)
                throw new IOException($"Cannot write at position {_position}: stream has ended (size: {_size}). The target partition may be smaller than the source.");

            Buffer.BlockCopy(buffer, offset, _buffer, (int)_position, count);
            _position += count;
            _validData = true;
            _dirty = true;

            await Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_writeEnabled && _dirty)
                    {
                        // Write all buffered data to disk
                        _disk.WriteBytesAsync(_address, _buffer.AsMemory(0, (int)_size), CancellationToken.None).GetAwaiter().GetResult();
                    }
                    // Return the rented buffer to the pool
                    if (_bufferRented)
                    {
                        ArrayPool<byte>.Shared.Return(_buffer);
                    }
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Asynchronously releases the unmanaged resources used by the <see cref="Fat32Stream"/>.
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (_writeEnabled && _dirty)
                {
                    // Write all buffered data to disk
                    await _disk.WriteBytesAsync(_address, _buffer.AsMemory(0, (int)_size), CancellationToken.None);
                }
                // Return the rented buffer to the pool
                if (_bufferRented)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// A stream that returns zeros without reading from disk.
    /// Used for unallocated blocks to optimize backup performance.
    /// </summary>
    internal class Fat32ZeroStream : Stream
    {
        /// <summary>
        /// The size of the stream in bytes.
        /// </summary>
        private readonly long _size;

        /// <summary>
        /// The shared zero buffer for this block size.
        /// </summary>
        private readonly byte[] _zeroBuffer;

        /// <summary>
        /// The current position within the stream.
        /// </summary>
        private long _position;

        /// <summary>
        /// Indicates whether the stream has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Fat32ZeroStream"/> class.
        /// </summary>
        /// <param name="size">The size of the stream in bytes.</param>
        public Fat32ZeroStream(int size)
        {
            if (size <= 0)
                throw new ArgumentException("Size must be positive.", nameof(size));

            _size = size;
            _position = 0;
            _disposed = false;

            // Get or create the shared zero buffer for this size
            _zeroBuffer = s_zeroBuffers.GetOrAdd(size, s => new byte[s]);
        }

        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => true;
        public override long Length => _size;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _size)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override void Flush()
        {
            // No-op
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _size + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0 || newPosition > _size)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _position = newPosition;
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Cannot change the length of this stream.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count)
                throw new ArgumentException("Invalid offset and count for buffer length");

            int bytesToRead = (int)Math.Min(count, _size - _position);
            if (bytesToRead <= 0)
                return 0;

            // Copy zeros from the shared buffer
            Buffer.BlockCopy(_zeroBuffer, 0, buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Write is not supported on this stream.");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Write is not supported on this stream.");
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            // No-op - the shared buffer is never returned
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
