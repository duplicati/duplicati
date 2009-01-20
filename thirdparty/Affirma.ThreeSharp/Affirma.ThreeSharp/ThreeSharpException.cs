/******************************************************************************* 
 *  Licensed under the Apache License, Version 2.0 (the "License"); 
 *  
 *  You may not use this file except in compliance with the License. 
 *  You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0.html 
 *  This file is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
 *  CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 *  specific language governing permissions and limitations under the License.
 * ***************************************************************************** 
 * 
 *  Joel Wetzel
 *  Affirma Consulting
 *  jwetzel@affirmaconsulting.com
 * 
 */

using System;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Net;
using Affirma.ThreeSharp;
using Affirma.ThreeSharp.Model;

namespace Affirma.ThreeSharp
{
    /// <summary>
    /// ThreeSharpException provides details of errors 
    /// returned by the S3 service
    /// </summary>
    public class ThreeSharpException : Exception
    {

        private String message = null;
        private HttpStatusCode statusCode = default(HttpStatusCode);
        private String errorCode = null;
        private String requestId = null;
        private String xml = null;


        /// <summary>
        /// Constructs ThreeSharpException with message
        /// </summary>
        /// <param name="message">Overview of error</param>
        public ThreeSharpException(String message)
        {
            this.message = message;
        }

        /// <summary>
        /// Constructs ThreeSharpException with message and status code
        /// </summary>
        /// <param name="message">Overview of error</param>
        /// <param name="statusCode">HTTP status code for error response</param>
        public ThreeSharpException(String message, HttpStatusCode statusCode)
            : this(message)
        {
            this.statusCode = statusCode;
        }


        /// <summary>
        /// Constructs ThreeSharpException with wrapped exception
        /// </summary>
        /// <param name="t">Wrapped exception</param>
        public ThreeSharpException(Exception t)
            : this(t.Message, t)
        {

        }

        /// <summary>
        /// Constructs ThreeSharpException with message and wrapped exception
        /// </summary>
        /// <param name="message">Overview of error</param>
        /// <param name="t">Wrapped exception</param>
        public ThreeSharpException(String message, Exception t)
            : base(message, t)
        {
            this.message = message;
            if (t is ThreeSharpException)
            {
                ThreeSharpException ex = (ThreeSharpException)t;
                this.statusCode = ex.StatusCode;
                this.errorCode = ex.ErrorCode;
                this.requestId = ex.RequestId;
                this.xml = ex.XML;
            }
        }


        /// <summary>
        /// Constructs ThreeSharpException with information available from service
        /// </summary>
        /// <param name="message">Overview of error</param>
        /// <param name="statusCode">HTTP status code for error response</param>
        /// <param name="errorCode">Error Code returned by the service</param>
        /// <param name="requestId">Request ID returned by the service</param>
        /// <param name="xml">Compete xml found in response</param>
        public ThreeSharpException(String message, HttpStatusCode statusCode, String errorCode, String requestId, String xml)
            : this(message, statusCode)
        {
            this.errorCode = errorCode;
            this.requestId = requestId;
            this.xml = xml;
        }

        /// <summary>
        /// Gets and sets of the ErrorCode property.
        /// </summary>
        public String ErrorCode
        {
            get { return this.errorCode; }
        }


        /// <summary>
        /// Gets error message
        /// </summary>
        public override String Message
        {
            get { return this.message; }
        }


        /// <summary>
        /// Gets status code returned by the service if available. If status
        /// code is set to -1, it means that status code was unavailable at the
        /// time exception was thrown
        /// </summary>
        public HttpStatusCode StatusCode
        {
            get { return this.statusCode; }
        }

        /// <summary>
        /// Gets XML returned by the service if available.
        /// </summary>
        public String XML
        {
            get { return this.xml; }
        }

        /// <summary>
        /// Gets Request ID returned by the service if available.
        /// </summary>
        public String RequestId
        {
            get { return this.requestId; }
        }

    }
}
