// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.General;
using Microsoft.Win32.SafeHandles;
using static Duplicati.Proprietary.DiskImage.General.Alignment;

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// The layout of a dynamic Virtual Hard Disk v2 (VHDX) file, as described in
/// [MS-VHDX]. New files are laid out as:
/// <list type="bullet">
/// <item>0: the file type identifier,</item>
/// <item>64 KiB and 128 KiB: the two headers,</item>
/// <item>192 KiB and 256 KiB: the two region tables,</item>
/// <item>1 MiB: an empty log (1 MiB),</item>
/// <item>2 MiB: the metadata region (1 MiB),</item>
/// <item>3 MiB: the block allocation table (BAT),</item>
/// <item>after the BAT: the allocated 2 MiB payload blocks, each 1 MiB aligned.</item>
/// </list>
/// The BAT is kept in memory and written by <see cref="ImageLayout.Commit"/>.
/// Existing dynamic files can be written as well, as long as their log is empty;
/// differencing files are not supported.
/// </summary>
internal sealed class VhdxImageLayout : DynamicImageLayout
{
    private const long MiB = 1024 * 1024;
    private const int HeaderSize = 4096;
    private const int RegionTableSize = 64 * 1024;
    private const int MetadataTableSize = 64 * 1024;

    private const long Header1Offset = 64 * 1024;
    private const long Header2Offset = 128 * 1024;
    private const long RegionTable1Offset = 192 * 1024;
    private const long RegionTable2Offset = 256 * 1024;
    private const long LogOffset = 1 * MiB;
    private const uint LogLength = (uint)(1 * MiB);
    private const long MetadataOffset = 2 * MiB;
    private const uint MetadataLength = (uint)(1 * MiB);
    private const long DefaultBatOffset = 3 * MiB;

    /// <summary>
    /// The block size used for new files.
    /// </summary>
    private const long DefaultBlockSize = 2 * MiB;

    /// <summary>
    /// The largest virtual disk a VHDX file can describe (64 TiB).
    /// </summary>
    private const long VhdxMaxSize = 64L * 1024 * 1024 * MiB;

    /// <summary>
    /// The physical sector size reported for new files.
    /// </summary>
    private const uint DefaultPhysicalSectorSize = 4096;

    private const ulong PayloadBlockFullyPresent = 6;
    private const ulong PayloadBlockPartiallyPresent = 7;
    private const ulong BatStateMask = 7;

    private const uint MetadataIsVirtualDisk = 2;
    private const uint MetadataIsRequired = 4;
    private const uint RegionIsRequired = 1;

    /// <summary>
    /// The signature that starts the file type identifier.
    /// </summary>
    public static ReadOnlySpan<byte> FileIdentifierSignature => "vhdxfile"u8;
    private static ReadOnlySpan<byte> HeaderSignature => "head"u8;
    private static ReadOnlySpan<byte> RegionTableSignature => "regi"u8;
    private static ReadOnlySpan<byte> MetadataTableSignature => "metadata"u8;

    private static readonly Guid BatRegionGuid = new("2DC27766-F623-4200-9D64-115E9BFD4A08");
    private static readonly Guid MetadataRegionGuid = new("8B7CA206-4790-4B9A-B8FE-575F050F886E");
    private static readonly Guid FileParametersGuid = new("CAA16737-FA36-4D43-B3B6-33F0AA44E76B");
    private static readonly Guid VirtualDiskSizeGuid = new("2FA54224-CD1B-4876-B211-5DBED83BF4B8");
    private static readonly Guid Page83DataGuid = new("BECA12AB-B2E6-4523-93EF-C309E000C746");
    private static readonly Guid LogicalSectorSizeGuid = new("8141BF1D-A96F-4709-BA47-F233A8FAAB5F");
    private static readonly Guid PhysicalSectorSizeGuid = new("CDA348C7-445D-4471-9CC9-E9885251C556");

    private readonly long m_blockSize;

    /// <summary>
    /// The blocks that were allocated when an existing file was opened.
    /// </summary>
    private readonly bool[]? m_preexisting;

    /// <summary>
    /// The current header of an existing file, and the offset it was read from.
    /// </summary>
    private byte[]? m_existingHeader;
    private long m_existingHeaderOffset;

    private readonly Guid m_page83 = Guid.NewGuid();

    /// <summary>
    /// The file offset of each payload block, or -1 if the block is not allocated.
    /// </summary>
    private volatile long[] m_blocks;

    /// <summary>
    /// The number of payload blocks per sector bitmap block, which decides where
    /// the sector bitmap entries are interleaved in the BAT.
    /// </summary>
    private long m_chunkRatio;

    /// <summary>
    /// The file offset of the BAT, or -1 if it is not placed yet.
    /// </summary>
    private long m_batOffset;

    /// <summary>
    /// The number of bytes reserved for the BAT.
    /// </summary>
    private long m_batCapacity;

    /// <summary>
    /// The file offset where the next allocated block goes.
    /// </summary>
    private long m_nextFree;

    private VhdxImageLayout(bool isNew, long size, int sectorSize, long blockSize, long[] blocks, long batOffset, long batCapacity, long nextFree, byte[]? existingHeader, long existingHeaderOffset, bool[]? preexisting)
        : base(isNew, size, sectorSize)
    {
        m_blockSize = blockSize;
        m_blocks = blocks;
        m_chunkRatio = GetChunkRatio(sectorSize, blockSize);
        m_batOffset = batOffset;
        m_batCapacity = batCapacity;
        m_nextFree = nextFree;
        m_existingHeader = existingHeader;
        m_existingHeaderOffset = existingHeaderOffset;
        m_preexisting = preexisting;
    }

    /// <summary>
    /// Creates the layout for a new file.
    /// </summary>
    /// <returns>The layout.</returns>
    public static VhdxImageLayout CreateNew()
        => new(isNew: true, size: 0, ImageFile.DefaultSectorSize, DefaultBlockSize, [], batOffset: -1, batCapacity: 0, nextFree: DefaultBatOffset, existingHeader: null, existingHeaderOffset: 0, preexisting: null);

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Vhdx;

    /// <inheritdoc />
    public override long MaxSize => VhdxMaxSize;

    /// <inheritdoc />
    protected override long BlockSize => m_blockSize;

    /// <inheritdoc />
    public override bool SupportsSectorSize(int sectorSize) => sectorSize is 512 or 4096;

    /// <summary>
    /// Gets the number of payload blocks covered by one sector bitmap block.
    /// </summary>
    private static long GetChunkRatio(int sectorSize, long blockSize)
        => (1L << 23) * sectorSize / blockSize;

    /// <summary>
    /// Gets the number of BAT entries, including the interleaved sector bitmap entries.
    /// </summary>
    private long GetBatEntryCount(long dataBlocks)
        => dataBlocks == 0 ? 0 : dataBlocks + (dataBlocks - 1) / m_chunkRatio;

    /// <inheritdoc />
    public override void Resize(SafeFileHandle handle, long size, int sectorSize)
    {
        if (!IsNew)
            throw new InvalidOperationException("The size of an existing VHDX file cannot be changed.");

        lock (m_lock)
        {
            var count = (size + m_blockSize - 1) / m_blockSize;
            var chunkRatio = GetChunkRatio(sectorSize, m_blockSize);

            // When blocks are already placed behind a BAT that is too small for
            // the new size, the BAT moves to the end of the file now, so a file
            // that cannot grow fails here, while the layout still describes the
            // smaller disk and can be committed
            var batSize = AlignUp((count == 0 ? 0 : count + (count - 1) / chunkRatio) * 8, MiB);
            if (m_batOffset >= 0 && batSize > m_batCapacity)
            {
                var batOffset = m_nextFree;
                var nextFree = batOffset + batSize;
                SetFileLength(handle, nextFree);

                m_batOffset = batOffset;
                m_batCapacity = batSize;
                m_nextFree = nextFree;
            }

            var blocks = m_blocks;
            if (count > blocks.Length)
            {
                var grown = new long[count];
                Array.Fill(grown, -1L);
                blocks.CopyTo(grown, 0);
                m_blocks = grown;
            }

            Size = size;
            SectorSize = sectorSize;
            m_chunkRatio = chunkRatio;
            MarkDirty();
        }
    }

    /// <inheritdoc />
    protected override long GetBlockOffset(long block)
    {
        // Read atomically, as 64-bit reads are not atomic on 32-bit runtimes and
        // the entry may be set concurrently by AllocateBlock
        var blocks = m_blocks;
        return block < blocks.Length ? Interlocked.Read(ref blocks[block]) : -1;
    }

    /// <inheritdoc />
    protected override bool IsPreexistingBlock(long block)
        => m_preexisting != null && block < m_preexisting.Length && m_preexisting[block];

    /// <inheritdoc />
    protected override long AllocateBlock(SafeFileHandle handle, long block)
    {
        // Reserve the BAT for the current disk size when the first block is
        // allocated; if the disk grows past that, Resize moves the BAT to the
        // end of the file
        if (m_batOffset < 0)
        {
            m_batOffset = DefaultBatOffset;
            m_batCapacity = AlignUp(GetBatEntryCount(m_blocks.Length) * 8, MiB);
            m_nextFree = m_batOffset + m_batCapacity;
        }

        // Grow the file, and for an existing file write the BAT entry (its BAT is
        // not rewritten on commit), before recording the block, so a failure
        // leaves the layout as it was
        var offset = m_nextFree;
        var nextFree = AlignUp(offset + m_blockSize, MiB);
        SetFileLength(handle, nextFree);

        if (!IsNew)
        {
            var entry = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(entry, GetBatEntry(offset));
            RandomAccess.Write(handle, entry, m_batOffset + GetBatIndex(block) * 8);
        }

        m_nextFree = nextFree;
        Interlocked.Exchange(ref m_blocks[block], offset);
        return offset;
    }

    /// <summary>
    /// Gets the BAT index of a payload block, which skips the interleaved sector bitmap entries.
    /// </summary>
    /// <param name="block">The payload block index.</param>
    /// <returns>The BAT index.</returns>
    private long GetBatIndex(long block)
        => block + block / m_chunkRatio;

    /// <summary>
    /// Gets the BAT entry of a fully present payload block.
    /// </summary>
    /// <param name="offset">The file offset of the block.</param>
    /// <returns>The BAT entry.</returns>
    private static ulong GetBatEntry(long offset)
        => ((ulong)(offset / MiB) << 20) | PayloadBlockFullyPresent;

    /// <inheritdoc />
    protected override void OnBeforeFirstWrite(SafeFileHandle handle)
    {
        // The specification requires the DataWriteGuid to change before the
        // first modification of an existing file's content
        if (!IsNew)
            WriteUpdatedHeader(handle);
    }

    /// <summary>
    /// Writes an updated header with new write GUIDs and the next sequence number
    /// to the slot that is not current, which makes it the current header. The
    /// current header stays valid until the new one is complete.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    private void WriteUpdatedHeader(SafeFileHandle handle)
    {
        var header = (byte[])m_existingHeader!.Clone();
        var sequence = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)) + 1;
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), sequence);
        Guid.NewGuid().TryWriteBytes(header.AsSpan(16)); // FileWriteGuid
        Guid.NewGuid().TryWriteBytes(header.AsSpan(32)); // DataWriteGuid
        SetChecksum(header);

        var offset = m_existingHeaderOffset == Header1Offset ? Header2Offset : Header1Offset;
        RandomAccess.Write(handle, header, offset);
        RandomAccess.FlushToDisk(handle);
        m_existingHeader = header;
        m_existingHeaderOffset = offset;
    }

    /// <inheritdoc />
    protected override void WriteMetadata(SafeFileHandle handle)
    {
        // An existing file's BAT entries are written as blocks are allocated,
        // and its header is updated before the first write; its regions and
        // metadata do not change
        if (!IsNew)
            return;

        var blocks = m_blocks;
        var batSize = AlignUp(GetBatEntryCount(blocks.Length) * 8, MiB);

        // The placement is worked out in locals and recorded only once it is
        // written, so a failed commit can be retried
        var batOffset = m_batOffset;
        var batCapacity = m_batCapacity;
        var nextFree = m_nextFree;

        if (batOffset < 0)
        {
            // No block was allocated; place the BAT after the metadata region
            batOffset = DefaultBatOffset;
            batCapacity = batSize;
            nextFree = Math.Max(nextFree, batOffset + batCapacity);
        }
        else if (batSize > batCapacity)
        {
            // The disk grew after blocks were placed behind the BAT; move it to the end
            batOffset = nextFree;
            batCapacity = batSize;
            nextFree = batOffset + batCapacity;
        }

        SetFileLength(handle, nextFree);

        var bat = new byte[batCapacity];
        for (var i = 0L; i < blocks.Length; i++)
            if (blocks[i] >= 0)
                BinaryPrimitives.WriteUInt64LittleEndian(bat.AsSpan((int)(GetBatIndex(i) * 8)), GetBatEntry(blocks[i]));
        RandomAccess.Write(handle, bat, batOffset);

        RandomAccess.Write(handle, CreateFileIdentifier(), 0);
        RandomAccess.Write(handle, CreateMetadataRegion(m_blockSize, Size, (uint)SectorSize, m_page83), MetadataOffset);

        var regionTable = CreateRegionTable(batOffset, (uint)batCapacity);
        RandomAccess.Write(handle, regionTable, RegionTable1Offset);
        RandomAccess.Write(handle, regionTable, RegionTable2Offset);

        var fileWriteGuid = Guid.NewGuid();
        var dataWriteGuid = Guid.NewGuid();
        RandomAccess.Write(handle, CreateHeader(1, fileWriteGuid, dataWriteGuid), Header1Offset);
        RandomAccess.Write(handle, CreateHeader(2, fileWriteGuid, dataWriteGuid), Header2Offset);

        m_batOffset = batOffset;
        m_batCapacity = batCapacity;
        m_nextFree = nextFree;
    }

    /// <summary>
    /// Writes the CRC-32C checksum of a structure into its checksum field (offset 4).
    /// </summary>
    /// <param name="data">The structure.</param>
    private static void SetChecksum(Span<byte> data)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data[4..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data[4..], Crc32C.Compute(data));
    }

    /// <summary>
    /// Checks the CRC-32C checksum of a structure with its checksum field at offset 4.
    /// </summary>
    /// <param name="data">The structure.</param>
    /// <returns><c>true</c> if the checksum matches.</returns>
    private static bool HasValidChecksum(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        var stored = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(4));
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(4), 0);
        return Crc32C.Compute(copy) == stored;
    }

    /// <summary>
    /// Creates the file type identifier.
    /// </summary>
    private static byte[] CreateFileIdentifier()
    {
        var data = new byte[64 * 1024];
        FileIdentifierSignature.CopyTo(data);
        Encoding.Unicode.GetBytes("Duplicati").CopyTo(data, 8);
        return data;
    }

    /// <summary>
    /// Creates a header with an empty log.
    /// </summary>
    /// <param name="sequenceNumber">The sequence number; the header with the highest valid sequence number is current.</param>
    /// <param name="fileWriteGuid">The file write GUID.</param>
    /// <param name="dataWriteGuid">The data write GUID.</param>
    private static byte[] CreateHeader(ulong sequenceNumber, Guid fileWriteGuid, Guid dataWriteGuid)
    {
        var header = new byte[HeaderSize];
        var span = header.AsSpan();
        HeaderSignature.CopyTo(span);
        BinaryPrimitives.WriteUInt64LittleEndian(span[8..], sequenceNumber);
        fileWriteGuid.TryWriteBytes(span[16..]);
        dataWriteGuid.TryWriteBytes(span[32..]);
        // LogGuid (offset 48) is zero: the log is empty
        BinaryPrimitives.WriteUInt16LittleEndian(span[64..], 0); // Log version
        BinaryPrimitives.WriteUInt16LittleEndian(span[66..], 1); // Version
        BinaryPrimitives.WriteUInt32LittleEndian(span[68..], LogLength);
        BinaryPrimitives.WriteUInt64LittleEndian(span[72..], LogOffset);
        SetChecksum(span);
        return header;
    }

    /// <summary>
    /// Creates the region table, listing the BAT and the metadata region.
    /// </summary>
    /// <param name="batOffset">The file offset of the BAT.</param>
    /// <param name="batLength">The length of the BAT region.</param>
    private static byte[] CreateRegionTable(long batOffset, uint batLength)
    {
        var table = new byte[RegionTableSize];
        var span = table.AsSpan();
        RegionTableSignature.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 2); // Entry count

        WriteRegionEntry(span[16..], BatRegionGuid, batOffset, batLength);
        WriteRegionEntry(span[48..], MetadataRegionGuid, MetadataOffset, MetadataLength);
        SetChecksum(span);
        return table;

        static void WriteRegionEntry(Span<byte> entry, Guid guid, long offset, uint length)
        {
            guid.TryWriteBytes(entry);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], (ulong)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[24..], length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[28..], RegionIsRequired);
        }
    }

    /// <summary>
    /// Creates the start of the metadata region: the metadata table and the items.
    /// </summary>
    /// <param name="blockSize">The block size in bytes.</param>
    /// <param name="size">The size of the virtual disk in bytes.</param>
    /// <param name="logicalSectorSize">The logical sector size in bytes.</param>
    /// <param name="page83">The SCSI page 83 identifier of the disk.</param>
    private static byte[] CreateMetadataRegion(long blockSize, long size, uint logicalSectorSize, Guid page83)
    {
        var data = new byte[MetadataTableSize + 40];
        var span = data.AsSpan();
        MetadataTableSignature.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], 5); // Entry count

        // The items follow the 64 KiB table
        var item = MetadataTableSize;
        var entry = 32;
        AddItem(span, ref entry, ref item, FileParametersGuid, 8, MetadataIsRequired, s =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s, (uint)blockSize);
            BinaryPrimitives.WriteUInt32LittleEndian(s[4..], 0); // Not LeaveBlocksAllocated, no parent
        });
        AddItem(span, ref entry, ref item, VirtualDiskSizeGuid, 8, MetadataIsVirtualDisk | MetadataIsRequired,
            s => BinaryPrimitives.WriteUInt64LittleEndian(s, (ulong)size));
        AddItem(span, ref entry, ref item, Page83DataGuid, 16, MetadataIsVirtualDisk | MetadataIsRequired,
            s => page83.TryWriteBytes(s));
        AddItem(span, ref entry, ref item, LogicalSectorSizeGuid, 4, MetadataIsVirtualDisk | MetadataIsRequired,
            s => BinaryPrimitives.WriteUInt32LittleEndian(s, logicalSectorSize));
        AddItem(span, ref entry, ref item, PhysicalSectorSizeGuid, 4, MetadataIsVirtualDisk | MetadataIsRequired,
            s => BinaryPrimitives.WriteUInt32LittleEndian(s, Math.Max(DefaultPhysicalSectorSize, logicalSectorSize)));

        return data;

        static void AddItem(Span<byte> region, ref int entry, ref int item, Guid id, int length, uint flags, SpanAction write)
        {
            id.TryWriteBytes(region[entry..]);
            BinaryPrimitives.WriteUInt32LittleEndian(region[(entry + 16)..], (uint)item);
            BinaryPrimitives.WriteUInt32LittleEndian(region[(entry + 20)..], (uint)length);
            BinaryPrimitives.WriteUInt32LittleEndian(region[(entry + 24)..], flags);
            write(region.Slice(item, length));
            entry += 32;
            item += length;
        }
    }

    /// <summary>
    /// Writes a metadata item value.
    /// </summary>
    private delegate void SpanAction(Span<byte> span);

    /// <summary>
    /// Opens the layout of an existing VHDX file.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="path">The path of the file, for messages.</param>
    /// <param name="length">The length of the file.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="UserInformationException">Thrown if the file is damaged or of a type that cannot be written.</exception>
    public static VhdxImageLayout Open(SafeFileHandle handle, string path, long length)
    {
        UserInformationException Damaged(string what)
            => new($"The VHDX file '{path}' has {what}.", "DiskImageFileDamaged");

        // The current header is the valid one with the highest sequence number
        byte[]? header = null;
        long headerOffset = 0;
        foreach (var offset in new[] { Header1Offset, Header2Offset })
        {
            var candidate = new byte[HeaderSize];
            ReadExactly(handle, candidate, offset);
            if (!candidate.AsSpan(0, 4).SequenceEqual(HeaderSignature) || !HasValidChecksum(candidate))
                continue;
            if (header == null || BinaryPrimitives.ReadUInt64LittleEndian(candidate.AsSpan(8)) > BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8)))
            {
                header = candidate;
                headerOffset = offset;
            }
        }

        if (header == null)
            throw Damaged("no valid header");
        if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(66)) != 1)
            throw Damaged("an unsupported version");
        if (header.AsSpan(48, 16).ContainsAnyExcept((byte)0))
            throw new UserInformationException($"The VHDX file '{path}' has changes in its log that have not been applied. Attach it once in Windows to apply them, then try again.", "DiskImageFileLogNotEmpty");

        // Region table: the first copy, or the second if the first is damaged
        var regions = new byte[RegionTableSize];
        ReadExactly(handle, regions, RegionTable1Offset);
        if (!regions.AsSpan(0, 4).SequenceEqual(RegionTableSignature) || !HasValidChecksum(regions))
        {
            ReadExactly(handle, regions, RegionTable2Offset);
            if (!regions.AsSpan(0, 4).SequenceEqual(RegionTableSignature) || !HasValidChecksum(regions))
                throw Damaged("no valid region table");
        }

        long batOffset = -1, batLength = 0, metadataOffset = -1;
        var regionCount = BinaryPrimitives.ReadUInt32LittleEndian(regions.AsSpan(8));
        if (regionCount > (RegionTableSize - 16) / 32)
            throw Damaged("an invalid region table");
        for (var i = 0; i < regionCount; i++)
        {
            var entry = regions.AsSpan(16 + i * 32, 32);
            var guid = new Guid(entry[..16]);
            var offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            var regionLength = BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]);
            if (guid == BatRegionGuid)
                (batOffset, batLength) = (offset, regionLength);
            else if (guid == MetadataRegionGuid)
                metadataOffset = offset;
            else if ((BinaryPrimitives.ReadUInt32LittleEndian(entry[28..]) & RegionIsRequired) != 0)
                throw new UserInformationException($"The VHDX file '{path}' uses a feature that is not supported (region {guid}).", "DiskImageFileUnsupported");
        }

        if (batOffset < 0 || metadataOffset < 0)
            throw Damaged("no block allocation table or metadata region");

        // Metadata
        var metadata = new byte[MetadataTableSize];
        ReadExactly(handle, metadata, metadataOffset);
        if (!metadata.AsSpan(0, 8).SequenceEqual(MetadataTableSignature))
            throw Damaged("an invalid metadata table");

        var items = new Dictionary<Guid, byte[]>();
        var itemCount = BinaryPrimitives.ReadUInt16LittleEndian(metadata.AsSpan(10));
        if (itemCount > (MetadataTableSize - 32) / 32)
            throw Damaged("an invalid metadata table");
        for (var i = 0; i < itemCount; i++)
        {
            var entry = metadata.AsSpan(32 + i * 32, 32);
            var id = new Guid(entry[..16]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            var itemLength = BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]);

            var known = id == FileParametersGuid || id == VirtualDiskSizeGuid || id == Page83DataGuid || id == LogicalSectorSizeGuid || id == PhysicalSectorSizeGuid;
            if (!known)
            {
                if ((flags & MetadataIsRequired) != 0)
                    throw new UserInformationException($"The VHDX file '{path}' uses a feature that is not supported (metadata item {id}).", "DiskImageFileUnsupported");
                continue;
            }

            if (itemLength > 64)
                throw Damaged("an invalid metadata item");
            var value = new byte[itemLength];
            ReadExactly(handle, value, metadataOffset + offset);
            items[id] = value;
        }

        if (!items.TryGetValue(FileParametersGuid, out var fileParameters) || fileParameters.Length < 8
            || !items.TryGetValue(VirtualDiskSizeGuid, out var sizeItem) || sizeItem.Length < 8
            || !items.TryGetValue(LogicalSectorSizeGuid, out var sectorItem) || sectorItem.Length < 4)
            throw Damaged("incomplete metadata");

        var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(fileParameters);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(fileParameters.AsSpan(4)) & 2) != 0)
            throw new UserInformationException($"The VHDX file '{path}' is a differencing disk, which cannot be restored into. Merge it into its parent first.", "DiskImageFileDifferencing");

        var size = (long)BinaryPrimitives.ReadUInt64LittleEndian(sizeItem);
        var sectorSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(sectorItem);
        if (blockSize < MiB || blockSize > 256 * MiB || (blockSize & (blockSize - 1)) != 0 || sectorSize is not (512 or 4096) || size <= 0 || size > VhdxMaxSize)
            throw Damaged("invalid metadata");

        // BAT
        var dataBlocks = (size + blockSize - 1) / blockSize;
        var chunkRatio = GetChunkRatio(sectorSize, blockSize);
        var entryCount = dataBlocks + (dataBlocks - 1) / chunkRatio;
        if (entryCount * 8 > batLength)
            throw Damaged("a block allocation table that is too small");

        var bat = new byte[entryCount * 8];
        ReadExactly(handle, bat, batOffset);
        var blocks = new long[dataBlocks];
        for (var i = 0L; i < dataBlocks; i++)
        {
            var entry = BinaryPrimitives.ReadUInt64LittleEndian(bat.AsSpan((int)((i + i / chunkRatio) * 8)));
            var state = entry & BatStateMask;
            if (state == PayloadBlockPartiallyPresent)
                throw new UserInformationException($"The VHDX file '{path}' is a differencing disk, which cannot be restored into. Merge it into its parent first.", "DiskImageFileDifferencing");
            blocks[i] = state == PayloadBlockFullyPresent ? (long)(entry >> 20) * MiB : -1;
        }

        var preexisting = blocks.Select(b => b >= 0).ToArray();
        return new VhdxImageLayout(isNew: false, size, sectorSize, blockSize, blocks, batOffset, batLength, AlignUp(length, MiB), header, headerOffset, preexisting);
    }
}
