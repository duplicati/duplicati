using System.Collections.Generic;
using Minio;
using Minio.Exceptions;
using Minio.DataModel;

namespace Duplicati.Library.Backend
{
    public class MinioWrapper
    {
        protected MinioClient m_client;

        public MinioWrapper(string awsID, string awsKey, string locationConstraint, 
                string servername, string storageClass, bool useSSL, Dictionary<string, string> options)
        {
            
            m_client = new MinioClient(
                (useSSL ? "https://" : "http://") + servername,
                awsID,
                awsKey,
                locationConstraint
            ).WithSSL();
        }
    }
}