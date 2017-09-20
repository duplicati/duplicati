//  Copyright (C) 2017, The Duplicati Team
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
using System.Security.Cryptography;

namespace Duplicati.Library.Utility
{
	/// <summary>
	/// This class helps picking the fastest hash algorithm implementation,
	/// which is what <seealso cref="System.Security.Cryptography.HashAlgorithm.Create"/> should do, but does not.
	/// </summary>
	public static class HashAlgorithmHelper
    {
        /// <summary>
        /// Cache of known types
        /// </summary>
        private static readonly Dictionary<string, Type> _knownTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Static initializer to pre-populate the type table
        /// </summary>
        static HashAlgorithmHelper()
        {
            _knownTypes["MD5"] = typeof(MD5Cng);
			_knownTypes["SHA1"] = typeof(SHA1Cng);
			_knownTypes["SHA256"] = typeof(SHA256Cng);
			_knownTypes["SHA384"] = typeof(SHA384Cng);
			_knownTypes["SHA512"] = typeof(SHA512Cng);
		}

        /// <summary>
        /// Create the hash algorithm with the specified name.
        /// </summary>
        /// <returns>The hash algorithm.</returns>
        /// <param name="name">The name of the algorithm to create.</param>
        public static HashAlgorithm Create(string name)
        {
            Type known;
            if (!_knownTypes.TryGetValue(name, out known))
                known = _knownTypes[name] = Type.GetType("System.Security.Cryptography." + name.ToUpperInvariant() + "Cng", false, true);

            if (known == null || known.GetConstructor(Type.EmptyTypes) == null)
                return HashAlgorithm.Create(name);
            else
			    return (HashAlgorithm)Activator.CreateInstance(known);

        }
    }
}
