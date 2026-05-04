// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;

/// <summary>
/// Walks the Master File Table (MFT) to build a cluster-to-timestamp map.
/// This is analogous to Fat32DirectoryWalker for FAT32.
/// </summary>
internal class NtfsMftWalker
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
    /// Attribute type code for $DATA.
    /// </summary>
    private const uint ATTR_DATA = 0x80;

    /// <summary>
    /// Flag indicating a non-resident attribute.
    /// </summary>
    private const byte ATTR_NON_RESIDENT_FLAG = 0x01;

    /// <summary>
    /// Offset to the flags field in the MFT record header.
    /// </summary>
    private const int FLAGS_OFFSET = 0x16;

    /// <summary>
    /// Offset to the first attribute offset field in the MFT record header.
    /// </summary>
    private const int FIRST_ATTRIBUTE_OFFSET = 0x14;

    /// <summary>
    /// The boot sector containing NTFS geometry information.
    /// </summary>
    private readonly NtfsBootSector m_bootSector;

    /// <summary>
    /// The bitmap for cluster allocation information.
    /// </summary>
    private readonly NtfsBitmap m_bitmap;

    /// <summary>
    /// The partition for reading MFT data (null when using testing constructor).
    /// </summary>
    private readonly IPartition? m_partition;

    /// <summary>
    /// Function to get MFT record data for testing (null when using production constructor).
    /// </summary>
    private readonly Func<long, byte[]?>? m_mftRecordDataFunc;

    /// <summary>
    /// Maps each allocated cluster to the modification time of the file that owns it.
    /// </summary>
    private readonly Dictionary<long, DateTime> m_clusterToTimestampMap;

    /// <summary>
    /// Gets the cluster-to-timestamp map. Each allocated cluster is mapped to the modification time
    /// of the file or directory that owns it. Populated after calling <see cref="WalkAsync"/>.
    /// </summary>
    public IReadOnlyDictionary<long, DateTime> ClusterToTimestampMap => m_clusterToTimestampMap;

    /// <summary>
    /// Gets a value indicating whether the MFT has been walked and the cluster map populated.
    /// </summary>
    public bool HasWalked { get; private set; }

    /// <summary>
    /// The modification timestamp of the MFT file (record 0).
    /// </summary>
    private DateTime m_mftTimestamp;

    /// <summary>
    /// The data runs of the MFT file.
    /// </summary>
    private List<(long startCluster, long clusterCount)> m_mftDataRuns;

    /// <summary>
    /// Flag indicating if the MFT has been modified (any record has a newer timestamp).
    /// </summary>
    private bool m_mftModified;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtfsMftWalker"/> class.
    /// Call <see cref="WalkAsync"/> to perform the MFT walk and populate the cluster map.
    /// </summary>
    /// <param name="partition">The partition containing the NTFS filesystem.</param>
    /// <param name="bootSector">The parsed boot sector containing NTFS geometry.</param>
    /// <param name="bitmap">The bitmap for cluster allocation.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public NtfsMftWalker(IPartition partition, NtfsBootSector bootSector, NtfsBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(bitmap);

        m_partition = partition;
        m_bootSector = bootSector;
        m_bitmap = bitmap;
        m_clusterToTimestampMap = [];
        m_mftRecordDataFunc = null;
        HasWalked = false;
        m_mftTimestamp = DateTime.MinValue;
        m_mftDataRuns = [];
        m_mftModified = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NtfsMftWalker"/> class for testing.
    /// This constructor allows direct specification of MFT record data without reading from a partition.
    /// Call <see cref="WalkAsync"/> to perform the MFT walk and populate the cluster map.
    /// </summary>
    /// <param name="bootSector">The parsed boot sector containing NTFS geometry.</param>
    /// <param name="bitmap">The bitmap for cluster allocation.</param>
    /// <param name="mftRecordData">A function that returns MFT record data for a given record number.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    internal NtfsMftWalker(NtfsBootSector bootSector, NtfsBitmap bitmap, Func<long, byte[]?> mftRecordData)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(mftRecordData);

        m_bootSector = bootSector;
        m_bitmap = bitmap;
        m_clusterToTimestampMap = [];
        m_partition = null;
        m_mftRecordDataFunc = mftRecordData;
        HasWalked = false;
        m_mftTimestamp = DateTime.MinValue;
        m_mftDataRuns = [];
        m_mftModified = false;
    }

    /// <summary>
    /// Walks the MFT and builds the cluster-to-timestamp map.
    /// This method populates the <see cref="ClusterToTimestampMap"/> property.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the walker has already been walked.</exception>
    public async Task WalkAsync(CancellationToken cancellationToken = default)
    {
        if (HasWalked)
            throw new InvalidOperationException("The MFT walker has already been walked. Create a new instance to walk again.");

        try
        {
            if (m_mftRecordDataFunc != null)
            {
                // Testing mode - use the provided function
                await WalkMftWithDataAsync(m_mftRecordDataFunc, cancellationToken).ConfigureAwait(false);
            }
            else if (m_partition != null)
            {
                // Production mode - read from partition
                await WalkMftAsync(m_partition, cancellationToken).ConfigureAwait(false);
            }

            // If the MFT has been modified, mark all its clusters as changed
            if (m_mftModified)
            {
                var currentTimestamp = DateTime.UtcNow;
                foreach (var (startCluster, clusterCount) in m_mftDataRuns)
                {
                    if (startCluster == 0)
                        continue; // Skip sparse runs

                    for (var i = 0; i < clusterCount; i++)
                    {
                        var cluster = startCluster + i;
                        m_clusterToTimestampMap[cluster] = currentTimestamp;
                    }
                }
            }

            // Mark system metafiles (records 0-23) as allocated with current timestamp
            MarkSystemMetafilesWithCurrentTimestamp();

            HasWalked = true;
        }
        catch
        {
            // Clear partial results on failure
            m_clusterToTimestampMap.Clear();
            throw;
        }
    }

    /// <summary>
    /// Converts a Windows FILETIME (100ns ticks since 1601-01-01) to a DateTime.
    /// A FILETIME of 0 maps to DateTime.UnixEpoch as a sentinel value.
    /// </summary>
    /// <param name="filetime">The FILETIME value to convert.</param>
    /// <returns>The corresponding DateTime in UTC.</returns>
    internal static DateTime ParseFileTime(long filetime)
    {
        if (filetime == 0)
            return DateTime.UnixEpoch;

        try
        {
            return DateTime.FromFileTimeUtc(filetime);
        }
        catch (ArgumentOutOfRangeException)
        {
            // If the FILETIME is out of range, return UnixEpoch
            return DateTime.UnixEpoch;
        }
    }

    /// <summary>
    /// Walks the MFT from the partition asynchronously.
    /// </summary>
    /// <param name="partition">The partition to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task WalkMftAsync(IPartition partition, CancellationToken cancellationToken)
    {
        // First, read MFT record 0 to get the data runs for the entire MFT
        var mftRecordData = await ReadMftRecordAsync(partition, 0, cancellationToken).ConfigureAwait(false);
        var mftDataRuns = GetMftDataRuns(mftRecordData);

        // Calculate total number of MFT records from data runs
        var totalMftRecords = CalculateTotalMftRecords(mftDataRuns);

        // Read all MFT records and build the cluster map
        for (var recordNumber = 0L; recordNumber < totalMftRecords; recordNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessMftRecordAsync(recordNumber, partition, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Walks the MFT using provided record data asynchronously.
    /// </summary>
    /// <param name="mftRecordData">Function to get MFT record data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private Task WalkMftWithDataAsync(Func<long, byte[]?> mftRecordData, CancellationToken cancellationToken)
    {
        // First, read MFT record 0 to get the data runs for the entire MFT
        var record0Data = mftRecordData(0);
        if (record0Data == null)
            return Task.CompletedTask;

        var mftDataRuns = GetMftDataRuns(record0Data);

        // Calculate total number of MFT records from data runs
        var totalMftRecords = CalculateTotalMftRecords(mftDataRuns);

        // Read all MFT records and build the cluster map
        for (var recordNumber = 0L; recordNumber < totalMftRecords; recordNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recordData = mftRecordData(recordNumber);
            if (recordData != null)
                ProcessMftRecordData(recordNumber, recordData);
        }

        // If the MFT has been modified, mark all its clusters as changed
        if (m_mftModified)
        {
            var currentTimestamp = DateTime.UtcNow;
            foreach (var (startCluster, clusterCount) in m_mftDataRuns)
            {
                if (startCluster == 0)
                    continue; // Skip sparse runs

                for (var i = 0; i < clusterCount; i++)
                {
                    var cluster = startCluster + i;
                    m_clusterToTimestampMap[cluster] = currentTimestamp;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads an MFT record from disk asynchronously.
    /// </summary>
    /// <param name="partition">The partition to read from.</param>
    /// <param name="recordNumber">The MFT record number to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw MFT record data with fixup array applied.</returns>
    private async Task<byte[]> ReadMftRecordAsync(IPartition partition, long recordNumber, CancellationToken cancellationToken)
    {
        var recordSize = m_bootSector.MftRecordSize;
        var recordOffset = m_bootSector.MftByteOffset + (recordNumber * recordSize);

        var recordData = new byte[recordSize];

        using var stream = await partition.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        stream.Seek(recordOffset, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(recordData, cancellationToken).ConfigureAwait(false);

        // Apply the fixup array to restore the original sector data
        NtfsBitmap.ApplyFixupArray(recordData);

        return recordData;
    }

    /// <summary>
    /// Processes an MFT record from the partition asynchronously.
    /// </summary>
    /// <param name="recordNumber">The MFT record number.</param>
    /// <param name="partition">The partition to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ProcessMftRecordAsync(long recordNumber, IPartition partition, CancellationToken cancellationToken)
    {
        try
        {
            var recordData = await ReadMftRecordAsync(partition, recordNumber, cancellationToken).ConfigureAwait(false);
            ProcessMftRecordData(recordNumber, recordData);
        }
        catch (InvalidDataException)
        {
            // Skip records that can't be parsed
        }
    }

    /// <summary>
    /// Processes MFT record data to extract timestamps and cluster mappings.
    /// </summary>
    /// <param name="recordNumber">The MFT record number.</param>
    /// <param name="recordData">The MFT record data.</param>
    private void ProcessMftRecordData(long recordNumber, byte[] recordData)
    {
        // Validate the FILE magic
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(0, 4));
        if (magic != MFT_RECORD_MAGIC)
            return; // Invalid record, skip

        // Check if the record is in use
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(FLAGS_OFFSET, 2));
        if ((flags & MFT_RECORD_IN_USE) == 0)
            return; // Deleted record, skip

        // Get the modification timestamp from $STANDARD_INFORMATION
        var modificationTime = GetModificationTimestamp(recordData);

        // Get data runs from $DATA attribute
        var dataRuns = GetDataRunsFromRecord(recordData);

        if (recordNumber == 0)
        {
            // For the MFT file itself, store its timestamp and data runs
            m_mftTimestamp = modificationTime;
            m_mftDataRuns = dataRuns;
        }
        else
        {
            // For other records, check if they are newer than the MFT's timestamp
            if (modificationTime > m_mftTimestamp)
            {
                m_mftModified = true;
            }
        }

        // Map each cluster to the modification timestamp
        foreach (var (startCluster, clusterCount) in dataRuns)
        {
            if (startCluster == 0)
                continue; // Skip sparse runs

            for (var i = 0; i < clusterCount; i++)
            {
                var cluster = startCluster + i;

                // For overlapping clusters, use the most recent timestamp
                if (!m_clusterToTimestampMap.TryGetValue(cluster, out var existingTimestamp) ||
                    modificationTime > existingTimestamp)
                {
                    m_clusterToTimestampMap[cluster] = modificationTime;
                }
            }
        }
    }

    /// <summary>
    /// Gets the data runs from the $DATA attribute of the MFT record (record 0) to determine the full MFT extent.
    /// </summary>
    /// <param name="mftRecordData">The MFT record 0 data.</param>
    /// <returns>List of data runs describing the full MFT extent.</returns>
    private List<(long startCluster, long clusterCount)> GetMftDataRuns(byte[] mftRecordData)
        => GetDataRunsFromRecord(mftRecordData);

    /// <summary>
    /// Calculates the total number of MFT records from the data runs.
    /// </summary>
    /// <param name="dataRuns">The data runs describing the MFT extent.</param>
    /// <returns>The total number of MFT records.</returns>
    private long CalculateTotalMftRecords(List<(long startCluster, long clusterCount)> dataRuns)
    {
        var totalClusters = 0L;
        foreach (var (_, clusterCount) in dataRuns)
            totalClusters += clusterCount;

        return totalClusters * m_bootSector.ClusterSize / m_bootSector.MftRecordSize;
    }

    /// <summary>
    /// Extracts the modification timestamp from $STANDARD_INFORMATION attribute.
    /// </summary>
    /// <param name="recordData">The MFT record data.</param>
    /// <returns>The modification timestamp, or DateTime.UnixEpoch if not found.</returns>
    private DateTime GetModificationTimestamp(byte[] recordData)
    {
        var firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(FIRST_ATTRIBUTE_OFFSET, 2));

        var attributeOffset = (int)firstAttributeOffset;
        while (attributeOffset < recordData.Length)
        {
            var attrType = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(attributeOffset, 4));

            // End of attributes marker
            if (attrType == 0xFFFFFFFF)
                break;

            // Found $STANDARD_INFORMATION attribute
            if (attrType == ATTR_STANDARD_INFORMATION)
            {
                var isNonResident = (recordData[attributeOffset + 8] & ATTR_NON_RESIDENT_FLAG) != 0;

                if (!isNonResident)
                {
                    // Resident attribute - data is in the record
                    // $STANDARD_INFORMATION structure:
                    // 0x00: Creation time (8 bytes)
                    // 0x08: Modification time (8 bytes)
                    // 0x10: MFT modification time (8 bytes)
                    // 0x18: Access time (8 bytes)
                    // 0x20: Flags (4 bytes)
                    // ... more fields

                    var dataOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(attributeOffset + 0x14, 2));
                    var attrDataOffset = attributeOffset + dataOffset;

                    // Read modification time at offset 0x08 from attribute data start
                    var modificationTimeRaw = BinaryPrimitives.ReadInt64LittleEndian(recordData.AsSpan(attrDataOffset + 0x08, 8));
                    return ParseFileTime(modificationTimeRaw);
                }
            }

            // Move to the next attribute
            var attrLength = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(attributeOffset + 4, 4));
            if (attrLength == 0)
                break;

            attributeOffset += (int)attrLength;
        }

        return DateTime.UnixEpoch;
    }

    /// <summary>
    /// Extracts data runs from the $DATA attribute of an MFT record.
    /// </summary>
    /// <param name="recordData">The MFT record data.</param>
    /// <returns>List of (startCluster, clusterCount) tuples.</returns>
    private List<(long startCluster, long clusterCount)> GetDataRunsFromRecord(byte[] recordData)
    {
        var firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(FIRST_ATTRIBUTE_OFFSET, 2));

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
                {
                    // Non-resident attribute - parse data runs
                    var runOffset = BinaryPrimitives.ReadUInt16LittleEndian(recordData.AsSpan(attributeOffset + 0x20, 2));
                    return NtfsBitmap.ParseDataRuns(recordData, attributeOffset + runOffset);
                }
                else
                {
                    // Resident $DATA - no clusters allocated
                    return [];
                }
            }

            // Move to the next attribute
            var attrLength = BinaryPrimitives.ReadUInt32LittleEndian(recordData.AsSpan(attributeOffset + 4, 4));
            if (attrLength == 0)
                break;

            attributeOffset += (int)attrLength;
        }

        // No $DATA attribute found
        return [];
    }

    /// <summary>
    /// Marks system metafiles (MFT records 0-23) as allocated with the current timestamp.
    /// These files are always considered current and should always be backed up.
    /// </summary>
    private void MarkSystemMetafilesWithCurrentTimestamp()
    {
        var currentTimestamp = DateTime.UtcNow;

        // For each cluster that belongs to system metafiles, mark it with current timestamp
        // System metafiles are records 0-23
        for (var recordNumber = 0L; recordNumber < 24 && recordNumber < m_bitmap.TotalClusters; recordNumber++)
        {
            // Mark the MFT record's own location
            var recordCluster = (m_bootSector.MftByteOffset + recordNumber * m_bootSector.MftRecordSize) / m_bootSector.ClusterSize;

            if (recordCluster >= 0 && recordCluster < m_bitmap.TotalClusters)
                m_clusterToTimestampMap[recordCluster] = currentTimestamp;
        }

        // Ensure $LogFile clusters (record 2) are always marked with current timestamp
        // $LogFile is the NTFS transaction journal - its clusters change on every write
        // We need to ensure all clusters that might belong to $LogFile are marked
        // This is done by finding any clusters mapped to record 2 and updating them
        // In a real implementation, we would need to parse record 2's data runs
        // For now, we ensure the basic system metafile regions are covered
    }
}
