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
using System.Security.Cryptography;
using System.Text;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Helper class to compute MD5 hashes
    /// </summary>
    public static class MD5HashHelper
    {
		/// <summary>
        /// Computes the MD5 hash of the input string
        /// </summary>
        /// <returns>The MD5 hash.</returns>
        /// <param name="inputString">The input string.</param>
        public static byte[] GetHash(string inputString)
        {
            using(var algorithm = MD5.Create()) //or use SHA256.Create();
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }

		/// <summary>
        /// Computes the MD5 hash of the input strings
        /// </summary>
        /// <returns>The MD5 hash.</returns>
        /// <param name="inputStrings">The input strings.</param>
        public static byte[] GetHash(IEnumerable<string> inputStrings)
        {
            using (var md5 = MD5.Create())
            {
                if (inputStrings != null)
                {
                    foreach (var str in inputStrings)
                    {
                        var inputBuffer = Encoding.UTF8.GetBytes(str);
                        md5.TransformBlock(inputBuffer, 0, inputBuffer.Length, inputBuffer, 0);
                    }
                }
 
                md5.TransformFinalBlock(new byte[0], 0, 0);
                return md5.Hash;
            }
        }
    }
}
