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

namespace Duplicati.Library.SourceProvider.Builtin.PostgreSQL;

/// <summary>
/// Cuts the rows of a table into segments that each start on a block boundary, so the fixed
/// size blocks of a backup match the previous backup again after a row was added, changed or removed.
/// <para>
/// The input is COPY text: one row per line, where a line feed inside a value is written escaped,
/// so a line feed byte always ends a row. A segment ends after a row whose hash hits a target,
/// once the segment holds at least one block and at the latest when it holds <see cref="MaxSegmentBlocks"/>
/// blocks. After each segment, NUL bytes are written up to the next block boundary. COPY text
/// never holds a NUL byte, so dropping every NUL byte gives the rows back unchanged.
/// </para>
/// </summary>
public sealed class PostgreSQLSegmenter
{
    /// <summary>The name of this way of cutting segments, recorded with each table</summary>
    public const string SchemeName = "row-hash-v1";
    /// <summary>The fewest blocks in a segment before it may end</summary>
    public const int MinSegmentBlocks = 1;
    /// <summary>The number of blocks after which a segment ends at the next row</summary>
    public const int MaxSegmentBlocks = 4;
    /// <summary>The byte that ends a row</summary>
    private const byte RowEnd = (byte)'\n';
    /// <summary>The byte segments are padded with</summary>
    public const byte Padding = 0;
    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private readonly int m_blockSize;
    private readonly long m_minSegment;
    private readonly long m_maxSegment;
    private long m_written;
    private long m_segmentLength;
    private long m_rowLength;
    private ulong m_rowHash = FnvOffset;
    private int m_pendingPadding;

    /// <summary>
    /// Creates a segmenter
    /// </summary>
    /// <param name="blockSize">The block size of the backup</param>
    public PostgreSQLSegmenter(int blockSize)
    {
        if (blockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize));

        m_blockSize = blockSize;
        m_minSegment = (long)blockSize * MinSegmentBlocks;
        m_maxSegment = (long)blockSize * MaxSegmentBlocks;
    }

    /// <summary>
    /// The number of padding bytes that must be written before more rows
    /// </summary>
    public int PendingPadding => m_pendingPadding;

    /// <summary>
    /// Writes pending padding to the output
    /// </summary>
    /// <param name="output">The output buffer</param>
    /// <returns>The number of bytes written</returns>
    public int WritePadding(Span<byte> output)
    {
        var count = Math.Min(m_pendingPadding, output.Length);
        output[..count].Fill(Padding);
        m_pendingPadding -= count;
        m_written += count;
        return count;
    }

    /// <summary>
    /// Copies rows from the input to the output until the output is full, the input is used up,
    /// or a segment ends. When a segment ends, <see cref="PendingPadding"/> is set.
    /// </summary>
    /// <param name="input">The row data</param>
    /// <param name="output">The output buffer</param>
    /// <returns>The number of bytes copied, which is the same for the input and the output</returns>
    public int CopyRows(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var count = Math.Min(input.Length, output.Length);
        for (var i = 0; i < count; i++)
        {
            var b = input[i];
            output[i] = b;
            m_rowHash = (m_rowHash ^ b) * FnvPrime;
            m_rowLength++;

            if (b != RowEnd)
                continue;

            m_segmentLength += m_rowLength;

            // A row of N bytes ends the segment with a chance of N in a block, so the distance
            // between the ends does not depend on how long the rows are
            var chance = (ulong)Math.Max(1, m_blockSize / m_rowLength);
            var endsSegment = m_segmentLength >= m_minSegment && (m_rowHash % chance == 0 || m_segmentLength >= m_maxSegment);
            m_rowHash = FnvOffset;
            m_rowLength = 0;

            if (endsSegment)
            {
                m_written += i + 1;
                m_segmentLength = 0;
                m_pendingPadding = (int)((m_blockSize - (m_written % m_blockSize)) % m_blockSize);
                return i + 1;
            }
        }

        m_written += count;
        return count;
    }

    /// <summary>
    /// Removes the padding from segmented rows
    /// </summary>
    /// <param name="data">The data, changed in place</param>
    /// <returns>The length of the data without padding</returns>
    public static int RemovePadding(Span<byte> data)
    {
        var length = 0;
        foreach (var b in data)
            if (b != Padding)
                data[length++] = b;
        return length;
    }
}
