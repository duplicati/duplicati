//  Copyright (C) 2015, The Duplicati Team

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
using System.Collections.Generic;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Common interface for a filter
    /// </summary>
    public interface IFilter
    {
        /// <summary>
        /// Gets a value indicating whether this <see cref="Duplicati.Library.Utility.IFilter"/> is empty.
        /// </summary>
        /// <value><c>true</c> if empty; otherwise, <c>false</c>.</value>
        bool Empty { get; }
        /// <summary>
        /// Performs a test to see if the entry matches the filter
        /// </summary>
        /// <param name="entry">The entry to match</param>
        /// <param name="result">The match result</param>
        /// <param name="match">The filter that matched</param>
        bool Matches(string entry, out bool result, out IFilter match);

        /// <summary>
        /// Returns a MD5 hash string representing the filter
        /// </summary>
        /// <returns></returns>
        string GetFilterHash();

        /// <summary>
        /// Determines if any of the filters are of the given type.
        /// </summary>
        bool ContainsFilterType(FilterType type);

        /// <summary>
        /// Determines if all of the filters are of the given type.
        /// </summary>
        bool OnlyContainsFilterType(FilterType type);

        /// <summary>
        /// Returns the file Ids from all <see cref="FilterType.Version"/> filters, if any are present.
        /// </summary>
        /// <returns></returns>
        IEnumerable<long> GetVersionFileIds();
    }
}

