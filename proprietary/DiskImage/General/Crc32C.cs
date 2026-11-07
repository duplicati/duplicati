// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Duplicati.Proprietary.DiskImage.General;

/// <summary>
/// CRC-32C (Castagnoli) checksum, as used by the VHDX format.
/// </summary>
internal static class Crc32C
{
    /// <summary>
    /// Computes the CRC-32C checksum of the data.
    /// </summary>
    /// <param name="data">The data to checksum.</param>
    /// <returns>The checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        var i = 0;
        for (; i + 8 <= data.Length; i += 8)
            crc = BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt64LittleEndian(data[i..]));
        for (; i < data.Length; i++)
            crc = BitOperations.Crc32C(crc, data[i]);
        return ~crc;
    }
}
