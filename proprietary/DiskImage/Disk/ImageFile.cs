// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.DiskImage.General;
using Microsoft.Win32.SafeHandles;

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// Implementation of the <see cref="IRawDisk"/> interface backed by an image file:
/// a raw (dd-style) image, or a dynamic VHD or VHDX file. Used to restore disk
/// images to a file instead of a physical device. A new image starts out empty,
/// and is sized from the source disk geometry once that is known, see
/// <see cref="ApplyGeometry(long, int)"/>. How the disk is stored in the file is
/// decided by its <see cref="ImageLayout"/>.
/// </summary>
internal sealed partial class ImageFile : IRawDisk
{
    private static readonly string LOGTAG = Log.LogTagFromType<ImageFile>();

    /// <summary>
    /// The sector size used until the geometry of the source disk is known.
    /// </summary>
    internal const int DefaultSectorSize = 512;

    /// <summary>
    /// The largest file a FAT12/FAT16/FAT32 filesystem can hold.
    /// </summary>
    private const long FatMaxFileSize = uint.MaxValue;

    /// <summary>
    /// FSCTL_SET_SPARSE from winioctl.h, marks a file as sparse on NTFS and ReFS.
    /// </summary>
    private const uint FSCTL_SET_SPARSE = 0x000900C4;

    private readonly string m_devicePath;
    private readonly string m_filePath;
    private readonly bool m_createNew;
    private readonly bool m_sparse;
    private readonly ImageFormat? m_format;
    private readonly Lock m_lock = new();
    private SafeFileHandle? m_handle;
    private ImageLayout? m_layout;
    private bool m_initialized;
    private bool m_opened;
    private bool m_writeable;
    private bool m_disposed;
    private bool m_hasGeometry;
    private volatile bool m_hasWritten;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageFile"/> class.
    /// </summary>
    /// <param name="devicePath">The path that identifies the image in restore paths; this is the final image path, even when the data is written to a different file.</param>
    /// <param name="filePath">The path of the file the data is written to.</param>
    /// <param name="createNew">If <c>true</c>, the file is created (or truncated) when initialized; otherwise it must exist.</param>
    /// <param name="sparse">If <c>true</c>, a raw image is made sparse and all-zero writes to regions that already read as zero are skipped. VHD and VHDX files only store allocated blocks, and are never made sparse.</param>
    /// <param name="format">The format of a new file; for an existing file, the format it must have, or <c>null</c> to accept any.</param>
    public ImageFile(string devicePath, string filePath, bool createNew, bool sparse, ImageFormat? format)
    {
        if (createNew && format == null)
            throw new ArgumentNullException(nameof(format), "A new image file needs a format.");

        m_devicePath = devicePath;
        m_filePath = filePath;
        m_createNew = createNew;
        m_sparse = sparse;
        m_format = format;
    }

    /// <inheritdoc />
    public static string Prefix => string.Empty;

    /// <inheritdoc />
    public string DevicePath => m_devicePath;

    /// <summary>
    /// Gets the path of the file the data is written to.
    /// </summary>
    public string FilePath => m_filePath;

    /// <summary>
    /// Gets the format of the image file.
    /// </summary>
    public ImageFormat Format => Layout.Format;

    /// <summary>
    /// Gets whether the geometry of the disk is known; for a new image, whether
    /// the geometry of the source disk has been applied.
    /// </summary>
    public bool HasGeometry => m_hasGeometry;

    /// <inheritdoc />
    public bool IsWriteable => m_writeable;

    /// <summary>
    /// Gets whether the file was opened (or created) by this instance.
    /// </summary>
    public bool IsOpen => m_opened;

    /// <summary>
    /// Gets the layout of the initialized image.
    /// </summary>
    private ImageLayout Layout
        => m_initialized && m_layout != null ? m_layout : throw new InvalidOperationException("Disk not initialized.");

    /// <inheritdoc />
    public long Size => Layout.Size;

    /// <inheritdoc />
    public int SectorSize => Layout.SectorSize;

    /// <inheritdoc />
    public long Sectors => Size / SectorSize;

    /// <inheritdoc />
    public Task<bool> AutoUnmountAsync(CancellationToken cancellationToken)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<string?> InitializeAsync(CancellationToken cancellationToken)
        => InitializeAsync(false, cancellationToken);

    /// <inheritdoc />
    public Task<string?> InitializeAsync(bool enableWrite, CancellationToken cancellationToken)
    {
        if (m_initialized)
            return Task.FromResult<string?>(null);

        if (m_createNew && !enableWrite)
            return Task.FromResult<string?>($"Cannot create the image file {m_filePath} without write access.");

        try
        {
            if (m_createNew)
            {
                m_handle = File.OpenHandle(m_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess);
                m_layout = ImageLayout.Create(m_format!.Value, m_sparse);

                // Windows refuses to attach VHD and VHDX files that are sparse
                if (m_format == ImageFormat.Raw && m_sparse)
                    MarkSparse(m_handle);
            }
            else
            {
                m_handle = File.OpenHandle(m_filePath, FileMode.Open, enableWrite ? FileAccess.ReadWrite : FileAccess.Read, enableWrite ? FileShare.None : FileShare.Read, FileOptions.RandomAccess);
                m_layout = ImageLayout.Open(m_handle, m_filePath, m_sparse);
                m_hasGeometry = true;

                if (m_format != null && m_layout.Format != m_format)
                    throw new UserInformationException($"The image file '{m_filePath}' is a {m_layout.Format} image, not a {m_format} image.", "DiskImageFileFormatMismatch");
            }

            // Dynamic formats grow as blocks are allocated, so the limit of the
            // filesystem holding the image is checked when the file grows
            if (IsFatFilesystem(GetFilesystemFormat(m_filePath)))
                m_layout.MaxFileLength = FatMaxFileSize;

            m_writeable = enableWrite;
            m_initialized = true;
            m_opened = true;

            Log.WriteInformationMessage(LOGTAG, "ImageFileInitialized", $"Opened {m_layout.Format} image file {m_filePath}: Size={m_layout.Size}, SectorSize={m_layout.SectorSize}");
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            m_handle?.Dispose();
            m_handle = null;
            m_layout = null;

            if (ex is UserInformationException)
                throw;

            Log.WriteErrorMessage(LOGTAG, "ImageFileInitializeFailed", ex, $"Failed to open image file {m_filePath}");
            return Task.FromResult<string?>($"Failed to open image file {m_filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Sizes a new image from the source disk geometry. A new raw image that is not
    /// sparse is recreated with the space preallocated, where the platform supports it.
    /// </summary>
    /// <param name="size">The size of the source disk in bytes.</param>
    /// <param name="sectorSize">The sector size of the source disk in bytes.</param>
    /// <exception cref="UserInformationException">Thrown if the geometry is invalid or the image format or target filesystem cannot hold the disk.</exception>
    public void ApplyGeometry(long size, int sectorSize)
    {
        var layout = Layout;

        if (sectorSize < DefaultSectorSize || (sectorSize & (sectorSize - 1)) != 0)
            throw new UserInformationException($"The backup describes the source disk with an invalid sector size of {sectorSize} bytes.", "DiskImageFileInvalidSectorSize");

        if (size <= 0 || size % sectorSize != 0)
            throw new UserInformationException($"The backup describes the source disk with an invalid size of {size} bytes.", "DiskImageFileInvalidSize");

        if (!layout.SupportsSectorSize(sectorSize))
            throw new UserInformationException($"The source disk has {sectorSize} byte sectors, which the {layout.Format} format does not support. Restore to a raw or VHDX image instead.", "DiskImageFileSectorSizeNotSupported");

        CheckFormatLimit(layout, size);

        lock (m_lock)
        {
            // All restored data is laid out by sector; changing the sector size
            // after data is written would misplace it
            if (m_hasWritten && sectorSize != layout.SectorSize)
                throw new UserInformationException($"Cannot change the sector size of {m_filePath} from {layout.SectorSize} to {sectorSize} bytes after data has been written.", "DiskImageFileSectorSizeChanged");

            if (layout.Format == ImageFormat.Raw)
                CheckFilesystemLimits(size);
            else if (!m_sparse)
                Log.WriteVerboseMessage(LOGTAG, "ImageFilePreallocateIgnored", $"The {layout.Format} image {m_filePath} only stores allocated blocks, so no space is reserved up front.");

            try
            {
                if (layout.Format == ImageFormat.Raw && m_createNew && !m_sparse && !m_hasWritten)
                {
                    // Recreate the (still empty) file with the space reserved up front;
                    // when that fails, reopen it as it was, so the file is still owned
                    // and is cleaned up when the failed restore is disposed
                    m_handle!.Dispose();
                    try
                    {
                        m_handle = File.OpenHandle(m_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess, size);
                    }
                    catch
                    {
                        m_handle = File.OpenHandle(m_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess);
                        throw;
                    }
                }

                layout.Resize(m_handle!, size, sectorSize);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new UserInformationException($"Could not size the image file {m_filePath} to {size} bytes: {ex.Message}", "DiskImageFileSizeFailed", ex);
            }

            m_hasGeometry = true;
        }

        Log.WriteInformationMessage(LOGTAG, "ImageFileSized", $"Sized {layout.Format} image file {m_filePath} to {size} bytes with {sectorSize} byte sectors");
    }

    /// <summary>
    /// Grows the image to at least the given size. Used when the backup has no
    /// geometry metadata, and the disk is laid out around the restored partitions.
    /// </summary>
    /// <param name="size">The minimum size in bytes.</param>
    /// <exception cref="UserInformationException">Thrown if the image format or target filesystem cannot hold the disk.</exception>
    public void EnsureSize(long size)
    {
        var layout = Layout;

        lock (m_lock)
        {
            if (size <= layout.Size)
                return;

            CheckFormatLimit(layout, size);
            if (layout.Format == ImageFormat.Raw)
                CheckFilesystemLimits(size);

            try
            {
                layout.Resize(m_handle!, size, layout.SectorSize);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new UserInformationException($"Could not grow the image file {m_filePath} to {size} bytes: {ex.Message}", "DiskImageFileSizeFailed", ex);
            }
        }

        Log.WriteInformationMessage(LOGTAG, "ImageFileGrown", $"Grew image file {m_filePath} to {size} bytes");
    }

    /// <summary>
    /// Fails if the image format cannot hold a disk of the given size.
    /// </summary>
    /// <param name="layout">The image layout.</param>
    /// <param name="size">The disk size in bytes.</param>
    /// <exception cref="UserInformationException">Thrown if the disk is too large for the format.</exception>
    private void CheckFormatLimit(ImageLayout layout, long size)
    {
        if (size > layout.MaxSize)
            throw new UserInformationException($"The disk is {size} bytes, but the {layout.Format} format cannot hold disks larger than {layout.MaxSize} bytes. Restore to a raw or VHDX image instead.", "DiskImageFileTooLargeForFormat");
    }

    /// <summary>
    /// Fails if the filesystem holding the image cannot store a file of the given
    /// size, and warns if it cannot store sparse files.
    /// </summary>
    /// <param name="size">The size of the image in bytes.</param>
    /// <exception cref="UserInformationException">Thrown if the image is too large for the filesystem.</exception>
    private void CheckFilesystemLimits(long size)
    {
        var format = GetFilesystemFormat(m_filePath);
        if (string.IsNullOrWhiteSpace(format))
            return;

        var isFat = IsFatFilesystem(format);

        if (isFat && size > FatMaxFileSize)
            throw new UserInformationException($"The image file {m_filePath} would be {size} bytes, but the {format} filesystem it is stored on cannot hold files larger than {FatMaxFileSize} bytes. Restore the image to a folder on a different filesystem, or to a VHD or VHDX image, which only stores the used parts of the disk.", "DiskImageFileTooLargeForFilesystem");

        if (m_sparse && (isFat || format.Equals("exFAT", StringComparison.OrdinalIgnoreCase) || format.Equals("hfs", StringComparison.OrdinalIgnoreCase)))
            Log.WriteWarningMessage(LOGTAG, "ImageFileNotSparse", null, $"The {format} filesystem holding {m_filePath} does not support sparse files, so the image will use {size} bytes of disk space.");
    }

    /// <summary>
    /// Checks whether a filesystem format is FAT12/FAT16/FAT32, which cannot hold
    /// files of 4 GiB or more.
    /// </summary>
    /// <param name="format">The filesystem format, as reported by <see cref="DriveInfo.DriveFormat"/>.</param>
    /// <returns><c>true</c> for FAT filesystems.</returns>
    private static bool IsFatFilesystem(string? format)
        => format != null
            && (format.Equals("FAT", StringComparison.OrdinalIgnoreCase)
                || format.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
                || format.Equals("vfat", StringComparison.OrdinalIgnoreCase)
                || format.Equals("msdos", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the format of the filesystem that holds the given path, by finding the
    /// mounted drive with the longest root directory that contains the path.
    /// </summary>
    /// <param name="path">The path to look up.</param>
    /// <returns>The filesystem format (e.g. "NTFS", "ext4", "vfat"), or <c>null</c> if it could not be determined.</returns>
    private static string? GetFilesystemFormat(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            // Match whole path segments, so /mnt/a does not match /mnt/ab/disk.img
            static string AsFolder(string path)
                => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

            return DriveInfo.GetDrives()
                .Where(d => fullPath.StartsWith(AsFolder(d.RootDirectory.FullName), comparison))
                .OrderByDescending(d => d.RootDirectory.FullName.Length)
                .Select(d => d.DriveFormat)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LOGTAG, "ImageFileFilesystemUnknown", ex, $"Could not determine the filesystem holding {path}");
            return null;
        }
    }

    /// <summary>
    /// Marks the file as sparse. On Linux and macOS, extending a file leaves a
    /// hole without further action, but on Windows the file must be marked sparse
    /// first, or the extension is allocated and zero-filled.
    /// </summary>
    /// <param name="handle">The handle of the newly created file.</param>
    private void MarkSparse(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!SetSparse(handle))
            Log.WriteWarningMessage(LOGTAG, "ImageFileSetSparseFailed", null,
                $"Could not mark {m_filePath} as sparse ({Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}), so the image will use the full disk size on disk.");
    }

    /// <summary>
    /// Sends FSCTL_SET_SPARSE for the file handle.
    /// </summary>
    /// <param name="handle">The file handle.</param>
    /// <returns><c>true</c> if the file was marked sparse.</returns>
    [SupportedOSPlatform("windows")]
    private static bool SetSparse(SafeFileHandle handle)
        => DeviceIoControl(handle, FSCTL_SET_SPARSE, 0, 0, 0, 0, out _, 0);

    /// <summary>
    /// Writes the headers and tables of the image format, and flushes the written
    /// data to the storage device.
    /// </summary>
    public void Commit()
    {
        if (m_handle == null || m_layout == null || !m_writeable)
            return;

        // An existing image whose layout did not change keeps its headers and
        // tables as they are
        if (m_layout.IsNew || m_layout.IsDirty)
            m_layout.Commit(m_handle);
        RandomAccess.FlushToDisk(m_handle);
    }

    /// <inheritdoc />
    public Task<bool> FinalizeAsync(CancellationToken cancellationToken)
    {
        Commit();
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<Stream> ReadSectorsAsync(long startSector, int sectorCount, CancellationToken cancellationToken)
        => ReadBytesAsync(startSector * SectorSize, sectorCount * SectorSize, cancellationToken);

    /// <inheritdoc />
    public async Task<Stream> ReadBytesAsync(long offset, int length, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var bytesRead = await ReadBytesAsync(offset, buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            return new PooledMemoryStream(buffer, bytesRead);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ReadBytesAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var (handle, layout) = GetOpenFile();

        if (offset < 0 || offset + destination.Length > layout.Size)
            throw new InvalidOperationException($"The requested read would read beyond disk size: {offset} + {destination.Length} > {layout.Size}");

        await layout.ReadAsync(handle, offset, destination, cancellationToken).ConfigureAwait(false);
        return destination.Length;
    }

    /// <inheritdoc />
    public Task<int> WriteSectorsAsync(long startSector, byte[] data, CancellationToken cancellationToken)
        => WriteBytesAsync(startSector * SectorSize, data.AsMemory(), cancellationToken);

    /// <inheritdoc />
    public Task<int> WriteBytesAsync(long offset, byte[] data, CancellationToken cancellationToken)
        => WriteBytesAsync(offset, data.AsMemory(), cancellationToken);

    /// <inheritdoc />
    public async Task<int> WriteBytesAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var (handle, layout) = GetOpenFile();

        if (!m_writeable)
            throw new InvalidOperationException("Disk not opened for write access.");

        if (offset < 0 || offset + data.Length > layout.Size)
            throw new InvalidOperationException($"The requested write would write beyond disk size: {offset} + {data.Length} > {layout.Size}");

        if (data.Length == 0)
            return 0;

        m_hasWritten = true;
        await layout.WriteAsync(handle, offset, data, cancellationToken).ConfigureAwait(false);
        return data.Length;
    }

    /// <summary>
    /// Gets the handle and layout of the open image file.
    /// </summary>
    /// <returns>The file handle and layout.</returns>
    private (SafeFileHandle Handle, ImageLayout Layout) GetOpenFile()
    {
        var layout = Layout;
        var handle = m_handle ?? throw new ObjectDisposedException(nameof(ImageFile));
        return (handle, layout);
    }

    /// <inheritdoc />
    public static async IAsyncEnumerable<PhysicalDriveInfo> ListPhysicalDrivesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Image files are not discoverable drives
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_disposed)
            return;

        // Keep the file a valid image, also when the restore did not complete
        try
        {
            if (m_layout?.IsDirty == true)
                Commit();
            else if (m_handle != null && m_writeable)
                RandomAccess.FlushToDisk(m_handle);
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "ImageFileCommitFailed", ex, $"Failed to write the headers of image file {m_filePath}");
        }

        m_handle?.Dispose();
        m_handle = null;
        m_disposed = true;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, nint lpInBuffer, uint nInBufferSize, nint lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, nint lpOverlapped);
}
