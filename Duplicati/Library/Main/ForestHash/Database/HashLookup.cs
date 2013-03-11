using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
{
    /// <summary>
    /// Provides a simple hash-table like lookup, 
    /// similar to a MRU cache
    /// </summary>
    internal class HashLookup<T>
    {
        private const long MAX_MEMORY = 32 * 1024 * 1024; //32mb

        /// <summary>
        /// The lookup table with all values
        /// </summary>
        private T[] m_lookup;

        /// <summary>
        /// Typecasted size of table
        /// </summary>
        private ulong m_modulo;

        public HashLookup(uint elementsize, ulong memorylimit = MAX_MEMORY)
        {
            //Allocate as a large boolean/bit array
            m_lookup = new T[Math.Max(32, Math.Max(1024, memorylimit * 8) / elementsize)];
            m_modulo = (ulong)m_lookup.Length;
        }

        /// <summary>
        /// Adds a hash to the table
        /// </summary>
        /// <param name="key">The hash key</param>
        /// <param name="value">The actual hash data</param>
        public void AddHash(ulong key, T value)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = key % m_modulo;
            m_lookup[bit] = value;
        }

        /// <summary>
        /// Queries the table to see if a hash exists.
        /// A true result value is definitive, a false result value should be interpreted as maybe.
        /// </summary>
        /// <param name="key">The hash key</param>
        /// <param name="data">The actual hash data</param>
        /// <returns>True if the hash was found, false otherwise</returns>
        public bool HashExists(ulong key, T value)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = key % m_modulo;
            return object.Equals(m_lookup[bit], value);
        }

        /// <summary>
        /// Calculates number of used entries in the table
        /// </summary>
        public ulong Entries
        {
            get
            {
                ulong c = 0;
                foreach (var x in m_lookup)
                    if (!object.Equals(x, default(T)))
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
                return Entries / (double)m_lookup.Length;
            }
        }
    }
}
