
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Vanara.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;

namespace Duplicati.Proprietary.DiskImage.Disk
{
    /// <summary>
    /// Windows implementation of the <see cref="IRawDisk"/> interface for raw disk access.
    /// Uses Windows API calls via Vanara.PInvoke to read from and write to physical disk devices.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class Windows : IRawDisk
    {
        private readonly string m_devicePath;
        private SafeHFILE? m_deviceHandle;
        private bool m_disposed = false;
        private bool m_initialized = false;
        private bool m_writeable = false;
        private uint m_sectorSize = 0;
        private long m_size = 0;
        private bool m_shouldFlush = false;

        /// <inheritdoc />
        public string DevicePath { get { return m_devicePath; } }

        /// <inheritdoc />
        public bool IsWriteable => m_writeable;

        /// <summary>
        /// Initializes a new instance of the <see cref="Windows"/> class.
        /// </summary>
        /// <param name="devicePath">The Windows device path (e.g., "\\.\PhysicalDrive0").</param>
        /// <exception cref="PlatformNotSupportedException">Thrown when not running on Windows.</exception>
        public Windows(string devicePath)
        {
            // Check if windows platform
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Windows raw disk access is only supported on Windows platforms.");

            m_devicePath = devicePath;
        }

        /// <inheritdoc />
        public long Size
        {
            get
            {
                if (!m_initialized)
                    throw new InvalidOperationException("Disk not initialized.");

                return (long)m_size;
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
        public Task<bool> InitializeAsync(CancellationToken cancellationToken)
            => InitializeAsync(false, cancellationToken);

        /// <inheritdoc />
        public async Task<bool> InitializeAsync(bool enableWrite, CancellationToken cancellationToken)
        {
            if (m_initialized)
                return true;

            // Determine access rights
            var access = enableWrite
                ? Kernel32.FileAccess.GENERIC_READ | Kernel32.FileAccess.GENERIC_WRITE
                : Kernel32.FileAccess.GENERIC_READ;

            // Open the device
            m_deviceHandle = CreateFile(
                m_devicePath,
                access,
                FILE_SHARE.FILE_SHARE_READ | FILE_SHARE.FILE_SHARE_WRITE,
                null,
                CreationOption.OPEN_EXISTING,
                FileFlagsAndAttributes.FILE_FLAG_NO_BUFFERING,
                IntPtr.Zero);

            if (m_deviceHandle.IsInvalid)
            {
                m_deviceHandle = null;
                return false;
            }

            // Allow extended DASD I/O
            DeviceIoControl(m_deviceHandle, IOControlCode.FSCTL_ALLOW_EXTENDED_DASD_IO);

            DeviceIoControl(m_deviceHandle, IOControlCode.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, out DISK_GEOMETRY_EX diskGeometry);

            m_sectorSize = diskGeometry.Geometry.BytesPerSector;

            DeviceIoControl(m_deviceHandle, IOControlCode.IOCTL_DISK_GET_LENGTH_INFO, out GET_LENGTH_INFORMATION lengthInfo);
            m_size = lengthInfo.Length;

            m_writeable = enableWrite;
            m_initialized = true;
            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_disposed)
                return;

            if (m_shouldFlush)
            {
                var flushed = FlushFileBuffers(m_deviceHandle);
                if (!flushed)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    Console.WriteLine($"Warning: Failed to flush file buffers. Win32 Error Code: {error}. Message: {msg}");
                }
            }
            m_deviceHandle?.Dispose();
            m_deviceHandle = null;

            m_disposed = true;
        }

        /// <inheritdoc />
        public Task<bool> FinalizeAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public async Task<Stream> ReadSectorsAsync(long startSector, int sectorCount, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            long offset = startSector * m_sectorSize;
            int length = sectorCount * (int)m_sectorSize;

            return await ReadBytesAsync(offset, length, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Stream> ReadBytesAsync(long offset, int length, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            if (m_deviceHandle == null || m_deviceHandle.IsInvalid)
                throw new InvalidOperationException("Device handle is invalid.");

            if (offset % SectorSize != 0)
                throw new InvalidOperationException($"Address {offset:X016} is not a multiple of the sector size {SectorSize}");

            if (length % SectorSize != 0)
                throw new InvalidOperationException($"The requested length of {length} is not a multiple of sector size {SectorSize}");

            if (offset + length > Size)
                throw new InvalidOperationException($"The requested read would read beyond disk size: {offset} + {length} > {Size}");

            // Move file pointer to the desired offset
            var seeked = SetFilePointerEx(m_deviceHandle, offset, out _, SeekOrigin.Begin);
            if (!seeked)
            {
                var error = Marshal.GetLastWin32Error();
                var msg = new System.ComponentModel.Win32Exception(error).Message;
                throw new IOException($"Failed to seek to offset. Win32 Error Code: {error}. Message: {msg}");
            }

            using var buffer = new SafeHGlobalHandle(length);
            bool result = ReadFile(m_deviceHandle, buffer, (uint)length, out uint bytesRead, IntPtr.Zero);

            if (!result || bytesRead != length)
            {
                var error = Marshal.GetLastWin32Error();
                var msg = new System.ComponentModel.Win32Exception(error).Message;
                throw new IOException($"Failed to read from disk. Win32 Error Code: {error}. Message: {msg}");
            }

            return new MemoryStream(buffer.AsBytes().ToArray(), 0, (int)bytesRead, false);
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
        public async Task<int> WriteBytesAsync(long offset, byte[] data, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            if (!m_writeable)
                throw new InvalidOperationException("Disk not opened for write access.");

            if (m_deviceHandle == null || m_deviceHandle.IsInvalid)
                throw new InvalidOperationException("Device handle is invalid.");

            // Move file pointer to the desired offset
            var seeked = SetFilePointerEx(m_deviceHandle, offset, out _, SeekOrigin.Begin);
            if (!seeked)
            {
                var error = Marshal.GetLastWin32Error();
                var msg = new System.ComponentModel.Win32Exception(error).Message;
                throw new IOException($"Failed to seek to offset. Win32 Error Code: {error}. Message: {msg}");
            }

            // Pad to sector size if necessary
            int remainder = data.Length % (int)m_sectorSize;
            int alignedLength = remainder == 0
                ? data.Length
                : data.Length + ((int)m_sectorSize - remainder);
            using var buffer = new SafeHGlobalHandle(alignedLength);
            if (remainder != 0)
            {
                // Read existing data to fill the remainder
                bool readResult = ReadFile(m_deviceHandle, buffer, (uint)alignedLength, out uint bytesRead, IntPtr.Zero);
                if (!readResult)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to read existing data for padding. Win32 Error Code: {error}. Message: {msg}");
                }
                // Seek back to the original offset after reading
                var seekedBack = SetFilePointerEx(m_deviceHandle, offset, out _, SeekOrigin.Begin);
                if (!seekedBack)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to seek back to offset after reading. Win32 Error Code: {error}. Message: {msg}");
                }
            }

            Marshal.Copy(data, 0, buffer.DangerousGetHandle(), data.Length);

            bool result = WriteFile(m_deviceHandle, buffer, (uint)alignedLength, out uint bytesWritten, IntPtr.Zero);

            if (!result)
            {
                // Error code 5 is "Access Denied", which can occur if the disk is mounted or online. It should be unmounted and offline for writing.
                // Error code 19 is "the media is write protected", which can occur if the disk is write protected. To fix use diskpart:
                // select disk <disk number>
                // attributes disk clear readonly
                var error = Marshal.GetLastWin32Error();
                var msg = new System.ComponentModel.Win32Exception(error).Message;
                throw new IOException($"Failed to write to disk. Win32 Error Code: {error}. Message: {msg}");
            }

            m_shouldFlush = true;

            return (int)bytesWritten;
        }
    }
}