using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Proprietary.DiskImage.Disk;

/// <summary>
/// Low-level disk access interface for raw disk reading and writing.
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
    /// Gets whether the disk is opened for write access.
    /// </summary>
    bool IsWriteable { get; }

    /// <summary>
    /// Initializes the disk access interface.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if initialization was successful.</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Initializes the disk access interface with write access.
    /// </summary>
    /// <param name="enableWrite">Whether to enable write access.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if initialization was successful.</returns>
    Task<bool> InitializeAsync(bool enableWrite, CancellationToken cancellationToken);

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

    /// <summary>
    /// Writes raw sectors to the disk.
    /// </summary>
    /// <param name="startSector">The starting sector number.</param>
    /// <param name="data">The data to write (must be sector-aligned).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes written.</returns>
    Task<int> WriteSectorsAsync(long startSector, byte[] data, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a specific byte range to the disk.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes written.</returns>
    Task<int> WriteBytesAsync(long offset, byte[] data, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a specific byte range from the disk into a caller-provided buffer.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    /// <param name="destination">The buffer to read data into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes read.</returns>
    Task<int> ReadBytesAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a specific byte range to the disk from a caller-provided buffer.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes written.</returns>
    Task<int> WriteBytesAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}
