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
    // Constants for GPT parsing
    private const int HeaderSize = 92;
    private const long GptSignature = 0x5452415020494645; // "EFI PART" in little-endian
    private const int MbrSize = 512;
    private const ushort MbrBootSignature = 0xAA55;
    private const byte ProtectiveMbrType = 0xEE;

    // Internal state
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
    private byte[]? m_protectiveMbrBytes;
    private long m_bytesPerSector;

    // Partition storage
    private List<IPartition>? m_partitions;

    public GPT() { }

    // IPartitionTable implementation
    public IRawDisk? RawDisk { get; private set; }

    public PartitionTableType TableType => PartitionTableType.GPT;

    public async Task<bool> ParseAsync(IRawDisk disk, CancellationToken token)
    {
        var parsedHeader = await ParseHeaderAsync(disk, token)
            .ConfigureAwait(false);

        if (!parsedHeader)
            return false;

        return await ParsePartitionEntriesAsync(disk, token)
            .ConfigureAwait(false);
    }

    public async Task<bool> ParseAsync(byte[] bytes, int sectorSize, CancellationToken token)
    {
        m_bytesPerSector = sectorSize;

        // Parse the protective MBR first (LBA 0; first sectorSize bytes)
        if (!await ParseProtectiveMbrAsync(bytes, sectorSize, token).ConfigureAwait(false))
            return false;

        // Now parse the GPT header (LBA 1)
        m_headerBytes = bytes[sectorSize..(sectorSize + HeaderSize)];
        var parsedHeader = await ParseHeaderAsync(m_headerBytes, token).ConfigureAwait(false);

        if (!parsedHeader)
            return false;

        m_numPartitionEntries = 4;

        // Calculate the byte offset for the partition entries
        int partitionEntriesOffset = (int)(m_partitionEntryLba * m_bytesPerSector);
        int sizeEntries = (int)(m_partitionEntrySize * m_numPartitionEntries);

        var partitionBytes = bytes[partitionEntriesOffset..(partitionEntriesOffset + sizeEntries)];

        return await ParsePartitionEntriesAsync(partitionBytes, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the protective MBR to verify this is a GPT disk.
    /// </summary>
    private async Task<bool> ParseProtectiveMbrAsync(byte[] bytes, int sectorSize, CancellationToken token)
    {
        if (bytes.Length < sectorSize)
            throw new ArgumentException($"Byte array must be at least {sectorSize} bytes long.", nameof(bytes));

        // Extract the MBR (first sector)
        m_protectiveMbrBytes = bytes[0..sectorSize];

        // Verify MBR boot signature (offset 510)
        ushort bootSignature = BitConverter.ToUInt16(m_protectiveMbrBytes, 510);
        if (bootSignature != MbrBootSignature)
            return false;

        // Check if first partition entry has protective MBR type (0xEE)
        byte partitionType = m_protectiveMbrBytes[450];
        if (partitionType != ProtectiveMbrType)
            return false;

        return true;
    }

    public async Task<bool> ParseHeaderAsync(IRawDisk disk, CancellationToken token)
    {
        m_headerBytes = new byte[HeaderSize];
        m_bytesPerSector = disk.SectorSize;

        // Read the GPT header (LBA 1)
        using var bytestream = await disk.ReadBytesAsync(m_bytesPerSector, (int)m_bytesPerSector, token)
            .ConfigureAwait(false);
        await bytestream.ReadAtLeastAsync(m_headerBytes, HeaderSize, cancellationToken: token)
            .ConfigureAwait(false);
        var result = await ParseHeaderAsync(m_headerBytes, token)
            .ConfigureAwait(false);

        if (result)
        {
            RawDisk = disk;
            m_rawDisk = disk;
        }

        return result;
    }

    public async Task<bool> ParseHeaderAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length < HeaderSize)
            throw new ArgumentException($"Byte array must be at least {HeaderSize} bytes long.", nameof(bytes));

        // Read signature (8 bytes, little-endian)
        m_signature = BitConverter.ToInt64(bytes, 0);

        // Verify signature
        if (m_signature != GptSignature)
            return false;

        // Read revision (4 bytes)
        m_revision = BitConverter.ToUInt32(bytes, 8);

        // Read header size (4 bytes)
        m_headerSize = BitConverter.ToUInt32(bytes, 12);

        // Read header CRC32 (4 bytes)
        m_headerCrc32 = BitConverter.ToUInt32(bytes, 16);

        // Reserved - must be zero (4 bytes at offset 20)
        var reserved = BitConverter.ToUInt32(bytes, 20);
        if (reserved != 0)
            return false;

        // Current LBA (8 bytes at offset 24)
        m_currentLba = BitConverter.ToInt64(bytes, 24);

        // Backup LBA (8 bytes at offset 32)
        m_backupLba = BitConverter.ToInt64(bytes, 32);

        // First usable LBA (8 bytes at offset 40)
        m_firstUsableLba = BitConverter.ToInt64(bytes, 40);

        // Last usable LBA (8 bytes at offset 48)
        m_lastUsableLba = BitConverter.ToInt64(bytes, 48);

        // Disk GUID (16 bytes at offset 56)
        var diskGuidBytes = new byte[16];
        Array.Copy(bytes, 56, diskGuidBytes, 0, 16);
        m_diskGuid = new Guid(diskGuidBytes);

        // Partition entry LBA (8 bytes at offset 72)
        m_partitionEntryLba = BitConverter.ToInt64(bytes, 72);

        // Number of partition entries (4 bytes at offset 80)
        m_numPartitionEntries = BitConverter.ToUInt32(bytes, 80);

        // Size of partition entry (4 bytes at offset 84)
        m_partitionEntrySize = BitConverter.ToUInt32(bytes, 84);

        // Partition entry CRC32 (4 bytes at offset 88)
        m_partitionEntryCrc32 = BitConverter.ToUInt32(bytes, 88);

        m_parsed = true;
        return true;
    }

    private async Task<bool> ParsePartitionEntriesAsync(IRawDisk disk, CancellationToken token)
    {
        if (m_partitions != null)
            return false;

        if (!m_parsed)
            return false;

        // Calculate the byte offset for the partition entries
        long partitionEntriesOffset = m_partitionEntryLba * m_bytesPerSector;

        // Read all partition entries in one go
        long totalSize = m_partitionEntrySize * m_numPartitionEntries;
        var buffer = new byte[totalSize];

        using var stream = await disk.ReadBytesAsync(partitionEntriesOffset, (int)totalSize, token)
            .ConfigureAwait(false);
        await stream.ReadAtLeastAsync(buffer, (int)totalSize, cancellationToken: token)
            .ConfigureAwait(false);

        return await ParsePartitionEntriesAsync(buffer, token)
            .ConfigureAwait(false);
    }

    private async Task<bool> ParsePartitionEntriesAsync(byte[] buffer, CancellationToken token)
    {
        if (m_partitions != null)
            return false;

        m_partitions = [];

        // Parse each partition entry
        for (int i = 0; i < 4; i++)
        {
            token.ThrowIfCancellationRequested();

            int offset = (int)(i * m_partitionEntrySize);

            // Check if this entry is empty (all zeros in the first 16 bytes = partition type GUID)
            bool isEmpty = true;
            for (int j = 0; j < 16; j++)
            {
                if (buffer[offset + j] != 0)
                {
                    isEmpty = false;
                    break;
                }
            }

            if (isEmpty)
                continue;

            // Parse partition entry
            var partition = ParsePartitionEntry(buffer, offset, i + 1);
            if (partition != null)
                m_partitions.Add(partition!);
        }

        return true;
    }

    private GPTPartition? ParsePartitionEntry(byte[] buffer, int offset, int partitionNumber)
    {
        // Partition type GUID (16 bytes at offset 0)
        var typeGuidBytes = new byte[16];
        Array.Copy(buffer, offset, typeGuidBytes, 0, 16);
        var typeGuid = new Guid(typeGuidBytes);

        // Unique partition GUID (16 bytes at offset 16)
        var uniqueGuidBytes = new byte[16];
        Array.Copy(buffer, offset + 16, uniqueGuidBytes, 0, 16);
        var uniqueGuid = new Guid(uniqueGuidBytes);

        // Starting LBA (8 bytes at offset 32)
        long startingLba = BitConverter.ToInt64(buffer, offset + 32);

        // Ending LBA (8 bytes at offset 40)
        long endingLba = BitConverter.ToInt64(buffer, offset + 40);

        // Attributes (8 bytes at offset 48)
        long attributes = BitConverter.ToInt64(buffer, offset + 48);

        // Partition name (36 UTF-16LE characters = 72 bytes at offset 56)
        var nameBytes = new byte[72];
        Array.Copy(buffer, offset + 56, nameBytes, 0, 72);
        string name = System.Text.Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');

        // Calculate byte offsets and size
        long startOffset = startingLba * m_bytesPerSector;
        long size = (endingLba - startingLba + 1) * m_bytesPerSector;

        // Determine partition type based on type GUID
        PartitionType partitionType = DeterminePartitionType(typeGuid);

        // Determine filesystem type based on partition name and known patterns
        FileSystemType fsType = DetermineFilesystemType(name, typeGuid);

        return new GPTPartition
        {
            PartitionNumber = partitionNumber,
            Type = partitionType,
            StartOffset = startOffset,
            Size = size,
            Name = string.IsNullOrEmpty(name) ? null : name,
            FilesystemType = fsType,
            VolumeGuid = uniqueGuid,
            RawDisk = m_rawDisk,
            StartingLba = startingLba,
            EndingLba = endingLba,
            Attributes = attributes
        };
    }

    private static PartitionType DeterminePartitionType(Guid typeGuid)
    {
        // TODO: Once PartitionType enum is extended with specific types, implement GUID mapping
        // Common GPT partition type GUIDs include:
        // - EFI System Partition: C12A7328-F81F-11D2-BA4B-00A0C93EC93B
        // - Microsoft Reserved: E3C9E316-0B5C-4DB8-817D-F92DF00215AE
        // - Microsoft Basic Data: EBD0A0A2-B9E5-4433-87C0-68B6B72699C7
        // - Linux filesystem: 0FC63DAF-8483-4772-8E79-3D69D8477DE4
        // For now, always return Unknown
        return PartitionType.Unknown;
    }

    private static FileSystemType DetermineFilesystemType(string name, Guid typeGuid)
    {
        // TODO: Once FileSystemType enum is extended, implement filesystem detection
        // For now, always return Unknown - actual detection requires reading the partition
        return FileSystemType.Unknown;
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

        // Ensure partitions are parsed
        if (m_partitions == null && m_rawDisk != null)
            await ParsePartitionEntriesAsync(m_rawDisk, cancellationToken).ConfigureAwait(false);

        if (m_partitions != null)
        {
            foreach (var partition in m_partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return partition;
            }
        }
    }

    public async Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        // Ensure partitions are parsed
        if (m_partitions == null && m_rawDisk != null)
            await ParsePartitionEntriesAsync(m_rawDisk, cancellationToken).ConfigureAwait(false);

        if (m_partitions == null)
            return null;

        if (partitionNumber >= 1 && partitionNumber <= m_partitions.Count)
            return m_partitions[partitionNumber - 1];

        return null;
    }

    public async Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        if (m_protectiveMbrBytes != null)
        {
            // Return a MemoryStream with the stored MBR bytes
            return new MemoryStream(m_protectiveMbrBytes, writable: false);
        }

        if (m_rawDisk != null)
        {
            // Read MBR from disk
            return await m_rawDisk.ReadBytesAsync(0, MbrSize, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("No protective MBR available.");
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
                m_protectiveMbrBytes = null;
                m_rawDisk = null;
                if (m_partitions != null)
                {
                    foreach (var partition in m_partitions)
                        partition.Dispose();
                    m_partitions = null;
                }
            }
            m_disposed = true;
        }
    }

    /// <summary>
    /// Represents a GPT partition entry.
    /// </summary>
    private class GPTPartition : IPartition
    {
        public int PartitionNumber { get; init; }
        public PartitionType Type { get; init; }
        public long StartOffset { get; init; }
        public long Size { get; init; }
        public string? Name { get; init; }
        public FileSystemType FilesystemType { get; init; }
        public Guid? VolumeGuid { get; init; }
        public IRawDisk? RawDisk { get; init; }
        public long StartingLba { get; init; }
        public long EndingLba { get; init; }
        public long Attributes { get; init; }

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            if (RawDisk == null)
                throw new InvalidOperationException("RawDisk not available.");
            return RawDisk.ReadBytesAsync(StartOffset, (int)Math.Min(Size, int.MaxValue), cancellationToken);
        }

        public void Dispose()
        {
            // No unmanaged resources to dispose
        }
    }
}
