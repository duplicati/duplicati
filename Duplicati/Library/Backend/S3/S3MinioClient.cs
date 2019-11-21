using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Minio;
using Minio.Exceptions;
using Minio.DataModel;

namespace Duplicati.Library.Backend
{
    public class S3MinioClient : S3Client
    {
        protected MinioClient m_client;

        public S3MinioClient(string awsID, string awsKey, string locationConstraint, 
                string servername, string storageClass, bool useSSL, Dictionary<string, string> options)
        {
            
            m_client = new MinioClient(
                (useSSL ? "https://" : "http://") + servername,
                awsID,
                awsKey,
                locationConstraint
            ).WithSSL();
        }

        public void Dispose()
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<IFileEntry> ListBucket(string bucketName, string prefix)
        {
            throw new System.NotImplementedException();
        }

        public void AddBucket(string bucketName)
        {
            throw new System.NotImplementedException();
        }

        public void DeleteObject(string bucketName, string keyName)
        {
            throw new System.NotImplementedException();
        }

        public void RenameFile(string bucketName, string source, string target)
        {
            throw new System.NotImplementedException();
        }

        public void GetFileStream(string bucketName, string keyName, Stream target)
        {
            throw new System.NotImplementedException();
        }

        public string GetDnsHost()
        {
            throw new System.NotImplementedException();
        }

        public Task AddFileStreamAsync(string bucketName, string keyName, Stream source, CancellationToken cancelToken)
        {
            throw new System.NotImplementedException();
        }
    }
}