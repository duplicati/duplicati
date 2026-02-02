using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Raw;

namespace Duplicati.Proprietary.DiskImage.Partition;

public class GPT : IPartitionTable
{
    private const int TableSize = 4096;
    private const int HeaderSize = 92;
    private const long GptSignature = 0x5452415020494645; // "EFI PART" in little-endian

    private bool m_parsed = false;
    private bool m_disposed = false;

    // GPT Header fields
    private long m_signature;
    private uint m_revision;
    private uint m_headerSize;
    private uint m_headerCrc32;
    private long m_currentLba;
    private long m_backupLba;
    private long m_firstUsableLba;
    private long m_lastUsableLba;
    private Guid m_diskGuid;
    private long m_partitionEntryLba;
    private uint m_numPartitionEntries;
    private uint m_partitionEntrySize;
    private uint m_partitionEntryCrc32;

    // Additional tracking
    private IRawDisk? m_rawDisk;
    private byte[]? m_headerBytes;

    public GPT() { }

    // IPartitionTable implementation
    public IRawDisk? RawDisk { get; private set; }

    public PartitionTableType TableType => PartitionTableType.Gpt;

    public async Task<bool> ParseFromBytesAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length < TableSize)
            throw new ArgumentException($"Byte array must be at least {TableSize} bytes long.", nameof(bytes));

        // Parse GPT header (starts at LBA 1, offset 0 in our buffer)
        m_headerBytes = new byte[HeaderSize];
        Array.Copy(bytes, 512, m_headerBytes, 0, HeaderSize);

        // Read signature (8 bytes, little-endian)
        m_signature = BitConverter.ToInt64(m_headerBytes, 0);

        // Verify signature
        if (m_signature != GptSignature)
            return false;

        // Read revision (4 bytes)
        m_revision = BitConverter.ToUInt32(m_headerBytes, 8);

        // Read header size (4 bytes)
        m_headerSize = BitConverter.ToUInt32(m_headerBytes, 12);

        // Read header CRC32 (4 bytes)
        m_headerCrc32 = BitConverter.ToUInt32(m_headerBytes, 16);

        // Reserved - must be zero (4 bytes at offset 20)
        // Skip reserved field

        // Current LBA (8 bytes at offset 24)
        m_currentLba = BitConverter.ToInt64(m_headerBytes, 24);

        // Backup LBA (8 bytes at offset 32)
        m_backupLba = BitConverter.ToInt64(m_headerBytes, 32);

        // First usable LBA (8 bytes at offset 40)
        m_firstUsableLba = BitConverter.ToInt64(m_headerBytes, 40);

        // Last usable LBA (8 bytes at offset 48)
        m_lastUsableLba = BitConverter.ToInt64(m_headerBytes, 48);

        // Disk GUID (16 bytes at offset 56)
        var diskGuidBytes = new byte[16];
        Array.Copy(m_headerBytes, 56, diskGuidBytes, 0, 16);
        m_diskGuid = new Guid(diskGuidBytes);

        // Partition entry LBA (8 bytes at offset 72)
        m_partitionEntryLba = BitConverter.ToInt64(m_headerBytes, 72);

        // Number of partition entries (4 bytes at offset 80)
        m_numPartitionEntries = BitConverter.ToUInt32(m_headerBytes, 80);

        // Size of partition entry (4 bytes at offset 84)
        m_partitionEntrySize = BitConverter.ToUInt32(m_headerBytes, 84);

        // Partition entry CRC32 (4 bytes at offset 88)
        m_partitionEntryCrc32 = BitConverter.ToUInt32(m_headerBytes, 88);

        m_parsed = true;
        return true;
    }

    public async Task<bool> ParseFromDiskAsync(IRawDisk disk, CancellationToken token)
    {
        var bytes = new byte[TableSize];
        using var bytestream = await disk.ReadBytesAsync(0, TableSize, token).ConfigureAwait(false);
        await bytestream.ReadAtLeastAsync(bytes, TableSize, cancellationToken: token).ConfigureAwait(false);
        var result = await ParseFromBytesAsync(bytes, token).ConfigureAwait(false);
        if (result)
        {
            RawDisk = disk;
            m_rawDisk = disk;
        }
        return result;
    }

    // GPT-specific properties
    public long Signature => m_parsed ? m_signature : throw new InvalidOperationException("GPT header not parsed.");
    public uint Revision => m_parsed ? m_revision : throw new InvalidOperationException("GPT header not parsed.");
    public uint HeaderSizeField => m_parsed ? m_headerSize : throw new InvalidOperationException("GPT header not parsed.");
    public uint HeaderCrc32 => m_parsed ? m_headerCrc32 : throw new InvalidOperationException("GPT header not parsed.");
    public long CurrentLba => m_parsed ? m_currentLba : throw new InvalidOperationException("GPT header not parsed.");
    public long BackupLba => m_parsed ? m_backupLba : throw new InvalidOperationException("GPT header not parsed.");
    public long FirstUsableLba => m_parsed ? m_firstUsableLba : throw new InvalidOperationException("GPT header not parsed.");
    public long LastUsableLba => m_parsed ? m_lastUsableLba : throw new InvalidOperationException("GPT header not parsed.");
    public Guid DiskGuid => m_parsed ? m_diskGuid : throw new InvalidOperationException("GPT header not parsed.");
    public long PartitionEntryLba => m_parsed ? m_partitionEntryLba : throw new InvalidOperationException("GPT header not parsed.");
    public uint NumPartitionEntries => m_parsed ? m_numPartitionEntries : throw new InvalidOperationException("GPT header not parsed.");
    public uint PartitionEntrySizeField => m_parsed ? m_partitionEntrySize : throw new InvalidOperationException("GPT header not parsed.");
    public uint PartitionEntryCrc32 => m_parsed ? m_partitionEntryCrc32 : throw new InvalidOperationException("GPT header not parsed.");

    // IPartitionTable methods
    public async IAsyncEnumerable<IPartition> EnumeratePartitions([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        // TODO: Implement partition enumeration from partition entries
        yield break;
    }

    public async Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        // TODO: Implement partition retrieval
        return null;
    }

    public async Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        // TODO: Implement protective MBR retrieval
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!m_disposed)
        {
            if (disposing)
            {
                m_headerBytes = null;
                m_rawDisk = null;
            }
            m_disposed = true;
        }
    }
}
