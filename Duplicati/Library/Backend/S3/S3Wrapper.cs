#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using System.Linq;
using System.Collections.Generic;
using Amazon.S3;
using Amazon.S3.Model;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Helper class that fixes long list support and injects location headers, includes using directives etc.
    /// </summary>
    public class S3Wrapper : IDisposable
    {
        private static string LOGTAG = Logging.Log.LogTagFromType<S3Wrapper>();
        private const int ITEM_LIST_LIMIT = 1000;

        protected string m_locationConstraint;
        protected string m_storageClass;
        protected AmazonS3Client m_client;

        public readonly string DNSHost;

        public S3Wrapper(string awsID, string awsKey, string locationConstraint, string servername, string storageClass, bool useSSL, Dictionary<string, string> options)
        {
            var cfg = new AmazonS3Config
            {
                UseHttp = !useSSL,
                ServiceURL = (useSSL ? "https://" : "http://") + servername,
                BufferSize = (int) Utility.Utility.DEFAULT_BUFFER_SIZE
            };

            foreach (var opt in options.Keys.Where(x => x.StartsWith("s3-ext-", StringComparison.OrdinalIgnoreCase)))
            {
                var prop = cfg.GetType().GetProperties().FirstOrDefault(x => string.Equals(x.Name, opt.Substring("s3-ext-".Length), StringComparison.OrdinalIgnoreCase));
                if (prop != null && prop.CanWrite)
                {
                    if (prop.PropertyType == typeof(bool))
                        prop.SetValue(cfg, Utility.Utility.ParseBoolOption(options, opt));
                    else if (prop.PropertyType.IsEnum)
                        prop.SetValue(cfg, Enum.Parse(prop.PropertyType, options[opt], true));
                    else if (prop.PropertyType == typeof(int))
                        prop.SetValue(cfg, int.Parse(options[opt]));
                    else if (prop.PropertyType == typeof(long))
                        prop.SetValue(cfg, long.Parse(options[opt]));
                    else if (prop.PropertyType == typeof(string))
                        prop.SetValue(cfg, options[opt]);
                }

                if (prop == null)
                    Logging.Log.WriteWarningMessage(LOGTAG, "UnsupportedOption", null, "Unsupported option: {0}", opt);
            }

            m_client = new AmazonS3Client(awsID, awsKey, cfg);

            m_locationConstraint = locationConstraint;
            m_storageClass = storageClass;
            DNSHost = string.IsNullOrWhiteSpace(cfg.ServiceURL) ? null : new Uri(cfg.ServiceURL).Host;
        }

        public void AddBucket(string bucketName)
        {
            PutBucketRequest request = new PutBucketRequest
            {
                BucketName = bucketName
            };

            if (!string.IsNullOrEmpty(m_locationConstraint))
                request.BucketRegionName = m_locationConstraint;

            m_client.PutBucket(request);
        }

        public virtual void GetFileStream(string bucketName, string keyName, System.IO.Stream target)
        {
            GetObjectRequest objectGetRequest = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = keyName
            };

            using (GetObjectResponse objectGetResponse = m_client.GetObject(objectGetRequest))
            using(System.IO.Stream s = objectGetResponse.ResponseStream)
            {
                try { s.ReadTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds; }
                catch { }

                Utility.Utility.CopyStream(s, target);
            }
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
            PutObjectRequest objectAddRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = keyName,
                InputStream = source
            };
            if (!string.IsNullOrWhiteSpace(m_storageClass))
                objectAddRequest.StorageClass = new S3StorageClass(m_storageClass);

            m_client.PutObject(objectAddRequest);
        }

        public void DeleteObject(string bucketName, string keyName)
        {
            DeleteObjectRequest objectDeleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = keyName
            };

            m_client.DeleteObject(objectDeleteRequest);
        }

        public virtual IEnumerable<IFileEntry> ListBucket(string bucketName, string prefix)
        {
            bool isTruncated = true;
            string filename = null;

            //TODO: Figure out if this is the case with AWSSDK too
            //Unfortunately S3 sometimes reports duplicate values when requesting more than one page of results
            //So, track the files that have already been returned and skip any duplicates.
            HashSet<string> alreadyReturned = new HashSet<string>();

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

                ListObjectsResponse listResponse = m_client.ListObjects(listRequest);
                isTruncated = listResponse.IsTruncated;
                filename = listResponse.NextMarker;

                foreach (S3Object obj in listResponse.S3Objects)
                {
                    if (alreadyReturned.Add(obj.Key))
                    {
                        yield return new Common.IO.FileEntry(
                            obj.Key,
                            obj.Size,
                            obj.LastModified,
                            obj.LastModified
                        );
                    }
                }
            }
        }

        public void RenameFile(string bucketName, string source, string target)
        {
            CopyObjectRequest copyObjectRequest = new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = source,
                DestinationBucket = bucketName,
                DestinationKey = target
            };

            m_client.CopyObject(copyObjectRequest);

            DeleteObject(bucketName, source);
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
