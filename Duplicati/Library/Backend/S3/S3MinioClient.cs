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
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Threading.Channels;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.ObjectLock;
using Minio.Exceptions;

namespace Duplicati.Library.Backend
{
    public class S3MinioClient : IS3Client
    {
        private static readonly string Logtag = Logging.Log.LogTagFromType<S3MinioClient>();

        private readonly IMinioClient m_client;
        private readonly string? m_locationConstraint;
        private readonly string m_dnsHost;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;
        private readonly string m_lockMode;

        public S3MinioClient(string awsID, string awsKey, string? locationConstraint,
            string servername, string? storageClass, bool useSSL, TimeoutOptionsHelper.Timeouts timeouts, Dictionary<string, string?> options, string lockMode)
        {
            m_timeouts = timeouts;
            m_locationConstraint = locationConstraint;
            m_lockMode = lockMode;
            m_client = new MinioClient()
                .WithEndpoint(servername)
                .WithCredentials(
                    awsID,
                    awsKey
                )
                .WithSSL(useSSL)
                .Build();

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
            await ThrowExceptionIfBucketDoesNotExist(bucketName, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(prefix))
                prefix = Util.AppendDirSeparator(prefix, "/");

            var observable = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancellationToken, ct
                           => m_client.ListObjectsAsync(new ListObjectsArgs().WithBucket(bucketName).WithPrefix(prefix).WithRecursive(recursive), ct)
                       ).ConfigureAwait(false);

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
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                    => m_client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName).WithLocation(m_locationConstraint), cancelToken)
                ).ConfigureAwait(false);
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
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                    => m_client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucketName).WithObject(keyName), ct)
                ).ConfigureAwait(false);
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
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                    => m_client.CopyObjectAsync(new CopyObjectArgs().WithBucket(bucketName).WithObject(target).WithCopyObjectSource(new CopySourceObjectArgs().WithBucket(bucketName).WithObject(source)), cancellationToken: ct)
                ).ConfigureAwait(false);
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
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                    => m_client.StatObjectAsync(new StatObjectArgs().WithBucket(bucketName).WithObject(keyName), cancellationToken: cancelToken)
                ).ConfigureAwait(false);

                // Get input stream to have content of 'my-objectname' from 'my-bucketname'
                await m_client.GetObjectAsync(new GetObjectArgs().WithBucket(bucketName).WithObject(keyName).WithCallbackStream(
                    (stream) =>
                    {
                        using var t = stream.ObserveReadTimeout(m_timeouts.ReadWriteTimeout);
                        Utility.Utility.CopyStream(t, target);
                    })).ConfigureAwait(false);
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

        public string? GetDnsHost()
        {
            return m_dnsHost;
        }

        public virtual async Task AddFileStreamAsync(string bucketName, string keyName, Stream source,
            CancellationToken cancelToken)
        {
            await ThrowExceptionIfBucketDoesNotExist(bucketName, cancelToken).ConfigureAwait(false);

            try
            {
                using var t = source.ObserveReadTimeout(m_timeouts.ReadWriteTimeout, false);
                await m_client.PutObjectAsync(new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(keyName)
                    .WithStreamData(t)
                    .WithObjectSize(t.Length)
                    .WithContentType("application/octet-stream"),
                    cancellationToken: cancelToken);
            }
            catch (MinioException e)
            {
                Logging.Log.WriteErrorMessage(Logtag, "ErrorPuttingObjectMinio", null,
                    "Error putting object {0} to {1} using Minio: {2}",
                    keyName, bucketName, e.ToString());
            }
        }

        public async Task<DateTime?> GetObjectLockUntilAsync(string bucketName, string keyName, CancellationToken cancelToken)
        {
            // For MinIO, object retention depends on bucket object-lock configuration.
            // If the bucket is not configured for object locking, treat it as "no lock".
            try
            {
                _ = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                        => m_client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationArgs().WithBucket(bucketName), ct)
                    )
                    .ConfigureAwait(false);
            }
            catch (MissingObjectLockConfigurationException)
            {
                return null;
            }
            catch (MinioException e)
            {
                ParseAndThrowNotFoundException(e, keyName, bucketName);
                throw;
            }

            try
            {
                var retention = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                        => m_client.GetObjectRetentionAsync(new GetObjectRetentionArgs().WithBucket(bucketName).WithObject(keyName), ct)
                    )
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(retention?.RetainUntilDate))
                    return null;

                // MinIO returns a timestamp string (typically ISO-8601/RFC3339).
                if (DateTime.TryParse(retention.RetainUntilDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
                    return parsed;

                // Best-effort fallback parsing
                if (DateTime.TryParse(retention.RetainUntilDate, out parsed))
                    return parsed.ToUniversalTime();

                return null;
            }
            catch (MissingObjectLockConfigurationException)
            {
                return null;
            }
            catch (MinioException e)
            {
                ParseAndThrowNotFoundException(e, keyName, bucketName);
                throw;
            }
        }

        public async Task SetObjectLockUntilAsync(string bucketName, string keyName, DateTime lockUntilUtc, CancellationToken cancelToken)
        {
            var mode = ParseRetentionMode(m_lockMode);
            var lockUntil = lockUntilUtc.ToUniversalTime();

            await ThrowExceptionIfBucketDoesNotExist(bucketName, cancelToken).ConfigureAwait(false);

            async Task ApplyRetentionAsync(CancellationToken ct)
            {
                await m_client.SetObjectRetentionAsync(
                    new SetObjectRetentionArgs()
                        .WithBucket(bucketName)
                        .WithObject(keyName)
                        .WithRetentionMode(mode)
                        .WithRetentionUntilDate(lockUntil)
                        .WithBypassGovernanceMode(false),
                    ct
                ).ConfigureAwait(false);
            }

            try
            {
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ApplyRetentionAsync).ConfigureAwait(false);
            }
            catch (MissingObjectLockConfigurationException)
            {
                // Bucket is missing object-lock configuration; attempt to enable it and retry once.
                await EnsureBucketObjectLockConfigurationAsync(bucketName, cancelToken).ConfigureAwait(false);
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ApplyRetentionAsync).ConfigureAwait(false);
            }
            catch (MinioException e)
            {
                ParseAndThrowNotFoundException(e, keyName, bucketName);
                throw;
            }
        }

        private static ObjectRetentionMode ParseRetentionMode(string lockMode)
        {
            if (Enum.TryParse<ObjectRetentionMode>(lockMode, ignoreCase: true, out var parsed))
                return parsed;

            // Default to GOVERNANCE
            return ObjectRetentionMode.GOVERNANCE;
        }

        private async Task EnsureBucketObjectLockConfigurationAsync(string bucketName, CancellationToken cancelToken)
        {
            try
            {
                // If configuration exists, do nothing.
                _ = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                        => m_client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationArgs().WithBucket(bucketName), ct)
                    )
                    .ConfigureAwait(false);
                return;
            }
            catch (MissingObjectLockConfigurationException)
            {
                // Fall through and try to set it
            }
            catch (MinioException e)
            {
                // Bucket missing, etc.
                if (e is BucketNotFoundException || e.ServerResponse?.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FolderMissingException($"Bucket {bucketName} not found", e);

                throw;
            }

            var config = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockConfiguration.LockEnabled,
                // Do not set a default retention period; Duplicati applies per-object retention.
                Rule = null
            };

            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                    => m_client.SetObjectLockConfigurationAsync(
                        new SetObjectLockConfigurationArgs()
                            .WithBucket(bucketName)
                            .WithLockConfiguration(config),
                        ct
                    )
                ).ConfigureAwait(false);
        }

        private void ParseAndThrowNotFoundException(MinioException e, string keyName, string bucketName)
        {
            if (e.ServerResponse?.StatusCode == System.Net.HttpStatusCode.NotFound || e.Response?.Code == "NoSuchKey")
                throw new FileMissingException($"File {keyName} not found in bucket {bucketName}");

            if (e is BucketNotFoundException)
                throw new FolderMissingException($"Bucket {bucketName} not found");
            if (e is ObjectNotFoundException)
                throw new FileMissingException($"File {keyName} not found in bucket {bucketName}");
        }

        private Task ThrowExceptionIfBucketDoesNotExist(string bucketName, CancellationToken cancelToken)
            => Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
            {
                if (!await m_client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), ct))
                    throw new FolderMissingException($"Bucket {bucketName} does not exist.");
            });

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

    }
}
