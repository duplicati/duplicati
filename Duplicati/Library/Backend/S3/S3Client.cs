using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public interface S3Client : IDisposable
    {
        IEnumerable<IFileEntry> ListBucket(string bucketName, string prefix);

        void AddBucket(string bucketName);

        void DeleteObject(string bucketName, string keyName);

        void RenameFile(string bucketName, string source, string target);

        void GetFileStream(string bucketName, string keyName, System.IO.Stream target);

        string GetDnsHost();

        Task AddFileStreamAsync(string bucketName, string keyName, System.IO.Stream source, CancellationToken cancelToken);
    }
}