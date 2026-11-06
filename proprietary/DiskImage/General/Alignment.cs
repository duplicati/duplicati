// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.DiskImage.General;

/// <summary>
/// Helpers for aligning sizes and offsets.
/// </summary>
internal static class Alignment
{
    /// <summary>
    /// Rounds a value up to a multiple of the alignment. An alignment of one or
    /// less leaves the value unchanged.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="alignment">The alignment.</param>
    /// <returns>The aligned value.</returns>
    public static long AlignUp(long value, long alignment)
        => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
}
