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
using System.Security.Cryptography;

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// MD4 instance creation helper, uses system MD4 if found, otherwise uses the bundled managed MD4 implementation from Mono
    /// </summary>
    public static class MD4Helper
    {
        /// <summary>
        /// Create a new instance of the MD4 hash algorithm
        /// </summary>
        public static HashAlgorithm Create()
        {
            HashAlgorithm o = CryptoConfig.CreateFromName("MD4") as System.Security.Cryptography.HashAlgorithm;
            if (o != null)
               return o;
            else
                return new Mono.Security.Cryptography.MD4Managed();
        }
    }
}

