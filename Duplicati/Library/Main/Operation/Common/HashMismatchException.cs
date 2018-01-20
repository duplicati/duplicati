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
namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Class to represent hash failures
    /// </summary>
    [Serializable]
    public class HashMismatchException : Exception
    {
        /// <summary>
        /// Default constructor, sets a generic string as the message
        /// </summary>
        public HashMismatchException() : base() { }

        /// <summary>
        /// Constructor with non-default message
        /// </summary>
        /// <param name="message">The exception message</param>
        public HashMismatchException(string message) : base(message) { }

        /// <summary>
        /// Constructor with non-default message and inner exception details
        /// </summary>
        /// <param name="message">The exception message</param>
        /// <param name="innerException">The exception that caused this exception</param>
        public HashMismatchException(string message, Exception innerException) : base(message, innerException) { }
    }
}
