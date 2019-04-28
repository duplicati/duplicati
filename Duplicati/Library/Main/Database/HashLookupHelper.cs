//  Copyright (C) 2013, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
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

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Lookup table for hashes
    /// </summary>
    public class HashLookupHelper<T>
    {
        /// <summary>
        /// The lookup table
        /// </summary>
        private readonly SortedList<string, T>[] m_lookup;
        /// <summary>
        /// The number of entries in the table
        /// </summary>
        private readonly ulong m_entries;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Main.HashLookupHelper{T}"/> class.
        /// </summary>
        /// <param name="maxmemory">The maximum amount of bytes to use for the lookup table</param>
        public HashLookupHelper (ulong maxmemory)
        {
            m_entries = Math.Max(16, maxmemory / (ulong)IntPtr.Size);
            m_lookup = new SortedList<string, T>[m_entries];
        }
        
        /// <summary>
        /// Adds the specified hash and value to the lookup,
        /// if not already found.
        /// </summary>
        /// <param name="hash">The hash to add</param>
        /// <param name="value">The value assocated with the hash</param>
        /// <returns>>True if the value was added, false otherwise</returns>
        public bool TryAdd(string hash, long size, T value)
        {
            var key = DecodeBase64Hash(hash) % m_entries;
            var lst = m_lookup[key];
            if (lst == null)
                lst = m_lookup[key] = new SortedList<string, T>(1);

            if (!lst.TryGetValue(hash + ':' + size.ToString(), out _))
            {
                lst.Add(hash + ':' + size.ToString(), value);
                return true;
            }

            //
            return false;
        }

        /// <summary>
        /// Adds the specified hash and value to the lookup
        /// </summary>
        /// <param name="hash">The hash to add</param>
        /// <param name="value">The value assocated with the hash</param>
        public void Add(string hash, long size, T value)
        {
            var key = DecodeBase64Hash(hash) % m_entries;
            var lst = m_lookup[key];
            if (lst == null)
                lst = m_lookup[key] = new SortedList<string, T>(1);
            lst.Add(hash + ':' + size.ToString(), value);
        }
        
        /// <summary>
        /// Retrieves the value for a given hash
        /// </summary>
        /// <returns><c>true</c>, if the value was found, <c>false</c> otherwise.</returns>
        /// <param name="hash">The hash to look for.</param>
        /// <param name="value">The value associated with the hash</param>
        public bool TryGet(string hash, long size, out T value)
        {
            var key = DecodeBase64Hash(hash) % m_entries;
            var lst = m_lookup[key];
            if (lst == null)
            {
                value = default(T);
                return false;
            }
            else
            {
                return lst.TryGetValue(hash + ':' + size.ToString(), out value);
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
        static HashLookupHelper()
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
}

