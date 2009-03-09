#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Helper class that allows a little more configuration than the original wrapper,
    /// and fixes various problems with it, such as EU bucket support
    /// </summary>
    public class S3Wrapper : Affirma.ThreeSharp.Wrapper.ThreeSharpWrapper
    {
        private static Dictionary<string, KeyValuePair<DateTime, string>> RedirectCache = new Dictionary<string, KeyValuePair<DateTime, string>>();

        protected const string EU_LOCATION_CONSTRAINT = "<CreateBucketConfiguration><LocationConstraint>EU</LocationConstraint></CreateBucketConfiguration>";
        protected bool m_euBucket;

        public S3Wrapper(string awsID, string awsKey, CallingFormat format, bool euBuckets)
        {
            this.config = new ThreeSharpConfig();
            this.config.AwsAccessKeyID = awsID;
            this.config.AwsSecretAccessKey = awsKey;
            this.config.Format = format;

            this.m_euBucket = euBuckets;

            if (euBuckets && format == CallingFormat.REGULAR)
                throw new Exception("EU buckets does not work with regular calling format");

            this.service = new ThreeSharpQuery(this.config);

        }

        public override void AddBucket(string bucketName)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (BucketAddRequest request = new BucketAddRequest(bucketName))
            {
                if (m_euBucket)
                    request.LoadStreamWithString(EU_LOCATION_CONSTRAINT);

                using (BucketAddResponse response = service.BucketAdd(request))
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
                using (ObjectGetResponse objectGetResponse = this.service.ObjectGet(objectGetRequest))
                    Core.Utility.CopyStream(objectGetResponse.DataStream, target);
            }
        }

        public override void GetFileObject(string bucketName, string keyName, string localfile)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                GetFileStream(bucketName, keyName, fs);
        }

        public override void AddFileObject(string bucketName, string keyName, string localfile)
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
                try { objectAddRequest.BytesTotal = source.Length; }
                catch { }

                objectAddRequest.RedirectUrl = GetRedirectUrl(bucketName, keyName);

                using (ObjectAddResponse objectAddResponse = this.service.ObjectAdd(objectAddRequest))
                { }
            }
        }

        public override void DeleteObject(string bucketName, string keyName)
        {
            //Due to a resource leak in the S3 code, we flush it here
            GC.Collect();
            using (ObjectDeleteRequest objectDeleteRequest = new ObjectDeleteRequest(bucketName, keyName))
            {
                objectDeleteRequest.RedirectUrl = GetRedirectUrl(bucketName, keyName);

                using (ObjectDeleteResponse objectDeleteResponse = service.ObjectDelete(objectDeleteRequest))
                { }
            }

        }

        public virtual List<FileEntry> ListBucket(string bucketName, string prefix)
        {
            bool isTruncated = true;
            string filename = null;

            string redirUrl = GetRedirectUrl(bucketName, null);
            List<FileEntry> files = new List<FileEntry>();

            //We truncate after 1000 elements, and then repeat
            while (isTruncated)
            {
                //Due to a resource leak in the S3 code, we flush it here
                GC.Collect();
                using (BucketListRequest listRequest = new BucketListRequest(bucketName))
                {
                    listRequest.RedirectUrl = redirUrl;
                    if (!string.IsNullOrEmpty(filename))
                        listRequest.QueryList.Add("marker", filename);

                    listRequest.QueryList.Add("max-keys", "1000");
                    if (!string.IsNullOrEmpty(prefix))
                        listRequest.QueryList.Add("prefix", prefix);

                    using (BucketListResponse listResponse = service.BucketList(listRequest))
                    {
                        XmlDocument bucketXml = listResponse.StreamResponseToXmlDocument();
                        XmlNodeList objects = bucketXml.SelectNodes("//*[local-name()='Contents']");

                        foreach (XmlNode obj in objects)
                        {
                            filename = obj["Key"].InnerXml;
                            long size = long.Parse(obj["Size"].InnerXml);
                            DateTime lastModified = DateTime.Parse(obj["LastModified"].InnerXml);
                            files.Add(new FileEntry(filename, size, lastModified, lastModified));
                        }

                        isTruncated = bool.Parse(bucketXml.SelectSingleNode("//*[local-name()='IsTruncated']").InnerXml);
                    }
                }
            }

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
                    using (BucketListResponse testResponse = service.BucketList(testRequest))
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
