using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
{
    /// <summary>
    /// Provides a hash map, similar to a bloom filter,
    /// but takes advantage of the fact that the input values are
    /// already high quality hashes and thus equally distributed
    /// </summary>
    internal class HashPrefixLookup
    {
        private const long MAX_MEMORY = 32 * 1024 * 1024; //32mb

        /// <summary>
        /// The lookup table with all values
        /// </summary>
        private ulong[] m_lookup;

        /// <summary>
        /// The number of bits used for the prefix
        /// </summary>
        private ulong m_bits;

        /// <summary>
        /// Constructs a new HashPrefixLookup
        /// </summary>
        /// <param name="memorylimit">The maximum number of bytes to allocate for storage table</param>
        /// <param name="data">Data to prepoulate the table with</param>
        public HashPrefixLookup(ulong memorylimit = MAX_MEMORY, IEnumerable<byte[]> data = null)
        {
            m_bits = Math.Max(1024, memorylimit * 8);

            //Allocate as a large boolean/bit array
            m_lookup = new ulong[m_bits / 64];

            if (data != null)
                foreach (var d in data)
                    AddHash(d);
        }

        /// <summary>
        /// Adds a hash to the table
        /// </summary>
        /// <param name="data">The bytes representing the hash</param>
        public void AddHash(byte[] data)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = BitConverter.ToUInt64(data, 0) % m_bits;
            m_lookup[bit >> 6] |= 1uL << (ushort)(bit & 0x3f);
        }

        /// <summary>
        /// Queries the table to see if a hash exists.
        /// A false result value is definitive, a true result value should be interpreted as maybe.
        /// </summary>
        /// <param name="data">The bytes representing the hash</param>
        /// <returns>True if the hash was found, false otherwise</returns>
        public bool HashExists(byte[] data)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = BitConverter.ToUInt64(data, 0) % m_bits;
            return (m_lookup[bit >> 6] & (1uL << (ushort)(bit & 0x3f))) != 0;
        }

        /// <summary>
        /// Calculates the number of bits used
        /// </summary>
        public ulong BitsUsed
        {
            get
            {
                ulong c = 0;
                foreach (var e in m_lookup)
                    if (e != 0)
                        for (var i = 0; i < 64; i++)
                            if ((e & (1uL << i)) != 0)
                                c++;
                return c;
            }
        }

        /// <summary>
        /// Gets a value between 0 and 1 descring how
        /// many entries are used
        /// </summary>
        public double TableUsageRatio
        {
            get
            {
                return (this.BitsUsed / (double)m_bits);
            }
        }
    }
}
