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
            using (ObjectGetRequest objectGetRequest = new ObjectGetRequest(bucketName, keyName))
            {
                objectGetRequest.RedirectUrl = GetRedirectUrl(bucketName, keyName);
                using (ObjectGetResponse objectGetResponse = this.service.ObjectGet(objectGetRequest))
                    Core.Utility.CopyStream(objectGetResponse.DataStream, target);
            }
        }

        public override void GetFileObject(string bucketName, string keyName, string localfile)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                GetFileStream(bucketName, keyName, fs);
        }

        public override void AddFileObject(string bucketName, string keyName, string localfile)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                AddFileStream(bucketName, keyName, fs);
        }

        public virtual void AddFileStream(string bucketName, string keyName, System.IO.Stream source)
        {
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
            //TODO: Cache this info

            string redirectUrl = null;
            using (BucketListRequest testRequest = new BucketListRequest(bucketName))
            {
                testRequest.Method = "HEAD";
                using (BucketListResponse testResponse = service.BucketList(testRequest))
                    if (testResponse.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect)
                        redirectUrl = testResponse.Headers["Location"].ToString() + (filename == null ? "" : filename);
            }

            return redirectUrl;
        }

    }
}
