// Copyright (C) 2024, The Duplicati Team
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
using System.Security.Cryptography;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class helps picking the fastest hash algorithm implementation,
    /// which is what <seealso cref="System.Security.Cryptography.HashAlgorithm.Create()"/> should do, but does not.
    /// It falls back to the aforementioned System library when creation of
    /// the faster hash function fails.
    /// </summary>
    public static class HashAlgorithmHelper
    {
        /// <summary>
        /// Create the hash algorithm with the specified name.
        /// </summary>
        /// <returns>The hash algorithm.</returns>
        /// <param name="name">The name of the algorithm to create.</param>
        public static HashAlgorithm Create(string name)
        {
            try
            {
                return FasterHashing.FasterHash.Create(name);
            }
            // Only having OpenSSL 3 installed might trigger this exception.
            catch (EntryPointNotFoundException)
            {
                return HashAlgorithm.Create(name);
            }
        }
    }
}
