#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using System.Xml;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Helper class that fixes long list support and injects location headers, includes using directives etc.
    /// </summary>
    public class S3Wrapper : IDisposable
    {
        private const int ITEM_LIST_LIMIT = 1000;

        protected string m_locationConstraint;
        protected bool m_useRRS;
		protected AmazonS3Client m_client;

        public S3Wrapper(string awsID, string awsKey, string locationConstraint, string servername, bool useRRS, bool useSSL)
        {
            AmazonS3Config cfg = new AmazonS3Config();
            
            cfg.CommunicationProtocol = useSSL ? Amazon.S3.Model.Protocol.HTTPS : Amazon.S3.Model.Protocol.HTTP;
            cfg.ServiceURL = servername;
            cfg.UserAgent = "Duplicati v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + " S3 client with AWS SDK v" + cfg.GetType().Assembly.GetName().Version.ToString();
            cfg.UseSecureStringForAwsSecretKey = false;
            cfg.BufferSize = (int)Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE;

            m_client = new Amazon.S3.AmazonS3Client(awsID, awsKey, cfg);

            m_locationConstraint = locationConstraint;
            m_useRRS = useRRS;
        }

        public void AddBucket(string bucketName)
        {
            PutBucketRequest request = new PutBucketRequest();
            request.BucketName = bucketName;

            if (!string.IsNullOrEmpty(m_locationConstraint))
                request.BucketRegionName = m_locationConstraint;

            using (PutBucketResponse response = m_client.PutBucket(request))
            { }
        }

        public virtual void GetFileStream(string bucketName, string keyName, System.IO.Stream target)
        {
            GetObjectRequest objectGetRequest = new GetObjectRequest();
            objectGetRequest.BucketName = bucketName;
            objectGetRequest.Key = keyName;
            objectGetRequest.Timeout = System.Threading.Timeout.Infinite;
            objectGetRequest.ReadWriteTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
            
            using (GetObjectResponse objectGetResponse = m_client.GetObject(objectGetRequest))
            using (System.IO.Stream s = objectGetResponse.ResponseStream)
                Utility.Utility.CopyStream(s, target);
        }

        public void GetFileObject(string bucketName, string keyName, string localfile)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                GetFileStream(bucketName, keyName, fs);
        }

        public void AddFileObject(string bucketName, string keyName, string localfile)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                AddFileStream(bucketName, keyName, fs);
        }

        public virtual void AddFileStream(string bucketName, string keyName, System.IO.Stream source)
        {
            PutObjectRequest objectAddRequest = new PutObjectRequest();
            objectAddRequest.BucketName = bucketName;
            objectAddRequest.Key = keyName;
            objectAddRequest.InputStream = source;
            objectAddRequest.StorageClass = m_useRRS ? S3StorageClass.ReducedRedundancy : S3StorageClass.Standard;
            objectAddRequest.GenerateMD5Digest = false; //We would like this, but cannot read the stream twice :(
            objectAddRequest.Timeout = System.Threading.Timeout.Infinite;
            objectAddRequest.ReadWriteTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;

            using (PutObjectResponse objectAddResponse = m_client.PutObject(objectAddRequest))
            { }
        }

        public void DeleteObject(string bucketName, string keyName)
        {
            DeleteObjectRequest objectDeleteRequest = new DeleteObjectRequest();
            objectDeleteRequest.BucketName = bucketName;
            objectDeleteRequest.Key = keyName;

            using (DeleteObjectResponse objectDeleteResponse = m_client.DeleteObject(objectDeleteRequest))
            { }
        }

        public virtual List<IFileEntry> ListBucket(string bucketName, string prefix)
        {
            bool isTruncated = true;
            string filename = null;

            List<IFileEntry> files = new List<IFileEntry>();

            //We truncate after ITEM_LIST_LIMIT elements, and then repeat
            while (isTruncated)
            {
                ListObjectsRequest listRequest = new ListObjectsRequest();
                listRequest.BucketName = bucketName;

                if (!string.IsNullOrEmpty(filename))
                    listRequest.Marker = filename;

                listRequest.MaxKeys = ITEM_LIST_LIMIT;
                if (!string.IsNullOrEmpty(prefix))
                    listRequest.Prefix = prefix;

                using (ListObjectsResponse listResponse = m_client.ListObjects(listRequest))
                {
                    isTruncated = listResponse.IsTruncated;
                    filename = listResponse.NextMarker;

                    foreach (S3Object obj in listResponse.S3Objects)
                    {
                        DateTime dt;
                        if (DateTime.TryParse(obj.LastModified, out dt))
                            files.Add(new FileEntry(
                                obj.Key,
                                obj.Size,
                                dt,
                                dt
                            ));
                        else
                            files.Add(new FileEntry(
                                obj.Key,
                                obj.Size
                            ));

                    }

                    //filename = files[files.Count - 1].Name;
                }
            }

            //TODO: Figure out if this is the case with AWSSDK too
            //Unfortunately S3 sometimes reports duplicate values when requesting more than one page of results
            Dictionary<string, string> tmp = new Dictionary<string, string>();
            for (int i = 0; i < files.Count; i++)
                if (tmp.ContainsKey(files[i].Name))
                {
                    files.RemoveAt(i);
                    i--;
                }
                else
                    tmp.Add(files[i].Name, null);

            return files;
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (m_client != null)
                m_client.Dispose();
            m_client = null;
        }

        #endregion
    }
}
