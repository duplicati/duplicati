// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Microsoft.Win32.SafeHandles;
using static Duplicati.Proprietary.DiskImage.General.Alignment;

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// The layout of a dynamic Virtual Hard Disk (VHD) file, as described in the
/// "Virtual Hard Disk Image Format Specification" (version 1.0). The file is:
/// <list type="bullet">
/// <item>a copy of the footer (512 bytes),</item>
/// <item>the dynamic disk header (1024 bytes),</item>
/// <item>the block allocation table (BAT), one big-endian sector offset per block,</item>
/// <item>the allocated blocks, each a sector bitmap followed by the block data,</item>
/// <item>the footer (512 bytes).</item>
/// </list>
/// New files use 2 MiB blocks with the data aligned to 4 KiB. The tables are
/// kept in memory and written by <see cref="ImageLayout.Commit"/>. Existing
/// dynamic files can be written as well; fixed VHD files are handled by
/// <see cref="RawImageLayout"/> and differencing files are not supported.
/// </summary>
internal sealed class VhdImageLayout : DynamicImageLayout
{
    /// <summary>
    /// The size of the footer in bytes.
    /// </summary>
    public const int FooterSize = 512;

    /// <summary>
    /// The size of the dynamic disk header in bytes.
    /// </summary>
    private const int DynamicHeaderSize = 1024;

    /// <summary>
    /// The size of the sectors the format addresses, in bytes.
    /// </summary>
    private const int VhdSectorSize = 512;

    /// <summary>
    /// The block size used for new files.
    /// </summary>
    private const int DefaultBlockSize = 2 * 1024 * 1024;

    /// <summary>
    /// The alignment of block data in new files.
    /// </summary>
    private const int DataAlignment = 4096;

    /// <summary>
    /// The largest virtual disk a VHD file can describe (2040 GiB).
    /// </summary>
    private const long VhdMaxSize = 2040L * 1024 * 1024 * 1024;

    /// <summary>
    /// The BAT value of a block that is not allocated.
    /// </summary>
    private const uint UnusedEntry = 0xFFFFFFFF;

    private const uint DiskTypeFixed = 2;
    private const uint DiskTypeDynamic = 3;
    private const uint DiskTypeDifferencing = 4;

    /// <summary>
    /// The cookie that starts the footer.
    /// </summary>
    public static ReadOnlySpan<byte> FooterCookie => "conectix"u8;

    /// <summary>
    /// The cookie that starts the dynamic disk header.
    /// </summary>
    private static ReadOnlySpan<byte> DynamicHeaderCookie => "cxsparse"u8;

    /// <summary>
    /// The time VHD timestamps count from.
    /// </summary>
    private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly long m_blockSize;
    private readonly int m_bitmapSize;

    /// <summary>
    /// The footer of an existing file, which is written back unchanged.
    /// </summary>
    private readonly byte[]? m_existingFooter;

    /// <summary>
    /// The blocks that were allocated when an existing file was opened.
    /// </summary>
    private readonly bool[]? m_preexisting;

    private readonly Guid m_uniqueId = Guid.NewGuid();
    private readonly uint m_timestamp = (uint)(DateTime.UtcNow - Epoch).TotalSeconds;

    /// <summary>
    /// The BAT, holding the sector offset of each block's sector bitmap.
    /// </summary>
    private volatile uint[] m_bat;

    /// <summary>
    /// The file offset of the BAT, or -1 if it is not placed yet.
    /// </summary>
    private long m_tableOffset;

    /// <summary>
    /// The number of entries the space reserved for the BAT can hold.
    /// </summary>
    private long m_tableCapacity;

    /// <summary>
    /// The file offset where the sector bitmap of the next allocated block goes.
    /// </summary>
    private long m_nextFree;

    /// <summary>
    /// The end of the data and tables in the file, where the footer goes.
    /// </summary>
    private long m_dataEnd;

    private VhdImageLayout(bool isNew, long size, long blockSize, uint[] bat, long tableOffset, long tableCapacity, long dataEnd, byte[]? existingFooter, bool[]? preexisting)
        : base(isNew, size, VhdSectorSize)
    {
        m_blockSize = blockSize;
        // One bit per sector, padded to a whole sector
        m_bitmapSize = (int)AlignUp((blockSize / VhdSectorSize + 7) / 8, VhdSectorSize);
        m_bat = bat;
        m_tableOffset = tableOffset;
        m_tableCapacity = tableCapacity;
        m_dataEnd = dataEnd;
        m_nextFree = NextBlockPosition(dataEnd);
        m_existingFooter = existingFooter;
        m_preexisting = preexisting;
    }

    /// <summary>
    /// Creates the layout for a new file.
    /// </summary>
    /// <returns>The layout.</returns>
    public static VhdImageLayout CreateNew()
        => new(isNew: true, size: 0, DefaultBlockSize, [], tableOffset: -1, tableCapacity: 0, dataEnd: FooterSize + DynamicHeaderSize, existingFooter: null, preexisting: null);

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Vhd;

    /// <inheritdoc />
    public override long MaxSize => VhdMaxSize;

    /// <inheritdoc />
    protected override long BlockSize => m_blockSize;

    /// <inheritdoc />
    public override bool SupportsSectorSize(int sectorSize) => sectorSize == VhdSectorSize;

    /// <inheritdoc />
    public override void Resize(SafeFileHandle handle, long size, int sectorSize)
    {
        if (!IsNew)
            throw new InvalidOperationException("The size of an existing VHD file cannot be changed.");

        lock (m_lock)
        {
            var entries = (size + m_blockSize - 1) / m_blockSize;

            // When blocks are already placed behind a BAT that is too small for
            // the new size, the BAT moves to the end of the file now, so a file
            // that cannot grow fails here, while the layout still describes the
            // smaller disk and can be committed
            if (m_tableOffset >= 0 && entries > m_tableCapacity)
            {
                var tableOffset = AlignUp(m_dataEnd, VhdSectorSize);
                var tableCapacity = AlignUp(entries * 4, VhdSectorSize) / 4;
                var dataEnd = tableOffset + tableCapacity * 4;
                CheckFileLength(AlignUp(dataEnd, VhdSectorSize) + FooterSize);
                SetFileLength(handle, dataEnd);

                m_tableOffset = tableOffset;
                m_tableCapacity = tableCapacity;
                m_dataEnd = dataEnd;
                m_nextFree = NextBlockPosition(dataEnd);
            }

            var bat = m_bat;
            if (entries > bat.Length)
            {
                var grown = new uint[entries];
                Array.Fill(grown, UnusedEntry);
                bat.CopyTo(grown, 0);
                m_bat = grown;
            }

            Size = size;
            SectorSize = sectorSize;
            MarkDirty();
        }
    }

    /// <inheritdoc />
    protected override long GetBlockOffset(long block)
    {
        var bat = m_bat;
        if (block >= bat.Length || bat[block] == UnusedEntry)
            return -1;

        return (long)bat[block] * VhdSectorSize + m_bitmapSize;
    }

    /// <inheritdoc />
    protected override bool IsPreexistingBlock(long block)
        => m_preexisting != null && block < m_preexisting.Length && m_preexisting[block];

    /// <inheritdoc />
    protected override long AllocateBlock(SafeFileHandle handle, long block)
    {
        // Reserve the BAT for the current disk size right after the headers when
        // the first block is allocated; if the disk grows past that, Resize
        // moves the BAT to the end of the file
        if (m_tableOffset < 0)
        {
            m_tableOffset = FooterSize + DynamicHeaderSize;
            m_tableCapacity = AlignUp(m_bat.Length * 4L, VhdSectorSize) / 4;
            m_dataEnd = m_tableOffset + m_tableCapacity * 4;
            m_nextFree = NextBlockPosition(m_dataEnd);
        }

        var bitmapOffset = m_nextFree;
        if (bitmapOffset / VhdSectorSize >= UnusedEntry)
            throw new IOException("The VHD file has grown beyond the size its block table can address.");

        var dataOffset = bitmapOffset + m_bitmapSize;
        var dataEnd = dataOffset + m_blockSize;

        if (IsNew)
        {
            SetFileLength(handle, dataEnd);
        }
        else
        {
            // Keep an existing file a valid VHD while the restore runs: move the
            // footer behind the new block before the block overwrites the old one
            // Writing past the end extends the file and places the footer in one step
            var footerOffset = AlignUp(dataEnd, VhdSectorSize);
            CheckFileLength(footerOffset + FooterSize);
            RandomAccess.Write(handle, m_existingFooter!, footerOffset);
        }

        // Mark every sector of the block as present; unwritten parts of the block
        // are zeros, which is what they read as
        var bitmap = new byte[m_bitmapSize];
        Array.Fill(bitmap, (byte)0xFF);
        RandomAccess.Write(handle, bitmap, bitmapOffset);

        m_dataEnd = dataEnd;
        m_nextFree = NextBlockPosition(m_dataEnd);
        m_bat[block] = (uint)(bitmapOffset / VhdSectorSize);

        // An existing file gets the BAT entry right away, so the block is found
        // even if the restore stops before the layout is committed
        if (!IsNew)
        {
            var entry = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(entry, m_bat[block]);
            RandomAccess.Write(handle, entry, m_tableOffset + block * 4);
        }

        return dataOffset;
    }

    /// <summary>
    /// Gets the position of the sector bitmap of a block placed after the given
    /// offset, so that the block data is aligned to <see cref="DataAlignment"/>.
    /// </summary>
    /// <param name="end">The end of the used part of the file.</param>
    /// <returns>The file offset of the next block's sector bitmap.</returns>
    private long NextBlockPosition(long end)
        => AlignUp(end + m_bitmapSize, DataAlignment) - m_bitmapSize;

    /// <inheritdoc />
    protected override void OnExistingBlockRead(SafeFileHandle handle, long block, long offsetInBlock, Span<byte> data)
    {
        // In a block of an existing file, sectors without their bit set read as zeros
        var bitmap = new byte[m_bitmapSize];
        lock (m_lock)
            ReadExactly(handle, bitmap, (long)m_bat[block] * VhdSectorSize);

        var first = offsetInBlock / VhdSectorSize;
        var last = (offsetInBlock + data.Length - 1) / VhdSectorSize;
        for (var sector = first; sector <= last; sector++)
        {
            if ((bitmap[sector / 8] & (0x80 >> (int)(sector % 8))) != 0)
                continue;

            var start = Math.Max(sector * VhdSectorSize, offsetInBlock) - offsetInBlock;
            var end = Math.Min((sector + 1) * VhdSectorSize, offsetInBlock + data.Length) - offsetInBlock;
            data[(int)start..(int)end].Clear();
        }
    }

    /// <inheritdoc />
    protected override void OnExistingBlockWritten(SafeFileHandle handle, long block, long offsetInBlock, int length)
    {
        // In a block of an existing file, sectors without their bit set read as
        // zeros, so the written sectors must be marked as present
        lock (m_lock)
        {
            var bitmapOffset = (long)m_bat[block] * VhdSectorSize;
            var bitmap = new byte[m_bitmapSize];
            ReadExactly(handle, bitmap, bitmapOffset);

            var changed = false;
            var first = offsetInBlock / VhdSectorSize;
            var last = (offsetInBlock + length - 1) / VhdSectorSize;
            for (var sector = first; sector <= last; sector++)
            {
                var mask = (byte)(0x80 >> (int)(sector % 8));
                if ((bitmap[sector / 8] & mask) == 0)
                {
                    bitmap[sector / 8] |= mask;
                    changed = true;
                }
            }

            if (changed)
                RandomAccess.Write(handle, bitmap, bitmapOffset);
        }
    }

    /// <inheritdoc />
    protected override void WriteMetadata(SafeFileHandle handle)
    {
        var bat = m_bat;

        // The placement is worked out in locals and recorded only once it is
        // written, so a failed commit can be retried
        var tableOffset = m_tableOffset;
        var tableCapacity = m_tableCapacity;
        var dataEnd = m_dataEnd;

        if (tableOffset < 0)
        {
            // No block was allocated; place the BAT right after the headers
            tableOffset = FooterSize + DynamicHeaderSize;
            tableCapacity = AlignUp(bat.Length * 4L, VhdSectorSize) / 4;
            dataEnd = Math.Max(dataEnd, tableOffset + tableCapacity * 4);
        }
        else if (bat.Length > tableCapacity)
        {
            // The disk grew after blocks were placed behind the BAT; move it to the end
            tableOffset = AlignUp(dataEnd, VhdSectorSize);
            tableCapacity = AlignUp(bat.Length * 4L, VhdSectorSize) / 4;
            dataEnd = tableOffset + tableCapacity * 4;
        }

        var footerOffset = AlignUp(dataEnd, VhdSectorSize);
        CheckFileLength(footerOffset + FooterSize);

        var table = new byte[tableCapacity * 4];
        Array.Fill(table, (byte)0xFF);
        for (var i = 0; i < bat.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(i * 4), bat[i]);
        RandomAccess.Write(handle, table, tableOffset);

        var footer = m_existingFooter ?? CreateFooter(Size, m_uniqueId, m_timestamp);
        if (IsNew)
        {
            RandomAccess.Write(handle, footer, 0);
            RandomAccess.Write(handle, CreateDynamicHeader(tableOffset, (uint)bat.Length, (uint)m_blockSize), FooterSize);
        }

        SetFileLength(handle, footerOffset + FooterSize);
        RandomAccess.Write(handle, footer, footerOffset);

        m_tableOffset = tableOffset;
        m_tableCapacity = tableCapacity;
        m_dataEnd = dataEnd;
        m_nextFree = NextBlockPosition(dataEnd);
    }

    /// <summary>
    /// Creates the footer of a new dynamic VHD file.
    /// </summary>
    /// <param name="size">The size of the virtual disk in bytes.</param>
    /// <param name="uniqueId">The unique ID of the disk.</param>
    /// <param name="timestamp">The creation time, in seconds since 2000-01-01 UTC.</param>
    /// <returns>The footer.</returns>
    internal static byte[] CreateFooter(long size, Guid uniqueId, uint timestamp)
    {
        var footer = new byte[FooterSize];
        var span = footer.AsSpan();
        FooterCookie.CopyTo(span);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x00000002); // Features: reserved bit, always set
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 0x00010000); // File format version 1.0
        BinaryPrimitives.WriteUInt64BigEndian(span[16..], FooterSize); // Offset of the dynamic disk header
        BinaryPrimitives.WriteUInt32BigEndian(span[24..], timestamp);

        // The creator application decides how readers determine the disk size:
        // Virtual PC and older QEMU versions use the CHS geometry, which rounds
        // the size down, while Hyper-V ("win ") images use the current size
        // field. The exact size is needed, as the backup GPT header is in the
        // last sector of the disk.
        "win "u8.CopyTo(span[28..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[32..], 0x000A0000); // Creator version
        "Wi2k"u8.CopyTo(span[36..]); // Creator host OS
        BinaryPrimitives.WriteUInt64BigEndian(span[40..], (ulong)size); // Original size
        BinaryPrimitives.WriteUInt64BigEndian(span[48..], (ulong)size); // Current size

        var (cylinders, heads, sectorsPerTrack) = GetGeometry(size);
        BinaryPrimitives.WriteUInt16BigEndian(span[56..], cylinders);
        span[58] = heads;
        span[59] = sectorsPerTrack;

        BinaryPrimitives.WriteUInt32BigEndian(span[60..], DiskTypeDynamic);
        uniqueId.TryWriteBytes(span[68..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[64..], Checksum(span, 64));
        return footer;
    }

    /// <summary>
    /// Creates the dynamic disk header of a new file.
    /// </summary>
    /// <param name="tableOffset">The file offset of the BAT.</param>
    /// <param name="maxTableEntries">The number of blocks of the disk.</param>
    /// <param name="blockSize">The block size in bytes.</param>
    /// <returns>The dynamic disk header.</returns>
    private static byte[] CreateDynamicHeader(long tableOffset, uint maxTableEntries, uint blockSize)
    {
        var header = new byte[DynamicHeaderSize];
        var span = header.AsSpan();
        DynamicHeaderCookie.CopyTo(span);
        BinaryPrimitives.WriteUInt64BigEndian(span[8..], ulong.MaxValue); // Data offset: unused
        BinaryPrimitives.WriteUInt64BigEndian(span[16..], (ulong)tableOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span[24..], 0x00010000); // Header version 1.0
        BinaryPrimitives.WriteUInt32BigEndian(span[28..], maxTableEntries);
        BinaryPrimitives.WriteUInt32BigEndian(span[32..], blockSize);
        BinaryPrimitives.WriteUInt32BigEndian(span[36..], Checksum(span, 36));
        return header;
    }

    /// <summary>
    /// Computes the checksum of a footer or header: the one's complement of the
    /// sum of all bytes, with the checksum field itself counted as zero.
    /// </summary>
    /// <param name="data">The footer or header.</param>
    /// <param name="checksumOffset">The offset of the 4-byte checksum field.</param>
    /// <returns>The checksum.</returns>
    internal static uint Checksum(ReadOnlySpan<byte> data, int checksumOffset)
    {
        uint sum = 0;
        for (var i = 0; i < data.Length; i++)
            if (i < checksumOffset || i >= checksumOffset + 4)
                sum += data[i];
        return ~sum;
    }

    /// <summary>
    /// Computes the CHS geometry for a disk size, using the algorithm from the
    /// VHD specification.
    /// </summary>
    /// <param name="size">The disk size in bytes.</param>
    /// <returns>The cylinders, heads and sectors per track.</returns>
    internal static (ushort Cylinders, byte Heads, byte SectorsPerTrack) GetGeometry(long size)
    {
        var totalSectors = size / VhdSectorSize;
        if (totalSectors > 65535L * 16 * 255)
            totalSectors = 65535L * 16 * 255;

        long sectorsPerTrack;
        long heads;
        long cylinderTimesHeads;
        if (totalSectors >= 65535L * 16 * 63)
        {
            sectorsPerTrack = 255;
            heads = 16;
            cylinderTimesHeads = totalSectors / sectorsPerTrack;
        }
        else
        {
            sectorsPerTrack = 17;
            cylinderTimesHeads = totalSectors / sectorsPerTrack;
            heads = Math.Max(4, (cylinderTimesHeads + 1023) / 1024);

            if (cylinderTimesHeads >= heads * 1024 || heads > 16)
            {
                sectorsPerTrack = 31;
                heads = 16;
                cylinderTimesHeads = totalSectors / sectorsPerTrack;
            }

            if (cylinderTimesHeads >= heads * 1024)
            {
                sectorsPerTrack = 63;
                heads = 16;
                cylinderTimesHeads = totalSectors / sectorsPerTrack;
            }
        }

        return ((ushort)(cylinderTimesHeads / heads), (byte)heads, (byte)sectorsPerTrack);
    }

    /// <summary>
    /// Opens the layout of an existing VHD file.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="path">The path of the file, for messages.</param>
    /// <param name="length">The length of the file.</param>
    /// <param name="footer">The footer read from the end of the file.</param>
    /// <param name="sparse">Whether all-zero writes to a fixed VHD skip regions that read as zero.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="UserInformationException">Thrown if the file is damaged or of a type that cannot be written.</exception>
    public static ImageLayout Open(SafeFileHandle handle, string path, long length, byte[] footer, bool sparse)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(footer.AsSpan(64)) != Checksum(footer, 64))
            throw new UserInformationException($"The VHD file '{path}' has a damaged footer.", "DiskImageFileDamaged");

        var diskType = BinaryPrimitives.ReadUInt32BigEndian(footer.AsSpan(60));
        var size = (long)BinaryPrimitives.ReadUInt64BigEndian(footer.AsSpan(48));

        if (diskType == DiskTypeFixed)
        {
            if (size > length - FooterSize)
                throw new UserInformationException($"The fixed VHD file '{path}' is shorter than the disk it describes.", "DiskImageFileDamaged");
            return new RawImageLayout(ImageFormat.Vhd, isNew: false, size, sparse, VhdSectorSize);
        }

        if (diskType == DiskTypeDifferencing)
            throw new UserInformationException($"The VHD file '{path}' is a differencing disk, which cannot be restored into. Merge it into its parent first.", "DiskImageFileDifferencing");

        if (diskType != DiskTypeDynamic)
            throw new UserInformationException($"The VHD file '{path}' has an unknown disk type ({diskType}).", "DiskImageFileDamaged");

        var headerOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(footer.AsSpan(16));
        var header = new byte[DynamicHeaderSize];
        ReadExactly(handle, header, headerOffset);
        if (!header.AsSpan(0, DynamicHeaderCookie.Length).SequenceEqual(DynamicHeaderCookie)
            || BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(36)) != Checksum(header, 36))
            throw new UserInformationException($"The VHD file '{path}' has a damaged dynamic disk header.", "DiskImageFileDamaged");

        var tableOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16));
        var entries = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(28));
        var blockSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(32));
        if (blockSize < VhdSectorSize || (blockSize & (blockSize - 1)) != 0
            || size <= 0 || size > VhdMaxSize
            || entries < (size + blockSize - 1) / blockSize
            || entries > (size + blockSize - 1) / blockSize + 1024)
            throw new UserInformationException($"The VHD file '{path}' has an invalid dynamic disk header.", "DiskImageFileDamaged");

        var table = new byte[entries * 4L];
        ReadExactly(handle, table, tableOffset);
        var bat = new uint[entries];
        for (var i = 0; i < entries; i++)
            bat[i] = BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(i * 4));

        var preexisting = bat.Select(e => e != UnusedEntry).ToArray();

        // New blocks are placed where the footer is, and the footer is written after them
        return new VhdImageLayout(isNew: false, size, blockSize, bat, tableOffset, entries, dataEnd: length - FooterSize, existingFooter: footer, preexisting: preexisting);
    }
}
