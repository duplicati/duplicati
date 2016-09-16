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
using System;
namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Class for reporting host-key violations
    /// </summary>
    [Serializable]
    public class HostKeyException : Exception
    {
        /// <summary>
        /// The key reported by the host, which is not accepted
        /// </summary>
        /// <value>The reported host key.</value>
        public string ReportedHostKey { get; set; }
        /// <summary>
        /// The key which was expected
        /// </summary>
        /// <value>The acceptable host key.</value>
        public string AcceptedHostKey { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Utility.HostKeyException"/> class.
        /// </summary>
        /// <param name="message">The error message to report.</param>
        /// <param name="reportedkey">The reported host key.</param>
        /// <param name="acceptedkey">The acceptable host key.</param>
        /// <param name="innerexception">The inner exception.</param>
        public HostKeyException(string message, string reportedkey, string acceptedkey, Exception innerexception)
            : base(message, innerexception)
        {
            ReportedHostKey = reportedkey;
            AcceptedHostKey = acceptedkey;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Utility.HostKeyException"/> class.
        /// </summary>
        /// <param name="message">The error message to report.</param>
        /// <param name="reportedkey">The reported host key.</param>
        /// <param name="acceptedkey">The acceptable host key.</param>
        public HostKeyException(string message, string reportedkey, string acceptedkey)
            : base(message)
        {
            ReportedHostKey = reportedkey;
            AcceptedHostKey = acceptedkey;
        }
    }
}

