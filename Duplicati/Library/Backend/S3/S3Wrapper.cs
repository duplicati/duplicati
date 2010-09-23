#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
using Affirma.ThreeSharp;
using Affirma.ThreeSharp.Query;
using Affirma.ThreeSharp.Model;
using System.Xml;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Helper class that allows a little more configuration than the original wrapper,
    /// and fixes various problems with it, such as EU bucket support and long lists
    /// </summary>
    public class S3Wrapper
    {
        private const int ITEM_LIST_LIMIT = 1000;
        private static Dictionary<string, KeyValuePair<DateTime, string>> RedirectCache = new Dictionary<string, KeyValuePair<DateTime, string>>();

        protected const string EU_LOCATION_CONSTRAINT = "<CreateBucketConfiguration><LocationConstraint>EU</LocationConstraint></CreateBucketConfiguration>";
        protected bool m_euBucket;
        protected bool m_useRRS;
		private ThreeSharpConfig m_config;
		private ThreeSharpQuery m_service;

        public S3Wrapper(string awsID, string awsKey, CallingFormat format, bool euBuckets, bool useRRS)
        {
            m_config = new ThreeSharpConfig();
            m_config.AwsAccessKeyID = awsID;
            m_config.AwsSecretAccessKey = awsKey;
            m_config.Format = format;
			m_config.IsSecure = false;

            m_euBucket = euBuckets;
            m_useRRS = useRRS;

            if (euBuckets && format == CallingFormat.REGULAR)
                throw new Exception(Strings.S3Wrapper.EuroBucketsRequireSubDomainError);

            m_service = new ThreeSharpQuery(m_config);

        }

        public void AddBucket(string bucketName)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (BucketAddRequest request = new BucketAddRequest(bucketName))
            {
                if (m_euBucket)
                    request.LoadStreamWithString(EU_LOCATION_CONSTRAINT);

                using (BucketAddResponse response = m_service.BucketAdd(request))
                { }
            }
        }

        public virtual void GetFileStream(string bucketName, string keyName, System.IO.Stream target)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (ObjectGetRequest objectGetRequest = new ObjectGetRequest(bucketName, keyName))
            {
                objectGetRequest.RedirectUrl = GetRedirectUrl(bucketName, keyName);
                using (ObjectGetResponse objectGetResponse = m_service.ObjectGet(objectGetRequest))
                    Core.Utility.CopyStream(objectGetResponse.DataStream, target);
            }
        }

        public void GetFileObject(string bucketName, string keyName, string localfile)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                GetFileStream(bucketName, keyName, fs);
        }

        public void AddFileObject(string bucketName, string keyName, string localfile)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                AddFileStream(bucketName, keyName, fs);
        }

        public virtual void AddFileStream(string bucketName, string keyName, System.IO.Stream source)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (ObjectAddRequest objectAddRequest = new ObjectAddRequest(bucketName, keyName))
            {
                objectAddRequest.DataStream = source;

				//objectAddRequest.ContentType = "application/octet-stream";
                objectAddRequest.BytesTotal = source.Length; //The source length MUST be readable
                objectAddRequest.RedirectUrl = GetRedirectUrl(bucketName, keyName);
                if (m_useRRS)
                    objectAddRequest.Headers.Add("x-amz-storage-class", "REDUCED_REDUNDANCY");

                using (ObjectAddResponse objectAddResponse = m_service.ObjectAdd(objectAddRequest))
                { }
            }
        }

        public void DeleteObject(string bucketName, string keyName)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (ObjectDeleteRequest objectDeleteRequest = new ObjectDeleteRequest(bucketName, keyName))
            {
                objectDeleteRequest.RedirectUrl = GetRedirectUrl(bucketName, keyName);

                using (ObjectDeleteResponse objectDeleteResponse = m_service.ObjectDelete(objectDeleteRequest))
                { }
            }

        }

        public virtual List<IFileEntry> ListBucket(string bucketName, string prefix)
        {
            bool isTruncated = true;
            string filename = null;

            string redirUrl = GetRedirectUrl(bucketName, null);
            List<IFileEntry> files = new List<IFileEntry>();

            //We truncate after ITEM_LIST_LIMIT elements, and then repeat
            while (isTruncated)
            {
                //Due to a resource leak in the S3 code, we flush it here
                GC.Collect();
                using (BucketListRequest listRequest = new BucketListRequest(bucketName))
                {
					listRequest.RedirectUrl = redirUrl;
                    if (!string.IsNullOrEmpty(filename))
                        listRequest.QueryList.Add("marker", filename);

                    listRequest.QueryList.Add("max-keys", ITEM_LIST_LIMIT.ToString());
                    if (!string.IsNullOrEmpty(prefix))
                        listRequest.QueryList.Add("prefix", prefix);

                    using (BucketListResponse listResponse = m_service.BucketList(listRequest))
                    {
                        XmlDocument bucketXml = listResponse.StreamResponseToXmlDocument();
                        XmlNodeList objects = bucketXml.SelectNodes("//*[local-name()='Contents']");

                        foreach (XmlNode obj in objects)
                        {
                            filename = obj["Key"].InnerText;
                            long size = long.Parse(obj["Size"].InnerText);
                            DateTime lastModified = DateTime.Parse(obj["LastModified"].InnerText);
                            files.Add(new FileEntry(filename, size, lastModified, lastModified));
                        }

                        isTruncated = bool.Parse(bucketXml.SelectSingleNode("//*[local-name()='IsTruncated']").InnerText);
                    }
                }
            }

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

        protected string GetRedirectUrl(string bucketName, string filename)
        {
            if (!RedirectCache.ContainsKey(bucketName) || RedirectCache[bucketName].Key < DateTime.Now)
            {
                //Due to a resource leak in the S3 code, we flush it here
                GC.Collect();
                using (BucketListRequest testRequest = new BucketListRequest(bucketName))
                {
                   	testRequest.Method = "HEAD";
                    using (BucketListResponse testResponse = m_service.BucketList(testRequest))
                        if (testResponse.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect)
                            RedirectCache[bucketName] = new KeyValuePair<DateTime,string>(DateTime.Now.AddMinutes(5), testResponse.Headers["Location"].ToString());
                        else
                            RedirectCache[bucketName] = new KeyValuePair<DateTime,string>(DateTime.Now.AddHours(1), null);
                            //If there are no temp redirects, the DNS system is updated, and will likely never require temporary redirects
                }
            }

            string tempurl = RedirectCache[bucketName].Value;
            if (tempurl == null)
                return null;
            else
                return RedirectCache[bucketName].Value + (filename == null ? "" : filename);
        }

    }
}
