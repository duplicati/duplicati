using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Proprietary.DiskImage.Raw;

/// <summary>
/// Low-level disk access interface for raw disk reading.
/// </summary>
public interface IRawDisk : IDisposable
{
    /// <summary>
    /// Gets the disk identifier (e.g., "\\.\PhysicalDrive0" or "/dev/sda")
    /// </summary>
    string DevicePath { get; }

    /// <summary>
    /// Gets the total size of the disk in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Gets the sector size in bytes.
    /// </summary>
    int SectorSize { get; }

    /// <summary>
    /// Gets the number of sectors on the disk.
    /// </summary>
    int Sectors { get; }

    /// <summary>
    /// Initializes the disk access interface.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if initialization was successful.</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finalizes the disk access interface.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if finalization was successful.</returns>
    Task<bool> FinalizeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads raw sectors from the disk.
    /// </summary>
    /// <param name="startSector">The starting sector number.</param>
    /// <param name="sectorCount">The number of sectors to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream containing the raw sector data.</returns>
    Task<Stream> ReadSectorsAsync(long startSector, int sectorCount, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a specific byte range from the disk.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream containing the raw data.</returns>
    Task<Stream> ReadBytesAsync(long offset, int length, CancellationToken cancellationToken);
}
