// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

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

        /// <inheritdoc />
        public async Task<bool> AutoUnmountAsync(CancellationToken cancellationToken)
        {
            // Find mounted partitions by parsing /proc/mounts
            // and unmount any that belong to this device
            var deviceName = Path.GetFileName(m_devicePath);

            try
            {
                // Read /proc/mounts to find mounted partitions for this device
                var mountPoints = new System.Collections.Generic.List<string>();
                using (var reader = new StreamReader("/proc/mounts"))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                    {
                        var parts = line.Split(' ');
                        if (parts.Length >= 2)
                        {
                            var dev = parts[0];
                            var mountPoint = parts[1];
                            // Check if this mount point belongs to our device
                            // Device could be /dev/sda1, /dev/nvme0n1p1, /dev/loop0p1, etc.
                            if (dev.StartsWith(m_devicePath) ||
                                (dev.StartsWith("/dev/") && dev.Contains(deviceName)))
                            {
                                mountPoints.Add(mountPoint.Replace("\\040", " ")); // Decode space encoding
                            }
                        }
                    }
                }

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
        public Task<bool> InitializeAsync(bool enableWrite, CancellationToken cancellationToken)
        {
            if (m_initialized)
                return Task.FromResult(true);

            // Open the device with O_DIRECT for unbuffered I/O (matching Windows FILE_FLAG_NO_BUFFERING behavior)
            // Note: O_DIRECT requires sector-aligned buffers and lengths
            int flags = enableWrite ? O_RDWR | O_DIRECT : O_RDONLY | O_DIRECT;
            m_fileDescriptor = open(m_devicePath, flags);

            if (m_fileDescriptor < 0)
            {
                // Try without O_DIRECT if it failed (some systems may not support it for certain devices)
                flags = enableWrite ? O_RDWR : O_RDONLY;
                m_fileDescriptor = open(m_devicePath, flags);

                if (m_fileDescriptor < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorMessage = GetErrnoMessage(errorCode);
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to open device {m_devicePath}: {errorMessage} (errno: {errorCode})");
                    return Task.FromResult(false);
                }
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
                    return Task.FromResult(false);
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
                    return Task.FromResult(false);
                }

                m_size = (long)sizeInBytes;
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
                // Use BLKFLSBUF on Linux to flush the block device buffer cache
                if (ioctl_no_arg(m_fileDescriptor, BLKFLSBUF) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    string errorMessage = GetErrnoMessage(errorCode);
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "dispose", null, $"Failed to flush data: {errorMessage} (errno: {errorCode})");
                }
            }

            if (m_fileDescriptor >= 0)
            {
                close(m_fileDescriptor);
                m_fileDescriptor = -1;
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

            if (offset % SectorSize != 0)
                throw new InvalidOperationException($"Address {offset:X016} is not a multiple of the sector size {SectorSize}");

            if (length % SectorSize != 0)
                throw new InvalidOperationException($"The requested length of {length} is not a multiple of sector size {SectorSize}");

            if (offset + length > Size)
                throw new InvalidOperationException($"The requested read would read beyond disk size: {offset} + {length} > {Size}");

            await m_ioLock.WaitAsync(cancellationToken);
            try
            {
                // Use pread for atomic position + read
                int totalBytesRead = 0;
                unsafe
                {
                    fixed (byte* bufferPtr = destination.Span)
                    {
                        while (totalBytesRead < length)
                        {
                            IntPtr bytesRead = pread(m_fileDescriptor, bufferPtr + totalBytesRead, (IntPtr)(length - totalBytesRead), offset + totalBytesRead);
                            if (bytesRead.ToInt64() < 0)
                            {
                                int errorCode = Marshal.GetLastWin32Error();
                                string errorMessage = GetErrnoMessage(errorCode);
                                throw new IOException($"Failed to read from disk at offset {offset + totalBytesRead}: {errorMessage} (errno: {errorCode})");
                            }
                            if (bytesRead.ToInt64() == 0)
                            {
                                // End of file/device
                                break;
                            }
                            totalBytesRead += (int)bytesRead.ToInt64();
                        }
                    }
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

            // Pad to sector size if necessary
            int remainder = dataLength % (int)m_sectorSize;
            int alignedLength = remainder == 0
                ? dataLength
                : dataLength + ((int)m_sectorSize - remainder);

            await m_ioLock.WaitAsync(cancellationToken);
            try
            {
                byte[]? rentedBuffer = null;
                try
                {
                    ReadOnlyMemory<byte> writeData;

                    if (remainder != 0)
                    {
                        // Need to read-modify-write for partial sector
                        rentedBuffer = ArrayPool<byte>.Shared.Rent(alignedLength);

                        // Read existing data
                        unsafe
                        {
                            fixed (byte* bufferPtr = rentedBuffer)
                            {
                                IntPtr bytesRead = pread(m_fileDescriptor, bufferPtr, (IntPtr)alignedLength, offset);
                                if (bytesRead.ToInt64() < 0)
                                {
                                    int errorCode = Marshal.GetLastWin32Error();
                                    string errorMessage = GetErrnoMessage(errorCode);
                                    throw new IOException($"Failed to read existing data for padding at offset {offset}: {errorMessage} (errno: {errorCode})");
                                }
                            }
                        }

                        // Copy new data over existing data
                        data.CopyTo(rentedBuffer);
                        writeData = rentedBuffer.AsMemory(0, alignedLength);
                    }
                    else
                    {
                        writeData = data;
                    }

                    // Write data using pwrite
                    int totalBytesWritten = 0;
                    unsafe
                    {
                        fixed (byte* bufferPtr = writeData.Span)
                        {
                            while (totalBytesWritten < alignedLength)
                            {
                                IntPtr bytesWritten = pwrite(m_fileDescriptor, bufferPtr + totalBytesWritten, (IntPtr)(alignedLength - totalBytesWritten), offset + totalBytesWritten);
                                if (bytesWritten.ToInt64() < 0)
                                {
                                    int errorCode = Marshal.GetLastWin32Error();
                                    string errorMessage = GetErrnoMessage(errorCode);
                                    string hint = errorCode == 13  // EACCES
                                        ? "The disk may be mounted or you don't have sufficient permissions. Try unmounting the disk before writing."
                                        : errorCode == 30  // EROFS
                                            ? "The disk is read-only."
                                            : "Check the error code for more details.";
                                    throw new IOException($"Failed to write to disk at offset {offset + totalBytesWritten}: {errorMessage} (errno: {errorCode}). {hint}");
                                }
                                totalBytesWritten += (int)bytesWritten.ToInt64();
                            }
                        }
                    }

                    m_shouldFlush = true;
                    return totalBytesWritten;
                }
                finally
                {
                    if (rentedBuffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(rentedBuffer);
                    }
                }
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
        private static unsafe partial IntPtr pread(int fd, byte* buf, IntPtr count, long offset);

        [LibraryImport("libc", SetLastError = true)]
        private static unsafe partial IntPtr pwrite(int fd, byte* buf, IntPtr count, long offset);

        [LibraryImport("libc", SetLastError = true)]
        private static partial IntPtr strerror(int errnum);

        #endregion

        /// <summary>
        /// Gets the error message corresponding to the specified errno value.
        /// </summary>
        /// <param name="errno">The error number.</param>
        /// <returns>A string describing the error.</returns>
        private static string GetErrnoMessage(int errno)
        {
            try
            {
                IntPtr msgPtr = strerror(errno);
                var result = msgPtr != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(msgPtr) ?? $"Unknown error (errno: {errno})"
                    : $"Unknown error (errno: {errno})";

                return result;
            }
            catch
            {
                return $"Unknown error (errno: {errno})";
            }
        }
    }
}
