
using System;
using System.Buffers;
using System.Diagnostics;
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
        private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<Windows>();

        private readonly string m_devicePath;
        private SafeHFILE? m_deviceHandle;
        private bool m_disposed = false;
        private bool m_initialized = false;
        private bool m_writeable = false;
        private uint m_sectorSize = 0;
        private long m_size = 0;
        private bool m_shouldFlush = false;

        // Reusable aligned native buffer for zero-copy I/O
        private SafeHGlobalHandle? m_alignedBuffer;
        private int m_alignedBufferSize = 0;
        private readonly SemaphoreSlim m_ioLock = new(1, 1);

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
        public async Task<bool> AutoUnmountAsync(CancellationToken cancellationToken)
        {
            var script = @$"
                $disk = Get-Disk -DevicePath '{m_devicePath}'
                if ($disk -eq $null) {{
                    exit 1
                }}
                $volumes = Get-Volume -DiskNumber $disk.Number
                foreach ($vol in $volumes) {{
                    if ($vol.DriveLetter) {{
                        Dismount-Volume -DriveLetter $vol.DriveLetter -Force
                    }}
                }}
                Set-Disk -Number $disk.Number -IsOffline $true
                Set-Disk -Number $disk.Number -IsReadOnly $false
            ";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = new Process { StartInfo = psi };

            p.Start();

            p.StandardInput.WriteLine(script);
            p.StandardInput.Close();

            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();

            p.WaitForExit();

            Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "autounmount", output, null);

            if (!string.IsNullOrWhiteSpace(error))
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "autounmount", null, error, null);

            return p.ExitCode == 0;
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

            m_alignedBuffer?.Dispose();
            m_alignedBuffer = null;
            m_alignedBufferSize = 0;

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

            if (m_deviceHandle == null || m_deviceHandle.IsInvalid)
                throw new InvalidOperationException("Device handle is invalid.");

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
                // Ensure reusable buffer is large enough
                EnsureAlignedBuffer(length);

                // Move file pointer to the desired offset
                var seeked = SetFilePointerEx(m_deviceHandle, offset, out _, SeekOrigin.Begin);
                if (!seeked)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to seek to offset. Win32 Error Code: {error}. Message: {msg}");
                }

                bool result = ReadFile(m_deviceHandle, m_alignedBuffer!, (uint)length, out uint bytesRead, IntPtr.Zero);

                if (!result || bytesRead != length)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to read from disk. Win32 Error Code: {error}. Message: {msg}");
                }

                // Copy from native buffer to destination
                unsafe
                {
                    var srcPtr = (byte*)m_alignedBuffer!.DangerousGetHandle().ToPointer();
                    var destSpan = destination.Span;
                    for (int i = 0; i < bytesRead; i++)
                    {
                        destSpan[i] = srcPtr[i];
                    }
                }

                return (int)bytesRead;
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
        public async Task<int> WriteBytesAsync(long offset, byte[] data, CancellationToken cancellationToken)
            => await WriteBytesAsync(offset, data.AsMemory(), cancellationToken);

        /// <inheritdoc />
        public async Task<int> WriteBytesAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (!m_initialized)
                throw new InvalidOperationException("Disk not initialized.");

            if (!m_writeable)
                throw new InvalidOperationException("Disk not opened for write access.");

            if (m_deviceHandle == null || m_deviceHandle.IsInvalid)
                throw new InvalidOperationException("Device handle is invalid.");

            int dataLength = data.Length;

            // Pad to sector size if necessary
            int remainder = dataLength % (int)m_sectorSize;
            int alignedLength = remainder == 0
                ? dataLength
                : dataLength + ((int)m_sectorSize - remainder);

            await m_ioLock.WaitAsync(cancellationToken);
            try
            {
                // Ensure reusable buffer is large enough
                EnsureAlignedBuffer(alignedLength);

                // Move file pointer to the desired offset
                var seeked = SetFilePointerEx(m_deviceHandle, offset, out _, SeekOrigin.Begin);
                if (!seeked)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to seek to offset. Win32 Error Code: {error}. Message: {msg}");
                }

                if (remainder != 0)
                {
                    // Read existing data to fill the remainder
                    bool readResult = ReadFile(m_deviceHandle, m_alignedBuffer!, (uint)alignedLength, out uint bytesRead, IntPtr.Zero);
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

                // Copy data from managed buffer to native buffer
                unsafe
                {
                    var destPtr = (byte*)m_alignedBuffer!.DangerousGetHandle().ToPointer();
                    var srcSpan = data.Span;
                    for (int i = 0; i < dataLength; i++)
                    {
                        destPtr[i] = srcSpan[i];
                    }
                }

                bool result = WriteFile(m_deviceHandle, m_alignedBuffer!, (uint)alignedLength, out uint bytesWritten, IntPtr.Zero);

                if (!result)
                {
                    // Error code 5 is "Access Denied", which can occur if the disk is mounted or online. It should be unmounted and offline for writing.
                    // Error code 19 is "the media is write protected", which can occur if the disk is write protected. To fix use diskpart:
                    // select disk <disk number>
                    // attributes disk clear readonly
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    var hint = error == 5
                        ? "The disk may be mounted or online. Try unmounting and taking the disk offline before writing."
                        : error == 19
                            ? "The disk may be write protected. Try clearing the readonly attribute using diskpart."
                            : "Check the error code and message for more details.";
                    throw new IOException($"Failed to write to disk. Win32 Error Code: {error}. Message: {msg}. Hint: {hint}");
                }

                m_shouldFlush = true;

                return (int)bytesWritten;
            }
            finally
            {
                m_ioLock.Release();
            }
        }

        /// <summary>
        /// Ensures the reusable aligned buffer is at least the specified size.
        /// Reallocates if necessary. Must be called while holding m_ioLock.
        /// </summary>
        private void EnsureAlignedBuffer(int requiredSize)
        {
            if (m_alignedBufferSize >= requiredSize)
                return;

            m_alignedBuffer?.Dispose();
            m_alignedBuffer = new SafeHGlobalHandle(requiredSize);
            m_alignedBufferSize = requiredSize;
        }
    }
}