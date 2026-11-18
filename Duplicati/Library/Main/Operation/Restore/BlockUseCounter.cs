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
using System.Collections;
using System.Collections.Generic;

#nullable enable

namespace Duplicati.Library.Main.Operation.Restore
{
    /// <summary>
    /// Counts how many more times each block of a restore is used, keyed by block ID.
    /// </summary>
    /// <remarks>
    /// A restore counts every block it needs up front, which is a couple of blocks per file. In a
    /// <see cref="Dictionary{TKey, TValue}"/> of longs that took about 40 bytes per block, which is
    /// hundreds of megabytes for millions of files (#7538). The block IDs are known once they are
    /// counted, so they are kept in a sorted array with their counts next to them, 12 bytes per block,
    /// and looked up by binary search. A block whose count is zero is absent, as in the dictionary.
    /// The counts are not thread-safe; the caller locks as it did for the dictionary.
    /// </remarks>
    internal sealed class BlockUseCounter : IEnumerable<KeyValuePair<long, long>>
    {
        /// <summary>
        /// The distinct block IDs, sorted
        /// </summary>
        private long[] m_ids = [];
        /// <summary>
        /// The remaining number of uses of the block at the same index of <see cref="m_ids"/>; zero once it is used up
        /// </summary>
        private int[] m_counts = [];
        /// <summary>
        /// The number of blocks with a count above zero
        /// </summary>
        private int m_present;

        /// <summary>
        /// Sets the counts from the block ID of every use, replacing any previous counts.
        /// </summary>
        /// <param name="uses">The block ID of every use of a block, in any order. The list is sorted in place.</param>
        public void Load(List<long> uses)
        {
            uses.Sort();
            var distinct = 0;
            for (var i = 0; i < uses.Count; i++)
                if (i == 0 || uses[i] != uses[i - 1])
                    distinct++;

            var ids = new long[distinct];
            var counts = new int[distinct];
            var n = -1;
            for (var i = 0; i < uses.Count; i++)
            {
                if (i == 0 || uses[i] != uses[i - 1])
                    ids[++n] = uses[i];
                counts[n] = checked(counts[n] + 1);
            }

            m_ids = ids;
            m_counts = counts;
            m_present = distinct;
        }

        /// <summary>
        /// The number of blocks that are still to be used
        /// </summary>
        public int Count => m_present;

        /// <summary>
        /// Gets the remaining number of uses of a block.
        /// </summary>
        /// <param name="blockId">The block ID</param>
        /// <param name="count">The remaining number of uses, or zero if the block is not counted or is used up</param>
        /// <returns><c>true</c> if the block is still to be used</returns>
        public bool TryGetValue(long blockId, out long count)
        {
            var i = Array.BinarySearch(m_ids, blockId);
            count = i >= 0 ? m_counts[i] : 0;
            return count > 0;
        }

        /// <summary>
        /// Gets or sets the remaining number of uses of a block that is still to be used.
        /// </summary>
        /// <param name="blockId">The block ID</param>
        /// <exception cref="KeyNotFoundException">The block is not counted or is used up</exception>
        /// <exception cref="ArgumentOutOfRangeException">The count set is not above zero; use <see cref="Remove"/></exception>
        public long this[long blockId]
        {
            get => TryGetValue(blockId, out var count) ? count : throw new KeyNotFoundException($"Block {blockId} is not counted");
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "A count must be above zero; use Remove");
                var i = Array.BinarySearch(m_ids, blockId);
                if (i < 0 || m_counts[i] == 0)
                    throw new KeyNotFoundException($"Block {blockId} is not counted");
                m_counts[i] = checked((int)value);
            }
        }

        /// <summary>
        /// Marks a block as used up.
        /// </summary>
        /// <param name="blockId">The block ID</param>
        /// <returns><c>true</c> if the block was still to be used</returns>
        public bool Remove(long blockId)
        {
            var i = Array.BinarySearch(m_ids, blockId);
            if (i < 0 || m_counts[i] == 0)
                return false;
            m_counts[i] = 0;
            m_present--;
            return true;
        }

        /// <summary>
        /// Lists the blocks that are still to be used, with their remaining number of uses
        /// </summary>
        public IEnumerator<KeyValuePair<long, long>> GetEnumerator()
        {
            for (var i = 0; i < m_ids.Length; i++)
                if (m_counts[i] > 0)
                    yield return new KeyValuePair<long, long>(m_ids[i], m_counts[i]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
