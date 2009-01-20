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
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Affirma.ThreeSharp.Model
{
    /// <summary>
    /// Represents a GET or a PUT request or response.
    /// The base class for Affirma.ThreeSharp.Model.Request and Affirma.ThreeSharp.Model.Response.
    /// Also used for statistical purposes.
    /// </summary>
    public class Transfer : IDisposable
    {
        private bool isDisposed = false;
        private String id;
        protected Stream dataStream;
        private String method;
        private String bucketName;
        private String key;
        private ThreeSharpServiceType serviceType = ThreeSharpServiceType.S3;
        protected SortedList headers;
        private long bytesTransferred = 0;
        private long bytesTotal = 0;

        public Transfer()
        {
            this.id = System.Guid.NewGuid().ToString();

            this.headers = new SortedList();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    if (dataStream != null)
                        dataStream.Dispose();
                }
                this.isDisposed = true;
            }
        }

        ~Transfer()
        {
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        public String ID
        {
            get { return this.id; }
        }
        public String Method
        {
            get { return this.method; }
            set { this.method = value; }
        }
        public String BucketName
        {
            get { return this.bucketName; }
            set { this.bucketName = value; }
        }
        public String Key
        {
            get { return this.key; }
            set { this.key = value; }
        }
        public ThreeSharpServiceType ServiceType
        {
            get { return this.serviceType; }
            set { this.serviceType = value; }
        }
        public SortedList Headers
        {
            get { return this.headers; }
            set { this.headers = value; }
        }
        public long BytesTransferred
        {
            get { return this.bytesTransferred; }
            set { this.bytesTransferred = value; }
        }
        public long BytesTotal
        {
            get { return this.bytesTotal; }
            set { this.bytesTotal = value; }
        }
        public Stream DataStream
        {
            get { return this.dataStream; }
            set { this.dataStream = value; }
        }

    }
}
