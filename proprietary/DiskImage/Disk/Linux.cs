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
using System.Text.Json;
using Duplicati.Proprietary.DiskImage.General;

namespace Duplicati.Proprietary.DiskImage.Disk
{
    /// <summary>
    /// Linux implementation of the <see cref="IRawDisk"/> interface for raw disk access.
    /// Uses POSIX API calls via P/Invoke to read from and write to block devices.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public partial class Linux : IRawDisk
    {
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Linux>();

        // Linux block device ioctl constants from <linux/fs.h>
        // BLKGETSIZE64: _IOR(0x12, 114, size_t) = 0x80081272 (on x86_64)
        // Returns the size of the block device in bytes as uint64.
        private const ulong BLKGETSIZE64 = 0x80081272;

        // BLKSSZGET: _IO(0x12, 104) = 0x1268
        // Returns the logical sector size of the block device as int.
        private const uint BLKSSZGET = 0x1268;

        // BLKFLSBUF: _IO(0x12, 97) = 0x1261
        // Flushes the buffer cache for the block device.
        private const uint BLKFLSBUF = 0x1261;

        // File open flags from <fcntl.h>
        private const int O_RDONLY = 0x0000;
        private const int O_RDWR = 0x0002;
        // TODO Should be processor architecture agnostic.
        // O_DIRECT: bypass kernel page cache for unbuffered I/O
        // Value is architecture-dependent: 0x4000 on x86_64, 0x10000 on aarch64
        // Using 0x4000 as the most common value (x86_64)
        private const int O_DIRECT = 0x4000;

        private readonly string m_devicePath;
        private int m_fileDescriptor = -1;
        private bool m_disposed = false;
        private bool m_initialized = false;
        private bool m_writeable = false;
        private uint m_sectorSize = 0;
        private long m_size = 0;
        private bool m_shouldFlush = false;
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
        /// Initializes a new instance of the <see cref="Linux"/> class.
        /// </summary>
        /// <param name="devicePath">The Linux device path (e.g., "/dev/sda", "/dev/nvme0n1", "/dev/loop0").</param>
        /// <exception cref="PlatformNotSupportedException">Thrown when not running on Linux.</exception>
        public Linux(string devicePath)
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("Linux raw disk access is only supported on Linux platforms.");

            m_devicePath = devicePath;
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

        private async Task<List<string>> GetMountedPartitionsAsync(CancellationToken cancellationToken)
        {
            var mountedPartitions = new List<string>();

            // Read /proc/mounts to find mounted partitions for this device
            using (var reader = new StreamReader("/proc/mounts"))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                    if (line.StartsWith(m_devicePath))
                    {
                        var partitionDevice = line.Split(' ').FirstOrDefault();
                        if (partitionDevice != null)
                            mountedPartitions.Add(partitionDevice);
                    }
            }

            return mountedPartitions;
        }

        /// <inheritdoc />
        public async Task<bool> AutoUnmountAsync(CancellationToken cancellationToken)
        {
            try
            {
                var mountPoints = await GetMountedPartitionsAsync(cancellationToken);

                // Unmount each partition
                bool allSucceeded = true;
                foreach (var mountPoint in mountPoints)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "umount",
                        Arguments = $"\"{mountPoint}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = psi };
                    process.Start();

                    string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                    string error = await process.StandardError.ReadToEndAsync(cancellationToken);

                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode != 0)
                    {
                        Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "autounmount", null, $"Failed to unmount {mountPoint}: {error}");
                        allSucceeded = false;
                    }
                    else
                    {
                        Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "autounmount", $"Successfully unmounted {mountPoint}");
                    }
                }

                return allSucceeded;
            }
            catch (Exception ex)
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "autounmount", ex, "Failed to auto-unmount disk");
                return false;
            }
        }

        /// <inheritdoc />
        public Task<bool> InitializeAsync(CancellationToken cancellationToken)
            => InitializeAsync(false, cancellationToken);

        /// <inheritdoc />
        public async Task<bool> InitializeAsync(bool enableWrite, CancellationToken cancellationToken)
        {
            if (m_initialized)
                return true;

            if (enableWrite && (await GetMountedPartitionsAsync(cancellationToken)).Count > 0)
            {
                throw new IOException($"Cannot initialize disk {m_devicePath} because it has mounted partitions. Please unmount all partitions before initializing.");
            }

            // Open the device with O_DIRECT for unbuffered I/O (matching Windows FILE_FLAG_NO_BUFFERING behavior)
            // Note: O_DIRECT requires sector-aligned buffers and lengths
            int flags = enableWrite ? O_RDWR | O_DIRECT : O_RDONLY | O_DIRECT;
            m_fileDescriptor = open(m_devicePath, flags);

            if (m_fileDescriptor < 0)
            {
                int errorCode = Marshal.GetLastWin32Error();
                string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode); ;
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to open device {m_devicePath}: {errorMessage} (errno: {errorCode})");
                return false;
            }

            // Get disk geometry using ioctls
            try
            {
                // Get logical block size (sector size)
                uint blockSize = 0;
                if (ioctl_uint32(m_fileDescriptor, BLKSSZGET, ref blockSize) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    close(m_fileDescriptor);
                    m_fileDescriptor = -1;
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to get block size: errno {errorCode}");
                    return false;
                }
                m_sectorSize = blockSize;

                // Get disk size in bytes
                ulong sizeInBytes = 0;
                if (ioctl_uint64(m_fileDescriptor, BLKGETSIZE64, ref sizeInBytes) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    close(m_fileDescriptor);
                    m_fileDescriptor = -1;
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to get disk size: errno {errorCode}");
                    return false;
                }

                m_size = (long)sizeInBytes;
                m_writeable = enableWrite;
                m_initialized = true;

                Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "initialize", $"Successfully initialized disk {m_devicePath}: Size={m_size}, SectorSize={m_sectorSize}");
                return true;
            }
            catch (Exception ex)
            {
                close(m_fileDescriptor);
                m_fileDescriptor = -1;
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", ex, "Failed to initialize disk");
                return false;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_disposed)
                return;

            if (m_shouldFlush && m_fileDescriptor >= 0)
            {
                // Use BLKFLSBUF on Linux to flush the block device buffer cache
                if (ioctl_no_arg(m_fileDescriptor, BLKFLSBUF) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode); ;
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
                        string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode); ;
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
                            string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode); ;
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
                    string errorMessage = Marshal.GetPInvokeErrorMessage(errorCode); ;
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

        // P/Invoke to the native wrapper library for ioctls
        [LibraryImport("libc_wrapper.so", SetLastError = true)]
        internal static partial int ioctl_uint32(int fd, uint request, ref uint value);

        [LibraryImport("libc_wrapper.so", SetLastError = true)]
        internal static partial int ioctl_uint64(int fd, ulong request, ref ulong value);

        [LibraryImport("libc_wrapper.so", SetLastError = true)]
        internal static partial int ioctl_no_arg(int fd, uint request);

        // Standard libc functions
        [LibraryImport("libc", SetLastError = true)]
        private static partial int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

        [LibraryImport("libc", SetLastError = true)]
        private static partial int close(int fd);

        [LibraryImport("libc", SetLastError = true)]
        private static unsafe partial nint pread(int fd, void* buf, nint count, long offset);

        [LibraryImport("libc", SetLastError = true)]
        private static unsafe partial nint pwrite(int fd, void* buf, nint count, long offset);

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
        }

        /// <inheritdoc />
        public static async IAsyncEnumerable<PhysicalDriveInfo> ListPhysicalDrivesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Use lsblk to get list of block devices in JSON format
            // -J: JSON output
            // -O: include all available columns
            // -b: sizes in bytes
            var result = await ProcessRunner.RunProcessAsync("lsblk", "-JO -b", 30_000, cancellationToken);
            if (result.ExitCode != 0)
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "listphysicaldrives", null, $"Failed to list physical drives: {result.Error}");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(result.Output))
                yield break;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(result.Output);
            }
            catch (JsonException ex)
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "listphysicaldrives", ex, "Failed to parse lsblk JSON output");
                yield break;
            }

            using (doc)
            {
                var rootElement = doc.RootElement;
                if (!rootElement.TryGetProperty("blockdevices", out var blockDevices))
                    yield break;

                foreach (var device in blockDevices.EnumerateArray())
                {
                    // Only include whole disks (type="disk"), not partitions (type="part")
                    if (!device.TryGetProperty("type", out var typeElement) || typeElement.GetString() != "disk")
                        continue;

                    var path = device.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    // Get size
                    var size = device.TryGetProperty("size", out var sizeElement) ? sizeElement.GetUInt64() : 0UL;
                    if (size == 0)
                        continue;

                    // Get device name/number (e.g., sda, nvme0n1)
                    var name = device.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;

                    // Get mount points from children (partitions)
                    var mountPoints = new List<string>();
                    if (device.TryGetProperty("children", out var children))
                    {
                        foreach (var child in children.EnumerateArray())
                        {
                            if (child.TryGetProperty("mountpoint", out var mpElement) && mpElement.ValueKind == JsonValueKind.String)
                            {
                                var mountPoint = mpElement.GetString();
                                if (!string.IsNullOrWhiteSpace(mountPoint))
                                    mountPoints.Add(mountPoint);
                            }
                        }
                    }

                    // Try to get UUID using blkid (this is expensive, so we only do it for valid disks)
                    string? guid = null;
                    try
                    {
                        var blkidResult = await ProcessRunner.RunProcessAsync("blkid", $"-o export {path}", 10_000, cancellationToken);
                        if (blkidResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(blkidResult.Output))
                        {
                            // Parse blkid output to find UUID
                            foreach (var line in blkidResult.Output.Split('\n'))
                            {
                                if (line.StartsWith("UUID="))
                                {
                                    guid = line[5..].Trim('"');
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore blkid failures - not all disks have UUIDs
                    }

                    var driveInfo = new PhysicalDriveInfo
                    {
                        Number = name ?? path,
                        Path = path,
                        Size = size,
                        DisplayName = name ?? path,
                        Guid = guid,
                        MountPoints = [.. mountPoints],
                        Online = null // Linux doesn't have an equivalent concept to "online" disks
                    };

                    yield return driveInfo;
                }
            }
        }
    }
}
