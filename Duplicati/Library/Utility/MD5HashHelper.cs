﻿#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion

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
                foreach (var str in inputStrings)
                {
                    var inputBuffer = Encoding.UTF8.GetBytes(str);
                    md5.TransformBlock(inputBuffer, 0, inputBuffer.Length, inputBuffer, 0);
                }
 
                md5.TransformFinalBlock(new byte[0], 0, 0);
                return md5.Hash;
            }
        }
    }
}
