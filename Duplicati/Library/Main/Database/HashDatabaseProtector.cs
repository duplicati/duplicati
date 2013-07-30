//  Copyright (C) 2011, Kenneth Skovhede
//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Database
{
    /// <summary>
    /// Possible results from a hash lookup.
    /// </summary>
    public enum HashLookupResult
    {
        /// <summary>
        /// The entry was not found with certainty
        /// </summary>
        NotFound,
        /// <summary>
        /// The entry was found with certainty
        /// </summary>
        Found,
        /// <summary>
        /// Nothing could be said about the entry
        /// </summary>
        Uncertain
    }
    
    /// <summary>
    /// Provides a hash map, similar to a bloom filter,
    /// but takes advantage of the fact that the input values are
    /// already high quality hashes and thus equally distributed
    /// </summary>
    internal class HashPrefixLookup : IDisposable
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
        
        #region IDisposable implementation
        
        public void Dispose ()
        {
            m_lookup = null;
            m_bits = 0;
        }
        
        #endregion
        
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

        /// <summary>
        /// Faster internal hex decoder, assumes valid hex string of at least 16 chars and no extended chars
        /// </summary>
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

        /// <summary>
        /// Faster internal base64 decoder, assumes valid base64 string of at least 11 chars and no extended chars
        /// </summary>
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

    /// <summary>
    /// Provides a simple hash-table like lookup, 
    /// similar to a MRU cache
    /// </summary>
    internal class HashLookup<T> : IDisposable
    {
        private const long DEFAULT_MEMORY = 32 * 1024 * 1024; //32mb
        
        /// <summary>
        /// The lookup table with all values
        /// </summary>
        private T[] m_lookup;
        
        /// <summary>
        /// Typecasted size of table
        /// </summary>
        private ulong m_modulo;
        
        public HashLookup(uint elementsize, ulong memorylimit = DEFAULT_MEMORY)
        {
            //Allocate as a large boolean/bit array
            m_lookup = new T[Math.Max(32, Math.Max(1024, memorylimit) / elementsize)];
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

        #region IDisposable implementation
        
        public void Dispose ()
        {
            m_lookup = null;
            m_modulo = 0;
        }
        
        #endregion
    }

    /// <summary>
    /// Provides a simple hash-table like lookup, 
    /// similar to a MRU cache
    /// </summary>
    internal class HashLookupWithData<TKey, TValue> : IDisposable
    {
        private const long DEFAULT_MEMORY = 32 * 1024 * 1024; //32mb
        
        /// <summary>
        /// The lookup table with all values
        /// </summary>
        private KeyValuePair<TKey, TValue>[] m_lookup;
        
        /// <summary>
        /// Typecasted size of table
        /// </summary>
        private ulong m_modulo;
        
        public HashLookupWithData(uint elementsize, ulong memorylimit = DEFAULT_MEMORY)
        {
            //Allocate as a large boolean/bit array
            m_lookup = new KeyValuePair<TKey, TValue>[Math.Max(32, Math.Max(1024, memorylimit) / elementsize)];
            m_modulo = (ulong)m_lookup.Length;
        }
        
        /// <summary>
        /// Adds a hash to the table
        /// </summary>
        /// <param name="key">The hash key</param>
        /// <param name="value">The actual hash data</param>
        public void AddHash(ulong key, TKey value, TValue data)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = key % m_modulo;
            m_lookup[bit] = new KeyValuePair<TKey,TValue>(value, data);
        }
        
        /// <summary>
        /// Queries the table to see if a hash exists.
        /// A true result value is definitive, a false result value should be interpreted as maybe.
        /// </summary>
        /// <param name="key">The hash key</param>
        /// <param name="data">The actual hash data</param>
        /// <returns>True if the hash was found, false otherwise</returns>
        public bool HashExists(ulong key, TKey value, out TValue data)
        {
            //Textbox hashing, the value modulo number of entries
            // works because the input is high quality random data
            var bit = key % m_modulo;
            var el = m_lookup[bit];
            if (object.Equals(el.Key, value))
            {
                data = el.Value;
                return true;
            }
            else
            {
                data = default(TValue);
                return false;
            }
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
                    if (!object.Equals(x.Key, default(TKey)))
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

        #region IDisposable implementation
        
        public void Dispose ()
        {
            m_lookup = null;
            m_modulo = 0;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Provides a hash-based lookup system that is able to quickly determine,
    /// if a given entry is found in the database, with some probability
    /// for returning an undetermined answer
    /// </summary>
    public class HashDatabaseProtector<TKey, TValue> : IDisposable
    {    
        private const long MEMORY_BIT_FRACTION = 8; // 1/8th of the memory used for bitmap
        private const long DEFAULT_MEMORY = 64 * 1024 * 1024; //64mb
        private HashPrefixLookup m_certainMissing;
        private HashLookupWithData<TKey, TValue> m_certainFound;

        public long PositiveMisses { get; set; }
        public long NegativeMisses { get; set; }

        public HashDatabaseProtector(uint elementsize, ulong memory = DEFAULT_MEMORY)
        {
            var bitlookup = memory / MEMORY_BIT_FRACTION;
            var elementlookup = memory - bitlookup;
            m_certainMissing = new HashPrefixLookup(bitlookup);
            m_certainFound = new HashLookupWithData<TKey, TValue>(elementsize, elementlookup);
        }
        
        public void Add(ulong hash, TKey key, TValue value)
        {
            m_certainMissing.AddHash(hash);
            m_certainFound.AddHash(hash, key, value);
        }
        
        public HashLookupResult HasValue (ulong hash, TKey key, out TValue value)
        {
            if (!m_certainMissing.HashExists(hash)) 
            {
                value = default(TValue);
                return HashLookupResult.NotFound;
            }
            
            if (m_certainFound.HashExists(hash, key, out value))
                return HashLookupResult.Found;
                
            return HashLookupResult.Uncertain;
        }
        
        public ulong PrefixBits { get { return m_certainMissing.BitsUsed; } }
        public ulong FullEntries { get { return m_certainFound.Entries; } }
        public double PrefixUsageRatio { get { return m_certainMissing.TableUsageRatio; } }
        public double FullUsageRatio { get { return m_certainFound.TableUsageRatio; } }

        #region IDisposable implementation

        public void Dispose ()
        {
            if (m_certainFound != null)
                try { m_certainFound.Dispose(); }
                finally { m_certainFound = null; }

            if (m_certainMissing != null)
                try { m_certainMissing.Dispose(); }
                finally { m_certainMissing = null; }
        }

        #endregion
    }
    
    /// <summary>
    /// Provides a hash-based lookup system that is able to quickly determine,
    /// if a given entry is found in the database, with some probability
    /// for returning an undetermined answer
    /// </summary>
    public class HashDatabaseProtector<TKey> : IDisposable
    {
        private const long MEMORY_BIT_FRACTION = 8; // 1/8th of the memory used for bitmap
        private const long DEFAULT_MEMORY = 64 * 1024 * 1024; //64mb
        private HashPrefixLookup m_certainMissing;
        private HashLookup<TKey> m_certainFound;

        public long PositiveMisses { get; set; }
        public long NegativeMisses { get; set; }
        
        public HashDatabaseProtector(uint elementsize, ulong memory = DEFAULT_MEMORY)
        {
            var bitlookup = memory / MEMORY_BIT_FRACTION;
            var elementlookup = memory - bitlookup;
            m_certainMissing = new HashPrefixLookup(bitlookup);
            m_certainFound = new HashLookup<TKey>(elementsize, elementlookup);
        }
        
        public void Add(ulong hash, TKey key)
        {
            m_certainMissing.AddHash(hash);
            m_certainFound.AddHash(hash, key);
        }
        
        public HashLookupResult HasValue (ulong hash, TKey key)
        {
            if (!m_certainMissing.HashExists(hash)) 
                return HashLookupResult.NotFound;
            
            if (m_certainFound.HashExists(hash, key))
                return HashLookupResult.Found;
            
            return HashLookupResult.Uncertain;
        }
        
        public ulong PrefixBits { get { return m_certainMissing.BitsUsed; } }
        public ulong FullEntries { get { return m_certainFound.Entries; } }
        public double PrefixUsageRatio { get { return m_certainMissing.TableUsageRatio; } }
        public double FullUsageRatio { get { return m_certainFound.TableUsageRatio; } }

        #region IDisposable implementation
        
        public void Dispose ()
        {
            if (m_certainFound != null)
                try { m_certainFound.Dispose(); }
                finally { m_certainFound = null; }
            
            if (m_certainMissing != null)
                try { m_certainMissing.Dispose(); }
                finally { m_certainMissing = null; }
        }
        
        #endregion
    }
    
    
}

