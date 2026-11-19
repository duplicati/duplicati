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
using System.Globalization;
using System.Linq;

namespace Duplicati.Library.SourceProvider.DiskImage.General;

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
