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
using System.Text;

namespace Affirma.ThreeSharp
{

    /// <summary>
    /// Configuration object for accessing the S3 service
    /// </summary>
    public class ThreeSharpConfig
    {
        private static int securePort = 443;
        private static int insecurePort = 80;

        private String userAgent = "Amazon S3 CSharp Library";
        private bool isSecure = true;
        private CallingFormat callingFormat;
        private ThreeSharpServiceType serviceType = ThreeSharpServiceType.S3;

        private String awsAccessKeyId = null;
        private String awsSecretAccessKey = null;

        private int connectionLimit = 10;

        private System.Net.IWebProxy proxy = null;

        private string GetServerByTpe()
        {
            return (serviceType == ThreeSharpServiceType.AWS100) ? "cloudfront.amazonaws.com" : "s3.amazonaws.com";
        }

        public ThreeSharpServiceType ServiceType
        {
            get { return this.serviceType; }
            set { this.serviceType = value; }
        }

        public String Server
        {
            get { return this.GetServerByTpe(); }
        }

        public int Port
        {
            get { return (this.IsSecure ? ThreeSharpConfig.securePort : ThreeSharpConfig.insecurePort); }
        }

        public bool IsSecure
        {
            get { return this.isSecure; }
            set { this.isSecure = value; }
        }

        public String UserAgent
        {
            get { return this.userAgent; }
            set { this.userAgent = value; }
        }

        public CallingFormat Format
        {
            get { return this.callingFormat; }
            set { this.callingFormat = value; }
        }

        public String AwsAccessKeyID
        {
            get { return this.awsAccessKeyId; }
            set { this.awsAccessKeyId = value; }
        }

        public String AwsSecretAccessKey
        {
            get { return this.awsSecretAccessKey; }
            set { this.awsSecretAccessKey = value; }
        }

        public int ConnectionLimit
        {
            get { return this.connectionLimit; }
            set { this.connectionLimit = value; }
        }

        public System.Net.IWebProxy Proxy
        {
            get { return this.proxy; }
            set { this.proxy = value; }
        }

    }
}
