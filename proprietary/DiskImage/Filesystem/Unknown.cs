
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

public sealed record UnkownFilesystemMetadata
{
    public int BlockSize { get; init; }
}

public class UnknownFilesystemFile : IFile
{
    public string? Path { get; init; }
    public long? Address { get; init; }
    public long Size { get; init; }
    public bool IsDirectory => false;
}

public class UnknownFilesystem : IFilesystem
{
    public FileSystemType Type => FileSystemType.Unknown;

    public IPartition Partition { get => m_partition; }

    private int m_blockSize;
    private bool m_disposed = false;
    private readonly IPartition m_partition;

    public UnknownFilesystem(IPartition partition, int blockSize = 1024 * 1024)
    {
        m_partition = partition;
        if (blockSize < 0 || blockSize % partition.PartitionTable.RawDisk?.SectorSize != 0)
            throw new ArgumentException("Block size must be non-negative and a multiple of the partition sector size.", nameof(blockSize));
        m_blockSize = blockSize;
    }

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
    }

    public Task<object?> GetFilesystemMetadataAsync(CancellationToken cancellationToken)
    {
        var cfg = new UnkownFilesystemMetadata
        {
            BlockSize = m_blockSize,
        };

        return Task.FromResult<object?>(cfg);
    }

    public async IAsyncEnumerable<IFile> ListFilesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long totalSize = m_partition.Size;
        long blockSize = m_blockSize;
        long blockCount = (totalSize + blockSize - 1) / blockSize;

        for (long i = 0; i < blockCount; i++)
        {
            long address = m_partition.StartOffset + i * blockSize;
            long size = Math.Min(blockSize, totalSize - i * blockSize);

            yield return new UnknownFilesystemFile()
            {
                Address = address,
                Size = size
            };
        }
    }

    public async IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (directory is not UnknownFilesystemFile)
            throw new ArgumentException("The specified directory does not belong to this filesystem.", nameof(directory));
        if (!directory.IsDirectory)
            throw new ArgumentException("The specified file is not a directory.", nameof(directory));

        yield break;
    }

    private static long ParsePathToAddress(string path)
    {
        // Path format: root/part_{PartitionTableType}_{PartitionNumber}/fs_{FileSystemType}/{Address}
        // We need to extract the part after the last slash
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            throw new ArgumentException("Invalid path format.", nameof(path));

        return Convert.ToInt64(parts[^1], 16);
    }

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

    public async Task<Stream> OpenReadStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = unknownFile.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = unknownFile.Size;

        BoundsCheck(address - m_partition.StartOffset, size);

        return new UnknownFilesystemStream(m_partition.PartitionTable.RawDisk!, address, size, readEnabled: true, writeEnabled: false);
    }

    public async Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, m_partition.Size - address);

        return await OpenReadStreamAsync(new UnknownFilesystemFile { Address = address, Size = size }, cancellationToken);
    }

    public async Task<Stream> OpenWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = unknownFile.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = unknownFile.Size;

        BoundsCheck(address - m_partition.StartOffset, size);

        return new UnknownFilesystemStream(m_partition.PartitionTable.RawDisk!, address, size, readEnabled: false, writeEnabled: true);
    }

    public async Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, m_partition.Size - address);

        return await OpenWriteStreamAsync(new UnknownFilesystemFile { Address = address, Size = size }, cancellationToken);
    }

    public async Task<Stream> OpenReadWriteStreamAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        long address = unknownFile.Address ?? throw new ArgumentException("File address is required.", nameof(file));
        long size = unknownFile.Size;

        BoundsCheck(address - m_partition.StartOffset, size);

        return new UnknownFilesystemStream(m_partition.PartitionTable.RawDisk!, address, size, readEnabled: true, writeEnabled: true);
    }

    public async Task<Stream> OpenReadWriteStreamAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long size = Math.Min((long)m_blockSize, m_partition.Size - address);

        return await OpenReadWriteStreamAsync(new UnknownFilesystemFile { Address = address, Size = size }, cancellationToken);
    }

    public Task<long> GetFileLengthAsync(IFile file, CancellationToken cancellationToken)
    {
        if (file is not UnknownFilesystemFile unknownFile)
            throw new ArgumentException("The specified file does not belong to this filesystem.", nameof(file));

        if (file.IsDirectory)
            throw new ArgumentException("The specified file is a directory.", nameof(file));

        return Task.FromResult(unknownFile.Size);
    }

    public Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken)
    {
        long address = ParsePathToAddress(path);
        long totalSize = m_partition.Size;
        long offsetInPartition = address - m_partition.StartOffset;
        long size = Math.Min((long)m_blockSize, totalSize - offsetInPartition);

        return Task.FromResult(size);
    }

    /// <summary>
    /// A stream that writes data directly to a specific address on the raw disk.
    /// </summary>
    private class UnknownFilesystemStream(IRawDisk disk, long address, long size, bool readEnabled, bool writeEnabled) : Stream
    {
        private readonly IRawDisk _disk = disk;
        private readonly long _address = address;
        private readonly long _size = size;
        private readonly MemoryStream _buffer = new MemoryStream();
        private bool _disposed = false;
        private bool _readEnabled = readEnabled;
        private bool _writeEnabled = writeEnabled;

        public override bool CanRead => _readEnabled;
        public override bool CanWrite => _writeEnabled;
        public override bool CanSeek => true;
        public override long Length => _buffer.Length;
        public override long Position
        {
            get => _buffer.Position;
            set => _buffer.Position = value;
        }

        public override void Flush() => _buffer.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _buffer.Seek(offset, origin);
        public override void SetLength(long value) => _buffer.SetLength(value);

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_readEnabled)
                throw new NotSupportedException("Read is not supported on this stream.");

            return _buffer.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_writeEnabled)
                throw new NotSupportedException("Write is not supported on this stream.");

            _buffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _writeEnabled)
                {
                    // Write all buffered data to disk
                    _buffer.Position = 0;
                    var data = _buffer.ToArray();
                    if (data.Length > 0)
                    {
                        _disk.WriteBytesAsync(_address, data, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    _buffer.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}