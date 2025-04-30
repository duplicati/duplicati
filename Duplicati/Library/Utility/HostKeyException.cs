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

