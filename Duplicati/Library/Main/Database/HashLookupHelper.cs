// Copyright (C) 2025, The Duplicati Team
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

