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
        private static readonly string Logtag = Logging.Log.LogTagFromType<S3MinioClient>();

        private readonly MinioClient m_client;
        private readonly string m_locationConstraint;        

        public S3MinioClient(string awsID, string awsKey, string locationConstraint, 
                string servername, string storageClass, bool useSSL, Dictionary<string, string> options)
        {

            m_locationConstraint = locationConstraint;
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
            try
            {
                m_client.MakeBucketAsync(bucketName, m_locationConstraint);
            }
            catch (MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorMakingBucketMinio", null,
                    "Error making bucket {0} using Minio: {1}", bucketName, e.ToString());
            }
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

        public virtual async Task AddFileStreamAsync(string bucketName, string keyName, Stream source, CancellationToken cancelToken)
        {
            try
            {
                await m_client.PutObjectAsync(bucketName,
                    keyName,
                    source,
                    source.Length,
                    "application/octet-stream", cancellationToken: cancelToken);
            }
            catch(MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorPuttingObjectMinio", null,
                    "Error putting object {0} to {1} using Minio: {2}",
                    keyName ,bucketName, e.ToString());
            }
        }
    }
}