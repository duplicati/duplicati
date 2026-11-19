// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.SourceProvider.DiskImage.General;

namespace Duplicati.Library.SourceProvider.DiskImage.Disk;

/// <summary>
/// Low-level disk access interface for raw disk reading and writing.
/// </summary>
internal interface IRawDisk : IDisposable
{
    /// <summary>
    /// Gets the platform-specific prefix for disk entries (e.g., "\\.\" on Windows, "/dev/" on Unix).
    /// </summary>
    static abstract string Prefix { get; }

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
    long Sectors { get; }

    /// <summary>
    /// Gets whether the disk is opened for write access.
    /// </summary>
    bool IsWriteable { get; }

    /// <summary>
    /// Automatically unmounts the target disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the disk was successfully unmounted.</returns>
    Task<bool> AutoUnmountAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Initializes the disk access interface.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>null</c> if initialization was successful, otherwise an error message.</returns>
    Task<string?> InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Initializes the disk access interface with write access.
    /// </summary>
    /// <param name="enableWrite">Whether to enable write access.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>null</c> if initialization was successful, otherwise an error message.</returns>
    Task<string?> InitializeAsync(bool enableWrite, CancellationToken cancellationToken);

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
    /// Reads a specific byte range from the disk into a caller-provided buffer.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    /// <param name="destination">The buffer to read data into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes read.</returns>
    Task<int> ReadBytesAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken);

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
    /// Writes a specific byte range to the disk from a caller-provided buffer.
    /// </summary>
    /// <param name="offset">The byte offset.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes written.</returns>
    Task<int> WriteBytesAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>
    /// Lists available physical drives on the system, returning a mapping of device paths to friendly names.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of physical drive information.</returns>
    static abstract IAsyncEnumerable<PhysicalDriveInfo> ListPhysicalDrivesAsync(CancellationToken cancellationToken);
}
