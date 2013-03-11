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
        public HashPrefixLookup(ulong memorylimit = MAX_MEMORY)
        {
            m_bits = Math.Max(1024, memorylimit * 8);

            //Allocate as a large boolean/bit array
            m_lookup = new ulong[m_bits / 64];
        }

        /// <summary>
        /// Adds a hash to the table
        /// </summary>
        /// <param name="data">The bytes representing the hash</param>
        public void AddHash(ulong data)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = data % m_bits;
            m_lookup[bit >> 6] |= 1uL << (ushort)(bit & 0x3f);
        }

        /// <summary>
        /// Queries the table to see if a hash exists.
        /// A false result value is definitive, a true result value should be interpreted as maybe.
        /// </summary>
        /// <param name="data">The bytes representing the hash</param>
        /// <returns>True if the hash was found, false otherwise</returns>
        public bool HashExists(ulong data)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = data % m_bits;
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

        /// <summary>
        /// Hex digit ASCII lookup table
        /// </summary>
        private static readonly byte[] HEXTB = new byte[128];

        /// <summary>
        /// Base64 digit ASCII lookup table
        /// </summary>
        private static readonly byte[] BAS64TB = new byte[128];

        /// <summary>
        /// Initializes the lookup
        /// </summary>
        static HashPrefixLookup()
        {
            for(byte i = 0; i < 10; i++)
                HEXTB['0' + i] = i;

            for (byte i = 0; i < 6; i++)
            {
                //Case insensitive support
                HEXTB['A' + i] = (byte)(i + 10);
                HEXTB['a' + i] = (byte)(i + 10);
            }

            for (byte i = 0; i < 26; i++)
                BAS64TB['A' + i] = i;
            for (byte i = 0; i < 26; i++)
                BAS64TB['a' + i] = (byte)(i + 26);
            for (byte i = 0; i < 10; i++)
                BAS64TB['0' + i] = (byte)(i + 52);

            BAS64TB['+'] = 62;
            BAS64TB['/'] = 63;
            
            //Base64 URL support
            BAS64TB['-'] = 62;
            BAS64TB['_'] = 63;
        }

        /// <summary>
        /// Debug help function to compare values
        /// </summary>
        /// <param name="value">The value to convert</param>
        /// <returns>The converted value</returns>
        public static ulong ConvertEndianness(ulong value)
        {
            return
                ((value << 56) & 0xff00000000000000uL) |
                ((value << 40) & 0x00ff000000000000uL) |
                ((value << 24) & 0x0000ff0000000000uL) |
                ((value << 8)  & 0x000000ff00000000uL) |
                ((value >> 8)  & 0x00000000ff000000uL) |
                ((value >> 24) & 0x0000000000ff0000uL) |
                ((value >> 40) & 0x000000000000ff00uL) |
                ((value >> 56) & 0x00000000000000ffuL);
        }

        //Faster internal hex decoder, assumes valid hex string of at least 16 chars and no extended chars
        public static ulong DecodeHexHash(string hash)
        {
            return
                (ulong)HEXTB[hash[0]] << 60 |
                (ulong)HEXTB[hash[1]] << 56 |
                (ulong)HEXTB[hash[2]] << 52 |
                (ulong)HEXTB[hash[3]] << 48 |
                (ulong)HEXTB[hash[4]] << 44 |
                (ulong)HEXTB[hash[5]] << 40 |
                (ulong)HEXTB[hash[6]] << 36 |
                (ulong)HEXTB[hash[7]] << 32 |
                (ulong)HEXTB[hash[8]] << 28 |
                (ulong)HEXTB[hash[9]] << 24 |
                (ulong)HEXTB[hash[10]] << 20 |
                (ulong)HEXTB[hash[11]] << 16 |
                (ulong)HEXTB[hash[12]] << 12 |
                (ulong)HEXTB[hash[13]] << 8 |
                (ulong)HEXTB[hash[14]] << 4 |
                (ulong)HEXTB[hash[15]] << 0;
        }

        //Faster internal base64 decoder, assumes valid hex string of at least 11 chars and no extended chars
        public static ulong DecodeBase64Hash(string hash)
        {
            return
                (ulong)BAS64TB[hash[0]] << 58 |
                (ulong)BAS64TB[hash[1]] << 52 |
                (ulong)BAS64TB[hash[2]] << 46 |
                (ulong)BAS64TB[hash[3]] << 40 |
                (ulong)BAS64TB[hash[4]] << 34 |
                (ulong)BAS64TB[hash[5]] << 28 |
                (ulong)BAS64TB[hash[6]] << 22 |
                (ulong)BAS64TB[hash[7]] << 16 |
                (ulong)BAS64TB[hash[8]] << 10 |
                (ulong)BAS64TB[hash[9]] << 4 |
                (((ulong)BAS64TB[hash[10]] >> 2) & 0x0f);
        }
    }
}
