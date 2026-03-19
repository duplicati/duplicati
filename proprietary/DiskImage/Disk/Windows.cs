
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.General;
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
        private const string DEVICE_PREFIX = @"\\.\PHYSICALDRIVE";

        // Reusable aligned native buffer for zero-copy I/O
        private SafeHGlobalHandle? m_alignedBuffer;
        private int m_alignedBufferSize = 0;
        private readonly SemaphoreSlim m_ioLock = new(1, 1);

        /// <inheritdoc />
        public static string Prefix => @"\\.\";

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

            m_devicePath = devicePath.TrimEnd(Path.DirectorySeparatorChar);
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

            if (!int.TryParse(m_devicePath.TrimEnd('/', '\\')[^1..], out var number))
                throw new InvalidDataException($"Failed to parse device number from {m_devicePath}");
            if (number < 0)
                throw new InvalidDataException($"Parsed invalid device number {number} from {m_devicePath}");

            var script = @$"
                Get-Partition -DiskNumber {number} |
                Where-Object DriveLetter -ne $null |
                ForEach-Object {{
                    Remove-PartitionAccessPath `
                        -DiskNumber {number} `
                        -PartitionNumber $_.PartitionNumber `
                        -AccessPath ""$($_.DriveLetter):\""
                }}
                Set-Disk -Number {number} -IsOffline $true
                Set-Disk -Number {number} -IsReadOnly $false
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

            if (offset + length > Size)
                throw new InvalidOperationException($"The requested read would read beyond disk size: {offset} + {length} > {Size}");

            if (length == 0)
                return 0;

            // Calculate aligned offset and length for unbuffered I/O (FILE_FLAG_NO_BUFFERING)
            long alignedOffset = (offset / SectorSize) * SectorSize;
            long offsetDelta = offset - alignedOffset;
            long alignedLength = ((offsetDelta + length + SectorSize - 1) / SectorSize) * SectorSize;

            await m_ioLock.WaitAsync(cancellationToken);
            try
            {
                // Ensure reusable buffer is large enough
                EnsureAlignedBuffer((int)alignedLength);

                // Move file pointer to the aligned offset
                var seeked = SetFilePointerEx(m_deviceHandle, alignedOffset, out _, SeekOrigin.Begin);
                if (!seeked)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to seek to offset {alignedOffset}. Win32 Error Code: {error}. Message: {msg}");
                }

                bool result = ReadFile(m_deviceHandle, m_alignedBuffer!, (uint)alignedLength, out uint bytesRead, IntPtr.Zero);

                if (!result || bytesRead < offsetDelta + length)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to read from disk at aligned offset {alignedOffset}. Win32 Error Code: {error}. Message: {msg}");
                }

                // Copy only the requested portion from the aligned buffer
                unsafe
                {
                    var srcPtr = (byte*)m_alignedBuffer!.DangerousGetHandle().ToPointer();
                    var destSpan = destination.Span;
                    int bytesToCopy = Math.Min(length, (int)(bytesRead - offsetDelta));
                    for (int i = 0; i < bytesToCopy; i++)
                    {
                        destSpan[i] = srcPtr[offsetDelta + i];
                    }
                }

                return Math.Min(length, (int)(bytesRead - offsetDelta));
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

            if (offset + dataLength > Size)
                throw new InvalidOperationException($"The requested write would write beyond disk size: {offset} + {dataLength} > {Size}");

            if (dataLength == 0)
                return 0;

            // Calculate aligned offset and length for unbuffered I/O (FILE_FLAG_NO_BUFFERING)
            long alignedOffset = (offset / SectorSize) * SectorSize;
            long offsetDelta = offset - alignedOffset;
            long alignedLength = ((offsetDelta + dataLength + SectorSize - 1) / SectorSize) * SectorSize;

            await m_ioLock.WaitAsync(cancellationToken);
            try
            {
                // Ensure reusable buffer is large enough
                EnsureAlignedBuffer((int)alignedLength);

                // Check if this is an unaligned write (needs read-modify-write)
                bool isUnaligned = offsetDelta != 0 || dataLength != alignedLength;

                if (isUnaligned)
                {
                    // Move file pointer to the aligned offset
                    var seeked = SetFilePointerEx(m_deviceHandle, alignedOffset, out _, SeekOrigin.Begin);
                    if (!seeked)
                    {
                        var error = Marshal.GetLastWin32Error();
                        var msg = new System.ComponentModel.Win32Exception(error).Message;
                        throw new IOException($"Failed to seek to aligned offset {alignedOffset}. Win32 Error Code: {error}. Message: {msg}");
                    }

                    // Read existing data first (read-modify-write)
                    bool readResult = ReadFile(m_deviceHandle, m_alignedBuffer!, (uint)alignedLength, out uint bytesRead, IntPtr.Zero);
                    if (!readResult)
                    {
                        var error = Marshal.GetLastWin32Error();
                        var msg = new System.ComponentModel.Win32Exception(error).Message;
                        throw new IOException($"Failed to read existing data for unaligned write at offset {alignedOffset}. Win32 Error Code: {error}. Message: {msg}");
                    }
                }

                // Copy data from managed buffer to native buffer at the correct offset
                unsafe
                {
                    var destPtr = (byte*)m_alignedBuffer!.DangerousGetHandle().ToPointer();
                    var srcSpan = data.Span;
                    for (int i = 0; i < dataLength; i++)
                    {
                        destPtr[offsetDelta + i] = srcSpan[i];
                    }
                }

                // Move file pointer to the aligned offset for writing
                var seekedForWrite = SetFilePointerEx(m_deviceHandle, alignedOffset, out _, SeekOrigin.Begin);
                if (!seekedForWrite)
                {
                    var error = Marshal.GetLastWin32Error();
                    var msg = new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException($"Failed to seek to aligned offset {alignedOffset} for writing. Win32 Error Code: {error}. Message: {msg}");
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

                return dataLength;
            }
            finally
            {
                m_ioLock.Release();
            }
        }

        /// <summary>
        /// Runs a PowerShell script and returns the standard output. Throws an exception if the script fails.
        /// </summary>
        /// <param name="script">The PowerShell script to run.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The standard output of the PowerShell script.</returns>
        /// <exception cref="IOException">Thrown if the PowerShell script fails.</exception>
        private static async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
        {
            var program = "powershell.exe";
            var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"";

            var result = await ProcessRunner.RunProcessAsync(program, args, 60_000, cancellationToken);
            if (result.ExitCode != 0)
            {
                Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "RunPowerShellAsync", null, $"PowerShell script failed with exit code {result.ExitCode}. Output: {result.Output}. Error: {result.Error}");
                throw new IOException($"PowerShell script failed with exit code {result.ExitCode}. Error: {result.Error}");
            }

            return result.Output;
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

        /// <inheritdoc />
        public static async IAsyncEnumerable<PhysicalDriveInfo> ListPhysicalDrivesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var script = @"
$diskInfo =
    Get-CimInstance Win32_DiskDrive |
    ForEach-Object {
        $wmi = $_
        $diskNumber = [int]$wmi.Index
        $disk = Get-Disk -Number $diskNumber -ErrorAction SilentlyContinue

        $driveLetters = @(
            Get-Partition -DiskNumber $diskNumber -ErrorAction SilentlyContinue |
            Where-Object { $_.DriveLetter } |
            ForEach-Object { $_.DriveLetter.ToString() } |
            Sort-Object -Unique
        )

        [pscustomobject]@{
            Path         = $wmi.DeviceID
            Size         = [uint64]$wmi.Size
            DisplayName  = if ($disk) { $disk.FriendlyName } else { $wmi.Model }
            Guid         = if ($disk) { $disk.Guid } else { $null }
            DriveLetters = $driveLetters   # Always an array
            Online       = if ($disk) { -not $disk.IsOffline } else { $null }
        }
    }

$diskInfo | ConvertTo-Json -Depth 4
";
            var output = await RunPowerShellAsync(script, cancellationToken);

            if (string.IsNullOrWhiteSpace(output))
                yield break;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(output);
            }
            catch (JsonException ex)
            {
                Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "FailedDriveOutputParsing", ex, "Failed to parse output");
                yield break;
            }
            using (doc)
            {



                // PowerShell ConvertTo-Json returns a single object for 1 item, array for multiple
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    PhysicalDriveInfo[]? drives = null;
                    try
                    {
                        drives = JsonSerializer.Deserialize<PhysicalDriveInfo[]>(output);
                    }
                    catch (JsonException ex)
                    {
                        Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "FailedDriveOutputParsing", ex, "Failed to parse output as array");
                    }

                    if (drives != null)
                    {
                        foreach (var drive in drives)
                        {
                            var number = -1;
                            if (drive.Path.StartsWith(DEVICE_PREFIX, StringComparison.OrdinalIgnoreCase))
                            {
                                int.TryParse(drive.Path.AsSpan(DEVICE_PREFIX.Length), out number);
                            }
                            drive.Number = number.ToString();

                            yield return drive;
                        }
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    PhysicalDriveInfo? drive = null;
                    try
                    {
                        drive = JsonSerializer.Deserialize<PhysicalDriveInfo>(output);
                    }
                    catch (JsonException ex)
                    {
                        Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "FailedDriveOutputParsing", ex, "Failed to parse output as single object");
                    }

                    if (drive != null)
                    {
                        // Extract drive number from path (e.g., \\.\PHYSICALDRIVE0 -> 0)
                        var number = -1;
                        if (drive.Path.StartsWith(DEVICE_PREFIX, StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(drive.Path.AsSpan(DEVICE_PREFIX.Length), out number);
                        }
                        drive.Number = number.ToString();
                        yield return drive;
                    }
                }


            }
        }
    }
}