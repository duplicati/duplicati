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

using COSXML;
using COSXML.Auth;
using COSXML.Model.Bucket;
using COSXML.Model.Object;
using COSXML.Model.Tag;
using COSXML.Utils;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend.TencentCOS
{
    /// <summary>
    /// Tencent COS
    /// en: https://intl.cloud.tencent.com/document/product/436
    /// zh: https://cloud.tencent.com/document/product/436
    /// </summary>
    public class COS : IBackend, IRenameEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<COS>();

        private const string COS_APP_ID = "cos-app-id";
        private const string COS_REGION = "cos-region";
        private const string COS_SECRET_ID = "cos-secret-id";
        private const string COS_SECRET_KEY = "cos-secret-key";
        private const string COS_BUCKET = "cos-bucket";
        private const string COS_STORAGE_CLASS = "cos-storage-class";

        /// <summary>
        /// Set default HTTPS request
        /// </summary>
        private const bool COS_USE_SSL = true;
        /// <summary>
        /// Connection timeout time, in milliseconds, default 45000ms
        /// </summary>
        private const int CONNECTION_TIMEOUT_MS = 1000 * 60 * 60;
        /// <summary>
        /// Read and write timeout time, in milliseconds, default 45000ms
        /// </summary>
        private const int READ_WRITE_TIMEOUT_MS = 1000 * 60 * 60;
        /// <summary>
        /// The valid time of each request signature, unit: second
        /// </summary>
        private const int KEY_DURATION_SECOND = 60 * 60;

        /// <summary>
        /// Tencent Cloud Object Storage Configuration
        /// </summary>
        private readonly CosOptions _cosOptions;
        /// <summary>
        /// The timeout values
        /// </summary>
        private readonly TimeoutOptionsHelper.Timeouts _timeouts;

        /// <summary>
        /// Tencent Cloud Object Storage Configuration
        /// </summary>
        public class CosOptions
        {
            /// <summary>
            /// Tencent Cloud Account APPID
            /// </summary>
            public required string? Appid { get; set; }
            /// <summary>
            /// Cloud API Secret ID
            /// </summary>
            public required string SecretId { get; set; }
            /// <summary>
            /// Cloud API Secret Key
            /// </summary>
            public required string SecretKey { get; set; }
            /// <summary>
            /// Bucket region ap-guangzhou ap-hongkong
            /// en: https://intl.cloud.tencent.com/document/product/436/6224
            /// zh: https://cloud.tencent.com/document/product/436/6224
            /// </summary>
            public required string? Region { get; set; }
            /// <summary>
            /// Bucket, format: BucketName-APPID
            /// </summary>
            public required string Bucket { get; set; }
            /// <summary>
            /// A path or subfolder in a bucket
            /// </summary>
            public required string? Path { get; set; }
            /// <summary>
            /// Storage class of the object
            /// en: https://intl.cloud.tencent.com/document/product/436/30925
            /// zh: https://cloud.tencent.com/document/product/436/33417
            /// </summary>
            public required string? StorageClass { get; set; }
        }

        public COS()
        {
            _cosOptions = null!;
            _timeouts = null!;
        }

        public COS(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url?.Trim());
            var prefix = uri.HostAndPath?.Trim()?.Trim('/')?.Trim('\\');
            var auth = AuthOptionsHelper.ParseWithAlias(options, uri, COS_SECRET_ID, COS_SECRET_KEY)
                .RequireCredentials();

            var bucket = options.GetValueOrDefault(COS_BUCKET);
            if (string.IsNullOrWhiteSpace(bucket))
                throw new ArgumentException("COS bucket name is required");

            _cosOptions = new CosOptions()
            {
                Appid = options.GetValueOrDefault(COS_APP_ID),
                Region = options.GetValueOrDefault(COS_REGION),
                SecretId = auth.Username!,
                SecretKey = auth.Password!,
                Bucket = bucket,
                StorageClass = options.GetValueOrDefault(COS_STORAGE_CLASS),
                Path = string.IsNullOrEmpty(prefix) ? null : Util.AppendDirSeparator(prefix, "/")
            };

            _timeouts = TimeoutOptionsHelper.Parse(options);
        }

        private CosXml GetCosXml()
        {
            var config = new CosXmlConfig.Builder()
              .SetConnectionTimeoutMs(CONNECTION_TIMEOUT_MS)
              .SetReadWriteTimeoutMs(READ_WRITE_TIMEOUT_MS)
              .IsHttps(COS_USE_SSL)
              .SetAppid(_cosOptions.Appid)
              .SetRegion(_cosOptions.Region)
              .Build();

            // Initialization QCloudCredentialProvider, COS SDK provides three ways: the permanent temporary keys custom
            var cosCredentialProvider = new DefaultQCloudCredentialProvider(_cosOptions.SecretId, _cosOptions.SecretKey, KEY_DURATION_SECOND);
            return new CosXmlServer(config, cosCredentialProvider);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            bool isTruncated = true;
            string? filename = null;

            while (isTruncated)
            {
                var cosXml = GetCosXml();
                var bucket = _cosOptions.Bucket;
                var prefix = _cosOptions.Path;

                var request = new GetBucketRequest(bucket);
                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), 600);

                if (!string.IsNullOrEmpty(filename))
                    request.SetMarker(filename);

                if (!string.IsNullOrEmpty(prefix))
                    request.SetPrefix(prefix);

                var tcs = new TaskCompletionSource<GetBucketResult>();
                var result = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken, async ct =>
                {
                    ct.Register(() => tcs.TrySetCanceled());
                    cosXml.GetBucket(request,
                        (result) => tcs.SetResult((GetBucketResult)result),
                        (clientEx, serverEx) => tcs.SetException((Exception)clientEx ?? serverEx)
                    );
                    return await tcs.Task.ConfigureAwait(false);
                }).ConfigureAwait(false);

                var info = result.listBucket;

                isTruncated = result.listBucket.isTruncated;
                filename = result.listBucket.nextMarker;

                foreach (var item in info.contentsList)
                {
                    var last = DateTime.Parse(item.lastModified);
                    var fileName = item.key;
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        fileName = fileName.Substring(prefix.Length);

                        if (fileName.StartsWith("/", StringComparison.Ordinal))
                            fileName = fileName.Trim('/');
                    }

                    yield return new FileEntry(fileName, item.size, last, last);
                }
            }
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var cosXml = GetCosXml();
                var bucket = _cosOptions.Bucket;
                var key = GetFullKey(remotename);
                await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct =>
                {
                    var request = new DeleteObjectRequest(bucket, key);
                    ct.Register(() => request.Cancel());
                    request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), 600);

                    cosXml.DeleteObject(request);
                }).ConfigureAwait(false);
            }
            catch (COSXML.CosException.CosClientException clientEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Delete", clientEx, "Delete failed: {0}", remotename);
                throw;
            }
            catch (COSXML.CosException.CosServerException serverEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Delete", serverEx, "Delete failed: {0}, {1}", remotename, serverEx.GetInfo());
                throw;
            }
        }

        public async Task TestAsync(CancellationToken cancelToken)
        {
            var json = JsonConvert.SerializeObject(_cosOptions);
            try
            {
                var cosXml = GetCosXml();
                string bucket = _cosOptions.Bucket;
                var result = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct =>
                {
                    var request = new HeadBucketRequest(bucket);
                    request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), 600);
                    cancelToken.Register(() => request.Cancel());
                    return cosXml.HeadBucket(request);
                }).ConfigureAwait(false);

                Logging.Log.WriteInformationMessage(LOGTAG, "Test", "Request complete {0}: {1}, {2}", result.httpCode, json, result.GetResultInfo());
            }
            catch (COSXML.CosException.CosClientException clientEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Test", clientEx, "Request failed: {0}", json);
                throw;
            }
            catch (COSXML.CosException.CosServerException serverEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Test", serverEx, "Request failed: {0}, {1}", json, serverEx.GetInfo());
                throw;
            }
        }

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            // No need to create folders
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            try
            {
                var cosXml = GetCosXml();
                var bucket = _cosOptions.Bucket;
                var key = GetFullKey(remotename);

                var request = new PutObjectRequest(bucket, key, filename);

                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), KEY_DURATION_SECOND);
                request.SetRequestHeader("Content-Type", "application/octet-stream");
                if (!string.IsNullOrEmpty(_cosOptions.StorageClass))
                {
                    request.SetRequestHeader("x-" + COS_STORAGE_CLASS, _cosOptions.StorageClass);
                }
                cancelToken.Register(() => request.Cancel());

                var result = cosXml.PutObject(request);
            }
            catch (COSXML.CosException.CosClientException clientEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "PutAsync", clientEx, "Put failed: {0}", remotename);
                throw;
            }
            catch (COSXML.CosException.CosServerException serverEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "PutAsync", serverEx, "Put failed: {0}, {1}", remotename, serverEx.GetInfo());
                throw;
            }

            return Task.CompletedTask;
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            try
            {
                var cosXml = GetCosXml();
                var bucket = _cosOptions.Bucket;
                var key = GetFullKey(remotename);

                var request = new GetObjectBytesRequest(bucket, key);

                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), KEY_DURATION_SECOND);
                cancelToken.Register(() => request.Cancel());

                var result = cosXml.GetObject(request);

                var bytes = result.content;
                await File.WriteAllBytesAsync(filename, bytes, cancelToken).ConfigureAwait(false);
            }
            catch (COSXML.CosException.CosClientException clientEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Get", clientEx, "Get failed: {0}", remotename);
                throw;
            }
            catch (COSXML.CosException.CosServerException serverEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Get", serverEx, "Get failed: {0}, {1}", remotename, serverEx.GetInfo());
                throw;
            }
        }

        public async Task RenameAsync(string oldname, string newname, CancellationToken cancelToken)
        {
            try
            {
                var cosXml = GetCosXml();
                var sourceAppid = _cosOptions.Appid;
                var sourceBucket = _cosOptions.Bucket;
                var sourceRegion = _cosOptions.Region;
                var sourceKey = GetFullKey(oldname);

                var copySource = new CopySourceStruct(sourceAppid, sourceBucket, sourceRegion, sourceKey);

                var bucket = _cosOptions.Bucket;
                var key = GetFullKey(newname);

                var result = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct =>
                {
                    var request = new CopyObjectRequest(bucket, key);
                    request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), KEY_DURATION_SECOND);
                    request.SetCopySource(copySource);
                    request.SetCopyMetaDataDirective(COSXML.Common.CosMetaDataDirective.COPY);
                    ct.Register(() => request.Cancel());

                    return cosXml.CopyObject(request);
                }).ConfigureAwait(false);

                //Console.WriteLine(result.GetResultInfo());

                await DeleteAsync(oldname, cancelToken).ConfigureAwait(false);
            }
            catch (COSXML.CosException.CosClientException clientEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Rename", clientEx, "Rename failed: {0} to {1}", oldname, newname);
                throw;
            }
            catch (COSXML.CosException.CosServerException serverEx)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Rename", serverEx, "Rename failed: {0} to {1}, {2}", oldname, newname, serverEx.GetInfo());
                throw;
            }
        }

        public string DisplayName => Strings.COSBackend.DisplayName;

        public string Description => Strings.COSBackend.Description;

        public string ProtocolKey => "cos";

        public IList<ICommandLineArgument> SupportedCommands => [
            new CommandLineArgument(COS_APP_ID, CommandLineArgument.ArgumentType.String, Strings.COSBackend.COSAccountDescriptionShort, Strings.COSBackend.COSAccountDescriptionLong),
            new CommandLineArgument(COS_REGION, CommandLineArgument.ArgumentType.String, Strings.COSBackend.COSLocationDescriptionShort, Strings.COSBackend.COSLocationDescriptionLong),
            new CommandLineArgument(COS_SECRET_ID, CommandLineArgument.ArgumentType.String, Strings.COSBackend.COSAPISecretIdDescriptionShort, Strings.COSBackend.COSAPISecretIdDescriptionLong, null, [AuthOptionsHelper.AuthUsername]),
            new CommandLineArgument(COS_SECRET_KEY, CommandLineArgument.ArgumentType.Password, Strings.COSBackend.COSAPISecretKeyDescriptionShort, Strings.COSBackend.COSAPISecretKeyDescriptionLong, null, [AuthOptionsHelper.AuthPassword]),
            new CommandLineArgument(COS_BUCKET, CommandLineArgument.ArgumentType.String, Strings.COSBackend.COSBucketDescriptionShort, Strings.COSBackend.COSBucketDescriptionLong),
            new CommandLineArgument(COS_STORAGE_CLASS, CommandLineArgument.ArgumentType.String, Strings.COSBackend.COSStorageClassDescriptionShort, Strings.COSBackend.COSStorageClassDescriptionLong),
            .. TimeoutOptionsHelper.GetOptions()
                .Where(x => x.Name != TimeoutOptionsHelper.READ_WRITE_TIMEOUT_OPTION)
        ];

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());

        private string GetFullKey(string name)
            => string.IsNullOrWhiteSpace(_cosOptions?.Path) ? name : _cosOptions.Path + name;
    }
}
