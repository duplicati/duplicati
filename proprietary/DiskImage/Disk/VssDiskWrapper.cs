// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// A wrapper around a raw disk that redirects reads to VSS snapshot devices for snapshotted volumes.
/// This ensures that data read from snapshotted volumes is crash-consistent, while data outside
/// snapshotted volumes (partition table, unallocated space) is read from the raw disk.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class VssDiskWrapper : IRawDisk
{
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<VssDiskWrapper>();

    /// <summary>
    /// The underlying raw disk.
    /// </summary>
    private readonly IRawDisk _rawDisk;

    /// <summary>
    /// Mapping from partition offset ranges to snapshot device handles.
    /// Key is the start offset of the partition, value is a tuple of (endOffset, snapshotHandle).
    /// </summary>
    private readonly List<(long StartOffset, long EndOffset, SafeHFILE Handle)> _snapshotVolumes = [];

    /// <summary>
    /// Indicates whether the wrapper has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="VssDiskWrapper"/> class.
    /// </summary>
    /// <param name="rawDisk">The raw disk to wrap.</param>
    private VssDiskWrapper(IRawDisk rawDisk)
    {
        _rawDisk = rawDisk ?? throw new ArgumentNullException(nameof(rawDisk));
    }

    /// <inheritdoc />
    public static string Prefix => Windows.Prefix;

    /// <inheritdoc />
    public string DevicePath => _rawDisk.DevicePath;

    /// <inheritdoc />
    public long Size => _rawDisk.Size;

    /// <inheritdoc />
    public int SectorSize => _rawDisk.SectorSize;

    /// <inheritdoc />
    public int Sectors => _rawDisk.Sectors;

    /// <inheritdoc />
    public bool IsWriteable => false; // VSS snapshots are read-only

    /// <summary>
    /// Creates a new VSS disk wrapper for the specified disk.
    /// </summary>
    /// <param name="rawDisk">The raw disk to wrap.</param>
    /// <param name="snapshotService">The snapshot service that provides snapshot device paths.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new VSS disk wrapper, or null if VSS is not available or no volumes were found.</returns>
    public static async Task<VssDiskWrapper?> CreateAsync(IRawDisk rawDisk, ISnapshotService snapshotService, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // Get the volumes on this disk
        var volumes = await GetVolumesOnDiskAsync(rawDisk.DevicePath, cancellationToken).ConfigureAwait(false);
        if (volumes.Count == 0)
        {
            Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "NoVolumesFound", "No volumes found on disk {0}, skipping VSS", rawDisk.DevicePath);
            return null;
        }

        Duplicati.Library.Logging.Log.WriteInformationMessage(LOGTAG, "CreatingVssSnapshot", "Creating VSS disk wrapper for disk {0} with volumes: {1}", rawDisk.DevicePath, string.Join(", ", volumes.Select(v => v.DriveLetter)));

        var wrapper = new VssDiskWrapper(rawDisk);
        var openedAny = false;

        foreach (var volume in volumes)
        {
            var driveLetter = volume.DriveLetter + ":\\";
            string snapshotPath;
            try
            {
                snapshotPath = snapshotService.ConvertToSnapshotPath(driveLetter);
            }
            catch (Exception ex)
            {
                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "SnapshotPathFailed", ex, "Failed to get snapshot path for volume {0}", driveLetter);
                continue;
            }

            // Check if the snapshot path is a real VSS snapshot device
            if (!snapshotPath.Contains("HarddiskVolumeShadowCopy", StringComparison.OrdinalIgnoreCase))
            {
                Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "NoSnapshotForVolume", "No VSS snapshot available for volume {0}", driveLetter);
                continue;
            }

            // Remove the trailing backslash to get the device path
            var devicePath = snapshotPath.TrimEnd('\\');

            var handle = CreateFile(
                devicePath,
                Kernel32.FileAccess.GENERIC_READ,
                FILE_SHARE.FILE_SHARE_READ | FILE_SHARE.FILE_SHARE_WRITE,
                null,
                CreationOption.OPEN_EXISTING,
                FileFlagsAndAttributes.FILE_FLAG_NO_BUFFERING,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotOpenFailed", null, "Failed to open snapshot device {0}: Win32 Error Code {1}", devicePath, error);
                continue;
            }

            wrapper._snapshotVolumes.Add((volume.Offset, volume.Offset + volume.Size, handle));
            openedAny = true;
            Duplicati.Library.Logging.Log.WriteVerboseMessage(LOGTAG, "SnapshotOpened", "Opened snapshot device {0} for partition at offset {1}", devicePath, volume.Offset);
        }

        if (!openedAny)
        {
            Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "NoSnapshotsOpened", null, "Failed to open any snapshot devices, falling back to raw disk");
            return null;
        }

        return wrapper;
    }

    /// <inheritdoc />
    public Task<string?> InitializeAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null); // Already initialized via CreateAsync

    /// <inheritdoc />
    public Task<string?> InitializeAsync(bool enableWrite, CancellationToken cancellationToken)
        => Task.FromResult<string?>(enableWrite ? "VSS snapshots are read-only" : null);

    /// <inheritdoc />
    public Task<bool> AutoUnmountAsync(CancellationToken cancellationToken)
        => _rawDisk.AutoUnmountAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> FinalizeAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<Stream> ReadSectorsAsync(long startSector, int sectorCount, CancellationToken cancellationToken)
    {
        long offset = startSector * SectorSize;
        int length = sectorCount * SectorSize;
        return await ReadBytesAsync(offset, length, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Stream> ReadBytesAsync(long offset, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var bytesRead = await ReadBytesAsync(offset, buffer, cancellationToken).ConfigureAwait(false);
        return new MemoryStream(buffer, 0, bytesRead);
    }

    /// <inheritdoc />
    public async Task<int> ReadBytesAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VssDiskWrapper));

        if (offset + destination.Length > Size)
            throw new InvalidOperationException($"The requested read would read beyond disk size: {offset} + {destination.Length} > {Size}");

        // Find the snapshot volume that contains this offset
        foreach (var (startOffset, endOffset, handle) in _snapshotVolumes)
        {
            if (offset >= startOffset && offset < endOffset)
            {
                // Calculate how much we can read from this snapshot
                var bytesAvailable = (int)Math.Min(destination.Length, endOffset - offset);
                var snapshotOffset = offset - startOffset;

                // Read from the snapshot device
                var bytesRead = await ReadFromSnapshotAsync(handle, snapshotOffset, destination.Slice(0, bytesAvailable), cancellationToken).ConfigureAwait(false);

                // If we read everything requested, we're done
                if (bytesRead >= destination.Length)
                    return bytesRead;

                // If there's more to read, continue reading from the raw disk for the remainder
                if (bytesRead < destination.Length)
                {
                    var remainingBytes = await _rawDisk.ReadBytesAsync(offset + bytesRead, destination.Slice(bytesRead), cancellationToken).ConfigureAwait(false);
                    return bytesRead + remainingBytes;
                }

                return bytesRead;
            }
        }

        // Not in a snapshotted volume, read from the raw disk
        return await _rawDisk.ReadBytesAsync(offset, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads data from a snapshot device handle.
    /// </summary>
    /// <param name="handle">The snapshot device handle.</param>
    /// <param name="offset">The offset within the snapshot to read from.</param>
    /// <param name="destination">The buffer to read into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes read.</returns>
    private static async Task<int> ReadFromSnapshotAsync(SafeHFILE handle, long offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        // Calculate aligned offset and length for unbuffered I/O (FILE_FLAG_NO_BUFFERING)
        var sectorSize = 512; // VSS snapshot devices use 512-byte sectors
        long alignedOffset = (offset / sectorSize) * sectorSize;
        long offsetDelta = offset - alignedOffset;
        long alignedLength = ((offsetDelta + destination.Length + sectorSize - 1) / sectorSize) * sectorSize;

        // Allocate aligned buffer
        var buffer = new byte[alignedLength];
        int totalBytesRead = 0;

        await Task.Run(() =>
        {
            var seeked = SetFilePointerEx(handle, alignedOffset, out _, SeekOrigin.Begin);
            if (!seeked)
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new IOException($"Failed to seek to offset {alignedOffset} in snapshot. Win32 Error Code: {error}");
            }

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    var result = ReadFile(handle, (IntPtr)ptr, (uint)alignedLength, out uint bytesRead, IntPtr.Zero);
                    if (!result)
                    {
                        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                        throw new IOException($"Failed to read from snapshot at offset {alignedOffset}. Win32 Error Code: {error}");
                    }
                    totalBytesRead = (int)bytesRead;
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        // Copy the requested portion from the aligned buffer
        int bytesToCopy = Math.Min(destination.Length, totalBytesRead - (int)offsetDelta);
        if (bytesToCopy > 0)
            buffer.AsSpan((int)offsetDelta, bytesToCopy).CopyTo(destination.Span);

        return Math.Max(0, bytesToCopy);
    }

    /// <inheritdoc />
    public Task<int> WriteSectorsAsync(long startSector, byte[] data, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Write operations are not supported on VSS snapshots.");

    /// <inheritdoc />
    public Task<int> WriteBytesAsync(long offset, byte[] data, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Write operations are not supported on VSS snapshots.");

    /// <inheritdoc />
    public Task<int> WriteBytesAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Write operations are not supported on VSS snapshots.");

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var (_, _, handle) in _snapshotVolumes)
        {
            try
            {
                handle?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
        _snapshotVolumes.Clear();

        _rawDisk?.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    public static IAsyncEnumerable<PhysicalDriveInfo> ListPhysicalDrivesAsync(CancellationToken cancellationToken)
        => Windows.ListPhysicalDrivesAsync(cancellationToken);

    /// <summary>
    /// Reads the disk number off a device path, e.g. "\\.\PhysicalDrive0" gives 0.
    /// </summary>
    /// <param name="devicePath">The disk device path.</param>
    /// <param name="diskNumber">The disk number, when the path carries one.</param>
    /// <returns>True when the path ends in a disk number.</returns>
    internal static bool TryGetDiskNumber(string devicePath, out int diskNumber)
    {
        var trimmed = (devicePath ?? string.Empty).TrimEnd('\\', '/');
        var start = trimmed.Length;
        while (start > 0 && char.IsAsciiDigit(trimmed[start - 1]))
            start--;

        diskNumber = 0;
        return start < trimmed.Length && int.TryParse(trimmed.AsSpan(start), out diskNumber);
    }

    /// <summary>
    /// Gets the volumes on the specified disk.
    /// </summary>
    /// <param name="devicePath">The disk device path (e.g., "\\.\PhysicalDrive0").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of volumes on the disk with their offsets and sizes.</returns>
    internal static async Task<List<VolumeInfo>> GetVolumesOnDiskAsync(string devicePath, CancellationToken cancellationToken)
    {
        if (!TryGetDiskNumber(devicePath, out var diskNumber))
            return [];

        var script = $@"
Get-CimInstance -ClassName MSFT_Partition -Namespace Root/Microsoft/Windows/Storage -Filter 'DiskNumber={diskNumber}' |
Where-Object {{ $_.DriveLetter -ne $null -and $_.DriveLetter -ne [char]0 -and $_.DriveLetter -ne '' }} |
ForEach-Object {{
    [pscustomobject]@{{
        DriveLetter = $_.DriveLetter.ToString()
        Offset      = [long]$_.Offset
        Size        = [long]$_.Size
    }}
}} | ConvertTo-Json -Depth 4
";

        var output = await Windows.RunPowerShellAsync(script, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var volumes = JsonSerializer.Deserialize<VolumeInfo[]>(output);
                return volumes?.ToList() ?? [];
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var volume = JsonSerializer.Deserialize<VolumeInfo>(output);
                return volume != null ? [volume] : [];
            }
        }
        catch (JsonException ex)
        {
            Duplicati.Library.Logging.Log.WriteWarningMessage(LOGTAG, "FailedVolumeParsing", ex, "Failed to parse volume output");
        }

        return [];
    }

    /// <summary>
    /// Information about a volume on a disk.
    /// </summary>
    internal sealed class VolumeInfo
    {
        public string DriveLetter { get; set; } = "";
        public long Offset { get; set; }
        public long Size { get; set; }
    }
}
