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
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Web;
using System.Xml;
using System.Security.Cryptography;
using Affirma.ThreeSharp.Statistics;

namespace Affirma.ThreeSharp.Model
{
    /// <summary>
    /// The base class for all Response objects
    /// </summary>
    public class Response : Transfer
    {
        private HttpStatusCode statusCode;
        public HttpStatusCode StatusCode
        {
            get { return this.statusCode; }
            set { this.statusCode = value; }
        }

        public byte[] StreamResponseToBytes()
        {
            MemoryStream memoryStream = new MemoryStream();

            this.TransferStream(this.dataStream, memoryStream);

            byte[] data = memoryStream.ToArray();

            memoryStream.Close();
            this.dataStream.Close();

            return data;
        }


        public string StreamResponseToString()
        {
            UTF8Encoding ue = new UTF8Encoding();
            return ue.GetString(this.StreamResponseToBytes());
        }


        public XmlDocument StreamResponseToXmlDocument()
        {
            XmlDocument xd = new XmlDocument();
            xd.LoadXml(this.StreamResponseToString());
            return xd;
        }


        public void StreamResponseToFile(String localfile)
        {
            using (FileStream fileStream = File.OpenWrite(localfile))
            {
                this.TransferStream(this.dataStream, fileStream);
            }

            this.dataStream.Close();
        }

        public void DecryptStream(string encryptionKey, string encryptionIV)
        {
        DecryptStream(new DESCryptoServiceProvider(), encryptionKey, encryptionIV);
        }

        public void DecryptStream(SymmetricAlgorithm cryptoServiceProvider, string encryptionKey, string encryptionIV)
        {
            Stream existingStream = this.dataStream;

            cryptoServiceProvider.Key = ASCIIEncoding.ASCII.GetBytes(encryptionKey);
            cryptoServiceProvider.IV = ASCIIEncoding.ASCII.GetBytes(encryptionIV);
            CryptoStream cryptoStream = new CryptoStream(existingStream, cryptoServiceProvider.CreateDecryptor(), CryptoStreamMode.Read);
            this.dataStream = cryptoStream;
        }

        
        private void TransferStream(Stream responseStream, Stream outputStream)
        {
            byte[] buffer = new byte[1024];
            int bytesRead = 0;
            while (true)
            {
                bytesRead = responseStream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                    break;

                outputStream.Write(buffer, 0, bytesRead);
                this.BytesTransferred += bytesRead;
            }
            this.BytesTotal = this.BytesTransferred;
        }

    }
}
