using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Represents a GPT (GUID Partition Table) partition table.
/// GPT is the modern partition table format used on UEFI-based systems.
/// </summary>
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

    /// <inheritdoc />
    public IRawDisk? RawDisk { get => m_rawDisk; }

    /// <inheritdoc />
    public PartitionTableType TableType => PartitionTableType.GPT;

    /// <summary>
    /// Initializes a new instance of the <see cref="GPT"/> class.
    /// </summary>
    /// <param name="disk">The raw disk to parse, or null for byte array parsing.</param>
    public GPT(IRawDisk? disk)
    {
        m_rawDisk = disk;
    }

    /// <summary>
    /// Parses the GPT partition table from the raw disk.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
    public async Task<bool> ParseAsync(CancellationToken token)
    {
        var parsedHeader = await ParseHeaderAsync(token)
            .ConfigureAwait(false);

        if (!parsedHeader)
            return false;

        return await ParsePartitionEntriesAsync(token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the GPT partition table from a byte array.
    /// </summary>
    /// <param name="bytes">The raw disk bytes.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
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

    /// <summary>
    /// Parses the GPT header from the raw disk.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
    public async Task<bool> ParseHeaderAsync(CancellationToken token)
    {
        if (m_rawDisk == null)
            throw new InvalidOperationException("No raw disk available for reading GPT header.");

        m_headerBytes = new byte[HeaderSize];
        m_bytesPerSector = m_rawDisk.SectorSize;

        // Read the GPT header (LBA 1)
        using var bytestream = await m_rawDisk.ReadBytesAsync(m_bytesPerSector, (int)m_bytesPerSector, token)
            .ConfigureAwait(false);
        await bytestream.ReadAtLeastAsync(m_headerBytes, HeaderSize, cancellationToken: token)
            .ConfigureAwait(false);
        var result = await ParseHeaderAsync(m_headerBytes, token)
            .ConfigureAwait(false);

        if (result)
        {
            // Verify backup header
            if (!await VerifyBackupHeaderAsync(token).ConfigureAwait(false))
                return false;
        }

        return result;
    }

    /// <summary>
    /// Parses the GPT header from a byte array.
    /// </summary>
    /// <param name="bytes">The header bytes.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
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

    /// <summary>
    /// Parses the partition entries from the disk.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the raw disk is not available or if the header has not been parsed.</exception>
    private async Task<bool> ParsePartitionEntriesAsync(CancellationToken token)
    {
        if (m_rawDisk == null)
            throw new InvalidOperationException("No raw disk available for reading GPT partition entries.");

        if (m_partitions != null)
            return false;

        if (!m_parsed)
            return false;

        // Calculate the byte offset for the partition entries
        long partitionEntriesOffset = m_partitionEntryLba * m_bytesPerSector;

        // Read all partition entries in one go
        long totalSize = m_partitionEntrySize * m_numPartitionEntries;
        var buffer = new byte[totalSize];

        using var stream = await m_rawDisk.ReadBytesAsync(partitionEntriesOffset, (int)totalSize, token)
            .ConfigureAwait(false);
        await stream.ReadAtLeastAsync(buffer, (int)totalSize, cancellationToken: token)
            .ConfigureAwait(false);

        return await ParsePartitionEntriesAsync(buffer, token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the partition entries from a byte array.
    /// </summary>
    /// <param name="buffer">The byte array containing the partition entries.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
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

    /// <summary>
    /// Parses a single partition entry from a byte array.
    /// </summary>
    /// <param name="buffer">The byte array containing the partition entry.</param>
    /// <param name="offset">The offset in the byte array where the partition entry starts.</param>
    /// <param name="partitionNumber">The partition number.</param>
    /// <returns>The parsed partition entry, or null if the entry is empty.</returns>
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
            PartitionTable = this,
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

    /// <summary>
    /// Determines the partition type based on the partition type GUID.
    /// </summary>
    /// <param name="typeGuid">The partition type GUID.</param>
    /// <returns>The corresponding <see cref="PartitionType"/>.</returns>
    private static PartitionType DeterminePartitionType(Guid typeGuid)
    {
        // Common GPT partition type GUIDs
        return typeGuid.ToString().ToUpper() switch
        {
            "C12A7328-F81F-11D2-BA4B-00A0C93EC93B" => PartitionType.EFI,
            "E3C9E316-0B5C-4DB8-817D-F92DF00215AE" => PartitionType.MicrosoftReserved,
            "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7" => PartitionType.Primary, // Microsoft Basic Data
            "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC" => PartitionType.Recovery, // Windows Recovery
            "0FC63DAF-8483-4772-8E79-3D69D8477DE4" => PartitionType.LinuxFilesystem,
            "0657FD6D-A4AB-43C4-84E5-0933C84B4F4F" => PartitionType.LinuxSwap,
            "E6D6D379-F507-44C2-A23C-238F2A3DF928" => PartitionType.LinuxLVM,
            "A19D880F-05FC-4D3B-A006-743F0F84911E" => PartitionType.LinuxRAID,
            "48465300-0000-11AA-AA11-00306543ECAC" => PartitionType.AppleHFS,
            "7C3457EF-0000-11AA-AA11-00306543ECAC" => PartitionType.AppleAPFS,
            "426F6F74-0000-11AA-AA11-00306543ECAC" => PartitionType.AppleBoot,
            "21686148-6449-6E6F-744E-656564454649" => PartitionType.BIOSBoot,
            _ => PartitionType.Unknown
        };
    }

    /// <summary>
    /// Determines the filesystem type based on the partition name and type GUID.
    /// Uses heuristics based on common naming patterns and known GUIDs.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="typeGuid">The partition type GUID.</param>
    /// <returns>The corresponding <see cref="FileSystemType"/>.</returns>
    private static FileSystemType DetermineFilesystemType(string name, Guid typeGuid)
    {
        if (!string.IsNullOrEmpty(name))
        {
            var upperName = name.ToUpperInvariant();
            if (upperName.Contains("NTFS")) return FileSystemType.NTFS;
            if (upperName.Contains("FAT32")) return FileSystemType.FAT32;
            if (upperName.Contains("FAT16")) return FileSystemType.FAT16;
            if (upperName.Contains("FAT12")) return FileSystemType.FAT12;
            if (upperName.Contains("EXFAT")) return FileSystemType.ExFAT;
            if (upperName.Contains("HFS")) return FileSystemType.HFSPlus;
            if (upperName.Contains("APFS")) return FileSystemType.APFS;
            if (upperName.Contains("EXT4")) return FileSystemType.Ext4;
            if (upperName.Contains("EXT3")) return FileSystemType.Ext3;
            if (upperName.Contains("EXT2")) return FileSystemType.Ext2;
            if (upperName.Contains("XFS")) return FileSystemType.XFS;
            if (upperName.Contains("BTRFS")) return FileSystemType.Btrfs;
            if (upperName.Contains("ZFS")) return FileSystemType.ZFS;
            if (upperName.Contains("REFS")) return FileSystemType.ReFS;
        }

        // Fallback based on GUID
        return typeGuid.ToString().ToUpper() switch
        {
            "48465300-0000-11AA-AA11-00306543ECAC" => FileSystemType.HFSPlus,
            "7C3457EF-0000-11AA-AA11-00306543ECAC" => FileSystemType.APFS,
            _ => FileSystemType.Unknown
        };
    }

    // GPT-specific properties
    /// <summary>
    /// Gets the GPT signature ("EFI PART" in little-endian).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public long Signature => m_parsed ? m_signature : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the GPT revision number.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public uint Revision => m_parsed ? m_revision : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the GPT header size in bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public uint HeaderSizeField => m_parsed ? m_headerSize : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the CRC32 checksum of the GPT header.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public uint HeaderCrc32 => m_parsed ? m_headerCrc32 : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the LBA (Logical Block Address) of the current GPT header.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public long CurrentLba => m_parsed ? m_currentLba : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the LBA of the backup GPT header.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public long BackupLba => m_parsed ? m_backupLba : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the first usable LBA for partitions.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public long FirstUsableLba => m_parsed ? m_firstUsableLba : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the last usable LBA for partitions.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public long LastUsableLba => m_parsed ? m_lastUsableLba : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the disk GUID.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public Guid DiskGuid => m_parsed ? m_diskGuid : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the LBA where partition entries start.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public long PartitionEntryLba => m_parsed ? m_partitionEntryLba : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the number of partition entries.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public uint NumPartitionEntries => m_parsed ? m_numPartitionEntries : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the size of each partition entry in bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public uint PartitionEntrySizeField => m_parsed ? m_partitionEntrySize : throw new InvalidOperationException("GPT header not parsed.");

    /// <summary>
    /// Gets the CRC32 checksum of the partition entries.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if GPT header has not been parsed.</exception>
    public uint PartitionEntryCrc32 => m_parsed ? m_partitionEntryCrc32 : throw new InvalidOperationException("GPT header not parsed.");

    /// <inheritdoc />
    public async IAsyncEnumerable<IPartition> EnumeratePartitions([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        // Ensure partitions are parsed
        if (m_partitions == null && m_rawDisk != null)
            await ParsePartitionEntriesAsync(cancellationToken).ConfigureAwait(false);

        if (m_partitions != null)
        {
            foreach (var partition in m_partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return partition;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        // Ensure partitions are parsed
        if (m_partitions == null && m_rawDisk != null)
            await ParsePartitionEntriesAsync(cancellationToken).ConfigureAwait(false);

        if (m_partitions == null)
            return null;

        if (partitionNumber >= 1 && partitionNumber <= m_partitions.Count)
            return m_partitions[partitionNumber - 1];

        return null;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("GPT header not parsed.");

        if (m_rawDisk == null)
            throw new InvalidOperationException("No raw disk available for reading GPT data.");

        // Calculate the total size needed:
        // - Protective MBR (512 bytes)
        // - GPT Header (1 sector)
        // - Partition Entries (m_partitionEntryLba sectors)
        long partitionEntriesEnd = m_partitionEntryLba * m_bytesPerSector + (m_numPartitionEntries * m_partitionEntrySize);
        long totalSize = partitionEntriesEnd;

        // Read all the data
        using var stream = await m_rawDisk.ReadBytesAsync(0, (int)totalSize, cancellationToken).ConfigureAwait(false);
        var buffer = new byte[totalSize];
        await stream.ReadAtLeastAsync(buffer, (int)totalSize, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new MemoryStream(buffer, writable: false);
    }

    /// <summary>
    /// Verifies the backup GPT header.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if the backup header is valid, false otherwise.</returns>
    private async Task<bool> VerifyBackupHeaderAsync(CancellationToken token)
    {
        if (m_rawDisk == null)
            throw new InvalidOperationException("No raw disk available for reading GPT backup header.");

        if (m_backupLba == 0)
            return false;

        var backupHeaderBytes = new byte[HeaderSize];
        long backupOffset = m_backupLba * m_bytesPerSector;

        try
        {
            using var stream = await m_rawDisk.ReadBytesAsync(backupOffset, (int)m_bytesPerSector, token).ConfigureAwait(false);
            await stream.ReadAtLeastAsync(backupHeaderBytes, HeaderSize, cancellationToken: token).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }

        // Verify Signature
        long signature = BitConverter.ToInt64(backupHeaderBytes, 0);
        if (signature != GptSignature)
            return false;

        // Verify CRC32
        uint storedCrc = BitConverter.ToUInt32(backupHeaderBytes, 16);

        // Zero out CRC field for calculation
        var copyForCrc = new byte[HeaderSize];
        Array.Copy(backupHeaderBytes, copyForCrc, HeaderSize);
        BitConverter.TryWriteBytes(copyForCrc.AsSpan(16), 0u);

        uint calculatedCrc = CalculateCrc32(copyForCrc, 0, HeaderSize);
        if (storedCrc != calculatedCrc)
            return false;

        // Verify other fields
        // Revision should be same
        if (BitConverter.ToUInt32(backupHeaderBytes, 8) != m_revision) return false;
        // Header size should be same
        if (BitConverter.ToUInt32(backupHeaderBytes, 12) != m_headerSize) return false;
        // Reserved should be 0
        if (BitConverter.ToUInt32(backupHeaderBytes, 20) != 0) return false;

        // Current LBA should be Backup LBA
        if (BitConverter.ToInt64(backupHeaderBytes, 24) != m_backupLba) return false;
        // Backup LBA should be Current LBA (Primary LBA)
        if (BitConverter.ToInt64(backupHeaderBytes, 32) != m_currentLba) return false;

        // Usable LBAs should be same
        if (BitConverter.ToInt64(backupHeaderBytes, 40) != m_firstUsableLba) return false;
        if (BitConverter.ToInt64(backupHeaderBytes, 48) != m_lastUsableLba) return false;

        // Disk GUID should be same
        var diskGuidBytes = new byte[16];
        Array.Copy(backupHeaderBytes, 56, diskGuidBytes, 0, 16);
        if (new Guid(diskGuidBytes) != m_diskGuid) return false;

        // Number of partition entries should be same
        if (BitConverter.ToUInt32(backupHeaderBytes, 80) != m_numPartitionEntries) return false;
        // Size of partition entry should be same
        if (BitConverter.ToUInt32(backupHeaderBytes, 84) != m_partitionEntrySize) return false;
        // Partition entry CRC32 should be same
        if (BitConverter.ToUInt32(backupHeaderBytes, 88) != m_partitionEntryCrc32) return false;

        return true;
    }

    /// <summary>
    /// Calculates the CRC32 checksum for the given buffer.
    /// </summary>
    /// <param name="buffer">The buffer containing the data.</param>
    /// <param name="offset">The offset in the buffer to start calculating the CRC32.</param>
    /// <param name="count">The number of bytes to include in the CRC32 calculation.</param>
    /// <returns>The calculated CRC32 checksum.</returns>
    private static uint CalculateCrc32(byte[] buffer, int offset, int count)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < count; i++)
        {
            byte b = buffer[offset + i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }
        return ~crc;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the GPT instance, releasing any resources. After disposal, the instance should not be used.
    /// </summary>
    /// <param name="disposing">Indicates whether the method is called from Dispose (true) or from a finalizer (false).</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!m_disposed)
        {
            if (disposing)
            {
                m_headerBytes = null;
                m_protectiveMbrBytes = null;
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
        /// <inheritdoc />
        public int PartitionNumber { get; init; }
        /// <inheritdoc />
        public PartitionType Type { get; init; }
        /// <inheritdoc />
        public required IPartitionTable PartitionTable { get; init; }
        /// <inheritdoc />
        public long StartOffset { get; init; }
        /// <inheritdoc />
        public long Size { get; init; }
        /// <inheritdoc />
        public string? Name { get; init; }
        /// <inheritdoc />
        public FileSystemType FilesystemType { get; init; }
        /// <inheritdoc />
        public Guid? VolumeGuid { get; init; }
        /// <inheritdoc />
        public required IRawDisk? RawDisk { get; init; }
        /// <inheritdoc />
        public long StartingLba { get; init; }
        /// <inheritdoc />
        public long EndingLba { get; init; }
        /// <inheritdoc />
        public long Attributes { get; init; }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            if (RawDisk == null)
                throw new InvalidOperationException("RawDisk not available.");
            return RawDisk.ReadBytesAsync(StartOffset, (int)Math.Min(Size, int.MaxValue), cancellationToken);
        }

        /// <inheritdoc />
        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            if (RawDisk == null)
                throw new InvalidOperationException("RawDisk not available.");
            // Return a stream that wraps the raw disk write capability
            return Task.FromResult<Stream>(new PartitionWriteStream(RawDisk, StartOffset, Size));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // No unmanaged resources to dispose
        }
    }

    /// <summary>
    /// A stream that writes data to a partition on the raw disk.
    /// </summary>
    private class PartitionWriteStream : Stream
    {
        private readonly IRawDisk _disk;
        private readonly long _startOffset;
        private readonly long _maxSize;
        private readonly MemoryStream _buffer;
        private bool _disposed = false;

        public PartitionWriteStream(IRawDisk disk, long startOffset, long maxSize)
        {
            _disk = disk;
            _startOffset = startOffset;
            _maxSize = maxSize;
            _buffer = new MemoryStream();
        }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position
        {
            get => _buffer.Position;
            set => _buffer.Position = value;
        }

        public override void Flush() => _buffer.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => _buffer.Seek(offset, origin);
        public override void SetLength(long value)
        {
            if (value > _maxSize)
                throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
            _buffer.SetLength(value);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_buffer.Position + count > _maxSize)
                throw new IOException($"Cannot write beyond partition size of {_maxSize} bytes.");
            _buffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Write all buffered data to disk
                    _buffer.Position = 0;
                    var data = _buffer.ToArray();
                    if (data.Length > 0)
                    {
                        _disk.WriteBytesAsync(_startOffset, data, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    _buffer.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
