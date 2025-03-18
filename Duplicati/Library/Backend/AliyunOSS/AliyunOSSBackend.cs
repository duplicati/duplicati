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
using Aliyun.OSS;
using Aliyun.OSS.Common;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.AliyunOSS
{
    /// <summary>
    /// Aliyun Object Storage Service(OSS) is a massive, secure, low-cost, and highly reliable cloud storage service, offering up to 99.995% service availability.It offers a variety of storage types to choose from, comprehensively optimizing storage costs.
    /// en: https://www.alibabacloud.com/en/product/object-storage-service
    /// zh: https://www.aliyun.com/product/oss
    /// </summary>
    public class OSS : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<OSS>();

        private const string OSS_BUCKET_NAME = "oss-bucket-name";
        private const string OSS_ENDPOINT = "oss-endpoint";
        private const string OSS_ACCESS_KEY_ID = "oss-access-key-id";
        private const string OSS_ACCESS_KEY_SECRET = "oss-access-key-secret";

        private AliyunOSSOptions _ossOptions;

        public OSS()
        { }

        public OSS(string url, Dictionary<string, string> options)
        {
            _ossOptions = new AliyunOSSOptions();

            var uri = new Utility.Uri(url?.Trim());
            var prefix = uri.HostAndPath?.TrimPath();

            if (!string.IsNullOrEmpty(prefix))
            {
                _ossOptions.Path = prefix;
            }

            if (options.ContainsKey(OSS_ACCESS_KEY_ID))
            {
                _ossOptions.AccessKeyId = options[OSS_ACCESS_KEY_ID];
            }

            if (options.ContainsKey(OSS_ACCESS_KEY_SECRET))
            {
                _ossOptions.AccessKeySecret = options[OSS_ACCESS_KEY_SECRET];
            }

            if (options.ContainsKey(OSS_BUCKET_NAME))
            {
                _ossOptions.BucketName = options[OSS_BUCKET_NAME];
            }

            if (options.ContainsKey(OSS_ENDPOINT))
            {
                _ossOptions.Endpoint = options[OSS_ENDPOINT];
            }
        }

        private OssClient GetClient(bool isUseNewServiceClient = true)
        {
            var endpoint = _ossOptions.Endpoint;
            var accessKeyId = _ossOptions.AccessKeyId;
            var accessKeySecret = _ossOptions.AccessKeySecret;
            if (isUseNewServiceClient)
            {
                return new OssClient(endpoint, accessKeyId, accessKeySecret);
            }
            return new OssClient(endpoint, accessKeyId, accessKeySecret, new ClientConfiguration()
            {
                UseNewServiceClient = false
            });
        }


        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var bucketName = _ossOptions.BucketName;

            var prefix = $"{_ossOptions.Path.TrimPath()}";

            var client = GetClient(false);

            var nextMarker = string.Empty;
            bool isTruncated;
            do
            {
                var listObjectsRequest = new ListObjectsRequest(bucketName)
                {
                    MaxKeys = 1000,
                    Marker = nextMarker,
                    Prefix = prefix,
                };

                ObjectListing result;
                try
                {
                    result = await Task.Run(() => client.ListObjects(listObjectsRequest), cancelToken).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                    if (result.HttpStatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception(result.HttpStatusCode.ToString());
                    }

                }
                catch (OssException ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "List", ex, "List object failed. {0}, {1}", ex.Message, ex.ErrorCode);
                    throw;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteErrorMessage(LOGTAG, "List", ex, "List object failed. {0}", ex.Message);
                    throw;
                }

                foreach (var summary in result.ObjectSummaries)
                {
                    var fileName = Path.GetFileName(summary.Key);
                    var time = summary.LastModified; // DateTimeOffset.Parse(summary.LastModified).ToLocalTime().DateTime;
                    yield return new FileEntry(fileName, summary.Size, time, time);
                }

                nextMarker = result.NextMarker;
                isTruncated = result.IsTruncated;
            } while (isTruncated);
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var client = GetClient();

                var bucketName = _ossOptions.BucketName;

                var objectName = $"{_ossOptions.Path.TrimPath()}/{remotename}".TrimPath();

                var res = client.DeleteObject(bucketName, objectName);
                if (res?.HttpStatusCode != HttpStatusCode.OK)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "Delete", "Delete object failed. it may have been deleted");
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Delete", ex, "Delete object failed. {0}", ex.Message);
                throw;
            }

            return Task.CompletedTask;
        }

        public Task TestAsync(CancellationToken cancelToken)
        {
            GetClient();
            return Task.CompletedTask;
        }

        public Task CreateFolderAsync(CancellationToken cancelToken)
            // No need to create folders
            => Task.CompletedTask;

        public void Dispose()
        {
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var bucketName = _ossOptions.BucketName;

            var objectName = $"{_ossOptions.Path.TrimPath()}/{remotename}".TrimPath();

            var client = GetClient();
            try
            {
                var objectResult = await Task.Factory.FromAsync(
                    (cb, state) => client.BeginPutObject(bucketName, objectName, stream, cb, state),
                    client.EndPutObject,
                    null).ConfigureAwait(false);

                if (objectResult?.HttpStatusCode != HttpStatusCode.OK)
                    throw new Exception("Put object failed");
            }
            catch (Exception ex)
            {
                throw new Exception($"Put object failed, {ex.Message}");
            }

        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var bucketName = _ossOptions.BucketName;

            var objectName = $"{_ossOptions.Path.TrimPath()}/{remotename}".TrimPath();

            var client = GetClient(false);

            try
            {
                var obj = await Task.Factory.FromAsync(
                    (cb, state) => client.BeginGetObject(bucketName, objectName, null, null),
                    client.EndGetObject,
                    null).ConfigureAwait(false);

                if (obj.HttpStatusCode != HttpStatusCode.OK)
                    throw new Exception("Get failed");

                using (var requestStream = obj.Content)
                    await Library.Utility.Utility.CopyStreamAsync(requestStream, stream, cancelToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Get", ex, "Get failed: {0}", ex.Message);
                throw;
            }
        }

        public async Task RenameAsync(string oldname, string newname, CancellationToken cancelToken)
        {
            var bucketName = _ossOptions.BucketName;

            var sourceBucket = bucketName;

            var targetBucket = bucketName;

            var sourceObject = $"{_ossOptions.Path.TrimPath()}/{oldname}".TrimPath();

            var targetObject = $"{_ossOptions.Path.TrimPath()}/{newname}".TrimPath();

            var client = GetClient();

            try
            {
                // copy file
                var req = new CopyObjectRequest(sourceBucket, sourceObject, targetBucket, targetObject);

                var res = await Task.Factory.FromAsync(
                    (cb, state) => client.BeginCopyObject(req, cb, state),
                    client.EndCopyResult,
                    null).ConfigureAwait(false);

                if (res?.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("file rename failed");
                }

                // del old file
                await DeleteAsync(oldname, cancelToken).ConfigureAwait(false);
            }
            catch (OssException ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Rename", ex, "Rename failed: {0} to {1}, {2}", oldname, newname, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Rename", ex, "Rename failed: {0} to {1}, {2}", oldname, newname, ex.Message);
                throw;
            }
        }

        public string DisplayName => Strings.OSSBackend.DisplayName;

        public string Description => Strings.OSSBackend.Description;

        public string ProtocolKey => "aliyunoss";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>([
                    new CommandLineArgument(OSS_ACCESS_KEY_ID, CommandLineArgument.ArgumentType.String, Strings.OSSBackend.OSSAccessKeyIdDescriptionShort, Strings.OSSBackend.OSSAccessKeyIdDescriptionLong),
                    new CommandLineArgument(OSS_ACCESS_KEY_SECRET, CommandLineArgument.ArgumentType.Password, Strings.OSSBackend.OSSAccessKeySecretDescriptionShort, Strings.OSSBackend.OSSAccessKeySecretDescriptionLong),
                    new CommandLineArgument(OSS_BUCKET_NAME, CommandLineArgument.ArgumentType.String, Strings.OSSBackend.OSSBucketNameDescriptionShort, Strings.OSSBackend.OSSBucketNameDescriptionLong),
                    new CommandLineArgument(OSS_ENDPOINT, CommandLineArgument.ArgumentType.String, Strings.OSSBackend.OSSEndpointDescriptionShort, Strings.OSSBackend.OSSEndpointDescriptionLong)
                ]);
            }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());
    }
}