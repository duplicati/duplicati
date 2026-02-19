// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Synthesizes MBR and GPT partition tables from geometry metadata.
/// This class handles the creation of raw byte arrays representing partition tables
/// for disk image restoration operations.
/// </summary>
internal static class PartitionTableSynthesizer
{
    // Constants from PartitionConstants
    private const int MbrSize = PartitionConstants.MbrSize;
    private const int GptHeaderSize = PartitionConstants.GptHeaderSize;
    private const ushort MbrBootSignature = PartitionConstants.MbrBootSignature;
    private const byte ProtectiveMbrType = PartitionConstants.ProtectiveMbrType;
    private const long GptSignature = PartitionConstants.GptSignature;
    private const uint GptRevision = PartitionConstants.GptRevision;
    private const int PartitionEntrySize = PartitionConstants.GptPartitionEntrySize;

    /// <summary>
    /// Synthesizes a partition table (MBR or GPT) from geometry metadata into a byte array.
    /// Auto-detects whether to create MBR or GPT based on the metadata.
    /// </summary>
    /// <param name="metadata">The geometry metadata containing partition table information.</param>
    /// <returns>A byte array containing the synthesized partition table data, or null if no partition table is specified.</returns>
    public static byte[]? SynthesizePartitionTable(GeometryMetadata metadata)
    {
        if (metadata.PartitionTable == null)
            return null;

        return metadata.PartitionTable.Type switch
        {
            PartitionTableType.MBR => SynthesizeMBR(metadata),
            PartitionTableType.GPT => SynthesizeGPT(metadata),
            _ => null
        };
    }

    /// <summary>
    /// Synthesizes an MBR partition table from geometry metadata.
    /// </summary>
    /// <param name="metadata">The geometry metadata containing partition information.</param>
    /// <returns>A byte array containing the synthesized MBR partition table.</returns>
    public static byte[] SynthesizeMBR(GeometryMetadata metadata)
    {
        var sectorSize = metadata.Disk?.SectorSize ?? MbrSize;
        var mbrData = new byte[sectorSize];

        // Boot code area (first 446 bytes) - typically zeros for new MBR
        // Could copy from original if available, but zeros are fine for restore

        // Partition entries start at offset 446
        int partitionEntryOffset = 446;
        int partitionEntrySize = 16;

        if (metadata.Partitions != null)
        {
            // MBR supports up to 4 primary partitions
            var mbrPartitions = metadata.Partitions
                .Where(p => p.TableType == PartitionTableType.MBR)
                .OrderBy(p => p.Number)
                .Take(4)
                .ToList();

            for (int i = 0; i < mbrPartitions.Count && i < 4; i++)
            {
                var part = mbrPartitions[i];
                int offset = partitionEntryOffset + (i * partitionEntrySize);

                WriteMBRPartitionEntry(mbrData, offset, part, sectorSize);
            }
        }

        // Boot signature at offset 510-511 (0xAA55)
        mbrData[510] = 0x55;
        mbrData[511] = 0xAA;

        return mbrData;
    }

    /// <summary>
    /// Writes a single MBR partition entry to the specified offset.
    /// </summary>
    /// <param name="mbrData">The MBR data buffer.</param>
    /// <param name="offset">The offset in the buffer to write the entry.</param>
    /// <param name="part">The partition geometry.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    private static void WriteMBRPartitionEntry(byte[] mbrData, int offset, PartitionGeometry part, int sectorSize)
    {
        // Status byte (0x80 = bootable, 0x00 = not bootable)
        // Default to not bootable, could be enhanced to detect bootable partitions
        mbrData[offset] = 0x00;

        // CHS start (3 bytes) - use LBA translation or zeros
        // Modern systems use LBA, so we can set these to 0xFF for invalid CHS
        mbrData[offset + 1] = 0xFF;
        mbrData[offset + 2] = 0xFF;
        mbrData[offset + 3] = 0xFF;

        // Partition type byte
        mbrData[offset + 4] = MbrPartitionTypes.ToTypeByte(part.FilesystemType, part.Type);

        // CHS end (3 bytes) - use LBA translation or zeros
        mbrData[offset + 5] = 0xFF;
        mbrData[offset + 6] = 0xFF;
        mbrData[offset + 7] = 0xFF;

        // Start LBA (4 bytes, little-endian)
        uint startLba = (uint)(part.StartOffset / sectorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(mbrData.AsSpan(offset + 8, 4), startLba);

        // Size in sectors (4 bytes, little-endian)
        uint sizeInSectors = (uint)(part.Size / sectorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(mbrData.AsSpan(offset + 12, 4), sizeInSectors);
    }

    /// <summary>
    /// Synthesizes a GPT partition table from geometry metadata.
    /// </summary>
    /// <param name="metadata">The geometry metadata containing partition information.</param>
    /// <returns>A byte array containing the synthesized GPT partition table.</returns>
    public static byte[] SynthesizeGPT(GeometryMetadata metadata)
    {
        var sectorSize = metadata.Disk?.SectorSize ?? MbrSize;
        var diskSize = metadata.Disk?.Size ?? 0;
        var diskSectors = diskSize / sectorSize;

        // Calculate sizes
        int numPartitionEntries = 128;  // Standard GPT supports 128 entries
        int partitionEntriesSize = numPartitionEntries * PartitionEntrySize;
        int partitionEntriesSectors = (partitionEntriesSize + sectorSize - 1) / sectorSize;

        // Total GPT data: Protective MBR (1 sector) + GPT Header (1 sector) + Partition Entries
        int totalGptSectors = 2 + partitionEntriesSectors;
        long totalSize = totalGptSectors * sectorSize;

        var gptData = new byte[totalSize];

        // Write protective MBR at LBA 0
        WriteProtectiveMBR(gptData, metadata, sectorSize, diskSectors);

        // Write GPT header at LBA 1 (sectorSize offset)
        WriteGPTHeader(gptData, metadata, sectorSize, partitionEntriesSectors, numPartitionEntries, diskSectors);

        // Write partition entries starting at LBA 2 (2 * sectorSize offset)
        WriteGPTPartitionEntries(gptData, metadata, sectorSize, partitionEntriesSectors);

        return gptData;
    }

    /// <summary>
    /// Writes the protective MBR for GPT.
    /// </summary>
    /// <param name="gptData">The GPT data buffer.</param>
    /// <param name="metadata">The geometry metadata.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="diskSectors">The total number of sectors on the disk.</param>
    private static void WriteProtectiveMBR(byte[] gptData, GeometryMetadata metadata, int sectorSize, long diskSectors)
    {
        // Boot code (first 446 bytes) - zeros

        // Partition entry 1 (at offset 446): Protective MBR entry
        // Status byte
        gptData[446] = 0x00;

        // CHS start
        gptData[447] = 0x00;
        gptData[448] = 0x02;
        gptData[449] = 0x00;

        // Partition type: 0xEE (GPT protective)
        gptData[450] = ProtectiveMbrType;

        // CHS end (max values for large disks)
        gptData[451] = 0xFF;
        gptData[452] = 0xFF;
        gptData[453] = 0xFF;

        // Start LBA = 1 (GPT header is at LBA 1)
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(454, 4), 1u);

        // Size in sectors (max 0xFFFFFFFF for protective MBR)
        uint sizeInSectors = diskSectors > uint.MaxValue ? uint.MaxValue : (uint)(diskSectors - 1);
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(458, 4), sizeInSectors);

        // Boot signature at offset 510-511
        gptData[510] = 0x55;
        gptData[511] = 0xAA;
    }

    /// <summary>
    /// Writes the GPT header.
    /// </summary>
    /// <param name="gptData">The GPT data buffer.</param>
    /// <param name="metadata">The geometry metadata.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="partitionEntriesSectors">The number of sectors for partition entries.</param>
    /// <param name="numPartitionEntries">The number of partition entries.</param>
    /// <param name="diskSectors">The total number of sectors on the disk.</param>
    private static void WriteGPTHeader(byte[] gptData, GeometryMetadata metadata, int sectorSize,
        int partitionEntriesSectors, int numPartitionEntries, long diskSectors)
    {
        int headerOffset = sectorSize;  // GPT header is at LBA 1

        // Signature: "EFI PART" in little-endian
        BinaryPrimitives.WriteInt64LittleEndian(gptData.AsSpan(headerOffset + 0, 8), GptSignature);

        // Revision: 1.0 (0x00010000)
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 8, 4), GptRevision);

        // Header size: 92 bytes
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 12, 4), (uint)GptHeaderSize);

        // CRC32 of header (calculated later) - set to 0 for now
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 16, 4), 0u);

        // Reserved: must be 0
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 20, 4), 0u);

        // Current LBA: 1 (this header is at LBA 1)
        BinaryPrimitives.WriteInt64LittleEndian(gptData.AsSpan(headerOffset + 24, 8), (long)1);

        // Backup LBA: last sector of disk
        long backupLba = diskSectors - 1;
        BinaryPrimitives.WriteInt64LittleEndian(gptData.AsSpan(headerOffset + 32, 8), backupLba);

        // First usable LBA: after partition entries
        long firstUsableLba = 2 + partitionEntriesSectors;
        BinaryPrimitives.WriteInt64LittleEndian(gptData.AsSpan(headerOffset + 40, 8), firstUsableLba);

        // Last usable LBA: before backup header
        long lastUsableLba = diskSectors - partitionEntriesSectors - 2;
        BinaryPrimitives.WriteInt64LittleEndian(gptData.AsSpan(headerOffset + 48, 8), lastUsableLba);

        // Disk GUID - generate new or use from metadata if available
        var diskGuid = Guid.NewGuid();
        diskGuid.ToByteArray().CopyTo(gptData, headerOffset + 56);

        // Partition entry LBA: 2 (entries start at LBA 2)
        BinaryPrimitives.WriteInt64LittleEndian(gptData.AsSpan(headerOffset + 72, 8), (long)2);

        // Number of partition entries
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 80, 4), (uint)numPartitionEntries);

        // Size of partition entry: 128 bytes
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 84, 4), (uint)PartitionEntrySize);

        // CRC32 of partition entries (calculated later)
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 88, 4), 0u);

        // Calculate and write CRC32 of header
        uint headerCrc = Crc32.Calculate(gptData, headerOffset, GptHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 16, 4), headerCrc);
    }

    /// <summary>
    /// Writes GPT partition entries.
    /// </summary>
    /// <param name="gptData">The GPT data buffer.</param>
    /// <param name="metadata">The geometry metadata.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <param name="partitionEntriesSectors">The number of sectors for partition entries.</param>
    private static void WriteGPTPartitionEntries(byte[] gptData, GeometryMetadata metadata, int sectorSize, int partitionEntriesSectors)
    {
        int entriesOffset = 2 * sectorSize;  // Entries start at LBA 2

        if (metadata.Partitions == null)
            return;

        var gptPartitions = metadata.Partitions
            .Where(p => p.TableType == PartitionTableType.GPT)
            .OrderBy(p => p.Number)
            .Take(128)  // GPT standard supports 128 entries
            .ToList();

        // Calculate partition entries CRC32
        var entriesData = new byte[partitionEntriesSectors * sectorSize];

        for (int i = 0; i < gptPartitions.Count; i++)
        {
            var part = gptPartitions[i];
            int entryOffset = i * PartitionEntrySize;
            WriteGPTPartitionEntry(entriesData, entryOffset, part, sectorSize);
        }

        // Copy entries to main buffer
        entriesData.CopyTo(gptData, entriesOffset);

        // Calculate and write CRC32 of partition entries to header
        uint entriesCrc = Crc32.Calculate(entriesData, 0, entriesData.Length);
        int headerOffset = sectorSize;
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 88, 4), entriesCrc);

        // Recalculate header CRC with updated partition entries CRC
        uint headerCrc = Crc32.Calculate(gptData, headerOffset, GptHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(gptData.AsSpan(headerOffset + 16, 4), headerCrc);
    }

    /// <summary>
    /// Writes a single GPT partition entry.
    /// </summary>
    /// <param name="entriesData">The partition entries buffer.</param>
    /// <param name="offset">The offset in the buffer to write the entry.</param>
    /// <param name="part">The partition geometry.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    private static void WriteGPTPartitionEntry(byte[] entriesData, int offset, PartitionGeometry part, int sectorSize)
    {
        // Partition type GUID (16 bytes)
        var typeGuid = GptPartitionTypeGuids.ToGuid(part.Type);
        typeGuid.ToByteArray().CopyTo(entriesData, offset + 0);

        // Unique partition GUID (16 bytes) - use VolumeGuid if available, otherwise generate
        var uniqueGuid = part.VolumeGuid ?? Guid.NewGuid();
        uniqueGuid.ToByteArray().CopyTo(entriesData, offset + 16);

        // Starting LBA (8 bytes)
        long startLba = part.StartOffset / sectorSize;
        BinaryPrimitives.WriteInt64LittleEndian(entriesData.AsSpan(offset + 32, 8), startLba);

        // Ending LBA (8 bytes)
        long sizeInSectors = part.Size / sectorSize;
        long endLba = startLba + sizeInSectors - 1;
        BinaryPrimitives.WriteInt64LittleEndian(entriesData.AsSpan(offset + 40, 8), endLba);

        // Attributes (8 bytes) - default to 0
        BinaryPrimitives.WriteInt64LittleEndian(entriesData.AsSpan(offset + 48, 8), (long)0);

        // Partition name (72 bytes, UTF-16LE)
        string name = part.Name ?? $"Partition {part.Number}";
        var nameBytes = Encoding.Unicode.GetBytes(name);
        int nameLength = Math.Min(nameBytes.Length, 72);
        Array.Copy(nameBytes, 0, entriesData, offset + 56, nameLength);
        // Pad remainder with zeros (already zeroed)
    }

    /// <summary>
    /// Writes the secondary (backup) GPT header and partition entries to the end of the disk.
    /// </summary>
    /// <param name="targetDisk">The raw disk to write to.</param>
    /// <param name="geometryMetadata">The geometry metadata.</param>
    /// <param name="primaryGptData">The primary GPT data.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteSecondaryGPTAsync(IRawDisk targetDisk, GeometryMetadata geometryMetadata, byte[] primaryGptData, CancellationToken cancel)
    {
        if (targetDisk == null || geometryMetadata?.Disk == null)
            return;

        var sectorSize = geometryMetadata.Disk.SectorSize;
        var diskSectors = geometryMetadata.Disk.Sectors;

        // Calculate sizes
        int numPartitionEntries = 128;
        int partitionEntriesSize = numPartitionEntries * PartitionEntrySize;
        int partitionEntriesSectors = (partitionEntriesSize + sectorSize - 1) / sectorSize;

        // Secondary GPT layout:
        // - Partition entries (before header)
        // - Secondary GPT header (last sector)

        // Read primary header to get disk GUID and other fields
        int primaryHeaderOffset = sectorSize;
        var diskGuid = new byte[16];
        Array.Copy(primaryGptData, primaryHeaderOffset + 56, diskGuid, 0, 16);

        // Read partition entries CRC from primary
        byte[] partitionEntriesCrcBytes = new byte[4];
        Array.Copy(primaryGptData, primaryHeaderOffset + 88, partitionEntriesCrcBytes, 0, 4);

        // Create secondary header
        var secondaryHeader = new byte[GptHeaderSize];

        // Signature: "EFI PART"
        BinaryPrimitives.WriteInt64LittleEndian(secondaryHeader.AsSpan(0, 8), GptSignature);

        // Revision: 1.0
        BinaryPrimitives.WriteUInt32LittleEndian(secondaryHeader.AsSpan(8, 4), GptRevision);

        // Header size: 92 bytes
        BinaryPrimitives.WriteUInt32LittleEndian(secondaryHeader.AsSpan(12, 4), (uint)GptHeaderSize);

        // CRC32 (calculated later)
        BinaryPrimitives.WriteUInt32LittleEndian(secondaryHeader.AsSpan(16, 4), 0u);

        // Reserved
        BinaryPrimitives.WriteUInt32LittleEndian(secondaryHeader.AsSpan(20, 4), 0u);

        // Current LBA: last sector (backup header location)
        long secondaryHeaderLba = diskSectors - 1;
        BinaryPrimitives.WriteInt64LittleEndian(secondaryHeader.AsSpan(24, 8), secondaryHeaderLba);

        // Backup LBA: 1 (primary header location)
        BinaryPrimitives.WriteInt64LittleEndian(secondaryHeader.AsSpan(32, 8), (long)1);

        // First usable LBA
        long firstUsableLba = 2 + partitionEntriesSectors;
        BinaryPrimitives.WriteInt64LittleEndian(secondaryHeader.AsSpan(40, 8), firstUsableLba);

        // Last usable LBA
        long lastUsableLba = diskSectors - partitionEntriesSectors - 2;
        BinaryPrimitives.WriteInt64LittleEndian(secondaryHeader.AsSpan(48, 8), lastUsableLba);

        // Disk GUID (same as primary)
        diskGuid.CopyTo(secondaryHeader, 56);

        // Partition entry LBA: right before the secondary header
        long secondaryEntriesLba = diskSectors - partitionEntriesSectors - 1;
        BinaryPrimitives.WriteInt64LittleEndian(secondaryHeader.AsSpan(72, 8), secondaryEntriesLba);

        // Number of partition entries
        BinaryPrimitives.WriteUInt32LittleEndian(secondaryHeader.AsSpan(80, 4), (uint)numPartitionEntries);

        // Size of partition entry
        BinaryPrimitives.WriteUInt32LittleEndian(secondaryHeader.AsSpan(84, 4), (uint)PartitionEntrySize);

        // Partition entries CRC32 (same as primary)
        partitionEntriesCrcBytes.CopyTo(secondaryHeader, 88);

        // Calculate and write CRC32 of secondary header
        uint headerCrc = Crc32.Calculate(secondaryHeader, 0, GptHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(secondaryHeader.AsSpan(16, 4), headerCrc);

        // Write secondary partition entries (same as primary)
        long entriesStartOffset = 2 * sectorSize;
        int entriesByteSize = partitionEntriesSectors * sectorSize;
        var partitionEntries = new byte[entriesByteSize];
        Array.Copy(primaryGptData, entriesStartOffset, partitionEntries, 0, entriesByteSize);

        long secondaryEntriesOffset = secondaryEntriesLba * sectorSize;
        await targetDisk.WriteBytesAsync(secondaryEntriesOffset, partitionEntries, cancel).ConfigureAwait(false);

        // Write secondary header at the last sector
        long secondaryHeaderOffset = secondaryHeaderLba * sectorSize;
        await targetDisk.WriteBytesAsync(secondaryHeaderOffset, secondaryHeader, cancel).ConfigureAwait(false);
    }
}
