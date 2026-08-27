// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Creates new partitions on a target disk that already has a partition table
/// (or initializes a new table on a blank disk). Used when restoring partition
/// backups to a whole-disk target, where the partition referenced by the backup
/// does not exist on the target disk and must be created in free space.
/// </summary>
internal static class PartitionCreator
{
    private static readonly string LOGTAG = Log.LogTagFromType(typeof(PartitionCreator));

    /// <summary>
    /// The alignment used when placing new partitions (1 MiB).
    /// </summary>
    public const long DefaultAlignment = 1024 * 1024;

    /// <summary>
    /// Describes a partition that has been allocated (placement decided) but not
    /// yet written to the target disk's partition table.
    /// </summary>
    /// <param name="TableType">The partition table type of the target disk the allocation was made for.</param>
    /// <param name="Slot">The reserved 1-based slot in the partition table.</param>
    /// <param name="StartOffset">The starting offset of the partition in bytes.</param>
    /// <param name="Size">The size of the partition in bytes.</param>
    /// <param name="Type">The partition type.</param>
    /// <param name="FilesystemType">The filesystem type of the partition content.</param>
    /// <param name="Name">The partition name (GPT only).</param>
    /// <param name="VolumeGuid">The unique partition GUID (GPT only).</param>
    public sealed record AllocatedPartition(
        PartitionTableType TableType,
        int Slot,
        long StartOffset,
        long Size,
        PartitionType Type,
        FileSystemType FilesystemType,
        string? Name,
        Guid? VolumeGuid);

    /// <summary>
    /// Finds free space for a new partition within the usable range of a disk.
    /// Prefers <paramref name="preferredStart"/> when that range is free, otherwise
    /// returns the first gap that fits the required size with the given alignment.
    /// </summary>
    /// <param name="existing">The occupied ranges on the disk (start offset and size in bytes).</param>
    /// <param name="usableStart">The first usable byte offset (inclusive).</param>
    /// <param name="usableEnd">The last usable byte offset (exclusive).</param>
    /// <param name="requiredSize">The required size in bytes.</param>
    /// <param name="alignment">The alignment for the start offset in bytes.</param>
    /// <param name="preferredStart">An optional preferred start offset (e.g. the source partition's original offset).</param>
    /// <returns>The start offset for the new partition, or null if no suitable free space exists.</returns>
    public static long? FindFreeSpace(
        IEnumerable<(long Start, long Size)> existing,
        long usableStart,
        long usableEnd,
        long requiredSize,
        long alignment,
        long? preferredStart = null)
    {
        if (requiredSize <= 0 || usableEnd <= usableStart)
            return null;

        var sorted = existing
            .Where(e => e.Size > 0 && e.Start + e.Size > usableStart && e.Start < usableEnd)
            .OrderBy(e => e.Start)
            .ToList();

        // Honor the preferred start (e.g. the source partition's original offset)
        // when that exact range is free.
        if (preferredStart.HasValue
            && preferredStart.Value >= usableStart
            && preferredStart.Value + requiredSize <= usableEnd
            && !sorted.Any(e => preferredStart.Value < e.Start + e.Size && e.Start < preferredStart.Value + requiredSize))
            return preferredStart.Value;

        var candidate = AlignUp(usableStart, alignment);
        foreach (var e in sorted)
        {
            if (candidate < e.Start && candidate + requiredSize <= e.Start)
                return candidate;

            candidate = Math.Max(candidate, AlignUp(e.Start + e.Size, alignment));
        }

        return candidate + requiredSize <= usableEnd ? candidate : null;
    }

    /// <summary>
    /// Allocates space for a new partition on the target disk by examining the
    /// disk's partition table and finding free space. Disks without a recognizable
    /// partition table are treated as blank and will get a new GPT table.
    /// </summary>
    /// <param name="disk">The target disk.</param>
    /// <param name="requiredSize">The required partition size in bytes (rounded up to sector size).</param>
    /// <param name="preferredStart">An optional preferred start offset (the source partition's original offset).</param>
    /// <param name="type">The partition type.</param>
    /// <param name="filesystemType">The filesystem type of the partition content.</param>
    /// <param name="name">The partition name (GPT only).</param>
    /// <param name="volumeGuid">The unique partition GUID (GPT only); a new GUID is generated when null, so the primary and secondary tables stay consistent.</param>
    /// <param name="pending">Previously allocated partitions that are not yet written to the disk.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>The allocation describing where the partition will be created.</returns>
    /// <exception cref="UserInformationException">Thrown if the disk has no suitable free space or no free partition table slot.</exception>
    public static async Task<AllocatedPartition> AllocateAsync(
        IRawDisk disk,
        long requiredSize,
        long? preferredStart,
        PartitionType type,
        FileSystemType filesystemType,
        string? name,
        Guid? volumeGuid,
        IReadOnlyList<AllocatedPartition> pending,
        CancellationToken cancel)
    {
        var sectorSize = disk.SectorSize;
        using var table = await PartitionTableFactory.CreateAsync(disk, cancel).ConfigureAwait(false);
        var tableType = table?.TableType ?? PartitionTableType.Unknown;

        // Round the required size up to a sector multiple
        requiredSize = AlignUp(requiredSize, sectorSize);

        var existing = new List<(long Start, long Size)>();
        var usedSlots = new HashSet<int>();
        long usableStart;
        long usableEnd;
        int maxSlots;

        if (tableType == PartitionTableType.GPT && table is GPT gpt)
        {
            usableStart = gpt.FirstUsableLba * sectorSize;
            usableEnd = (gpt.LastUsableLba + 1) * sectorSize;
            maxSlots = (int)gpt.NumPartitionEntries;
        }
        else if (tableType == PartitionTableType.MBR)
        {
            usableStart = sectorSize;
            // MBR uses 32-bit LBA fields, limiting the addressable range
            usableEnd = Math.Min(disk.Size, ((long)uint.MaxValue + 1) * sectorSize);
            maxSlots = PartitionConstants.MaxMbrPartitionEntries;
        }
        else
        {
            // No recognizable partition table: treat the disk as blank and
            // reserve space for a new GPT table (primary and secondary)
            tableType = PartitionTableType.GPT;
            var entriesSectors = (PartitionConstants.GptNumPartitionEntries * PartitionConstants.GptPartitionEntrySize + sectorSize - 1) / sectorSize;
            usableStart = (2 + entriesSectors) * (long)sectorSize;
            usableEnd = disk.Size - (entriesSectors + 1) * (long)sectorSize;
            maxSlots = PartitionConstants.GptNumPartitionEntries;
        }

        if (table != null && table.TableType is PartitionTableType.GPT or PartitionTableType.MBR)
        {
            await foreach (var p in table.EnumeratePartitions(cancel).ConfigureAwait(false))
            {
                existing.Add((p.StartOffset, p.Size));
                usedSlots.Add(p.PartitionNumber);
            }
        }

        // Exclude previously allocated (but not yet written) partitions
        foreach (var a in pending)
        {
            existing.Add((a.StartOffset, a.Size));
            usedSlots.Add(a.Slot);
        }

        var slot = Enumerable.Range(1, maxSlots).FirstOrDefault(s => !usedSlots.Contains(s));
        if (slot == 0)
            throw new UserInformationException($"Cannot create a partition on '{disk.DevicePath}': the {tableType} partition table has no free entries.", "DiskRestoreNoFreePartitionSlot");

        var start = FindFreeSpace(existing, usableStart, usableEnd, requiredSize, DefaultAlignment, preferredStart);
        if (start == null)
            throw new UserInformationException($"Cannot create a partition on '{disk.DevicePath}': the disk has no contiguous free space of {requiredSize} bytes.", "DiskRestoreNoFreeSpace");

        // MBR: the partition must fit in the 32-bit LBA fields
        if (tableType == PartitionTableType.MBR
            && (start / sectorSize > uint.MaxValue || requiredSize / sectorSize > uint.MaxValue))
            throw new UserInformationException($"Cannot create a partition on '{disk.DevicePath}': the partition does not fit within the MBR addressable range.", "DiskRestoreNoFreeSpace");

        Log.WriteInformationMessage(LOGTAG, "PartitionAllocated",
            $"Allocated {requiredSize} bytes at offset {start} for a new partition (slot {slot}, {tableType}) on {disk.DevicePath}");

        // Resolve the partition GUID once, so the primary and secondary GPT
        // entries written later carry the same unique partition GUID
        volumeGuid ??= Guid.NewGuid();

        return new AllocatedPartition(tableType, slot, start.Value, requiredSize, type, filesystemType, name, volumeGuid);
    }

    /// <summary>
    /// Writes previously allocated partitions into the target disk's partition table.
    /// When the disk has no recognizable partition table, a new GPT table containing
    /// the partitions is synthesized instead.
    /// </summary>
    /// <param name="disk">The target disk.</param>
    /// <param name="partitions">The partitions to write.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>An awaitable task.</returns>
    public static async Task WritePartitionsAsync(IRawDisk disk, IReadOnlyList<AllocatedPartition> partitions, CancellationToken cancel)
    {
        if (partitions.Count == 0)
            return;

        using var table = await PartitionTableFactory.CreateAsync(disk, cancel).ConfigureAwait(false);
        var tableType = table?.TableType ?? PartitionTableType.Unknown;

        if (tableType == PartitionTableType.Unknown)
        {
            // Blank disk: synthesize a new GPT table containing all partitions
            await SynthesizeNewTableAsync(disk, partitions, cancel).ConfigureAwait(false);
            return;
        }

        foreach (var partition in partitions.OrderBy(p => p.Slot))
        {
            if (partition.TableType != tableType)
                throw new InvalidOperationException($"The partition was allocated for a {partition.TableType} table, but the disk '{disk.DevicePath}' has a {tableType} table.");

            if (tableType == PartitionTableType.MBR)
                await WriteMbrEntryAsync(disk, partition, cancel).ConfigureAwait(false);
            else if (tableType == PartitionTableType.GPT)
                await WriteGptEntryAsync(disk, partition, cancel).ConfigureAwait(false);
            else
                throw new NotSupportedException($"Cannot add a partition to a {tableType} partition table.");

            Log.WriteInformationMessage(LOGTAG, "PartitionCreated",
                $"Created partition in slot {partition.Slot} ({tableType}) on {disk.DevicePath}, offset: {partition.StartOffset}, size: {partition.Size} bytes");
        }
    }

    /// <summary>
    /// Writes an MBR partition entry into an MBR sector buffer, preserving all
    /// existing content (boot code, disk signature, other entries).
    /// </summary>
    /// <param name="mbrSector">The buffer containing the MBR sector; must be at least 512 bytes.</param>
    /// <param name="sectorSize">The disk's sector size in bytes.</param>
    /// <param name="slot">The 1-based slot (1-4) to write the entry into; must be empty.</param>
    /// <param name="startOffset">The partition start offset in bytes.</param>
    /// <param name="size">The partition size in bytes.</param>
    /// <param name="filesystemType">The filesystem type of the partition content.</param>
    /// <param name="type">The partition type.</param>
    /// <returns><c>true</c> if the entry was written; <c>false</c> if the slot is occupied, invalid, or the values exceed the 32-bit LBA fields.</returns>
    public static bool TryWriteMbrEntry(byte[] mbrSector, int sectorSize, int slot, long startOffset, long size, FileSystemType filesystemType, PartitionType type)
    {
        if (mbrSector.Length < PartitionConstants.MbrSize)
            throw new ArgumentException($"The buffer must be at least {PartitionConstants.MbrSize} bytes.", nameof(mbrSector));

        if (slot < 1 || slot > PartitionConstants.MaxMbrPartitionEntries)
            return false;

        var startLba = startOffset / sectorSize;
        var sizeInSectors = size / sectorSize;
        if (startLba > uint.MaxValue || sizeInSectors > uint.MaxValue || sizeInSectors == 0)
            return false;

        var offset = 446 + (slot - 1) * PartitionConstants.MbrPartitionEntrySize;

        // The slot must be empty (type byte 0)
        if (mbrSector[offset + 4] != 0)
            return false;

        // Status byte (not bootable)
        mbrSector[offset] = 0x00;

        // CHS start/end: set to 0xFF (invalid), modern systems use LBA
        mbrSector[offset + 1] = 0xFF;
        mbrSector[offset + 2] = 0xFF;
        mbrSector[offset + 3] = 0xFF;
        mbrSector[offset + 5] = 0xFF;
        mbrSector[offset + 6] = 0xFF;
        mbrSector[offset + 7] = 0xFF;

        // Partition type byte
        mbrSector[offset + 4] = MbrPartitionTypes.ToTypeByte(filesystemType, type);

        // Start LBA and size in sectors
        BinaryPrimitives.WriteUInt32LittleEndian(mbrSector.AsSpan(offset + 8, 4), (uint)startLba);
        BinaryPrimitives.WriteUInt32LittleEndian(mbrSector.AsSpan(offset + 12, 4), (uint)sizeInSectors);

        return true;
    }

    /// <summary>
    /// Writes a GPT partition entry into a partition entries buffer.
    /// </summary>
    /// <param name="entriesBuffer">The buffer containing the GPT partition entries.</param>
    /// <param name="entrySize">The size of each partition entry in bytes (typically 128).</param>
    /// <param name="slot">The 1-based slot to write the entry into; must be empty.</param>
    /// <param name="startLba">The starting LBA of the partition.</param>
    /// <param name="endLba">The ending LBA of the partition (inclusive).</param>
    /// <param name="type">The partition type.</param>
    /// <param name="volumeGuid">The unique partition GUID, or null to generate a new one.</param>
    /// <param name="name">The partition name (max 36 UTF-16 characters).</param>
    /// <returns><c>true</c> if the entry was written; <c>false</c> if the slot is occupied or out of range.</returns>
    public static bool TryWriteGptEntry(Span<byte> entriesBuffer, int entrySize, int slot, long startLba, long endLba, PartitionType type, Guid? volumeGuid, string? name)
    {
        if (slot < 1 || (long)slot * entrySize > entriesBuffer.Length)
            return false;

        var offset = (slot - 1) * entrySize;
        var entry = entriesBuffer.Slice(offset, entrySize);

        // The slot must be empty (partition type GUID all zeros)
        if (entry.Slice(0, 16).IndexOfAnyExcept((byte)0) != -1)
            return false;

        // Partition type GUID (16 bytes)
        GptPartitionTypeGuids.ToGuid(type).ToByteArray().CopyTo(entry);

        // Unique partition GUID (16 bytes)
        (volumeGuid ?? Guid.NewGuid()).ToByteArray().CopyTo(entry.Slice(16));

        // Starting and ending LBA
        BinaryPrimitives.WriteInt64LittleEndian(entry.Slice(32, 8), startLba);
        BinaryPrimitives.WriteInt64LittleEndian(entry.Slice(40, 8), endLba);

        // Attributes (8 bytes) - default to 0
        BinaryPrimitives.WriteInt64LittleEndian(entry.Slice(48, 8), 0);

        // Partition name (72 bytes, UTF-16LE)
        var nameBytes = Encoding.Unicode.GetBytes(name ?? "");
        nameBytes.AsSpan(0, Math.Min(nameBytes.Length, 72)).CopyTo(entry.Slice(56));

        return true;
    }

    /// <summary>
    /// Updates the partition entries CRC32 in a GPT header and recomputes the
    /// header CRC32. Must be called after modifying the partition entries.
    /// </summary>
    /// <param name="headerSector">The sector containing the GPT header (first 92 bytes are used).</param>
    /// <param name="entriesBuffer">The partition entries buffer the CRC is computed over.</param>
    /// <param name="entriesByteSize">The exact size of the partition entries array in bytes (number of entries times entry size).</param>
    public static void PatchGptHeader(Span<byte> headerSector, byte[] entriesBuffer, int entriesByteSize)
    {
        // CRC32 of partition entries (offset 88)
        var entriesCrc = Crc32.Calculate(entriesBuffer, 0, entriesByteSize);
        BinaryPrimitives.WriteUInt32LittleEndian(headerSector.Slice(88, 4), entriesCrc);

        // Recompute header CRC32 (offset 16, calculated with the field zeroed)
        BinaryPrimitives.WriteUInt32LittleEndian(headerSector.Slice(16, 4), 0u);
        var headerCrc = Crc32.Calculate(headerSector.ToArray(), 0, PartitionConstants.GptHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(headerSector.Slice(16, 4), headerCrc);
    }

    /// <summary>
    /// Adds a partition entry to an existing MBR partition table, preserving the
    /// boot code and other entries.
    /// </summary>
    private static async Task WriteMbrEntryAsync(IRawDisk disk, AllocatedPartition partition, CancellationToken cancel)
    {
        var sectorSize = disk.SectorSize;

        // Read the full first sector (MBR), patch it, write it back
        var mbrSector = new byte[sectorSize];
        using (var stream = await disk.ReadBytesAsync(0, sectorSize, cancel).ConfigureAwait(false))
            await stream.ReadAtLeastAsync(mbrSector, sectorSize, cancellationToken: cancel).ConfigureAwait(false);

        var bootSignature = BinaryPrimitives.ReadUInt16LittleEndian(mbrSector.AsSpan(510, 2));
        if (bootSignature != PartitionConstants.MbrBootSignature)
            throw new InvalidOperationException($"The disk '{disk.DevicePath}' does not have a valid MBR boot signature.");

        if (!TryWriteMbrEntry(mbrSector, sectorSize, partition.Slot, partition.StartOffset, partition.Size, partition.FilesystemType, partition.Type))
            throw new InvalidOperationException($"Failed to add the partition to slot {partition.Slot} of the MBR on '{disk.DevicePath}': the slot is occupied or the partition exceeds the MBR limits. The disk may have been modified since the restore started.");

        await disk.WriteBytesAsync(0, mbrSector, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a partition entry to an existing GPT partition table, updating both the
    /// primary and secondary headers and entries along with their CRC32 checksums.
    /// The disk GUID and all other entries are preserved.
    /// </summary>
    private static async Task WriteGptEntryAsync(IRawDisk disk, AllocatedPartition partition, CancellationToken cancel)
    {
        var sectorSize = disk.SectorSize;

        // Read the primary GPT header sector (LBA 1)
        var headerSector = new byte[sectorSize];
        using (var stream = await disk.ReadBytesAsync(sectorSize, sectorSize, cancel).ConfigureAwait(false))
            await stream.ReadAtLeastAsync(headerSector, sectorSize, cancellationToken: cancel).ConfigureAwait(false);

        if (BinaryPrimitives.ReadInt64LittleEndian(headerSector.AsSpan(0, 8)) != PartitionConstants.GptSignature)
            throw new InvalidOperationException($"The disk '{disk.DevicePath}' does not have a valid GPT header.");

        var backupLba = BinaryPrimitives.ReadInt64LittleEndian(headerSector.AsSpan(32, 8));
        var entriesLba = BinaryPrimitives.ReadInt64LittleEndian(headerSector.AsSpan(72, 8));
        var numEntries = BinaryPrimitives.ReadUInt32LittleEndian(headerSector.AsSpan(80, 4));
        var entrySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerSector.AsSpan(84, 4));

        // The header is re-read from disk here (not taken from the parsed table),
        // so validate the entry parameters before sizing buffers from them
        if (entrySize < PartitionConstants.GptPartitionEntrySize || numEntries == 0 || (long)numEntries * entrySize > int.MaxValue)
            throw new InvalidOperationException($"The GPT header on '{disk.DevicePath}' has invalid partition entry parameters (entries: {numEntries}, entry size: {entrySize}).");

        var entriesByteSize = (int)(numEntries * entrySize);
        var entriesSectors = (entriesByteSize + sectorSize - 1) / sectorSize;
        var entriesBufferSize = entriesSectors * sectorSize;

        var startLba = partition.StartOffset / sectorSize;
        var endLba = startLba + (partition.Size / sectorSize) - 1;

        // Patch the primary partition entries
        var primaryEntries = new byte[entriesBufferSize];
        using (var stream = await disk.ReadBytesAsync(entriesLba * sectorSize, entriesBufferSize, cancel).ConfigureAwait(false))
            await stream.ReadAtLeastAsync(primaryEntries, entriesBufferSize, cancellationToken: cancel).ConfigureAwait(false);

        if (!TryWriteGptEntry(primaryEntries, entrySize, partition.Slot, startLba, endLba, partition.Type, partition.VolumeGuid, partition.Name))
            throw new InvalidOperationException($"Failed to add the partition to slot {partition.Slot} of the GPT on '{disk.DevicePath}': the slot is occupied. The disk may have been modified since the restore started.");

        await disk.WriteBytesAsync(entriesLba * sectorSize, primaryEntries, cancel).ConfigureAwait(false);

        // Patch the primary header (entries CRC + header CRC)
        PatchGptHeader(headerSector, primaryEntries, entriesByteSize);
        await disk.WriteBytesAsync(sectorSize, headerSector, cancel).ConfigureAwait(false);

        // Read the secondary GPT header to locate the secondary entries
        var secondaryHeader = new byte[sectorSize];
        using (var stream = await disk.ReadBytesAsync(backupLba * sectorSize, sectorSize, cancel).ConfigureAwait(false))
            await stream.ReadAtLeastAsync(secondaryHeader, sectorSize, cancellationToken: cancel).ConfigureAwait(false);

        if (BinaryPrimitives.ReadInt64LittleEndian(secondaryHeader.AsSpan(0, 8)) != PartitionConstants.GptSignature)
        {
            Log.WriteWarningMessage(LOGTAG, "SecondaryGptInvalid", null,
                $"The secondary GPT header on '{disk.DevicePath}' is invalid; only the primary partition table was updated.");
            return;
        }

        var secondaryEntriesLba = BinaryPrimitives.ReadInt64LittleEndian(secondaryHeader.AsSpan(72, 8));

        // Patch the secondary partition entries
        var secondaryEntries = new byte[entriesBufferSize];
        using (var stream = await disk.ReadBytesAsync(secondaryEntriesLba * sectorSize, entriesBufferSize, cancel).ConfigureAwait(false))
            await stream.ReadAtLeastAsync(secondaryEntries, entriesBufferSize, cancellationToken: cancel).ConfigureAwait(false);

        TryWriteGptEntry(secondaryEntries, entrySize, partition.Slot, startLba, endLba, partition.Type, partition.VolumeGuid, partition.Name);
        await disk.WriteBytesAsync(secondaryEntriesLba * sectorSize, secondaryEntries, cancel).ConfigureAwait(false);

        // Patch the secondary header (entries CRC + header CRC)
        PatchGptHeader(secondaryHeader, secondaryEntries, entriesByteSize);
        await disk.WriteBytesAsync(backupLba * sectorSize, secondaryHeader, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Synthesizes a new GPT partition table containing the given partitions and
    /// writes it to a blank disk (primary and secondary).
    /// </summary>
    private static async Task SynthesizeNewTableAsync(IRawDisk disk, IReadOnlyList<AllocatedPartition> partitions, CancellationToken cancel)
    {
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry
            {
                DevicePath = disk.DevicePath,
                Size = disk.Size,
                SectorSize = disk.SectorSize,
                Sectors = disk.Sectors,
                TableType = PartitionTableType.GPT
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.GPT,
                SectorSize = disk.SectorSize,
                HasProtectiveMbr = true,
                HeaderSize = PartitionConstants.GptHeaderSize
            },
            Partitions = partitions
                .OrderBy(p => p.Slot)
                .Select(p => new PartitionGeometry
                {
                    Number = p.Slot,
                    Type = p.Type,
                    StartOffset = p.StartOffset,
                    Size = p.Size,
                    Name = p.Name,
                    FilesystemType = p.FilesystemType,
                    VolumeGuid = p.VolumeGuid,
                    TableType = PartitionTableType.GPT
                })
                .ToList()
        };

        var tableData = PartitionTableSynthesizer.SynthesizePartitionTable(metadata)
            ?? throw new InvalidOperationException("Failed to synthesize a GPT partition table for the blank disk.");

        await disk.WriteBytesAsync(0, tableData, cancel).ConfigureAwait(false);
        await PartitionTableSynthesizer.WriteSecondaryGPTAsync(disk, metadata, tableData, cancel).ConfigureAwait(false);

        Log.WriteInformationMessage(LOGTAG, "PartitionTableCreated",
            $"Wrote a new GPT partition table with {partitions.Count} partition(s) to blank disk {disk.DevicePath}");
    }

    /// <summary>
    /// Rounds the given value up to the next multiple of the alignment.
    /// </summary>
    private static long AlignUp(long value, long alignment)
        => alignment <= 1 ? value : ((value + alignment - 1) / alignment) * alignment;
}
