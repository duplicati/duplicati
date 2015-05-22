using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Google.Apis;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Services;
using Google.Apis.Storage.v1;
using Google.Apis.Storage.v1.Data;
using Google.Apis.Util.Store;
using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Backend.GoogleCloudstore
{
    class GSWrapper : IDisposable
    {
        private const int ITEM_LIST_LIMIT = 1000;

        private string projectId;
        private string bucketName;
        private string clientId;
        private string clientSecret;
        private Google.Apis.Storage.v1.StorageService service;

        private void auth(string proj, string cid, string csecret)
        {


            ServiceAccountCredential credential;

            string certificateFile = System.IO.Path.Combine(Environment.CurrentDirectory, csecret);
            string serviceAccountEmail = cid;

            var certificate = new X509Certificate2(certificateFile, "notasecret", X509KeyStorageFlags.Exportable);
            credential = new ServiceAccountCredential(
                new ServiceAccountCredential.Initializer(serviceAccountEmail)
                {
                    Scopes = new[] { StorageService.Scope.DevstorageReadWrite }
                }.FromCertificate(certificate));

            this.service = new StorageService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Duplicati Backend GSWrapper",
            });
            this.projectId = proj;
        }

        public GSWrapper(string proj, string cid, string csecret)
        {

            this.auth(proj, cid, csecret);
        }

        public virtual List<IFileEntry> ListBucket(string bucketName, string prefix)
        {
            bool isTruncated = true;
            string nextpage = null;
            var t = service.Buckets.Get(bucketName);
            List<IFileEntry> files = new List<IFileEntry>();
            while (isTruncated)
            {
                ObjectsResource.ListRequest listRequest = service.Objects.List(bucketName);
                //ListObjectsRequest listRequest = new ListObjectsRequest();
                //listRequest.BucketName = bucketName;

                if (!string.IsNullOrEmpty(nextpage)) {
                    listRequest.PageToken = nextpage;
                }
                //if (!string.IsNullOrEmpty(filename))
                    //listRequest.Marker = filename;

                listRequest.Delimiter = "/";
                listRequest.MaxResults = ITEM_LIST_LIMIT;

                if (!string.IsNullOrEmpty(prefix))
                    listRequest.Prefix = prefix;




                Google.Apis.Storage.v1.Data.Objects listResponse = listRequest.Execute();
                nextpage = listResponse.NextPageToken;
                isTruncated = ! string.IsNullOrEmpty(nextpage);

                if (listResponse.Items != null)
                    foreach (Google.Apis.Storage.v1.Data.Object obj in listResponse.Items)
                    {
                        files.Add(new FileEntry(
                                obj.Name,
                                (long)obj.Size,
                                (DateTime)obj.Updated,
                                (DateTime)obj.Updated
                            ));
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

        public void AddFileObject(string bucketName, string fileName, string localfile)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                AddFileStream(bucketName, fileName, fs);
        }

        public void AddFileStream(string bucketName, string fileName, System.IO.Stream source)
        {
            Google.Apis.Storage.v1.Data.Object fileobj = new Google.Apis.Storage.v1.Data.Object() { Name = fileName };
            service.Objects.Insert(fileobj, bucketName, source, "application/octet-stream").Upload();
        }

        public virtual void GetFileStream(string bucketName, string fileName, System.IO.Stream target)
        {
            Google.Apis.Storage.v1.Data.Object fileobj = service.Objects.Get(bucketName, fileName).Execute();
            MediaDownloader downloader = new MediaDownloader(service);
            downloader.Download(fileobj.MediaLink, target);
        }

        public void GetFileObject(string bucketName, string fileName, string localfile)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                GetFileStream(bucketName, fileName, fs);
        }

        public void DeleteObject(string bucketName, string fileName)
        {
            service.Objects.Delete(bucketName, fileName);
        }



        #region IDisposable Members

        public void Dispose()
        {
            if (this.service != null) {
                this.service.Dispose();
            }
/*            if (m_client != null)
                m_client.Dispose();
            m_client = null; */
        }

        #endregion

    }
}
