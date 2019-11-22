using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Minio;
using Minio.Exceptions;
using Minio.DataModel;

namespace Duplicati.Library.Backend
{
    public class S3MinioClient : IS3Client
    {
        private readonly MinioClient m_client;

        public S3MinioClient(string awsID, string awsKey, string locationConstraint, 
                string servername, string storageClass, bool useSSL, Dictionary<string, string> options)
        {

            m_client = new MinioClient(
                servername,
                awsID,
                awsKey,
                locationConstraint
            );

            if (useSSL)
            {
                m_client = m_client.WithSSL();
            }
        }

        public void Dispose()
        {
            return;
        }

        public IEnumerable<IFileEntry> ListBucket(string bucketName, string prefix)
        {
            var observable = m_client.ListObjectsAsync(bucketName, prefix, true);

            // TODO: add exception handling
            foreach (var obj in observable.ToEnumerable())
            {
                yield return new Common.IO.FileEntry(
                    obj.Key,
                    (long) obj.Size,
                    Convert.ToDateTime(obj.LastModified),
                    Convert.ToDateTime(obj.LastModified)
                );
            }
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