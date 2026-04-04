// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Duplicati.Proprietary.DiskImage.General;

namespace Duplicati.Proprietary.DiskImage.Disk
{
    /// <summary>
    /// macOS implementation of the <see cref="IRawDisk"/> interface for raw disk access.
    /// Uses POSIX API calls via P/Invoke to read from and write to physical disk devices.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public partial class Mac : IRawDisk
    {
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Mac>();

        // Disk ioctl constants from <sys/disk.h>
        private const ulong DKIOCGETBLOCKSIZE = 0x4004_6418;  // _IOR('d', 24, uint32_t)
        private const ulong DKIOCGETBLOCKCOUNT = 0x4008_6419; // _IOR('d', 25, uint64_t)
        private const ulong DKIOCSYNCHRONIZECACHE = 0x2000_6416; // _IO('d', 22)

        // File open flags from <fcntl.h>
        private const int O_RDONLY = 0x0000;
        private const int O_RDWR = 0x0002;
        private const int O_SYNC = 0x0080;

        private readonly string m_devicePath;
        private int m_fileDescriptor = -1;
        private bool m_disposed = false;
        private bool m_initialized = false;
        private bool m_writeable = false;
        private uint m_sectorSize = 0;
        private long m_size = 0;
        private bool m_shouldFlush = false;
        private const string DEVICE_PREFIX = "/dev/";
        private readonly SemaphoreSlim m_ioLock = new(1, 1);

        // Aligned buffers for O_DIRECT I/O (must be sector-aligned and a multiple of sector size)
        private long m_allignedBufferSize = 0;
        unsafe private byte* m_allignedBufferPtr = null;

        /// <inheritdoc />
        public static string Prefix => "/dev/";

        /// <inheritdoc />
        public string DevicePath { get { return m_devicePath; } }

        /// <inheritdoc />
        public bool IsWriteable => m_writeable;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mac"/> class.
        /// </summary>
        /// <param name="devicePath">The macOS device path (e.g., "/dev/rdisk0").</param>
        /// <exception cref="PlatformNotSupportedException">Thrown when not running on macOS.</exception>
        public Mac(string devicePath)
        {
            if (!OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("macOS raw disk access is only supported on macOS platforms.");

            m_devicePath = NormalizeDevicePath(devicePath);
        }

        /// <summary>
        /// Normalizes the device path to use raw device access (/dev/rdiskN instead of /dev/diskN).
        /// </summary>
        /// <param name="devicePath">The device path to normalize.</param>
        /// <returns>The normalized device path using raw device access.</returns>
        private static string NormalizeDevicePath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
                return devicePath;

            // Convert /dev/diskN to /dev/rdiskN for raw/character device access
            if (devicePath.StartsWith($"{DEVICE_PREFIX}disk") && !devicePath.StartsWith($"{DEVICE_PREFIX}rdisk"))
            {
                return devicePath.Replace($"{DEVICE_PREFIX}disk", $"{DEVICE_PREFIX}rdisk");
            }

            return devicePath;
        }

        /// <inheritdoc />
        public long Size
        {
            get
            {
                if (!m_initialized)
                    throw new InvalidOperationException("Disk not initialized.");

                return m_size;
            }
        }

        /// <inheritdoc />
        public int SectorSize
        {
            get
            {
                if (!m_initialized)
                    throw new InvalidOperationException("Disk not initialized.");

                return (int)m_sectorSize;
            }
        }

        /// <inheritdoc />
        public int Sectors
        {
            get
            {
                if (!m_initialized)
                    throw new InvalidOperationException("Disk not initialized.");

                return (int)(m_size / m_sectorSize);
            }
        }

        /// <inheritdoc />
        public async Task<bool> AutoUnmountAsync(CancellationToken cancellationToken)
        {
            // Extract disk number from device path
            string blockDevicePath = m_devicePath.Replace($"{DEVICE_PREFIX}rdisk", $"{DEVICE_PREFIX}disk");

            // Use diskutil to unmount all volumes on the disk
            var unmount = await ProcessRunner.RunProcessAsync("diskutil", $"unmountDisk {blockDevicePath}", 30_000, cancellationToken);

            // diskutil returns 0 on success, but may also succeed with warnings
            return unmount.ExitCode == 0 || unmount.Output.Contains("successful");
        }

        /// <inheritdoc />
        public Task<bool> InitializeAsync(CancellationToken cancellationToken)
            => InitializeAsync(false, cancellationToken);

        /// <inheritdoc />
        public Task<bool> InitializeAsync(bool enableWrite, CancellationToken cancellationToken)
        {
            if (m_initialized)
                return Task.FromResult(true);

            // Open the device without O_DIRECT initially
            int flags = enableWrite ? O_RDWR : O_RDONLY;
            m_fileDescriptor = open(m_devicePath, flags);

            if (m_fileDescriptor < 0)
            {
                int errorCode = Marshal.GetLastWin32Error();
                string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode);
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to open device {m_devicePath}: {errorMessage} (errno: {errorCode})");
                return Task.FromResult(false);
            }

            // Use F_NOCACHE to bypass kernel cache (equivalent to O_DIRECT on Linux)
            if (fcntl_nocache(m_fileDescriptor) < 0)
            {
                int errorCode = Marshal.GetLastWin32Error();
                string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode);
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to set F_NOCACHE on device {m_devicePath}: {errorMessage} (errno: {errorCode})");
                close(m_fileDescriptor);
                m_fileDescriptor = -1;
                return Task.FromResult(false);
            }

            // Get disk geometry using ioctls
            try
            {
                // Get block size (sector size)
                uint blockSize = 0;
                if (ioctl_uint32(m_fileDescriptor, DKIOCGETBLOCKSIZE, ref blockSize) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    close(m_fileDescriptor);
                    m_fileDescriptor = -1;
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to get block size: {Marshal.GetPInvokeErrorMessage(errorCode)} (errno: {errorCode})");
                    return Task.FromResult(false);
                }
                m_sectorSize = blockSize;

                // Get block count
                ulong blockCount = 0;
                if (ioctl_uint64(m_fileDescriptor, DKIOCGETBLOCKCOUNT, ref blockCount) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    close(m_fileDescriptor);
                    m_fileDescriptor = -1;
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to get block count: {Marshal.GetPInvokeErrorMessage(errorCode)} (errno: {errorCode})");
                    return Task.FromResult(false);
                }

                m_size = (long)(blockCount * blockSize);
                m_writeable = enableWrite;
                m_initialized = true;

                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "initialize", $"Successfully initialized disk {m_devicePath}: Size={m_size}, SectorSize={m_sectorSize}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                close(m_fileDescriptor);
                m_fileDescriptor = -1;
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", ex, "Failed to initialize disk");
                return Task.FromResult(false);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_disposed)
                return;

            if (m_shouldFlush && m_fileDescriptor >= 0)
            {
                // Use DKIOCSYNCHRONIZECACHE on macOS to guarantee flush to physical media
                if (ioctl_no_arg(m_fileDescriptor, DKIOCSYNCHRONIZECACHE) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode);
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "dispose", null, $"Failed to flush data: {errorMessage} (errno: {errorCode})");
                }
            }

            if (m_fileDescriptor >= 0)
            {
                close(m_fileDescriptor);
                m_fileDescriptor = -1;
            }

            unsafe
            {
                if (m_allignedBufferPtr is not null)
                {
                    NativeMemory.AlignedFree(m_allignedBufferPtr);
                    m_allignedBufferPtr = null;
                    m_allignedBufferSize = 0;
                }
            }

            m_ioLock.Dispose();

            m_disposed = true;
        }

        /// <inheritdoc />
        public Task<bool> FinalizeAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public Task<Stream> ReadSectorsAsync(long startSector, int sectorCount, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            long offset = startSector * m_sectorSize;
            int length = sectorCount * (int)m_sectorSize;

            return ReadBytesAsync(offset, length, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Stream> ReadBytesAsync(long offset, int length, CancellationToken cancellationToken)
        {
            // Rent a pooled buffer and read directly into it
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                int bytesRead = await ReadBytesAsync(offset, buffer.AsMemory(0, length), cancellationToken);
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
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            if (m_fileDescriptor < 0)
                throw new InvalidOperationException("Device is not open.");

            int length = destination.Length;

            if (offset + length > Size)
                throw new InvalidOperationException($"The requested read would read beyond disk size: {offset} + {length} > {Size}");

            if (length == 0)
                return 0;

            // Calculate aligned offset and length for O_DIRECT I/O
            long alignedOffset = (offset / SectorSize) * SectorSize;
            long offsetDelta = offset - alignedOffset;
            long alignedLength = ((offsetDelta + length + SectorSize - 1) / SectorSize) * SectorSize;

            await m_ioLock.WaitAsync(cancellationToken);
            try
            {
                // Use pread for atomic position + read
                int totalBytesRead = 0;
                unsafe
                {
                    EnsureAllignedBuffer((int)alignedLength);

                    var bytesRead = pread(m_fileDescriptor, m_allignedBufferPtr, (nint)alignedLength, alignedOffset);

                    if (bytesRead.ToInt64() < 0)
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode);
                        throw new IOException($"Failed to read {alignedLength} bytes from disk at offset {alignedOffset}: {errorMessage} (errno: {errorCode})");
                    }

                    // Copy only the requested portion from the aligned buffer
                    int bytesToCopy = Math.Min(length, (int)(bytesRead.ToInt64() - offsetDelta));
                    if (bytesToCopy > 0)
                    {
                        var srcSpan = new ReadOnlySpan<byte>(m_allignedBufferPtr + offsetDelta, bytesToCopy);
                        srcSpan.CopyTo(destination.Span);
                    }
                    totalBytesRead = bytesToCopy;
                }

                return totalBytesRead;
            }
            finally
            {
                m_ioLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<int> WriteSectorsAsync(long startSector, byte[] data, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            if (!m_writeable)
                throw new InvalidOperationException("Disk not opened for write access.");

            long offset = startSector * m_sectorSize;
            return await WriteBytesAsync(offset, data, cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> WriteBytesAsync(long offset, byte[] data, CancellationToken cancellationToken)
            => WriteBytesAsync(offset, data.AsMemory(), cancellationToken);

        /// <inheritdoc />
        public async Task<int> WriteBytesAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            if (!m_writeable)
                throw new InvalidOperationException("Disk not opened for write access.");

            if (m_fileDescriptor < 0)
                throw new InvalidOperationException("Device is not open.");

            int dataLength = data.Length;

            if (offset + dataLength > Size)
                throw new InvalidOperationException($"The requested write would write beyond disk size: {offset} + {dataLength} > {Size}");

            if (dataLength == 0)
                return 0;

            // Calculate aligned offset and length for O_DIRECT I/O
            long alignedOffset = (offset / SectorSize) * SectorSize;
            long offsetDelta = offset - alignedOffset;
            long alignedLength = ((offsetDelta + dataLength + SectorSize - 1) / SectorSize) * SectorSize;

            await m_ioLock.WaitAsync(cancellationToken);
            try
            {
                // Ensure we have an aligned buffer for O_DIRECT writes
                EnsureAllignedBuffer((int)alignedLength);

                int totalBytesWritten = 0;
                unsafe
                {
                    // Check if this is an unaligned write (needs read-modify-write)
                    bool isUnaligned = offsetDelta != 0 || dataLength != alignedLength;

                    if (isUnaligned)
                    {
                        // Read existing data first (read-modify-write)
                        var bytesRead = pread(m_fileDescriptor, m_allignedBufferPtr, (nint)alignedLength, alignedOffset);
                        if (bytesRead.ToInt64() < 0)
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode);
                            throw new IOException($"Failed to read existing data for unaligned write at offset {alignedOffset}: {errorMessage} (errno: {errorCode})");
                        }
                    }

                    // Copy new data into the aligned buffer at the correct offset
                    var destSpan = new Span<byte>(m_allignedBufferPtr + offsetDelta, dataLength);
                    data.Span.CopyTo(destSpan);

                    // Write the aligned buffer
                    var bytesWritten = pwrite(m_fileDescriptor, m_allignedBufferPtr, (nint)alignedLength, alignedOffset);
                    totalBytesWritten = (int)bytesWritten.ToInt64();
                }

                if (totalBytesWritten < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode);
                    throw new IOException($"Failed to write {dataLength} bytes to disk at offset {offset}: {errorMessage} (errno: {errorCode})");
                }

                m_shouldFlush = true;
                return dataLength;
            }
            finally
            {
                m_ioLock.Release();
            }
        }

        #region P/Invoke Declarations

        [LibraryImport("runtimes/osx/native/libSystem_wrapper.dylib", SetLastError = true)]
        internal static partial int ioctl_uint32(int fd, ulong request, ref uint value);

        [LibraryImport("runtimes/osx/native/libSystem_wrapper.dylib", SetLastError = true)]
        internal static partial int ioctl_uint64(int fd, ulong request, ref ulong value);

        [LibraryImport("runtimes/osx/native/libSystem_wrapper.dylib", SetLastError = true)]
        internal static partial int ioctl_no_arg(int fd, ulong request);

        [LibraryImport("runtimes/osx/native/libSystem_wrapper.dylib", SetLastError = true)]
        internal static partial int fcntl_nocache(int fd);

        [LibraryImport("libSystem", SetLastError = true)]
        private static partial int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

        [LibraryImport("libSystem", SetLastError = true)]
        private static partial int close(int fd);

        [LibraryImport("libSystem", SetLastError = true)]
        private static unsafe partial IntPtr pread(int fd, byte* buf, IntPtr count, long offset);

        [LibraryImport("libSystem", SetLastError = true)]
        private static unsafe partial IntPtr pwrite(int fd, byte* buf, IntPtr count, long offset);

        #endregion

        unsafe
        private void EnsureAllignedBuffer(int requiredSize)
        {
            if (m_allignedBufferSize >= requiredSize)
                return;

            nuint alignedSize = (nuint)((requiredSize + SectorSize - 1) / SectorSize * SectorSize);
            // Free existing buffer if it exists
            if (m_allignedBufferPtr is not null)
            {
                m_allignedBufferPtr = (byte*)NativeMemory.AlignedRealloc(m_allignedBufferPtr, alignedSize, (nuint)m_sectorSize);
            }
            else
            {
                m_allignedBufferPtr = (byte*)NativeMemory.AlignedAlloc(alignedSize, (nuint)m_sectorSize);
            }
            m_allignedBufferSize = (long)alignedSize;
        }

        /// <inheritdoc />
        public static async IAsyncEnumerable<PhysicalDriveInfo> ListPhysicalDrivesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var list = await ProcessRunner.RunProcessAsync("diskutil", "list -plist", 30_000, cancellationToken);
            if (list.ExitCode != 0)
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "listphysicaldrives", null, $"Failed to list physical drives: {list.Error}");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(list.Output))
                yield break;

            XDocument plist;
            try
            {
                plist = XDocument.Parse(list.Output);
            }
            catch (Exception ex)
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "listphysicaldrives", ex, "Failed to parse diskutil list plist output");
                yield break;
            }

            // Parse the plist XML structure
            var rootDict = plist.Element("plist")?.Element("dict");
            if (rootDict == null)
                yield break;

            // Get AllDisksAndPartitions array
            var allDisksAndPartitions = PlistHelper.GetArrayElement(rootDict, "AllDisksAndPartitions");
            if (allDisksAndPartitions == null)
                yield break;

            foreach (var diskElement in allDisksAndPartitions.Elements("dict"))
            {
                var identifier = PlistHelper.GetStringValue(diskElement, "DeviceIdentifier");
                if (string.IsNullOrWhiteSpace(identifier))
                    continue;

                // Skip synthesized disks (e.g., APFS containers)
                var isSynthetic = PlistHelper.GetBoolValue(diskElement, "SyntheticDisk");
                if (isSynthetic)
                    continue;

                var path = $"{DEVICE_PREFIX}{identifier}";
                var size = PlistHelper.GetLongValue(diskElement, "Size");
                if (size <= 0)
                    continue;

                // Get detailed info using diskutil info -plist
                var info = await ProcessRunner.RunProcessAsync("diskutil", $"info -plist {path}", 30_000, cancellationToken);
                if (info.ExitCode != 0)
                {
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "listphysicaldrives", null, $"Failed to get info for {path}: {info.Error}");
                    continue;
                }

                string? displayName = null;
                string? guid = null;
                try
                {
                    var infoDict = PlistHelper.ParsePlistDict(info.Output);
                    if (infoDict != null)
                    {
                        displayName = PlistHelper.GetStringValue(infoDict, "MediaName");
                        guid = PlistHelper.GetStringValue(infoDict, "DiskUUID");
                    }
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "listphysicaldrives", ex, $"Failed to parse diskutil info plist for {path}");
                }

                // Get mount points from partitions
                var mountPoints = new List<string>();
                var partitions = PlistHelper.GetArrayElement(diskElement, "Partitions") ?? PlistHelper.GetArrayElement(diskElement, "APFSVolumes");
                if (partitions != null)
                {
                    foreach (var partition in partitions.Elements("dict"))
                    {
                        var mountPoint = PlistHelper.GetStringValue(partition, "MountPoint");
                        if (!string.IsNullOrWhiteSpace(mountPoint))
                            mountPoints.Add(mountPoint);
                    }
                }

                var driveInfo = new PhysicalDriveInfo
                {
                    Number = identifier,
                    Path = path,
                    Size = (ulong)size,
                    DisplayName = displayName ?? identifier,
                    Guid = guid,
                    MountPoints = mountPoints.ToArray()
                };

                yield return driveInfo;
            }
        }

    }
}
