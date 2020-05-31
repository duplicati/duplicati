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
    }
}

