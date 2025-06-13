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

using System.Text;
using System.Security.Cryptography;
using Duplicati.Library.Utility;

public static class HashExtentions
{

    /// <summary>
    /// Computes the hash of the given string using the given hash algorithm
    /// </summary>
    /// <param name="value">string to be hashed</param>
    /// <param name="hashAlgorithm">hash algoritm instance to be used</param>
    /// <returns></returns>
    public static byte[] ComputeHash(this string value, HashAlgorithm hashAlgorithm)
    {
        return hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Computes the hash of the given string using the given hash algorithm and returns it as a hex string
    /// </summary>
    /// <param name="value">string to be hashed</param>
    /// <param name="hashAlgorithm">hash algoritm instance to be used</param>
    /// <returns></returns>
    public static string ComputeHashToHex(this string value, HashAlgorithm hashAlgorithm)
    {
        return Utility.ByteArrayAsHexString(ComputeHash(value, hashAlgorithm));
    }
}