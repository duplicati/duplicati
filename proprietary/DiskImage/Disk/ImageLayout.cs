// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Microsoft.Win32.SafeHandles;
using static Duplicati.Proprietary.DiskImage.General.Alignment;

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// The file formats an image file can be written in.
/// </summary>
internal enum ImageFormat
{
    /// <summary>A raw (dd-style) image, where the file holds the disk bytes as they are.</summary>
    Raw,

    /// <summary>A Virtual Hard Disk (VHD) image.</summary>
    Vhd,

    /// <summary>A Virtual Hard Disk v2 (VHDX) image.</summary>
    Vhdx
}

/// <summary>
/// Maps the virtual disk of an image file onto the file itself. The raw layout
/// stores the disk as it is; the VHD and VHDX layouts store allocated blocks
/// and the tables that locate them.
/// </summary>
internal abstract class ImageLayout
{
    /// <summary>
    /// The size of the virtual disk in bytes.
    /// </summary>
    private long m_size;

    /// <summary>
    /// Whether the layout has changes that are not yet written to the file.
    /// </summary>
    private volatile bool m_dirty;

    /// <summary>
    /// Serializes changes to the layout's tables.
    /// </summary>
    protected readonly Lock m_lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageLayout"/> class.
    /// </summary>
    /// <param name="isNew">Whether the layout describes a new file.</param>
    /// <param name="size">The initial size of the virtual disk in bytes.</param>
    /// <param name="sectorSize">The initial sector size in bytes.</param>
    protected ImageLayout(bool isNew, long size, int sectorSize)
    {
        IsNew = isNew;
        m_size = size;
        SectorSize = sectorSize;
    }

    /// <summary>
    /// Gets the format of the file.
    /// </summary>
    public abstract ImageFormat Format { get; }

    /// <summary>
    /// Gets the largest virtual disk the format can hold, in bytes.
    /// </summary>
    public abstract long MaxSize { get; }

    /// <summary>
    /// Gets whether the layout describes a file created by this restore.
    /// </summary>
    public bool IsNew { get; }

    /// <summary>
    /// Gets the size of the virtual disk in bytes.
    /// </summary>
    public long Size
    {
        get => Interlocked.Read(ref m_size);
        protected set => Interlocked.Exchange(ref m_size, value);
    }

    /// <summary>
    /// Gets the sector size of the virtual disk in bytes.
    /// </summary>
    public int SectorSize { get; protected set; }

    /// <summary>
    /// Gets whether the layout has changes that are not yet written to the file.
    /// </summary>
    public bool IsDirty => m_dirty;

    /// <summary>
    /// Gets or sets the largest file the filesystem holding the image can store.
    /// </summary>
    public long MaxFileLength { get; set; } = long.MaxValue;

    /// <summary>
    /// Sets the length of the file, failing with a clear message when the
    /// filesystem holding it cannot store a file that large.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="length">The new file length.</param>
    /// <exception cref="UserInformationException">Thrown if the length exceeds <see cref="MaxFileLength"/>.</exception>
    protected void SetFileLength(SafeFileHandle handle, long length)
    {
        CheckFileLength(length);
        RandomAccess.SetLength(handle, length);
    }

    /// <summary>
    /// Fails with a clear message when the filesystem holding the file cannot
    /// store a file of the given length.
    /// </summary>
    /// <param name="length">The file length.</param>
    /// <exception cref="UserInformationException">Thrown if the length exceeds <see cref="MaxFileLength"/>.</exception>
    protected void CheckFileLength(long length)
    {
        if (length > MaxFileLength)
            throw new UserInformationException($"The image file has grown to {length} bytes, but the filesystem holding it cannot store files larger than {MaxFileLength} bytes. Restore the image to a folder on a different filesystem.", "DiskImageFileTooLargeForFilesystem");
    }

    /// <summary>
    /// Marks the layout as having changes that must be written by <see cref="Commit"/>.
    /// </summary>
    protected void MarkDirty() => m_dirty = true;

    /// <summary>
    /// Checks whether the format supports the given sector size.
    /// </summary>
    /// <param name="sectorSize">The sector size in bytes.</param>
    /// <returns><c>true</c> if the sector size is supported.</returns>
    public abstract bool SupportsSectorSize(int sectorSize);

    /// <summary>
    /// Sets the size and sector size of the virtual disk of a new file.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="size">The size of the virtual disk in bytes.</param>
    /// <param name="sectorSize">The sector size in bytes.</param>
    public abstract void Resize(SafeFileHandle handle, long size, int sectorSize);

    /// <summary>
    /// Reads from the virtual disk. Regions that are not stored in the file read as zeros.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="offset">The offset on the virtual disk.</param>
    /// <param name="destination">The buffer to read into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An awaitable task.</returns>
    public abstract ValueTask ReadAsync(SafeFileHandle handle, long offset, Memory<byte> destination, CancellationToken cancellationToken);

    /// <summary>
    /// Writes to the virtual disk.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="offset">The offset on the virtual disk.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An awaitable task.</returns>
    public abstract ValueTask WriteAsync(SafeFileHandle handle, long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the headers and tables that describe the virtual disk to the file.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    public void Commit(SafeFileHandle handle)
    {
        lock (m_lock)
        {
            WriteMetadata(handle);
            m_dirty = false;
        }
    }

    /// <summary>
    /// Writes the headers and tables that describe the virtual disk to the file.
    /// Called with the layout lock held.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    protected abstract void WriteMetadata(SafeFileHandle handle);

    /// <summary>
    /// Creates the layout for a new file.
    /// </summary>
    /// <param name="format">The file format.</param>
    /// <param name="sparse">Whether a raw image skips all-zero writes to regions that read as zero.</param>
    /// <returns>The layout.</returns>
    public static ImageLayout Create(ImageFormat format, bool sparse)
        => format switch
        {
            ImageFormat.Raw => new RawImageLayout(ImageFormat.Raw, isNew: true, size: 0, sparse: sparse, sectorSize: ImageFile.DefaultSectorSize),
            ImageFormat.Vhd => VhdImageLayout.CreateNew(),
            ImageFormat.Vhdx => VhdxImageLayout.CreateNew(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    /// <summary>
    /// Opens the layout of an existing file, detecting its format from the content.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="path">The path of the file, for messages.</param>
    /// <param name="sparse">Whether a raw image skips all-zero writes to regions that read as zero.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="UserInformationException">Thrown if the file uses a variant of the format that cannot be written.</exception>
    public static ImageLayout Open(SafeFileHandle handle, string path, bool sparse)
    {
        var length = RandomAccess.GetLength(handle);

        if (length >= VhdxImageLayout.FileIdentifierSignature.Length)
        {
            var signature = new byte[VhdxImageLayout.FileIdentifierSignature.Length];
            ReadExactly(handle, signature, 0);
            if (signature.AsSpan().SequenceEqual(VhdxImageLayout.FileIdentifierSignature))
                return VhdxImageLayout.Open(handle, path, length);
        }

        if (length >= VhdImageLayout.FooterSize)
        {
            var footer = new byte[VhdImageLayout.FooterSize];
            ReadExactly(handle, footer, length - VhdImageLayout.FooterSize);
            if (footer.AsSpan(0, VhdImageLayout.FooterCookie.Length).SequenceEqual(VhdImageLayout.FooterCookie))
                return VhdImageLayout.Open(handle, path, length, footer, sparse);
        }

        return new RawImageLayout(ImageFormat.Raw, isNew: false, size: length, sparse: sparse, sectorSize: DetectSectorSize(handle, length));
    }

    /// <summary>
    /// Detects the sector size of a raw disk image from the position of its GPT
    /// header, which is in the second sector. Images without a GPT header are
    /// assumed to have 512-byte sectors, as MBR disks almost always have.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="length">The length of the file.</param>
    /// <returns>The sector size in bytes.</returns>
    protected static int DetectSectorSize(SafeFileHandle handle, long length)
    {
        var signature = new byte[8];
        foreach (var sectorSize in new[] { 512, 4096 })
        {
            if (length < sectorSize * 2)
                break;

            ReadExactly(handle, signature, sectorSize);
            if (signature.AsSpan().SequenceEqual("EFI PART"u8))
                return sectorSize;
        }

        return ImageFile.DefaultSectorSize;
    }

    /// <summary>
    /// Reads exactly the length of the buffer from the file.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="fileOffset">The file offset to read from.</param>
    /// <exception cref="EndOfStreamException">Thrown if the file ends before the buffer is filled.</exception>
    protected static void ReadExactly(SafeFileHandle handle, Span<byte> buffer, long fileOffset)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = RandomAccess.Read(handle, buffer[total..], fileOffset + total);
            if (read == 0)
                throw new EndOfStreamException($"Unexpected end of file at offset {fileOffset + total}.");
            total += read;
        }
    }

    /// <summary>
    /// Reads from the file, filling the part of the buffer past the end of the file with zeros.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="fileOffset">The file offset to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An awaitable task.</returns>
    protected static async ValueTask ReadOrZeroAsync(SafeFileHandle handle, Memory<byte> buffer, long fileOffset, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await RandomAccess.ReadAsync(handle, buffer[total..], fileOffset + total, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                buffer.Span[total..].Clear();
                return;
            }
            total += read;
        }
    }
}

/// <summary>
/// A raw image layout, where the file holds the virtual disk as it is. Also used
/// for fixed VHD files, whose disk data is followed by a footer that is not changed.
/// </summary>
internal sealed class RawImageLayout : ImageLayout
{
    /// <summary>
    /// The size of the chunks used when checking whether a region of the image is all zero.
    /// </summary>
    private const int ZeroCheckChunkSize = 1024 * 1024;

    private readonly ImageFormat m_format;
    private readonly bool m_sparse;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawImageLayout"/> class.
    /// </summary>
    /// <param name="format">The reported format (<see cref="ImageFormat.Vhd"/> for fixed VHD files).</param>
    /// <param name="isNew">Whether the file is created by this restore.</param>
    /// <param name="size">The size of the virtual disk in bytes.</param>
    /// <param name="sparse">Whether all-zero writes to regions that read as zero are skipped.</param>
    /// <param name="sectorSize">The sector size of the virtual disk in bytes.</param>
    public RawImageLayout(ImageFormat format, bool isNew, long size, bool sparse, int sectorSize)
        : base(isNew, size, sectorSize)
    {
        m_format = format;
        m_sparse = sparse;
    }

    /// <inheritdoc />
    public override ImageFormat Format => m_format;

    /// <inheritdoc />
    public override long MaxSize => long.MaxValue;

    /// <inheritdoc />
    public override bool SupportsSectorSize(int sectorSize) => true;

    /// <inheritdoc />
    public override void Resize(SafeFileHandle handle, long size, int sectorSize)
    {
        if (!IsNew)
            throw new InvalidOperationException("The size of an existing image file cannot be changed.");

        lock (m_lock)
        {
            RandomAccess.SetLength(handle, size);
            Size = size;
            SectorSize = sectorSize;
        }
    }

    /// <inheritdoc />
    public override ValueTask ReadAsync(SafeFileHandle handle, long offset, Memory<byte> destination, CancellationToken cancellationToken)
        => ReadOrZeroAsync(handle, destination, offset, cancellationToken);

    /// <inheritdoc />
    public override async ValueTask WriteAsync(SafeFileHandle handle, long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        // Restores write every block, including the all-zero blocks of unused
        // disk space. Writing those would allocate the holes of a sparse image,
        // so they are skipped when the region already reads as zero. Reading a
        // hole does not touch the storage device, so the check is cheap, and it
        // is also correct for regions that were written before.
        if (m_sparse && !data.Span.ContainsAnyExcept((byte)0) && await IsRegionZeroAsync(handle, offset, data.Length, cancellationToken).ConfigureAwait(false))
            return;

        await RandomAccess.WriteAsync(handle, data, offset, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void WriteMetadata(SafeFileHandle handle)
    {
        // The raw layout has no metadata, and the footer of a fixed VHD is not changed
    }

    /// <summary>
    /// Checks whether a region of the image reads as all zero.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="offset">The offset of the region.</param>
    /// <param name="length">The length of the region.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if every byte in the region is zero.</returns>
    private static async Task<bool> IsRegionZeroAsync(SafeFileHandle handle, long offset, int length, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(length, ZeroCheckChunkSize));
        try
        {
            var position = 0;
            while (position < length)
            {
                var chunk = buffer.AsMemory(0, Math.Min(buffer.Length, length - position));
                var read = await RandomAccess.ReadAsync(handle, chunk, offset + position, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return false;
                if (chunk.Span[..read].ContainsAnyExcept((byte)0))
                    return false;
                position += read;
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>
/// Base class for image layouts that store the virtual disk in fixed-size blocks,
/// which are only allocated in the file when data other than zeros is written to
/// them. New blocks are appended to the file; reads from blocks that are not
/// allocated return zeros.
/// </summary>
internal abstract class DynamicImageLayout : ImageLayout
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicImageLayout"/> class.
    /// </summary>
    /// <param name="isNew">Whether the layout describes a new file.</param>
    /// <param name="size">The initial size of the virtual disk in bytes.</param>
    /// <param name="sectorSize">The initial sector size in bytes.</param>
    protected DynamicImageLayout(bool isNew, long size, int sectorSize)
        : base(isNew, size, sectorSize)
    {
    }

    /// <summary>
    /// Gets the size of a block in bytes.
    /// </summary>
    protected abstract long BlockSize { get; }

    /// <summary>
    /// Gets the file offset of the data of a block, or -1 if the block is not allocated.
    /// </summary>
    /// <param name="block">The block index.</param>
    /// <returns>The file offset, or -1.</returns>
    protected abstract long GetBlockOffset(long block);

    /// <summary>
    /// Allocates a block at the end of the file. Called with the layout lock held,
    /// and only for blocks that are not allocated.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="block">The block index.</param>
    /// <returns>The file offset of the block's data.</returns>
    protected abstract long AllocateBlock(SafeFileHandle handle, long block);

    /// <summary>
    /// Called after data is written to a block that was allocated before this restore.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="block">The block index.</param>
    /// <param name="offsetInBlock">The offset of the written data within the block.</param>
    /// <param name="length">The length of the written data.</param>
    protected virtual void OnExistingBlockWritten(SafeFileHandle handle, long block, long offsetInBlock, int length)
    {
    }

    /// <summary>
    /// Called after data is read from a block that was allocated before this
    /// restore, so formats can clear the parts that read as zeros.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="block">The block index.</param>
    /// <param name="offsetInBlock">The offset of the read data within the block.</param>
    /// <param name="data">The data that was read.</param>
    protected virtual void OnExistingBlockRead(SafeFileHandle handle, long block, long offsetInBlock, Span<byte> data)
    {
    }

    /// <summary>
    /// Whether <see cref="OnBeforeFirstWrite"/> has run.
    /// </summary>
    private bool m_firstWriteDone;

    /// <summary>
    /// Called once, with the layout lock held, before the first write to the file.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    protected virtual void OnBeforeFirstWrite(SafeFileHandle handle)
    {
    }

    /// <summary>
    /// Gets whether a block was allocated before this restore.
    /// </summary>
    /// <param name="block">The block index.</param>
    /// <returns><c>true</c> if the block was allocated when the file was opened.</returns>
    protected abstract bool IsPreexistingBlock(long block);

    /// <inheritdoc />
    public override async ValueTask ReadAsync(SafeFileHandle handle, long offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var position = 0;
        while (position < destination.Length)
        {
            var virtualOffset = offset + position;
            var block = virtualOffset / BlockSize;
            var offsetInBlock = virtualOffset % BlockSize;
            var length = (int)Math.Min(destination.Length - position, BlockSize - offsetInBlock);
            var segment = destination.Slice(position, length);

            var blockOffset = GetBlockOffset(block);
            if (blockOffset < 0)
            {
                segment.Span.Clear();
            }
            else
            {
                await ReadOrZeroAsync(handle, segment, blockOffset + offsetInBlock, cancellationToken).ConfigureAwait(false);
                if (IsPreexistingBlock(block))
                    OnExistingBlockRead(handle, block, offsetInBlock, segment.Span);
            }

            position += length;
        }
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(SafeFileHandle handle, long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var position = 0;
        while (position < data.Length)
        {
            var virtualOffset = offset + position;
            var block = virtualOffset / BlockSize;
            var offsetInBlock = virtualOffset % BlockSize;
            var length = (int)Math.Min(data.Length - position, BlockSize - offsetInBlock);
            var segment = data.Slice(position, length);
            position += length;

            var blockOffset = GetBlockOffset(block);
            if (blockOffset < 0 && !segment.Span.ContainsAnyExcept((byte)0))
            {
                // A block that is not allocated reads as zeros, so zeros need not be stored
                continue;
            }

            if (!Volatile.Read(ref m_firstWriteDone))
            {
                lock (m_lock)
                {
                    if (!m_firstWriteDone)
                    {
                        OnBeforeFirstWrite(handle);
                        Volatile.Write(ref m_firstWriteDone, true);
                    }
                }
            }

            if (blockOffset < 0)
            {
                lock (m_lock)
                {
                    blockOffset = GetBlockOffset(block);
                    if (blockOffset < 0)
                    {
                        blockOffset = AllocateBlock(handle, block);
                        MarkDirty();
                    }
                }
            }

            await RandomAccess.WriteAsync(handle, segment, blockOffset + offsetInBlock, cancellationToken).ConfigureAwait(false);

            if (IsPreexistingBlock(block))
                OnExistingBlockWritten(handle, block, offsetInBlock, length);
        }
    }
}
