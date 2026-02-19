
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

/// <summary>
/// Metadata for unknown filesystems, storing basic block size information.
/// </summary>
public sealed record UnkownFilesystemMetadata
{
    /// <summary>
    /// Gets the block size used for reading/writing data.
    /// </summary>
    public int BlockSize { get; init; }
}

/// <summary>
/// Represents a file in an unknown filesystem.
/// Used for raw block-based access when filesystem type cannot be determined.
/// </summary>
public class UnknownFilesystemFile : IFile
{
    /// <inheritdoc />
    public string? Path { get; init; }

    /// <inheritdoc />
    public long? Address { get; init; }

    /// <inheritdoc />
    public long Size { get; init; }

    /// <inheritdoc />
    public bool IsDirectory => false;
}

/// <summary>
/// Represents an unknown or unsupported filesystem.
/// Provides raw block-level access to partition data when the filesystem type cannot be determined.
/// </summary>
internal class UnknownFilesystem : IFilesystem
{
    /// <inheritdoc />
    public FileSystemType Type => FileSystemType.Unknown;

    /// <inheritdoc />
    public IPartition Partition { get => m_partition; }

    /// <summary>
    /// Gets or sets the block size used for reading/writing data. Must be a multiple of the partition sector size.
    /// </summary>
    private int m_blockSize;

    /// <summary>
    /// Flag to indicate whether the object has been disposed. Used to prevent multiple disposals and access after disposal.
    /// </summary>
    private bool m_disposed = false;

    /// <summary>
    /// The parent partition containing this filesystem.
    /// </summary>
    private readonly IPartition m_partition;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownFilesystem"/> class.
    /// </summary>
    /// <param name="partition">The parent partition.</param>
    /// <param name="blockSize">The block size for reading/writing (default is 1MB).</param>
    /// <exception cref="ArgumentException">Thrown if block size is invalid.</exception>
    public UnknownFilesystem(IPartition partition, int blockSize = 1024 * 1024)
    {
        m_partition = partition;
        if (blockSize < 0 || blockSize % partition.PartitionTable.RawDisk?.SectorSize != 0)
            throw new ArgumentException("Block size must be non-negative and a multiple of the partition sector size.", nameof(blockSize));
        m_blockSize = blockSize;
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
        var cfg = new UnkownFilesystemMetadata
        {
            BlockSize = m_blockSize,
        };

        return Task.FromResult<object?>(cfg);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFile> ListFilesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long totalSize = m_partition.Size;
        long blockSize = m_blockSize;
        long blockCount = (totalSize + blockSize - 1) / blockSize;

        for (long i = 0; i < blockCount; i++)
        {
            long address = i * blockSize;
            long size = Math.Min(blockSize, totalSize - i * blockSize);

            if (size > 0)
                yield return new UnknownFilesystemFile()
                {
                    Address = address,
                    Size = size
                };
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (directory is not UnknownFilesystemFile)
            throw new ArgumentException("The specified directory does not belong to this filesystem.", nameof(directory));
        if (!directory.IsDirectory)
            throw new ArgumentException("The specified file is not a directory.", nameof(directory));

        yield break;
    }

    /// <summary>
    /// Parses a file path to extract the corresponding block address for raw access.
    /// </summary>
    /// <param name="path">The file path to parse.</param>
    /// <returns>The block address corresponding to the file path.</returns>
    /// <exception cref="ArgumentException">Thrown if the path format is invalid.</exception>
    private static long ParsePathToAddress(string path)
    {
        // Path format: root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/{Address}
        // We need to extract the part after the last slash
        var parts = path.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
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

        if (start + size > m_partition.Size)
            throw new ArgumentOutOfRangeException("The specified range exceeds the partition size.");

        var sectorSize = m_partition.PartitionTable.RawDisk?.SectorSize ?? 512;

        if (start % sectorSize != 0 || size % sectorSize != 0)
            throw new ArgumentException($"Start and size must be multiples of the sector size ({sectorSize} bytes).");
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = unknownFile.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = unknownFile.Size;

        BoundsCheck(address, size);

        return new UnknownFilesystemStream(m_partition.PartitionTable.RawDisk!, m_partition.StartOffset + address, size, readEnabled: true, writeEnabled: false);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, m_partition.Size - address);

        return await OpenReadStreamAsync(new UnknownFilesystemFile { Address = address, Size = size }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = unknownFile.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = unknownFile.Size;

        BoundsCheck(address, size);

        return new UnknownFilesystemStream(m_partition.PartitionTable.RawDisk!, m_partition.StartOffset + address, size, readEnabled: false, writeEnabled: true);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, m_partition.Size - address);

        return await OpenWriteStreamAsync(new UnknownFilesystemFile { Address = address, Size = size }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = unknownFile.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = unknownFile.Size;

        BoundsCheck(address, size);

        return new UnknownFilesystemStream(m_partition.PartitionTable.RawDisk!, m_partition.StartOffset + address, size, readEnabled: true, writeEnabled: true);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, m_partition.Size - address);

        return await OpenReadWriteStreamAsync(new UnknownFilesystemFile { Address = address, Size = size }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> GetFileLengthAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        return Task.FromResult(unknownFile.Size);
    }

    /// <inheritdoc />
    public Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long totalSize = m_partition.Size;
        long size = Math.Min((long)m_blockSize, totalSize - address);

        return Task.FromResult(size);
    }

    /// <summary>
    /// A stream that writes data directly to a specific address on the raw disk.
    /// </summary>
    private class UnknownFilesystemStream : Stream
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
        /// Rented from ArrayPool to avoid LOH allocations for large buffers.
        /// </summary>
        private readonly byte[] _buffer;

        /// <summary>
        /// Indicates whether the buffer was rented from the ArrayPool and needs to be returned.
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
        /// Initializes a new instance of the <see cref="UnknownFilesystemStream"/> class.
        /// </summary>
        /// <param name="disk">The raw disk to read from/write to.</param>
        /// <param name="address">The starting address on the disk for this stream.</param>
        /// <param name="size">The size of the stream in bytes.</param>
        /// <param name="readEnabled">Whether reading is enabled.</param>
        /// <param name="writeEnabled">Whether writing is enabled.</param>
        public UnknownFilesystemStream(IRawDisk disk, long address, long size, bool readEnabled, bool writeEnabled)
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
                // Use _size to limit the read to the actual data size, not the potentially larger rented buffer
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

            if (_position + count > _size)
                throw new ArgumentException("Write would exceed stream size");

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
                // Use _size to limit the read to the actual data size, not the potentially larger rented buffer
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

            if (_position + count > _size)
                throw new ArgumentException("Write would exceed stream size");

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
        /// Asynchronously releases the unmanaged resources used by the <see cref="UnknownFilesystemStream"/>.
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
}