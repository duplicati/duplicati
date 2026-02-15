// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Provides CRC32 calculation utilities for partition table operations.
/// </summary>
internal static class Crc32
{
    /// <summary>
    /// Calculates the CRC32 checksum for the given data.
    /// </summary>
    /// <param name="buffer">The data buffer.</param>
    /// <param name="offset">The offset in the buffer to start calculation.</param>
    /// <param name="count">The number of bytes to include in the CRC32 calculation.</param>
    /// <returns>The calculated CRC32 checksum.</returns>
    public static uint Calculate(byte[] buffer, int offset, int count)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < count; i++)
        {
            byte b = buffer[offset + i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }
        return ~crc;
    }
}
