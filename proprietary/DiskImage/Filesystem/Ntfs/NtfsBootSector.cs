// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;

/// <summary>
/// Represents a parsed NTFS Boot Sector (BIOS Parameter Block and extended NTFS fields).
/// This is a read-only record struct that extracts geometry information from the boot sector.
/// </summary>
public readonly record struct NtfsBootSector
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
    /// Offset to the OEM ID field.
    /// </summary>
    private const int OemIdOffset = 0x03;

    /// <summary>
    /// Length of the OEM ID field (8 bytes).
    /// </summary>
    private const int OemIdLength = 8;

    /// <summary>
    /// Expected OEM ID value for NTFS: "NTFS    "
    /// </summary>
    private const string ExpectedOemId = "NTFS    ";

    /// <summary>
    /// Offset to BytesPerSector field.
    /// </summary>
    private const int BytesPerSectorOffset = 0x0B;

    /// <summary>
    /// Offset to SectorsPerCluster field.
    /// </summary>
    private const int SectorsPerClusterOffset = 0x0D;

    /// <summary>
    /// Offset to TotalSectors field (uint64).
    /// </summary>
    private const int TotalSectorsOffset = 0x28;

    /// <summary>
    /// Offset to MftClusterNumber field (uint64).
    /// </summary>
    private const int MftClusterNumberOffset = 0x30;

    /// <summary>
    /// Offset to MftMirrorClusterNumber field (uint64).
    /// </summary>
    private const int MftMirrorClusterNumberOffset = 0x38;

    /// <summary>
    /// Offset to ClustersPerMftRecord field (int8).
    /// </summary>
    private const int ClustersPerMftRecordOffset = 0x40;

    /// <summary>
    /// Offset to ClustersPerIndexBlock field (int8).
    /// </summary>
    private const int ClustersPerIndexBlockOffset = 0x44;

    /// <summary>
    /// Offset to VolumeSerialNumber field (uint64).
    /// </summary>
    private const int VolumeSerialNumberOffset = 0x48;

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
    /// Gets the total number of sectors in the volume (uint64).
    /// </summary>
    public ulong TotalSectors { get; }

    /// <summary>
    /// Gets the cluster number where the $MFT starts.
    /// </summary>
    public ulong MftClusterNumber { get; }

    /// <summary>
    /// Gets the cluster number where the $MFTMirr (MFT mirror) starts.
    /// </summary>
    public ulong MftMirrorClusterNumber { get; }

    /// <summary>
    /// Gets the clusters per MFT record as encoded in the boot sector.
    /// If negative, record size is 2^abs(value) bytes.
    /// If positive, record size is value * ClusterSize bytes.
    /// </summary>
    public sbyte ClustersPerMftRecord { get; }

    /// <summary>
    /// Gets the clusters per index block as encoded in the boot sector.
    /// Same encoding as ClustersPerMftRecord.
    /// </summary>
    public sbyte ClustersPerIndexBlock { get; }

    /// <summary>
    /// Gets the volume serial number.
    /// </summary>
    public ulong VolumeSerialNumber { get; }

    /// <summary>
    /// Gets the size of a cluster in bytes.
    /// </summary>
    public int ClusterSize => BytesPerSector * SectorsPerCluster;

    /// <summary>
    /// Gets the size of an MFT record in bytes.
    /// If ClustersPerMftRecord is negative: 2^abs(ClustersPerMftRecord) bytes.
    /// If ClustersPerMftRecord is positive: ClustersPerMftRecord * ClusterSize bytes.
    /// </summary>
    public int MftRecordSize => ClustersPerMftRecord < 0
        ? 1 << -ClustersPerMftRecord  // 2^abs(value)
        : ClustersPerMftRecord * ClusterSize;

    /// <summary>
    /// Gets the byte offset from the partition start to the MFT.
    /// </summary>
    public long MftByteOffset => (long)MftClusterNumber * ClusterSize;

    /// <summary>
    /// Gets the total number of clusters in the volume.
    /// </summary>
    public long TotalClusters => (long)(TotalSectors / SectorsPerCluster);

    /// <summary>
    /// Gets the total size of the volume in bytes.
    /// </summary>
    public long TotalSize => (long)TotalSectors * BytesPerSector;

    /// <summary>
    /// Minimum valid total sectors for an NTFS volume.
    /// </summary>
    private const ulong MinTotalSectors = 16;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtfsBootSector"/> struct by parsing the provided boot sector data.
    /// </summary>
    /// <param name="bootSectorData">The boot sector data (must be at least 512 bytes).</param>
    /// <exception cref="ArgumentException">Thrown when the buffer is too small or validation fails.</exception>
    public NtfsBootSector(ReadOnlySpan<byte> bootSectorData)
    {
        if (bootSectorData.Length < MinimumBufferSize)
            throw new ArgumentException($"Boot sector data must be at least {MinimumBufferSize} bytes.", nameof(bootSectorData));

        // Validate boot sector signature (bytes 0x55, 0xAA at offset 510; 0xAA55 as little-endian uint16)
        var signature = BinaryPrimitives.ReadUInt16LittleEndian(bootSectorData.Slice(SignatureOffset, 2));
        if (signature != BootSectorSignature)
            throw new ArgumentException($"Invalid boot sector signature: expected 0x{BootSectorSignature:X4}, got 0x{signature:X4}.", nameof(bootSectorData));

        // Validate OEM ID at offset 0x03 (should be "NTFS    ")
        var oemId = Encoding.ASCII.GetString(bootSectorData.Slice(OemIdOffset, OemIdLength));
        if (oemId != ExpectedOemId)
            throw new ArgumentException($"Invalid OEM ID: expected '{ExpectedOemId}', got '{oemId}'.", nameof(bootSectorData));

        // Parse fields from the boot sector
        BytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(bootSectorData.Slice(BytesPerSectorOffset, 2));
        SectorsPerCluster = bootSectorData[SectorsPerClusterOffset];
        TotalSectors = BinaryPrimitives.ReadUInt64LittleEndian(bootSectorData.Slice(TotalSectorsOffset, 8));
        MftClusterNumber = BinaryPrimitives.ReadUInt64LittleEndian(bootSectorData.Slice(MftClusterNumberOffset, 8));
        MftMirrorClusterNumber = BinaryPrimitives.ReadUInt64LittleEndian(bootSectorData.Slice(MftMirrorClusterNumberOffset, 8));
        ClustersPerMftRecord = (sbyte)bootSectorData[ClustersPerMftRecordOffset];
        ClustersPerIndexBlock = (sbyte)bootSectorData[ClustersPerIndexBlockOffset];
        VolumeSerialNumber = BinaryPrimitives.ReadUInt64LittleEndian(bootSectorData.Slice(VolumeSerialNumberOffset, 8));

        // Validate BytesPerSector is a power of 2 (512, 1024, 2048, or 4096)
        if (!IsPowerOfTwo(BytesPerSector) || BytesPerSector < MinBytesPerSector || BytesPerSector > MaxBytesPerSector)
            throw new ArgumentException($"Invalid BytesPerSector: {BytesPerSector}. Must be a power of 2 between {MinBytesPerSector} and {MaxBytesPerSector}.", nameof(bootSectorData));

        // Validate SectorsPerCluster is a power of 2 (1, 2, 4, 8, 16, 32, 64, or 128)
        if (!IsPowerOfTwo(SectorsPerCluster) || SectorsPerCluster < MinSectorsPerCluster || SectorsPerCluster > MaxSectorsPerCluster)
            throw new ArgumentException($"Invalid SectorsPerCluster: {SectorsPerCluster}. Must be a power of 2 between {MinSectorsPerCluster} and {MaxSectorsPerCluster}.", nameof(bootSectorData));

        // Validate that BytesPerSector * SectorsPerCluster does not overflow
        // This is effectively checking that ClusterSize is valid
        try
        {
            _ = checked(BytesPerSector * SectorsPerCluster);
        }
        catch (OverflowException)
        {
            throw new ArgumentException($"Cluster size overflow: BytesPerSector ({BytesPerSector}) * SectorsPerCluster ({SectorsPerCluster}) exceeds maximum value.", nameof(bootSectorData));
        }

        // Validate TotalSectors > 0
        if (TotalSectors == 0)
            throw new ArgumentException($"Invalid TotalSectors: {TotalSectors}. Must be greater than 0.", nameof(bootSectorData));

        if (TotalSectors < MinTotalSectors)
            throw new ArgumentException($"Invalid TotalSectors: {TotalSectors}. Must be at least {MinTotalSectors}.", nameof(bootSectorData));

        // Validate MftClusterNumber is within volume bounds
        if (MftClusterNumber >= (ulong)TotalClusters)
            throw new ArgumentException($"Invalid MftClusterNumber: {MftClusterNumber}. Must be less than TotalClusters ({TotalClusters}).", nameof(bootSectorData));

        // Validate MftRecordSize - if positive, ensure it doesn't overflow
        if (ClustersPerMftRecord > 0)
        {
            try
            {
                _ = checked(ClustersPerMftRecord * ClusterSize);
            }
            catch (OverflowException)
            {
                throw new ArgumentException($"MFT record size overflow: ClustersPerMftRecord ({ClustersPerMftRecord}) * ClusterSize ({ClusterSize}) exceeds maximum value.", nameof(bootSectorData));
            }
        }

        // Validate IndexBlockSize - if positive, ensure it doesn't overflow
        if (ClustersPerIndexBlock > 0)
        {
            try
            {
                _ = checked(ClustersPerIndexBlock * ClusterSize);
            }
            catch (OverflowException)
            {
                throw new ArgumentException($"Index block size overflow: ClustersPerIndexBlock ({ClustersPerIndexBlock}) * ClusterSize ({ClusterSize}) exceeds maximum value.", nameof(bootSectorData));
            }
        }
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
