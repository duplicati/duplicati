// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Duplicati.Proprietary.DiskImage.Filesystem.Fat32;

/// <summary>
/// Represents a parsed FAT32 Boot Sector (BIOS Parameter Block).
/// This is a read-only struct that extracts geometry information from the boot sector.
/// </summary>
public readonly record struct Fat32BootSector
{
    /// <summary>
    /// The boot sector signature at offset 510.
    /// On disk the bytes are 0x55, 0xAA; read as little-endian uint16 this is 0xAA55.
    /// </summary>
    private const ushort BootSectorSignature = 0xAA55;

    /// <summary>
    /// Offset to the boot sector signature.
    /// </summary>
    private const int SignatureOffset = 510;

    /// <summary>
    /// Offset to the filesystem type string (should contain "FAT32").
    /// </summary>
    private const int FilesystemTypeOffset = 0x52;

    /// <summary>
    /// Length of the filesystem type string (8 bytes).
    /// </summary>
    private const int FilesystemTypeLength = 8;

    /// <summary>
    /// Offset to BytesPerSector field.
    /// </summary>
    private const int BytesPerSectorOffset = 0x0B;

    /// <summary>
    /// Offset to SectorsPerCluster field.
    /// </summary>
    private const int SectorsPerClusterOffset = 0x0D;

    /// <summary>
    /// Offset to ReservedSectorCount field.
    /// </summary>
    private const int ReservedSectorCountOffset = 0x0E;

    /// <summary>
    /// Offset to NumberOfFats field.
    /// </summary>
    private const int NumberOfFatsOffset = 0x10;

    /// <summary>
    /// Offset to TotalSectors32 field.
    /// </summary>
    private const int TotalSectors32Offset = 0x20;

    /// <summary>
    /// Offset to FatSize32 field (sectors per FAT).
    /// </summary>
    private const int FatSize32Offset = 0x24;

    /// <summary>
    /// Offset to RootCluster field.
    /// </summary>
    private const int RootClusterOffset = 0x2C;

    /// <summary>
    /// Offset to FsInfoSector field.
    /// </summary>
    private const int FsInfoSectorOffset = 0x30;

    /// <summary>
    /// Minimum valid bytes per sector (512).
    /// </summary>
    private const ushort MinBytesPerSector = 512;

    /// <summary>
    /// Maximum valid bytes per sector (4096).
    /// </summary>
    private const ushort MaxBytesPerSector = 4096;

    /// <summary>
    /// Minimum valid sectors per cluster (1).
    /// </summary>
    private const byte MinSectorsPerCluster = 1;

    /// <summary>
    /// Maximum valid sectors per cluster (128).
    /// </summary>
    private const byte MaxSectorsPerCluster = 128;

    /// <summary>
    /// Minimum required buffer size (512 bytes for boot sector).
    /// </summary>
    private const int MinimumBufferSize = 512;

    /// <summary>
    /// Gets the number of bytes per sector (typically 512, 1024, 2048, or 4096).
    /// </summary>
    public ushort BytesPerSector { get; }

    /// <summary>
    /// Gets the number of sectors per cluster (power of 2: 1, 2, 4, 8, 16, 32, 64, 128).
    /// </summary>
    public byte SectorsPerCluster { get; }

    /// <summary>
    /// Gets the number of reserved sectors (includes boot sector and FSInfo).
    /// </summary>
    public ushort ReservedSectorCount { get; }

    /// <summary>
    /// Gets the number of FAT copies (typically 2).
    /// </summary>
    public byte NumberOfFats { get; }

    /// <summary>
    /// Gets the total number of sectors in the volume (32-bit value).
    /// </summary>
    public uint TotalSectors32 { get; }

    /// <summary>
    /// Gets the number of sectors per FAT.
    /// </summary>
    public uint FatSize32 { get; }

    /// <summary>
    /// Gets the root directory cluster number (typically 2).
    /// </summary>
    public uint RootCluster { get; }

    /// <summary>
    /// Gets the FSInfo sector number.
    /// </summary>
    public ushort FsInfoSector { get; }

    /// <summary>
    /// Gets the size of a cluster in bytes.
    /// </summary>
    public int ClusterSize => BytesPerSector * SectorsPerCluster;

    /// <summary>
    /// Gets the byte offset from the partition start to the first FAT.
    /// </summary>
    public long FatStartOffset => (long)ReservedSectorCount * BytesPerSector;

    /// <summary>
    /// Gets the byte offset from the partition start to the data region.
    /// </summary>
    public long DataStartOffset => (long)(ReservedSectorCount + NumberOfFats * FatSize32) * BytesPerSector;

    /// <summary>
    /// Gets the total number of data clusters in the volume.
    /// </summary>
    public uint TotalDataClusters => (uint)((TotalSectors32 - (ReservedSectorCount + NumberOfFats * FatSize32)) / SectorsPerCluster);

    /// <summary>
    /// Minimum valid total sectors for a FAT32 volume (1 MB = 2048 sectors at 512 bytes).
    /// </summary>
    private const uint MinTotalSectors = 2048;

    /// <summary>
    /// Minimum valid FAT size in sectors (for a small volume).
    /// </summary>
    private const uint MinFatSize = 1;

    /// <summary>
    /// Maximum valid FAT size in sectors (for very large volumes, e.g., 2TB with 4KB sectors).
    /// </summary>
    private const uint MaxFatSize = 16777215;

    /// <summary>
    /// Initializes a new instance of the <see cref="Fat32BootSector"/> struct by parsing the provided boot sector data.
    /// </summary>
    /// <param name="bootSectorData">The boot sector data (must be at least 512 bytes).</param>
    /// <exception cref="ArgumentException">Thrown when the buffer is too small or validation fails.</exception>
    public Fat32BootSector(ReadOnlySpan<byte> bootSectorData)
    {
        if (bootSectorData.Length < MinimumBufferSize)
            throw new ArgumentException($"Boot sector data must be at least {MinimumBufferSize} bytes.", nameof(bootSectorData));

        // Validate boot sector signature (bytes 0x55, 0xAA at offset 510; 0xAA55 as little-endian uint16)
        var signature = BinaryPrimitives.ReadUInt16LittleEndian(bootSectorData.Slice(SignatureOffset, 2));
        if (signature != BootSectorSignature)
            throw new ArgumentException($"Invalid boot sector signature: expected 0x{BootSectorSignature:X4}, got 0x{signature:X4}.", nameof(bootSectorData));

        // Parse fields from the boot sector
        BytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(bootSectorData.Slice(BytesPerSectorOffset, 2));
        SectorsPerCluster = bootSectorData[SectorsPerClusterOffset];
        ReservedSectorCount = BinaryPrimitives.ReadUInt16LittleEndian(bootSectorData.Slice(ReservedSectorCountOffset, 2));
        NumberOfFats = bootSectorData[NumberOfFatsOffset];
        TotalSectors32 = BinaryPrimitives.ReadUInt32LittleEndian(bootSectorData.Slice(TotalSectors32Offset, 4));
        FatSize32 = BinaryPrimitives.ReadUInt32LittleEndian(bootSectorData.Slice(FatSize32Offset, 4));
        RootCluster = BinaryPrimitives.ReadUInt32LittleEndian(bootSectorData.Slice(RootClusterOffset, 4));
        FsInfoSector = BinaryPrimitives.ReadUInt16LittleEndian(bootSectorData.Slice(FsInfoSectorOffset, 2));

        // Validate BytesPerSector is a power of 2 (512, 1024, 2048, or 4096)
        if (!IsPowerOfTwo(BytesPerSector) || BytesPerSector < MinBytesPerSector || BytesPerSector > MaxBytesPerSector)
            throw new ArgumentException($"Invalid BytesPerSector: {BytesPerSector}. Must be a power of 2 between {MinBytesPerSector} and {MaxBytesPerSector}.", nameof(bootSectorData));

        // Validate SectorsPerCluster is a power of 2 (1, 2, 4, 8, 16, 32, 64, or 128)
        if (!IsPowerOfTwo(SectorsPerCluster) || SectorsPerCluster < MinSectorsPerCluster || SectorsPerCluster > MaxSectorsPerCluster)
            throw new ArgumentException($"Invalid SectorsPerCluster: {SectorsPerCluster}. Must be a power of 2 between {MinSectorsPerCluster} and {MaxSectorsPerCluster}.", nameof(bootSectorData));

        // Validate NumberOfFats >= 1
        if (NumberOfFats < 1)
            throw new ArgumentException($"Invalid NumberOfFats: {NumberOfFats}. Must be at least 1.", nameof(bootSectorData));

        // Validate TotalSectors32 is reasonable (at least 1 MB worth of sectors)
        if (TotalSectors32 < MinTotalSectors)
            throw new ArgumentException($"Invalid TotalSectors32: {TotalSectors32}. Must be at least {MinTotalSectors} (1 MB).", nameof(bootSectorData));

        // Validate FatSize32 is reasonable
        if (FatSize32 < MinFatSize || FatSize32 > MaxFatSize)
            throw new ArgumentException($"Invalid FatSize32: {FatSize32}. Must be between {MinFatSize} and {MaxFatSize}.", nameof(bootSectorData));

        // Validate RootCluster (typically 2, but allow any reasonable value >= 2)
        if (RootCluster < 2)
            throw new ArgumentException($"Invalid RootCluster: {RootCluster}. Must be at least 2.", nameof(bootSectorData));

        // Validate ReservedSectorCount is reasonable (typically 0-65535)
        if (ReservedSectorCount < 1 || ReservedSectorCount > 65535)
            throw new ArgumentException($"Invalid ReservedSectorCount: {ReservedSectorCount}. Must be between 1 and 65535.", nameof(bootSectorData));

        // Validate the filesystem type string at offset 0x52 contains "FAT32" (exact match)
        var fsType = Encoding.ASCII.GetString(bootSectorData.Slice(FilesystemTypeOffset, FilesystemTypeLength));
        if (fsType != "FAT32   " && fsType != "FAT32")
            throw new ArgumentException($"Invalid filesystem type: expected 'FAT32   ' or 'FAT32' in filesystem type string, got '{fsType}'.", nameof(bootSectorData));
    }

    /// <summary>
    /// Converts a cluster number to its byte offset in the data region.
    /// </summary>
    /// <param name="clusterNumber">The cluster number (clusters are 2-indexed).</param>
    /// <returns>The byte offset from the partition start.</returns>
    /// <exception cref="ArgumentException">Thrown when cluster number is less than 2.</exception>
    public long ClusterToByteOffset(uint clusterNumber)
    {
        if (clusterNumber < 2)
            throw new ArgumentException("Cluster numbers start at 2.", nameof(clusterNumber));

        return DataStartOffset + (clusterNumber - 2) * ClusterSize;
    }

    /// <summary>
    /// Checks if a value is a power of 2.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is a power of 2; otherwise, false.</returns>
    private static bool IsPowerOfTwo(ushort value) => value != 0 && (value & (value - 1)) == 0;

    /// <summary>
    /// Checks if a value is a power of 2.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is a power of 2; otherwise, false.</returns>
    private static bool IsPowerOfTwo(byte value) => value != 0 && (value & (value - 1)) == 0;
}
