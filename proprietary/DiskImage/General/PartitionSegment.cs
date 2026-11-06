// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Globalization;
using System.Linq;

namespace Duplicati.Proprietary.DiskImage.General;

/// <summary>
/// The path segment that names a partition in the disk hierarchy, in the format
/// "part_{PartitionTableType}_{PartitionNumber}", e.g. "part_GPT_1".
/// </summary>
internal static class PartitionSegment
{
    /// <summary>
    /// The prefix of a partition segment.
    /// </summary>
    public const string Prefix = "part_";

    /// <summary>
    /// The format of a partition segment, for messages.
    /// </summary>
    public const string FormatDescription = "part_{PartitionTableType}_{PartitionNumber}";

    /// <summary>
    /// Creates the segment for a partition.
    /// </summary>
    /// <param name="tableType">The partition table type.</param>
    /// <param name="partitionNumber">The partition number.</param>
    /// <returns>The partition segment.</returns>
    public static string Format(PartitionTableType tableType, int partitionNumber)
        => $"{Prefix}{tableType}_{partitionNumber.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Parses a partition segment. The table type must be a partition table type
    /// name (not a number), and the partition number a plain decimal number.
    /// </summary>
    /// <param name="segment">The path segment.</param>
    /// <param name="tableType">The partition table type, if the segment is valid.</param>
    /// <param name="partitionNumber">The partition number, if the segment is valid.</param>
    /// <returns><c>true</c> if the segment names a partition.</returns>
    public static bool TryParse(string segment, out PartitionTableType tableType, out int partitionNumber)
    {
        tableType = default;
        partitionNumber = 0;

        var parts = segment.Split('_');
        return parts.Length == 3
            && parts[0].Equals("part", StringComparison.OrdinalIgnoreCase)
            && parts[1].Length > 0 && parts[1].All(char.IsAsciiLetter)
            && Enum.TryParse(parts[1], true, out tableType)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out partitionNumber);
    }
}
