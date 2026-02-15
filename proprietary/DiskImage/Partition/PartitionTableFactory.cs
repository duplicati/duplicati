using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Factory for creating partition table instances with auto-detection.
/// Parses the MBR first to determine if the disk uses GPT or MBR partition tables.
/// </summary>
public static class PartitionTableFactory
{
    // MBR constants (from PartitionConstants)
    private const int MbrSize = PartitionConstants.MbrSize;
    private const ushort MbrBootSignature = PartitionConstants.MbrBootSignature;
    private const byte ProtectiveMbrType = PartitionConstants.ProtectiveMbrType;

    // GPT constants (from PartitionConstants)
    private const int HeaderSize = PartitionConstants.GptHeaderSize;
    private const long GptSignature = PartitionConstants.GptSignature;

    /// <summary>
    /// Creates a partition table instance by auto-detecting the partition table type.
    /// Reads the MBR first, which determines if we have a GPT or MBR partition table.
    /// </summary>
    /// <param name="disk">The raw disk to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The appropriate partition table instance (GPT or MBR), or null if detection fails.</returns>
    public static async Task<IPartitionTable?> CreateAsync(IRawDisk disk, CancellationToken cancellationToken)
    {
        if (disk == null)
            throw new ArgumentNullException(nameof(disk));

        // Read the first sector (LBA 0 - MBR)
        var mbrBytes = new byte[MbrSize];
        using var stream = await disk.ReadBytesAsync(0, MbrSize, cancellationToken).ConfigureAwait(false);
        await stream.ReadAtLeastAsync(mbrBytes, MbrSize, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Detect partition table type from MBR
        var tableType = DetectPartitionTableType(mbrBytes);

        return tableType switch
        {
            PartitionTableType.GPT => await CreateGPTAsync(disk, mbrBytes, cancellationToken).ConfigureAwait(false),
            PartitionTableType.MBR => await CreateMBRAsync(disk, mbrBytes, cancellationToken).ConfigureAwait(false),
            _ => new UnknownPartitionTable(disk)
        };
    }

    /// <summary>
    /// Creates a partition table instance from raw byte array with auto-detection.
    /// </summary>
    /// <param name="bytes">The raw disk bytes.</param>
    /// <param name="sectorSize">The sector size (typically 512 or 4096).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The appropriate partition table instance (GPT or MBR), or null if detection fails.</returns>
    public static async Task<IPartitionTable?> CreateAsync(byte[] bytes, int sectorSize, CancellationToken cancellationToken)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        if (bytes.Length < MbrSize)
            throw new ArgumentException($"Byte array must be at least {MbrSize} bytes long.", nameof(bytes));

        if (sectorSize <= 0)
            throw new ArgumentException("Sector size must be positive.", nameof(sectorSize));

        // Read the first sector (LBA 0 - MBR)
        var mbrBytes = bytes[0..sectorSize];

        // Detect partition table type from MBR
        var tableType = DetectPartitionTableType(mbrBytes);

        return tableType switch
        {
            PartitionTableType.GPT => await CreateGPTAsync(bytes, sectorSize, cancellationToken).ConfigureAwait(false),
            PartitionTableType.MBR => await CreateMBRAsync(mbrBytes, sectorSize, cancellationToken).ConfigureAwait(false),
            _ => new UnknownPartitionTable(null)
        };
    }

    /// <summary>
    /// Detects the partition table type by examining the MBR.
    /// </summary>
    private static PartitionTableType DetectPartitionTableType(byte[] mbrBytes)
    {
        // Check for valid MBR boot signature
        ushort bootSignature = BitConverter.ToUInt16(mbrBytes, 510);
        if (bootSignature != MbrBootSignature)
            return PartitionTableType.Unknown;

        // Check for GPT protective MBR (type 0xEE in first partition entry)
        byte partitionType = mbrBytes[450];

        // If first partition entry has type 0xEE, this is likely a GPT disk
        if (partitionType == ProtectiveMbrType)
        {
            return PartitionTableType.GPT;
        }

        // Otherwise, it's a traditional MBR disk
        return PartitionTableType.MBR;
    }

    /// <summary>
    /// Checks if the disk has a valid GPT header at LBA 1.
    /// This is used to confirm GPT detection when protective MBR is present.
    /// </summary>
    private static async Task<bool> HasValidGptHeaderAsync(IRawDisk disk, CancellationToken cancellationToken)
    {
        try
        {
            var sectorSize = disk.SectorSize;
            var headerBytes = new byte[HeaderSize];

            // Read a sector at LBA 1 (GPT header)
            using var stream = await disk.ReadBytesAsync(sectorSize, sectorSize, cancellationToken).ConfigureAwait(false);
            await stream.ReadAtLeastAsync(headerBytes, HeaderSize, cancellationToken: cancellationToken).ConfigureAwait(false);

            var signature = BitConverter.ToInt64(headerBytes, 0);
            return signature == GptSignature;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the byte array has a valid GPT header at sector 1.
    /// </summary>
    private static bool HasValidGptHeader(byte[] bytes, int sectorSize)
    {
        try
        {
            if (bytes.Length < sectorSize + HeaderSize)
                return false;

            var headerBytes = bytes[sectorSize..(sectorSize + HeaderSize)];
            var signature = BitConverter.ToInt64(headerBytes, 0);
            return signature == GptSignature;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<IPartitionTable> CreateGPTAsync(IRawDisk disk, byte[] mbrBytes, CancellationToken cancellationToken)
    {
        var gpt = new GPT(disk);

        // Verify GPT signature at LBA 1
        if (!await HasValidGptHeaderAsync(disk, cancellationToken).ConfigureAwait(false))
        {
            // Fall back to parsing as MBR if GPT header is invalid
            return await CreateMBRAsync(disk, mbrBytes, cancellationToken).ConfigureAwait(false);
        }

        await gpt.ParseAsync(cancellationToken).ConfigureAwait(false);

        return gpt;
    }

    private static async Task<IPartitionTable> CreateGPTAsync(byte[] mbrBytes, int sectorSize, CancellationToken cancellationToken)
    {
        var gpt = new GPT(null);

        // Verify GPT signature at LBA 1
        if (!HasValidGptHeader(mbrBytes, sectorSize))
        {
            // Fall back to parsing as MBR if GPT header is invalid
            return await CreateMBRAsync(mbrBytes, sectorSize, cancellationToken).ConfigureAwait(false);
        }

        await gpt.ParseAsync(mbrBytes, sectorSize, cancellationToken).ConfigureAwait(false);
        return gpt;
    }

    private static async Task<IPartitionTable> CreateMBRAsync(IRawDisk disk, byte[] mbrBytes, CancellationToken cancellationToken)
    {
        var mbr = new MBR(disk);
        // Use the already read bytes to avoid race conditions and redundant reads
        if (!await mbr.ParseAsync(mbrBytes, disk.SectorSize, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Failed to parse MBR from validated bytes.");
        }
        return mbr;
    }

    private static async Task<IPartitionTable> CreateMBRAsync(byte[] mbrBytes, int sectorSize, CancellationToken cancellationToken)
    {
        var mbr = new MBR(null);
        await mbr.ParseAsync(mbrBytes, sectorSize, cancellationToken).ConfigureAwait(false);
        return mbr;
    }
}
