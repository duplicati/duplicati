// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Minio;
using Minio.Exceptions;

namespace Duplicati.Library.Backend
{
    public class S3MinioClient : IS3Client
    {
        private static readonly string Logtag = Logging.Log.LogTagFromType<S3MinioClient>();

        private MinioClient m_client;
        private readonly string m_locationConstraint;
        private readonly string m_dnsHost;

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

            m_dnsHost = servername;
        }

        private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            IObservable<T> observable,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateUnbounded<T>(); // Buffered channel for async iteration

            using var subscription = observable.Subscribe(
                item => channel.Writer.TryWrite(item),
                ex => channel.Writer.TryComplete(ex),
                () => channel.Writer.TryComplete()
            );

            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }

        public async IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ThrowExceptionIfBucketDoesNotExist(bucketName);

            if (!string.IsNullOrWhiteSpace(prefix))
                prefix = Util.AppendDirSeparator(prefix, "/");

            var observable = m_client.ListObjectsAsync(bucketName, prefix, recursive, cancellationToken);
            await foreach (var obj in ToAsyncEnumerable(observable, cancellationToken).ConfigureAwait(false))
            {
                if (obj.Key == prefix || !obj.Key.StartsWith(prefix))
                    continue;

                yield return new FileEntry(
                    obj.Key.Substring(prefix.Length),
                    (long)obj.Size,
                    Convert.ToDateTime(obj.LastModified),
                    Convert.ToDateTime(obj.LastModified)
                )
                { IsFolder = obj.Key.EndsWith("/") };
            }
        }

        public async Task AddBucketAsync(string bucketName, CancellationToken cancelToken)
        {
            try
            {
                await m_client.MakeBucketAsync(bucketName, m_locationConstraint, cancelToken).ConfigureAwait(false);
            }
            catch (MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorMakingBucketMinio", null,
                    "Error making bucket {0} using Minio: {1}", bucketName, e.ToString());
                throw;
            }
        }

        public async Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancelToken)
        {
            try
            {
                await m_client.RemoveObjectAsync(bucketName, keyName, cancelToken).ConfigureAwait(false);
            }
            catch (MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorRemovingObjectMinio", null,
                    "Error removing from bucket {0} object {1} using Minio: {1}",
                    bucketName, keyName, e.ToString());

                ParseAndThrowNotFoundException(e, keyName, bucketName);
                throw;
            }
        }

        public async Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken)
        {
            try
            {
                await m_client.CopyObjectAsync(bucketName, source,
                    bucketName, target, cancellationToken: cancelToken).ConfigureAwait(false);
            }
            catch (MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorCopyingObjectMinio", null,
                    "Error copying object {0} to {1} in bucket {2} using Minio: {3}",
                    source, target, bucketName, e.ToString());

                throw;
            }

            await DeleteObjectAsync(bucketName, source, cancelToken).ConfigureAwait(false);
        }

        public async Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken)
        {
            try
            {
                // Check whether the object exists using statObject().
                // If the object is not found, statObject() throws an exception,
                // else it means that the object exists.
                // Execution is successful.
                await m_client.StatObjectAsync(bucketName, keyName, cancellationToken: cancelToken).ConfigureAwait(false);

                // Get input stream to have content of 'my-objectname' from 'my-bucketname'
                await m_client.GetObjectAsync(bucketName, keyName,
                    (stream) => { Utility.Utility.CopyStream(stream, target); }).ConfigureAwait(false);
            }
            catch (MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorGettingObjectMinio", null,
                    "Error getting object {0} to {1} using Minio: {2}",
                    keyName, bucketName, e.ToString());

                ParseAndThrowNotFoundException(e, keyName, bucketName);
                throw;
            }
        }

        public string GetDnsHost()
        {
            return m_dnsHost;
        }

        public virtual async Task AddFileStreamAsync(string bucketName, string keyName, Stream source,
            CancellationToken cancelToken)
        {
            ThrowExceptionIfBucketDoesNotExist(bucketName);

            try
            {
                await m_client.PutObjectAsync(bucketName,
                    keyName,
                    source,
                    source.Length,
                    "application/octet-stream", cancellationToken: cancelToken);
            }
            catch (MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorPuttingObjectMinio", null,
                    "Error putting object {0} to {1} using Minio: {2}",
                    keyName, bucketName, e.ToString());
            }
        }

        private void ParseAndThrowNotFoundException(MinioException e, string keyName, string bucketName)
        {
            if (e.ServerResponse?.StatusCode == System.Net.HttpStatusCode.NotFound || e.Response?.Code == "NoSuchKey")
                throw new FileMissingException($"File {keyName} not found in bucket {bucketName}");
        }

        private void ThrowExceptionIfBucketDoesNotExist(string bucketName)
        {
            if (!m_client.BucketExistsAsync(bucketName).Await())
                throw new FolderMissingException($"Bucket {bucketName} does not exist.");
        }


        #region IDisposable Members

        public void Dispose()
        {
            m_client = null;
        }

        #endregion

    }
}