
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
    [SupportedOSPlatform("windows")]
    public class Windows : IRawDisk
    {
        private readonly string m_devicePath;
        private SafeHFILE? m_deviceHandle;
        private bool m_disposed = false;
        private bool m_initialized = false;
        private uint m_sectorSize = 0;
        private long m_size = 0;

        public string DevicePath { get { return m_devicePath; } }

        public Windows(string devicePath)
        {
            // Check if windows platform
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Windows raw disk access is only supported on Windows platforms.");

            m_devicePath = devicePath;
        }

        public long Size
        {
            get
            {
                if (!m_initialized)
                    throw new InvalidOperationException("Disk not initialized.");

                return (long)m_size;
            }
        }

        public int SectorSize
        {
            get
            {
                if (!m_initialized)
                    throw new InvalidOperationException("Disk not initialized.");

                return (int)m_sectorSize;
            }
        }

        public int Sectors
        {
            get
            {
                if (!m_initialized)
                    throw new InvalidOperationException("Disk not initialized.");

                return (int)(m_size / m_sectorSize);
            }
        }

        public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
        {
            if (m_initialized)
                return true;

            // Open the device
            m_deviceHandle = CreateFile(
                m_devicePath,
                Kernel32.FileAccess.GENERIC_READ,
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

            m_initialized = true;
            return true;
        }

        public void Dispose()
        {
            if (m_disposed)
                return;

            m_deviceHandle?.Dispose();
            m_deviceHandle = null;

            m_disposed = true;
        }

        public Task<bool> FinalizeAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.FromResult(true);
        }

        public async Task<Stream> ReadSectorsAsync(long startSector, int sectorCount, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            long offset = startSector * m_sectorSize;
            int length = sectorCount * (int)m_sectorSize;

            return await ReadBytesAsync(offset, length, cancellationToken);
        }

        public async Task<Stream> ReadBytesAsync(long offset, int length, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            if (m_deviceHandle == null || m_deviceHandle.IsInvalid)
                throw new InvalidOperationException("Device handle is invalid.");

            // Move file pointer to the desired offset
            SetFilePointerEx(m_deviceHandle, offset, out _, SeekOrigin.Begin);

            using var buffer = new SafeHGlobalHandle(length);
            bool result = ReadFile(m_deviceHandle, buffer, (uint)length, out uint bytesRead, IntPtr.Zero);

            if (!result || bytesRead != length)
                throw new IOException("Failed to read from disk.");

            return new MemoryStream(buffer.AsBytes().ToArray(), 0, (int)bytesRead, false);
        }

    }
}