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
using System.Collections.Generic;
using System.Text;
using Affirma.ThreeSharp.Model;
using Affirma.ThreeSharp.Query;

namespace Affirma.ThreeSharp.Wrapper
{
    /// <summary>
    /// Wraps basic functionality of the ThreeSharp Library into single-line commands
    /// </summary>
    public class ThreeSharpWrapper
    {
        private ThreeSharpConfig config;
        private IThreeSharp service;

        public ThreeSharpWrapper(String awsAccessKeyId, String awsSecretAccessKey)
        {
            this.config = new ThreeSharpConfig();
            this.config.AwsAccessKeyID = awsAccessKeyId;
            this.config.AwsSecretAccessKey = awsSecretAccessKey;

            this.service = new ThreeSharpQuery(this.config);
        }

        /// <summary>
        /// Adds a bucket to an S3 account
        /// </summary>
        public void AddBucket(String bucketName)
        {
            using (BucketAddRequest bucketAddRequest = new BucketAddRequest(bucketName))
            using (BucketAddResponse bucketAddResponse = this.service.BucketAdd(bucketAddRequest))
            { }
        }

        /// <summary>
        /// Returns a string of XML, describing the contents of a bucket
        /// </summary>
        public String ListBucket(String bucketName)
        {
            using (BucketListRequest bucketListRequest = new BucketListRequest(bucketName))
            using (BucketListResponse bucketListResponse = this.service.BucketList(bucketListRequest))
            {
                return bucketListResponse.StreamResponseToString();
            }
        }

        /// <summary>
        /// Adds a string to a bucket, as an object
        /// </summary>
        public void AddStringObject(String bucketName, String keyName, String data)
        {
            using (ObjectAddRequest objectAddRequest = new ObjectAddRequest(bucketName, keyName))
            {
                objectAddRequest.LoadStreamWithString(data);
                using (ObjectAddResponse objectAddResponse = this.service.ObjectAdd(objectAddRequest))
                { }
            }
        }

        /// <summary>
        /// Streams a file to a bucket as an object
        /// </summary>
        public void AddFileObject(String bucketName, String keyName, String localfile)
        {
            using (ObjectAddRequest objectAddRequest = new ObjectAddRequest(bucketName, keyName))
            {
                objectAddRequest.LoadStreamWithFile(localfile);
                using (ObjectAddResponse objectAddResponse = this.service.ObjectAdd(objectAddRequest))
                { }
            }
        }

        /// <summary>
        /// Streams a file to a bucket as an object, with encryption
        /// </summary>
        public void AddEncryptFileObject(String bucketName, String keyName, String localfile, String encryptionKey, String encryptionIV)
        {
            using (ObjectAddRequest objectAddRequest = new ObjectAddRequest(bucketName, keyName))
            {
                objectAddRequest.LoadStreamWithFile(localfile);
                objectAddRequest.EncryptStream(encryptionKey, encryptionIV);
                using (ObjectAddResponse objectAddResponse = this.service.ObjectAdd(objectAddRequest))
                { }
            }
        }

        /// <summary>
        /// Gets a string object from a bucket, and returns it as a String
        /// </summary>
        public String GetStringObject(String bucketName, String keyName)
        {
            String stringResponse = null;

            using (ObjectGetRequest objectGetRequest = new ObjectGetRequest(bucketName, keyName))
            using (ObjectGetResponse objectGetResponse = this.service.ObjectGet(objectGetRequest))
            {
                stringResponse = objectGetResponse.StreamResponseToString();
            }

            return stringResponse;
        }

        /// <summary>
        /// Gets a file object from a bucket, and streams it to disk
        /// </summary>
        public void GetFileObject(String bucketName, String keyName, String localfile)
        {
            using (ObjectGetRequest objectGetRequest = new ObjectGetRequest(bucketName, keyName))
            using (ObjectGetResponse objectGetResponse = this.service.ObjectGet(objectGetRequest))
            {
                objectGetResponse.StreamResponseToFile(localfile);
            }
        }

        /// <summary>
        /// Gets a file object from a bucket, streaming it to disk, with decryption
        /// </summary>
        public void GetDecryptFileObject(String bucketName, String keyName, String localfile, String encryptionKey, String encryptionIV)
        {
            using (ObjectGetRequest objectGetRequest = new ObjectGetRequest(bucketName, keyName))
            using (ObjectGetResponse objectGetResponse = this.service.ObjectGet(objectGetRequest))
            {
                objectGetResponse.DecryptStream(encryptionKey, encryptionIV);
                objectGetResponse.StreamResponseToFile(localfile);
            }
        }

        /// <summary>
        /// Copies an object from a source location to a destination location
        /// </summary>
        public void CopyObject(String sourceBucketName, String sourceKey, String destinationBucketName, String destinationKey)
        {
            using (ObjectCopyRequest objectCopyRequest = new ObjectCopyRequest(sourceBucketName, sourceKey, destinationBucketName, destinationKey))
            using (ObjectCopyResponse objectCopyResponse = this.service.ObjectCopy(objectCopyRequest))
            { }
        }

        /// <summary>
        /// Generates a URL to access an S3 object in a bucket
        /// </summary>
        public String GetUrl(String bucketName, String keyName)
        {
            using (UrlGetRequest urlGetRequest = new UrlGetRequest(bucketName, keyName))
            {
                urlGetRequest.ExpiresIn = 60 * 1000;
                using (UrlGetResponse urlGetResponse = this.service.UrlGet(urlGetRequest))
                {
                    return urlGetResponse.StreamResponseToString();
                }
            }
        }

        /// <summary>
        /// Deletes an object from a bucket
        /// </summary>
        public void DeleteObject(String bucketName, String keyName)
        {
            using (ObjectDeleteRequest objectDeleteRequest = new ObjectDeleteRequest(bucketName, keyName))
            using (ObjectDeleteResponse objectDeleteResponse = service.ObjectDelete(objectDeleteRequest))
            { }
        }

        /// <summary>
        /// Deletes a bucket from an S3 account
        /// </summary>
        public void DeleteBucket(String bucketName)
        {
            using (BucketDeleteRequest bucketDeleteRequest = new BucketDeleteRequest(bucketName))
            using (BucketDeleteResponse bucketDeleteResponse = service.BucketDelete(bucketDeleteRequest))
            { }
        }
    }
}
