// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;

/// <summary>
/// Reads and provides access to the NTFS $Bitmap file, which tracks cluster allocation.
/// Each bit in the bitmap represents one cluster: 1 = allocated, 0 = free.
/// </summary>
internal class NtfsBitmap
{
    /// <summary>
    /// Magic number "FILE" at the start of an MFT record (little-endian: 0x454C4946).
    /// </summary>
    private const uint MFT_RECORD_MAGIC = 0x454C4946;

    /// <summary>
    /// MFT record flag indicating the record is in use.
    /// </summary>
    private const ushort MFT_RECORD_IN_USE = 0x0001;

    /// <summary>
    /// Attribute type code for $STANDARD_INFORMATION.
    /// </summary>
    private const uint ATTR_STANDARD_INFORMATION = 0x10;

    /// <summary>
    /// Attribute type code for $ATTRIBUTE_LIST.
    /// </summary>
    private const uint ATTR_ATTRIBUTE_LIST = 0x20;

    /// <summary>
    /// Attribute type code for $FILE_NAME.
    /// </summary>
    private const uint ATTR_FILE_NAME = 0x30;

    /// <summary>
    /// Attribute type code for $DATA.
    /// </summary>
    private const uint ATTR_DATA = 0x80;

    /// <summary>
    /// Flag indicating a non-resident attribute.
    /// </summary>
    private const byte ATTR_NON_RESIDENT_FLAG = 0x01;

    /// <summary>
    /// The boot sector containing NTFS geometry information.
    /// </summary>
    private readonly NtfsBootSector m_bootSector;

    /// <summary>
    /// The raw bitmap data.
    /// </summary>
    private readonly byte[] m_bitmapData;

    /// <summary>
    /// BitArray for fast cluster allocation lookup.
    /// </summary>
    private readonly BitArray m_allocationBitmap;

    /// <summary>
    /// Total number of clusters in the volume.
    /// </summary>
    private readonly long m_totalClusters;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtfsBitmap"/> class by reading the $Bitmap from disk.
    /// </summary>
    /// <param name="partition">The partition containing the NTFS filesystem.</param>
    /// <param name="bootSector">The parsed boot sector containing NTFS geometry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown if partition or bootSector is null.</exception>
    /// <exception cref="InvalidDataException">Thrown if the $Bitmap record cannot be parsed.</exception>
    public NtfsBitmap(IPartition partition, NtfsBootSector bootSector, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partition);
        m_bootSector = bootSector;
        m_totalClusters = bootSector.TotalClusters;

        // Calculate the size of the bitmap in bytes (1 bit per cluster, rounded up)
        var bitmapSizeInBytes = (int)((m_totalClusters + 7) / 8);

        // Read the $Bitmap MFT record (record 6)
        var bitmapRecordData = ReadMftRecord(partition, 6, cancellationToken);

        // Parse the $DATA attribute to get the bitmap data
        m_bitmapData = ExtractBitmapData(partition, bitmapRecordData, bitmapSizeInBytes, cancellationToken);

        // Build the allocation bitmap
        m_allocationBitmap = BuildAllocationBitmap();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NtfsBitmap"/> class from raw bitmap data.
    /// This constructor is primarily used for testing.
    /// </summary>
    /// <param name="bootSector">The parsed boot sector containing NTFS geometry.</param>
    /// <param name="bitmapData">The raw bitmap data.</param>
    public NtfsBitmap(NtfsBootSector bootSector, byte[] bitmapData)
    {
        ArgumentNullException.ThrowIfNull(bitmapData);
        m_bootSector = bootSector;
        m_bitmapData = bitmapData;
        m_totalClusters = bootSector.TotalClusters;
        m_allocationBitmap = BuildAllocationBitmap();
    }

    /// <summary>
    /// Gets the total number of clusters in the volume.
    /// </summary>
    public long TotalClusters => m_totalClusters;

    /// <summary>
    /// Gets the number of allocated (in-use) clusters.
    /// </summary>
    public long AllocatedClusters
    {
        get
        {
            var count = 0;
            for (var i = 0; i < m_allocationBitmap.Length; i++)
                if (m_allocationBitmap[i])
                    count++;

            return count;
        }
    }

    /// <summary>
    /// Gets the number of free clusters.
    /// </summary>
    public long FreeClusters => m_totalClusters - AllocatedClusters;

    /// <summary>
    /// Checks if a cluster is allocated (in use).
    /// </summary>
    /// <param name="clusterNumber">The cluster number to check.</param>
    /// <returns>True if the cluster is allocated; otherwise, false.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if cluster number is out of range.</exception>
    public bool IsClusterAllocated(long clusterNumber)
    {
        if (clusterNumber < 0 || clusterNumber >= m_totalClusters)
            throw new ArgumentOutOfRangeException(nameof(clusterNumber), $"Cluster number {clusterNumber} is out of range. Valid range is 0 to {m_totalClusters - 1}.");

        return m_allocationBitmap[(int)clusterNumber];
    }

    /// <summary>
    /// Reads an MFT record from disk.
    /// </summary>
    /// <param name="partition">The partition to read from.</param>
    /// <param name="recordNumber">The MFT record number to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw MFT record data with fixup array applied.</returns>
    private byte[] ReadMftRecord(IPartition partition, long recordNumber, CancellationToken cancellationToken)
    {
        var recordSize = m_bootSector.MftRecordSize;
        var recordOffset = m_bootSector.MftByteOffset + (recordNumber * recordSize);

        var recordData = new byte[recordSize];

        using var stream = partition.OpenReadAsync(cancellationToken).Result;
        stream.Seek(recordOffset, SeekOrigin.Begin);
        stream.ReadExactly(recordData);

        // Apply the fixup array to restore the original sector data
        ApplyFixupArray(recordData);

        return recordData;
    }

    /// <summary>
    /// Extracts the bitmap data from the $Bitmap MFT record.
    /// </summary>
    /// <param name="partition">The partition to read from.</param>
    /// <param name="recordData">The MFT record data.</param>
    /// <param name="expectedSize">The expected size of the bitmap data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bitmap data.</returns>
    private byte[] ExtractBitmapData(IPartition partition, byte[] recordData, int expectedSize, CancellationToken cancellationToken)
    {
        // Validate the FILE magic
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(0, 4));
        if (magic != MFT_RECORD_MAGIC)
            throw new InvalidDataException($"Invalid MFT record magic: expected 0x{MFT_RECORD_MAGIC:X8}, got 0x{magic:X8}.");

        // Get the offset to the first attribute
        var firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(0x14, 2));

        // Find the $DATA attribute (type 0x80)
        var attributeOffset = (int)firstAttributeOffset;
        while (attributeOffset < recordData.Length)
        {
            var attrType = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(attributeOffset, 4));

            // End of attributes marker
            if (attrType == 0xFFFFFFFF)
                break;

            // Found $DATA attribute
            if (attrType == ATTR_DATA)
            {
                var isNonResident = (recordData[attributeOffset + 8] & ATTR_NON_RESIDENT_FLAG) != 0;

                if (isNonResident)
                    return ReadNonResidentAttributeData(partition, recordData, attributeOffset, expectedSize, cancellationToken);
                else
                    return ReadResidentAttributeData(recordData, attributeOffset);
            }

            // Move to the next attribute
            var attrLength = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(attributeOffset + 4, 4));
            if (attrLength == 0)
                break;

            attributeOffset += (int)attrLength;
        }

        throw new InvalidDataException("$DATA attribute not found in $Bitmap MFT record.");
    }

    /// <summary>
    /// Reads data from a resident attribute.
    /// </summary>
    /// <param name="recordData">The MFT record data.</param>
    /// <param name="attributeOffset">The offset to the attribute.</param>
    /// <returns>The attribute data.</returns>
    private static byte[] ReadResidentAttributeData(byte[] recordData, int attributeOffset)
    {
        // Resident attribute header:
        // 0x00: Attribute type (4 bytes)
        // 0x04: Attribute length (4 bytes)
        // 0x08: Non-resident flag (1 byte)
        // 0x09: Name length (1 byte)
        // 0x0A: Name offset (2 bytes)
        // 0x0C: Flags (2 bytes)
        // 0x0E: Attribute ID (2 bytes)
        // 0x10: Data length (4 bytes)
        // 0x14: Data offset (2 bytes)
        // 0x16: Reserved (1 byte)
        // 0x17: Reserved (1 byte)
        // Data starts at offset specified by data offset field

        var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(attributeOffset + 0x10, 4));
        var dataOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(attributeOffset + 0x14, 2));

        var data = new byte[dataLength];
        Buffer.BlockCopy(recordData, attributeOffset + dataOffset, data, 0, (int)dataLength);
        return data;
    }

    /// <summary>
    /// Reads data from a non-resident attribute by following data runs.
    /// </summary>
    /// <param name="partition">The partition to read from.</param>
    /// <param name="recordData">The MFT record data.</param>
    /// <param name="attributeOffset">The offset to the attribute.</param>
    /// <param name="expectedSize">The expected size of the data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attribute data.</returns>
    private byte[] ReadNonResidentAttributeData(IPartition partition, byte[] recordData, int attributeOffset, int expectedSize, CancellationToken cancellationToken)
    {
        // Non-resident attribute header:
        // 0x00: Attribute type (4 bytes)
        // 0x04: Attribute length (4 bytes)
        // 0x08: Non-resident flag (1 byte)
        // 0x09: Name length (1 byte)
        // 0x0A: Name offset (2 bytes)
        // 0x0C: Flags (2 bytes)
        // 0x0E: Attribute ID (2 bytes)
        // 0x10: Starting VCN (8 bytes)
        // 0x18: Ending VCN (8 bytes)
        // 0x20: Run offset (2 bytes) - offset to data run list from start of attribute
        // 0x22: Compression unit size (2 bytes)
        // 0x24: Padding (4 bytes)
        // 0x28: Allocated size (8 bytes)
        // 0x30: Data size (8 bytes)
        // 0x38: Initialized size (8 bytes)
        // 0x40: Data runs start at offset specified by run offset field. If the attribute is named, then the name starts here. Otherwise, the data runs start here.

        var runOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(attributeOffset + 0x20, 2));
        var dataSize = BinaryPrimitives.ReadUInt64LittleEndian(recordData.AsSpan(attributeOffset + 0x30, 8));

        // Parse the data runs
        var dataRuns = ParseDataRuns(recordData, attributeOffset + runOffset);

        // Read the data from the clusters specified by the data runs
        var result = new byte[Math.Min(expectedSize, (long)dataSize)];
        var resultOffset = 0;

        using var stream = partition.OpenReadAsync(cancellationToken).Result;

        foreach (var (startCluster, clusterCount) in dataRuns)
        {
            if (startCluster == 0)
            {
                // Sparse run (unallocated extent) - fill with zeros
                var sparseBytes = (int)Math.Min(clusterCount * m_bootSector.ClusterSize, result.Length - resultOffset);
                Array.Clear(result, resultOffset, sparseBytes);
                resultOffset += sparseBytes;
            }
            else
            {
                var bytesToRead = (int)Math.Min(clusterCount * m_bootSector.ClusterSize, result.Length - resultOffset);
                var byteOffset = startCluster * m_bootSector.ClusterSize;

                stream.Seek(byteOffset, SeekOrigin.Begin);
                stream.ReadExactly(result, resultOffset, bytesToRead);
                resultOffset += bytesToRead;
            }

            if (resultOffset >= result.Length)
                break;
        }

        return result;
    }

    /// <summary>
    /// Parses the data run list from an attribute.
    /// </summary>
    /// <param name="recordData">The MFT record data containing the data runs.</param>
    /// <param name="runsOffset">The offset to the start of the data run list.</param>
    /// <returns>A list of (startCluster, clusterCount) tuples.</returns>
    internal static List<(long startCluster, long clusterCount)> ParseDataRuns(byte[] recordData, int runsOffset)
    {
        var runs = new List<(long startCluster, long clusterCount)>();
        long currentCluster = 0;
        var offset = runsOffset;

        while (offset < recordData.Length)
        {
            var header = recordData[offset++];

            // End of data runs
            if (header == 0)
                break;

            // Extract length and offset byte counts from header
            var lengthBytes = header & 0x0F;
            var offsetBytes = (header >> 4) & 0x0F;

            if (lengthBytes == 0 || lengthBytes > 8 || offsetBytes > 8)
                throw new InvalidDataException($"Invalid data run header: 0x{header:X2}");

            // Read the run length (number of clusters)
            long runLength = 0;
            for (var i = 0; i < lengthBytes; i++)
                runLength |= (long)recordData[offset++] << (i * 8);

            // Read the run offset (signed, relative to previous run)
            long runOffsetValue = 0;
            if (offsetBytes > 0)
            {
                // Check if the offset is negative (sign bit is set in the last byte)
                var isNegative = (recordData[offset + offsetBytes - 1] & 0x80) != 0;

                for (var i = 0; i < offsetBytes; i++)
                    runOffsetValue |= (long)recordData[offset++] << (i * 8);

                // Sign-extend if negative
                if (isNegative)
                    runOffsetValue |= -1L << (offsetBytes * 8);
            }

            // Calculate absolute cluster number
            currentCluster += runOffsetValue;

            // A zero offset indicates a sparse run (unallocated extent)
            if (runOffsetValue == 0)
                runs.Add((0, runLength)); // Sparse run
            else
                runs.Add((currentCluster, runLength));
        }

        return runs;
    }

    /// <summary>
    /// Parses the data run list from a ReadOnlySpan.
    /// </summary>
    /// <param name="data">The data containing the data runs.</param>
    /// <param name="runsOffset">The offset to the start of the data run list.</param>
    /// <returns>A list of (startCluster, clusterCount) tuples.</returns>
    internal static List<(long startCluster, long clusterCount)> ParseDataRuns(ReadOnlySpan<byte> data, int runsOffset)
    {
        var runs = new List<(long startCluster, long clusterCount)>();
        var currentCluster = 0L;
        var offset = runsOffset;

        while (offset < data.Length)
        {
            var header = data[offset++];

            // End of data runs
            if (header == 0)
                break;

            // Extract length and offset byte counts from header
            var lengthBytes = header & 0x0F;
            var offsetBytes = (header >> 4) & 0x0F;

            if (lengthBytes == 0 || lengthBytes > 8 || offsetBytes > 8)
                throw new InvalidDataException($"Invalid data run header: 0x{header:X2}");

            // Read the run length (number of clusters)
            var runLength = 0L;
            for (var i = 0; i < lengthBytes; i++)
                runLength |= (long)data[offset++] << (i * 8);

            // Read the run offset (signed, relative to previous run)
            var runOffsetValue = 0L;
            if (offsetBytes > 0)
            {
                // Check if the offset is negative (sign bit is set in the last byte)
                var isNegative = (data[offset + offsetBytes - 1] & 0x80) != 0;

                for (var i = 0; i < offsetBytes; i++)
                    runOffsetValue |= (long)data[offset++] << (i * 8);

                // Sign-extend if negative
                if (isNegative)
                    runOffsetValue |= -1L << (offsetBytes * 8);
            }

            // Calculate absolute cluster number
            currentCluster += runOffsetValue;

            // A zero offset indicates a sparse run (unallocated extent)
            if (runOffsetValue == 0)
                runs.Add((0, runLength)); // Sparse run
            else
                runs.Add((currentCluster, runLength));
        }

        return runs;
    }

    /// <summary>
    /// Applies the fixup array to restore the original sector data in an MFT record.
    /// Every NTFS multi-sector structure uses a fixup array for integrity checking.
    /// The last two bytes of each 512-byte sector are replaced with a sequence number during write;
    /// the original values are stored in the fixup array.
    /// </summary>
    /// <param name="recordData">The MFT record data to fix up.</param>
    /// <exception cref="InvalidDataException">Thrown if fixup validation fails.</exception>
    internal static void ApplyFixupArray(byte[] recordData)
    {
        // Fixup array is at offset 0x04 in the MFT record header:
        // 0x04: Fixup offset (2 bytes) - offset to fixup array
        // 0x06: Fixup count (2 bytes) - number of entries in fixup array (also number of sectors)

        var fixupOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(0x04, 2));
        var fixupCount = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(0x06, 2));

        if (fixupCount == 0)
            return; // No fixup needed

        // The fixup array starts with the sequence number (2 bytes), followed by the original values
        var sequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(fixupOffset, 2));

        // Apply fixup to each sector
        for (var i = 1; i < fixupCount; i++)
        {
            var sectorOffset = i * 512 - 2; // Last 2 bytes of each 512-byte sector

            // Verify the sequence number matches
            var sectorSequence = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(sectorOffset, 2));
            if (sectorSequence != sequenceNumber)
                throw new InvalidDataException(
                    $"Fixup sequence number mismatch at sector {i}: expected 0x{sequenceNumber:X4}, got 0x{sectorSequence:X4}.");

            // Restore the original value from the fixup array
            var originalValue = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(fixupOffset + i * 2, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(recordData.AsSpan(sectorOffset, 2), originalValue);
        }
    }

    /// <summary>
    /// Applies the fixup array to restore the original sector data in an MFT record (span version).
    /// </summary>
    /// <param name="recordData">The MFT record data to fix up.</param>
    /// <exception cref="InvalidDataException">Thrown if fixup validation fails.</exception>
    internal static void ApplyFixupArray(Span<byte> recordData)
    {
        var fixupOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.Slice(0x04, 2));
        var fixupCount = BinaryPrimitives.ReadUInt16LittleEndian(recordData.Slice(0x06, 2));

        if (fixupCount == 0)
            return;

        var sequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(recordData.Slice(fixupOffset, 2));

        for (var i = 1; i < fixupCount; i++)
        {
            var sectorOffset = i * 512 - 2;

            var sectorSequence = BinaryPrimitives.ReadUInt16LittleEndian(recordData.Slice(sectorOffset, 2));
            if (sectorSequence != sequenceNumber)
                throw new InvalidDataException(
                    $"Fixup sequence number mismatch at sector {i}: expected 0x{sequenceNumber:X4}, got 0x{sectorSequence:X4}.");

            var originalValue = BinaryPrimitives.ReadUInt16LittleEndian(recordData.Slice(fixupOffset + i * 2, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(recordData.Slice(sectorOffset, 2), originalValue);
        }
    }

    /// <summary>
    /// Builds the cluster allocation bitmap from the bitmap data.
    /// </summary>
    /// <returns>A BitArray where each bit indicates whether the corresponding cluster is allocated.</returns>
    private BitArray BuildAllocationBitmap()
    {
        var bitmap = new BitArray((int)m_totalClusters);

        for (var i = 0; i < m_totalClusters; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;

            if (byteIndex < m_bitmapData.Length)
                // NTFS uses LSB-first ordering: bit 0 of byte 0 is cluster 0
                bitmap[i] = (m_bitmapData[byteIndex] & (1 << bitIndex)) != 0;
            else
                bitmap[i] = false; // Beyond bitmap size, treat as free
        }

        return bitmap;
    }
}