using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Represents an MBR (Master Boot Record) partition table.
/// MBR is the traditional partition table format used on BIOS-based systems.
/// </summary>
public class MBR : IPartitionTable
{
    // Constants for MBR parsing
    private const int MbrSize = 512;
    private const int PartitionEntrySize = 16;
    private const int MaxPartitionEntries = 4;
    private const ushort MbrBootSignature = 0xAA55;

    // Internal state
    private bool m_parsed = false;
    private bool m_disposed = false;

    // MBR Header fields
    private ushort m_mbrBootSignature;
    private List<MBRPartitionEntry>? m_partitionEntries;

    // Additional tracking
    private IRawDisk? m_rawDisk;
    private byte[]? m_mbrBytes;
    private long m_bytesPerSector;

    // Partition storage
    private List<IPartition>? m_partitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="MBR"/> class.
    /// </summary>
    /// <param name="disk">The raw disk to parse, or null for byte array parsing.</param>
    public MBR(IRawDisk? disk)
    {
        m_rawDisk = disk;
    }

    // IPartitionTable implementation
    /// <inheritdoc />
    public IRawDisk? RawDisk { get => m_rawDisk; }

    /// <inheritdoc />
    public PartitionTableType TableType => PartitionTableType.MBR;

    /// <summary>
    /// Parses the MBR partition table from the raw disk.
    /// </summary>
    /// <param name="disk">The raw disk to parse.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
    public async Task<bool> ParseAsync(IRawDisk disk, CancellationToken token)
    {
        m_bytesPerSector = disk.SectorSize;

        // Read the MBR (LBA 0)
        m_mbrBytes = new byte[MbrSize];
        using var bytestream = await disk.ReadBytesAsync(0, MbrSize, token).ConfigureAwait(false);
        await bytestream.ReadAtLeastAsync(m_mbrBytes, MbrSize, cancellationToken: token).ConfigureAwait(false);

        var result = await ParseMBRBytesAsync(m_mbrBytes, token).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Parses the MBR partition table from a byte array.
    /// </summary>
    /// <param name="bytes">The raw disk bytes.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
    public async Task<bool> ParseAsync(byte[] bytes, int sectorSize, CancellationToken token)
    {
        m_bytesPerSector = sectorSize;

        // Read MBR from the first sector
        m_mbrBytes = bytes[0..sectorSize];
        return await ParseMBRBytesAsync(m_mbrBytes, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the MBR partition table from the given byte array containing the MBR sector.
    /// Assumes the byte array is at least 512 bytes and contains the MBR data.
    /// </summary>
    /// <param name="bytes">The byte array containing the MBR sector.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if parsing was successful.</returns>
    /// <exception cref="ArgumentException">Thrown if the byte array is smaller than the MBR size.</exception>
    private async Task<bool> ParseMBRBytesAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length < MbrSize)
            throw new ArgumentException($"Byte array must be at least {MbrSize} bytes long.", nameof(bytes));

        // Read boot signature (2 bytes at offset 510)
        m_mbrBootSignature = BitConverter.ToUInt16(bytes, 510);

        // Verify boot signature
        if (m_mbrBootSignature != MbrBootSignature)
            return false;

        m_partitionEntries = [];

        // Parse partition entries (64 bytes starting at offset 446)
        for (int i = 0; i < MaxPartitionEntries; i++)
        {
            token.ThrowIfCancellationRequested();

            int offset = 446 + (i * PartitionEntrySize);
            var entry = ParsePartitionEntry(bytes, offset, i + 1);

            if (entry != null)
            {
                m_partitionEntries.Add(entry);

                // Add to partitions list for IPartitionTable implementation
                if (m_partitions == null)
                    m_partitions = [];

                m_partitions.Add(new BasePartition
                {
                    PartitionNumber = i + 1,
                    Type = entry.PartitionType,
                    PartitionTable = this,
                    StartOffset = entry.StartLBA * m_bytesPerSector,
                    Size = entry.SizeInSectors * m_bytesPerSector,
                    Name = $"Partition {i + 1}",
                    FilesystemType = DetermineFilesystemType(entry),
                    VolumeGuid = null,
                    RawDisk = m_rawDisk,
                    StartingLba = entry.StartLBA,
                    EndingLba = entry.StartLBA + entry.SizeInSectors - 1
                });
            }
        }

        m_parsed = true;
        return true;
    }

    /// <summary>
    /// Parses a single partition entry from the MBR.
    /// </summary>
    /// <param name="bytes">The byte array containing the MBR sector.</param>
    /// <param name="offset">The offset within the byte array where the partition entry starts.</param>
    /// <param name="entryNumber">The partition entry number (1-based).</param>
    /// <returns>The parsed MBR partition entry, or null if the entry is empty or invalid.</returns>
    private MBRPartitionEntry? ParsePartitionEntry(byte[] bytes, int offset, int entryNumber)
    {
        // Read partition type byte
        byte partitionType = bytes[offset + 4];

        // Skip empty entries
        if (partitionType == 0)
            return null;

        // Read CHS values (not commonly used, but we store them)
        byte startHead = bytes[offset + 1];
        byte startSector = bytes[offset + 2];
        ushort startCylinder = (ushort)(((bytes[offset + 3] & 0xC0) << 2) | bytes[offset + 2]);

        byte endHead = bytes[offset + 5];
        byte endSector = bytes[offset + 6];
        ushort endCylinder = (ushort)(((bytes[offset + 7] & 0xC0) << 2) | bytes[offset + 6]);

        // Read LBA values (more reliable than CHS)
        uint startLBA = BitConverter.ToUInt32(bytes, offset + 8);
        uint sizeInSectors = BitConverter.ToUInt32(bytes, offset + 12);

        // Skip entries with zero size
        if (sizeInSectors == 0)
            return null;

        return new MBRPartitionEntry
        {
            EntryNumber = entryNumber,
            PartitionType = DeterminePartitionType(partitionType),
            PartitionTypeByte = partitionType,
            StartLBA = startLBA,
            SizeInSectors = sizeInSectors,
            StartHead = startHead,
            StartSector = startSector,
            StartCylinder = startCylinder,
            EndHead = endHead,
            EndSector = endSector,
            EndCylinder = endCylinder
        };
    }

    /// <summary>
    /// Determines the partition type based on the partition type byte.
    /// </summary>
    /// <param name="typeByte">The partition type byte from the MBR.</param>
    /// <returns>The determined partition type.</returns>
    private static PartitionType DeterminePartitionType(byte typeByte)
    {
        // MBR partition type byte mappings
        return typeByte switch
        {
            0x01 => PartitionType.Primary,      // FAT12
            0x04 => PartitionType.Primary,      // FAT16 (less than 32MB)
            0x06 => PartitionType.Primary,      // FAT16
            0x07 => PartitionType.Primary,      // NTFS or IFS
            0x0B => PartitionType.Primary,      // FAT32 (CHS)
            0x0C => PartitionType.Primary,      // FAT32 (LBA)
            0x0E => PartitionType.Primary,      // FAT16 (LBA)
            0x0F => PartitionType.Extended,     // Extended (LBA)
            0x11 => PartitionType.Primary,      // Hidden FAT12 (CHS)
            0x14 => PartitionType.Primary,      // Hidden FAT16 (CHS, less than 32MB)
            0x16 => PartitionType.Primary,      // Hidden FAT16
            0x17 => PartitionType.Primary,      // Hidden NTFS
            0x1B => PartitionType.Primary,      // Hidden FAT32 (CHS)
            0x1C => PartitionType.Primary,      // Hidden FAT32 (LBA)
            0x1E => PartitionType.Primary,      // Hidden FAT16 (LBA)
            0x5 => PartitionType.Extended,      // Extended (CHS)
            0x85 => PartitionType.Logical,      // Linux extended
            0x8E => PartitionType.Primary,      // Linux LVM
            0xEE => PartitionType.Protective,   // GPT protective
            0xEF => PartitionType.EFI,          // EFI System Partition
            0xFD => PartitionType.Primary,      // Linux RAID
            _ => PartitionType.Unknown
        };
    }

    /// <summary>
    /// Determines the filesystem type based on the partition type byte.
    /// </summary>
    /// <param name="entry">The MBR partition entry.</param>
    /// <returns>The determined filesystem type.</returns>
    private static FileSystemType DetermineFilesystemType(MBRPartitionEntry entry)
    {
        // Determine filesystem type based on partition type byte
        return entry.PartitionTypeByte switch
        {
            0x01 or 0x11 or 0x81 => FileSystemType.FAT12,
            0x04 or 0x06 or 0x0E or 0x14 or 0x16 or 0x1E => FileSystemType.FAT16,
            0x0B or 0x0C or 0x1B or 0x1C => FileSystemType.FAT32,
            0x07 or 0x17 => FileSystemType.NTFS,
            0xEF => FileSystemType.Unknown, // EFI System Partition, actual FS unknown
            _ => FileSystemType.Unknown
        };
    }

    // MBR-specific properties
    /// <summary>
    /// Gets the MBR boot signature (0xAA55).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if MBR has not been parsed.</exception>
    public ushort MbrBootSignatureValue => m_parsed ? m_mbrBootSignature : throw new InvalidOperationException("MBR not parsed.");

    /// <summary>
    /// Gets the number of partition entries.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if MBR has not been parsed.</exception>
    public int NumPartitionEntries => m_parsed ? (m_partitionEntries?.Count ?? 0) : throw new InvalidOperationException("MBR not parsed.");

    /// <inheritdoc />
    public async IAsyncEnumerable<IPartition> EnumeratePartitions([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!m_parsed)
            throw new InvalidOperationException("MBR not parsed.");

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
            throw new InvalidOperationException("MBR not parsed.");

        if (m_partitions == null)
            return null;

        if (partitionNumber >= 1 && partitionNumber <= m_partitions.Count)
            return m_partitions[partitionNumber - 1];

        return null;
    }

    /// <inheritdoc />
    public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
    {
        // MBR doesn't have a protective MBR - it IS the MBR
        throw new NotSupportedException("MBR partition tables do not have a protective MBR. Use GetPartitionAsync instead.");
    }

    /// <inheritdoc />
    public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
    {
        if (!m_parsed || m_mbrBytes == null)
            throw new InvalidOperationException("MBR not parsed.");

        // Return the MBR sector (512 bytes)
        return Task.FromResult<Stream>(new MemoryStream(m_mbrBytes, writable: false));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the MBR instance, releasing any resources. After disposal, the instance should not be used.
    /// </summary>
    /// <param name="disposing">Indicates whether the method is called from Dispose (true) or from a finalizer (false).</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!m_disposed)
        {
            if (disposing)
            {
                m_mbrBytes = null;
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
    /// Represents an MBR partition entry.
    /// </summary>
    private class MBRPartitionEntry
    {
        /// <summary>Gets the entry number (1-4).</summary>
        public int EntryNumber { get; init; }

        /// <summary>Gets the partition type.</summary>
        public PartitionType PartitionType { get; init; }

        /// <summary>Gets the raw partition type byte.</summary>
        public byte PartitionTypeByte { get; init; }

        /// <summary>Gets the starting LBA.</summary>
        public uint StartLBA { get; init; }

        /// <summary>Gets the size in sectors.</summary>
        public uint SizeInSectors { get; init; }

        /// <summary>Gets the starting head (CHS).</summary>
        public byte StartHead { get; init; }

        /// <summary>Gets the starting sector (CHS).</summary>
        public byte StartSector { get; init; }

        /// <summary>Gets the starting cylinder (CHS).</summary>
        public ushort StartCylinder { get; init; }

        /// <summary>Gets the ending head (CHS).</summary>
        public byte EndHead { get; init; }

        /// <summary>Gets the ending sector (CHS).</summary>
        public byte EndSector { get; init; }

        /// <summary>Gets the ending cylinder (CHS).</summary>
        public ushort EndCylinder { get; init; }
    }

}
