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
    /// macOS implementation of the <see cref="IRawDisk"/> interface for raw disk access.
    /// Uses POSIX API calls via P/Invoke to read from and write to physical disk devices.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public class Mac : IRawDisk
    {
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Mac>();

        // Disk ioctl constants from <sys/disk.h>
        private const uint DKIOCGETBLOCKSIZE = 0x40046418;  // _IOR('d', 24, uint32_t)
        private const uint DKIOCGETBLOCKCOUNT = 0x40046419; // _IOR('d', 25, uint64_t)

        // File open flags
        private const int O_RDONLY = 0x0000;
        private const int O_RDWR = 0x0002;
        private const int O_SYNC = 0x0080;

        // fcntl constants
        private const int F_FULLFSYNC = 51;

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
            if (devicePath.StartsWith("/dev/disk") && !devicePath.StartsWith("/dev/rdisk"))
            {
                return devicePath.Replace("/dev/disk", "/dev/rdisk");
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
            string blockDevicePath = m_devicePath.Replace("/dev/rdisk", "/dev/disk");

            // Use diskutil to unmount all volumes on the disk
            var psi = new ProcessStartInfo
            {
                FileName = "diskutil",
                Arguments = $"unmountDisk {blockDevicePath}",
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

            Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "autounmount", output, null);

            if (!string.IsNullOrWhiteSpace(error))
                Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "autounmount", null, error, null);

            // diskutil returns 0 on success, but may also succeed with warnings
            return process.ExitCode == 0 || output.Contains("successful");
        }

        /// <inheritdoc />
        public Task<bool> InitializeAsync(CancellationToken cancellationToken)
            => InitializeAsync(false, cancellationToken);

        /// <inheritdoc />
        public Task<bool> InitializeAsync(bool enableWrite, CancellationToken cancellationToken)
        {
            if (m_initialized)
                return Task.FromResult(true);

            // First, try to unmount the disk if it's mounted (required for write access)
            if (enableWrite)
            {
                try
                {
                    string blockDevicePath = m_devicePath.Replace("/dev/rdisk", "/dev/disk");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "diskutil",
                        Arguments = $"unmountDisk {blockDevicePath}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = psi };
                    process.Start();
                    process.WaitForExit(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "initialize", null, $"Failed to unmount disk before opening: {ex.Message}");
                }
            }

            // Open the device
            int flags = enableWrite ? O_RDWR | O_SYNC : O_RDONLY;
            m_fileDescriptor = open(m_devicePath, flags);

            if (m_fileDescriptor < 0)
            {
                int errorCode = Marshal.GetLastWin32Error();
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to open device {m_devicePath}: errno {errorCode}");
                return Task.FromResult(false);
            }

            // Get disk geometry using ioctls
            try
            {
                // Get block size (sector size)
                uint blockSize = 0;
                if (ioctl(m_fileDescriptor, DKIOCGETBLOCKSIZE, ref blockSize) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    close(m_fileDescriptor);
                    m_fileDescriptor = -1;
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to get block size: errno {errorCode}");
                    return Task.FromResult(false);
                }
                m_sectorSize = blockSize;

                // Get block count
                ulong blockCount = 0;
                if (ioctl(m_fileDescriptor, DKIOCGETBLOCKCOUNT, ref blockCount) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    close(m_fileDescriptor);
                    m_fileDescriptor = -1;
                    Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "initialize", null, $"Failed to get block count: errno {errorCode}");
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
                // Use F_FULLFSYNC on macOS to guarantee flush to physical media
                if (fcntl(m_fileDescriptor, F_FULLFSYNC, 0) < 0)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "dispose", null, $"Failed to flush data: errno {errorCode}");
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
                                throw new IOException($"Failed to read from disk at offset {offset + totalBytesRead}: errno {errorCode}");
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
                                    throw new IOException($"Failed to read existing data for padding at offset {offset}: errno {errorCode}");
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
                                    string hint = errorCode == 13  // EACCES
                                        ? "The disk may be mounted or you don't have sufficient permissions. Try unmounting the disk before writing."
                                        : errorCode == 30  // EROFS
                                            ? "The disk is read-only."
                                            : "Check the error code for more details.";
                                    throw new IOException($"Failed to write to disk at offset {offset + totalBytesWritten}: errno {errorCode}. {hint}");
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

        [DllImport("libc", SetLastError = true)]
        private static extern int open(string pathname, int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, uint request, ref uint arg);

        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, uint request, ref ulong arg);

        [DllImport("libc", SetLastError = true)]
        private static extern int fcntl(int fd, int cmd, int arg);

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe IntPtr pread(int fd, byte* buf, IntPtr count, long offset);

        [DllImport("libc", SetLastError = true)]
        private static extern unsafe IntPtr pwrite(int fd, byte* buf, IntPtr count, long offset);

        #endregion
    }
}
