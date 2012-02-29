#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// This class reads signature files in the same format as RDiff
    /// </summary>
    public class ChecksumFileReader
    {
        /// <summary>
        /// The number of bytes in a long
        /// </summary>
        private const int BYTES_PR_LONG = 8;

        /// <summary>
        /// The re-used MD4 algorithm
        /// </summary>
        private System.Security.Cryptography.HashAlgorithm m_hashAlgorithm;
        /// <summary>
        /// The length of a datablock
        /// </summary>
        private int m_blocklen;
        /// <summary>
        /// The number of bytes used for storing a single strong signature
        /// </summary>
        private int m_stronglen;

        /// <summary>
        /// This is a lookup table for a hash of the weak checksum,
        /// used to quickly determine if a weak checksum exists
        /// </summary>
        private bool[] m_weakLookup;

        /// <summary>
        /// This is a list of all weak values, sorted by value
        /// </summary>
        private uint[] m_weakValues;
        /// <summary>
        /// This list contains index values for <see cref="m_strongIndex"/> and is sorted to match the index of <see cref="m_weakValues"/> 
        /// </summary>
        private long[] m_weakToStrongIndex;

        /// <summary>
        /// This list contains all strong hashes, encoded as unsigned int64,
        /// which compares much faster than a byte[]. If the strong hash does not fill
        /// an equal number of longs, the remaining bytes are set to zero
        /// before generating the long value.
        /// </summary>
        private ulong[] m_strongIndex;

        /// <summary>
        /// The number of long values each strong hash occupies
        /// </summary>
        private int m_longs_pr_strong;

        /// <summary>
        /// Reads a ChecksumFile from a stream
        /// </summary>
        /// <param name="input">The stream to read from</param>
        public ChecksumFileReader(System.IO.Stream input)
        {
            m_hashAlgorithm = MD4Helper.Create();
            m_hashAlgorithm.Initialize();

            //Read and verify that the file header is valid
            byte[] sig = new byte[4];
            if (Utility.ForceStreamRead(input, sig, 4) != 4)
                throw new Exception(Strings.ChecksumFile.EndofstreamBeforeSignatureError);
            for (int i = 0; i < sig.Length; i++)
                if (RDiffBinary.SIGNATURE_MAGIC[i] != sig[i])
                    throw new Exception(Strings.ChecksumFile.InvalidSignatureHeaderError);

            if (Utility.ForceStreamRead(input, sig, 4) != 4)
                throw new Exception(Strings.ChecksumFile.EndofstreamInBlocksizeError);
            m_blocklen = BitConverter.ToInt32(RDiffBinary.FixEndian(sig), 0);
            if (m_blocklen < 1 || m_blocklen > int.MaxValue / 2)
                throw new Exception(string.Format(Strings.ChecksumFile.InvalidBlocksizeError, m_blocklen));
            if (Utility.ForceStreamRead(input, sig, 4) != 4)
                throw new Exception(Strings.ChecksumFile.EndofstreamInStronglenError);
            m_stronglen = BitConverter.ToInt32(RDiffBinary.FixEndian(sig), 0);
            if (m_stronglen < 1 || m_stronglen > (m_hashAlgorithm.HashSize / 8))
                throw new Exception(string.Format(Strings.ChecksumFile.InvalidStrongsizeError, m_stronglen));

            //Prepare the data structures
            m_weakLookup = new bool[0x10000];
            m_longs_pr_strong = (m_stronglen + (BYTES_PR_LONG - 1)) / BYTES_PR_LONG;
            KeyComparer<uint, long> comparer = new KeyComparer<uint,long>();
            byte[] strongbuf = new byte[m_longs_pr_strong * BYTES_PR_LONG];

            //We would like to use static allocation for these lists, but unfortunately
            // the zip stream does not report the correct length
            List<KeyValuePair<uint, long>> tempWeakTable = new List<KeyValuePair<uint, long>>();
            List<ulong> tempStrongIndex = new List<ulong>();

            long count = 0;

            //Repeat until the stream is exhausted
            while (true)
            {
                if (Utility.ForceStreamRead(input, sig, 4) != 4)
                    break;
                uint weak = BitConverter.ToUInt32(RDiffBinary.FixEndian(sig), 0); ;

                if (Utility.ForceStreamRead(input, strongbuf, m_stronglen) != m_stronglen)
                    throw new Exception(Strings.ChecksumFile.EndofstreamInStrongSignatureError);

                //Record the entries
                tempWeakTable.Add(new KeyValuePair<uint,long>(weak, count));
                for (int i = 0; i < m_longs_pr_strong; i++)
                    tempStrongIndex.Add(BitConverter.ToUInt64(strongbuf, i * BYTES_PR_LONG));

                count++;
            }

            m_strongIndex = tempStrongIndex.ToArray();
            tempStrongIndex.Clear();
            tempStrongIndex = null;

            tempWeakTable.Sort(comparer);
            
            m_weakValues = new uint[tempWeakTable.Count];
            m_weakToStrongIndex = new long[m_weakValues.Length];

            //Initialize the weakest lookup table
            for (int i = 0; i < tempWeakTable.Count; i++)
            {
                m_weakValues[i] = tempWeakTable[i].Key;
                m_weakToStrongIndex[i] = tempWeakTable[i].Value;
                m_weakLookup[m_weakValues[i] >> 16] = true;
            }

            tempWeakTable.Clear();
            tempWeakTable = null;
        }

        /// <summary>
        /// A comparer that allows sorting a list/array of KeyValuePair items
        /// </summary>
        /// <typeparam name="TKey">The key type</typeparam>
        /// <typeparam name="TValue">The value type</typeparam>
        private class KeyComparer<TKey, TValue> : IEqualityComparer<KeyValuePair<TKey, TValue>>, IComparer<KeyValuePair<TKey, TValue>>
             where TKey : IComparable
        {
            #region IEqualityComparer<KeyValuePair<TKey,TValue>> Members

            /// <summary>
            /// Determines if the two entries are equal
            /// </summary>
            /// <param name="x">A KeyValuePair</param>
            /// <param name="y">A KeyValuePair</param>
            /// <returns>True if the keys are equal, false otherwise</returns>
            public bool Equals(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y)
            {
                return x.Key.Equals(y.Key);
            }

            /// <summary>
            /// Returns the hash code of the KeyValuePair
            /// </summary>
            /// <param name="obj"></param>
            /// <returns></returns>
            public int GetHashCode(KeyValuePair<TKey, TValue> obj)
            {
                return obj.GetHashCode();
            }

            #endregion

            #region IComparer<KeyValuePair<TKey,TValue>> Members

            /// <summary>
            /// Determines the sort order of the KeyValuePairs
            /// </summary>
            /// <param name="x">A KeyValuePair</param>
            /// <param name="y">A KeyValuePair</param>
            /// <returns>A signed value indicating the sort order of the KeyValuePairs</returns>
            public int Compare(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y)
            {
                return x.Key.CompareTo(y.Key);
            }

            #endregion
        }

        /// <summary>
        /// Gets the block index of data the equals the given data
        /// </summary>
        /// <param name="weakChecksum">The weak checksum for the data</param>
        /// <param name="data">The data buffer</param>
        /// <param name="offset">The offset into the data buffer</param>
        /// <param name="length">The number of bytes to read from the buffer</param>
        /// <param name="preferedIndex">The prefered index if multiple entries are found</param>
        /// <returns>The index of the matching element</returns>
        public long LookupChunck(uint weakChecksum, byte[] data, int offset, int length, long preferedIndex)
        {
            //Entry level check, this table is quite small and lookup is O(1)
            if (!m_weakLookup[weakChecksum >> 16])
                return -1;

            long sh;
            if (m_weakValues.LongLength <= int.MaxValue)
            {
                //Do a O(log n) lookup on the weak hash, a hashtable would have O(1), but requires too much memory
                sh = Array.BinarySearch<uint>(m_weakValues, weakChecksum);
            }
            else
            {
                //If the table gets too big, Array.BinarySearch fails because it can only return int values
                //Below is a custom binary search algorithm
                long maxIndex = m_weakValues.LongLength - 1;
                long minIndex = 0;
                sh = -1;

                while (minIndex < maxIndex)
                {
                    long testIndex = ((maxIndex - minIndex) / 2) + minIndex;
                    if (m_weakValues[testIndex] == weakChecksum)
                    {
                        sh = testIndex;
                        break;
                    }
                    else if (m_weakValues[testIndex] > weakChecksum)
                        maxIndex = testIndex - 1;
                    else
                        minIndex = testIndex + 1;
                }

                if (sh == -1)
                    sh = ~minIndex;
            }

            if (sh < 0)
                return -1;
            
            //At this point we know that the weak hash matches something, so we have to calculate the strong hash
            if (!m_hashAlgorithm.CanReuseTransform)
                m_hashAlgorithm = Mono.Security.Cryptography.MD4.Create("MD4");
            byte[] hash = m_hashAlgorithm.ComputeHash(data, offset, length);

            //Pad with zeros if needed
            if (hash.Length % BYTES_PR_LONG != 0)
                Array.Resize<byte>(ref hash,hash.Length + (BYTES_PR_LONG - hash.Length % BYTES_PR_LONG));

            //Convert the hash to a list of long's so we can compare at max speed
            //Even though we allocate this buffer each time, it is optimized away,
            // so it is faster than having a pre-allocated buffer
            ulong[] stronghash = new ulong[m_longs_pr_strong];
            for (int i = 0; i < stronghash.Length; i++)
                stronghash[i] = BitConverter.ToUInt64(hash, (i * BYTES_PR_LONG));

            //Test first with prefered index as that gives the smallest possible file
            // we should test with the weak first, but we do not have a direct lookup into
            // the list of weak checksums, and this check should be rather fast,
            // the slow part is generating the MD4 hash, and that  has already been done
            if (preferedIndex >= 0 && preferedIndex < (m_strongIndex.Length / m_longs_pr_strong))
            {
                int i;
                for (i = 0; i < stronghash.Length; i++)
                    if (stronghash[i] != m_strongIndex[(preferedIndex * m_longs_pr_strong) + i])
                        break;

                if (i == stronghash.Length)
                    return preferedIndex;
            }

            //Since the binary search can hit any of the possible matches, we must search both up and down
            long shMid = sh;

            //First we search down, starting with the match
            while (sh < m_weakValues.LongLength && m_weakValues[sh] == weakChecksum)
            {
                long index = m_weakToStrongIndex[sh];
                int i;
                for (i = 0; i < stronghash.Length; i++)
                    if (stronghash[i] != m_strongIndex[(index * m_longs_pr_strong) + i])
                        break;

                if (i == stronghash.Length)
                    return index;

                sh++;
            }

            //Then we search up, starting with the one above the match
            sh = shMid - 1;
            while (sh >= 0 && m_weakValues[sh] == weakChecksum)
            {
                long index = m_weakToStrongIndex[sh];
                int i;
                for (i = 0; i < stronghash.Length; i++)
                    if (stronghash[i] != m_strongIndex[(index * m_longs_pr_strong) + i])
                        break;

                if (i == stronghash.Length)
                    return index;

                sh--;
            }

            //No matches
            return -1;
        }

        /// <summary>
        /// Gets the number of bytes in each hashed block
        /// </summary>
        public int BlockLength { get { return m_blocklen; } }
        /// <summary>
        /// Gets the number of bytes in a signature file
        /// </summary>
        public int StrongLength { get { return m_stronglen; } }

        /// <summary>
        /// Gets a lookup table with a checksum of all known weak hashes
        /// </summary>
        public bool[] WeakLookup { get { return m_weakLookup; } }

    }
}
