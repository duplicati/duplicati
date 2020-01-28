//  Copyright (C) 2018, The Duplicati Team
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
namespace Duplicati.Library.Logging
{
    /// <summary>
    /// An interface for filtering log messages
    /// </summary>
    public interface ILogFilter
    {
        /// <summary>
        /// A method called to determine if a given message should be filtered or not
        /// </summary>
        /// <returns><c>true</c> if the message is included; <c>false</c> otherwise</returns>
        /// <param name="entry">The entry to examine</param>
        bool Accepts(LogEntry entry);
    }
}
