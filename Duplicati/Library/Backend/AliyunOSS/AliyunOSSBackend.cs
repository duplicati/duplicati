using AlibabaCloud.SDK.Oss20190517;
using AlibabaCloud.SDK.Oss20190517.Models;
using Aliyun.OSS;
using Aliyun.OSS.Common;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.AliyunOSS
{
    /// <summary>
    /// 阿里云对象存储 OSS（Object Storage Service）是一款海量、安全、低成本、高可靠的云存储服务，提供最高可达 99.995 % 的服务可用性。多种存储类型供选择，全面优化存储成本。
    /// en: https://www.alibabacloud.com/zh/product/object-storage-service
    /// zh: https://www.aliyun.com/product/oss
    /// </summary>
    public class OSS : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<OSS>();

        private const string OSS_REGION = "oss-region";
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
            var prefix = uri.HostAndPath?.Trim()?.Trim('/')?.Trim('\\');

            if (!string.IsNullOrEmpty(prefix))
            {
                _ossOptions.Path = prefix;
            }

            if (options.ContainsKey(OSS_REGION))
            {
                _ossOptions.Region = options[OSS_REGION];
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

        private OssClient GetClient()
        {
            var endpoint = _ossOptions.Endpoint;
            var accessKeyId = _ossOptions.AccessKeyId;
            var accessKeySecret = _ossOptions.AccessKeySecret;
            return new OssClient(endpoint, accessKeyId, accessKeySecret);
        }

        private OssClient GetClientV3()
        {
            var endpoint = _ossOptions.Endpoint;

            var accessKeyId = _ossOptions.AccessKeyId;
            var accessKeySecret = _ossOptions.AccessKeySecret;
            return new OssClient(endpoint, accessKeyId, accessKeySecret, new ClientConfiguration()
            {
                UseNewServiceClient = false
            });
        }

        private Client GetClienV2()
        {
            return new Client(new AlibabaCloud.OpenApiClient.Models.Config()
            {
                AccessKeyId = _ossOptions.AccessKeyId,
                AccessKeySecret = _ossOptions.AccessKeySecret,
                Endpoint = _ossOptions.Endpoint,
                RegionId = _ossOptions.Region
            });
        }

        public IEnumerable<IFileEntry> List()
        {
            // 填写Bucket名称，例如examplebucket。
            var bucketName = _ossOptions.BucketName;

            // 列举包含指定前缀的文件。
            var prefix = $"{_ossOptions.Path.TrimPath()}";

            var client = new Client(new AlibabaCloud.OpenApiClient.Models.Config()
            {
                AccessKeyId = _ossOptions.AccessKeyId,
                AccessKeySecret = _ossOptions.AccessKeySecret,
                Endpoint = _ossOptions.Endpoint,
                RegionId = _ossOptions.Region
            });

            var list = new List<ObjectSummary>();
            try
            {
                var nextMarker = string.Empty;
                var isTruncated = false;
                do
                {
                    var result = client.ListObjects(bucketName, new AlibabaCloud.SDK.Oss20190517.Models.ListObjectsRequest()
                    {
                        MaxKeys = 1000,
                        Prefix = prefix,
                        Marker = nextMarker,
                    });
                    if (result.StatusCode != 200)
                    {
                        throw new Exception(result?.StatusCode?.ToString());
                    }

                    foreach (var summary in result.Body.Contents)
                    {
                        list.Add(summary);
                    }

                    nextMarker = result.Body.NextMarker;
                    isTruncated = result.Body.IsTruncated ?? false;

                } while (isTruncated);
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

            foreach (var item in list)
            {
                var fileName = Path.GetFileName(item.Key);
                var time = DateTimeOffset.Parse(item.LastModified).ToLocalTime().DateTime;
                yield return new FileEntry(fileName, item.Size ?? 0, time, time);
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken);
        }

        public void Get(string remotename, string filename)
        {
            using (var fs = File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            try
            {
                var client = GetClient();

                var bucketName = _ossOptions.BucketName;

                var objectName = $"{_ossOptions.Path.TrimPath()}/{remotename}".TrimPath();

                var res = client.DeleteObject(bucketName, objectName);
                if (res?.HttpStatusCode != HttpStatusCode.OK)
                {
                    //throw new Exception("Delete object failed");
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Delete", ex, "Delete object failed. {0}", ex.Message);
                throw;
            }
        }

        public void Test()
        {
            GetClient();
        }

        public void CreateFolder()
        {
            // No need to create folders
        }

        public void Dispose()
        {

        }

        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var bucketName = _ossOptions.BucketName;

            var objectName = $"{_ossOptions.Path.TrimPath()}/{remotename}".TrimPath();

            var client = GetClient();
            try
            {
                // 上传文件。
                var objectResult = client.PutObject(bucketName, objectName, stream);
                if (objectResult?.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("文件上传保存失败");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Put object failed, {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public void Get(string remotename, Stream stream)
        {
            var bucketName = _ossOptions.BucketName;

            var objectName = $"{_ossOptions.Path.TrimPath()}/{remotename}".TrimPath();

            var client = GetClientV3();

            try
            {
                var obj = client.GetObject(bucketName, objectName);

                if (obj.HttpStatusCode != HttpStatusCode.OK)
                    throw new Exception("Get failed");

                using (var requestStream = obj.Content)
                {
                    requestStream.CopyTo(stream);
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Get", ex, "Get failed: {0}", ex.Message);
                throw;
            }


            //var clientV2 = GetClienV2();

            //try
            //{
            //    var res = clientV2.GetObject(bucketName, objectName,
            //        new AlibabaCloud.SDK.Oss20190517.Models.GetObjectRequest()
            //        {

            //        });

            //    if (res.StatusCode != 200)
            //        throw new Exception(res.StatusCode?.ToString() ?? "Get failed");

            //    using (var requestStream = res.Body)
            //    {
            //        requestStream.CopyTo(stream);
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Logging.Log.WriteErrorMessage(LOGTAG, "Get", ex, "Get failed: {0}", ex.Message);
            //    throw;
            //}
        }

        public void Rename(string oldname, string newname)
        {
            var bucketName = _ossOptions.BucketName;

            var sourceBucket = bucketName;

            var targetBucket = bucketName;

            // 填写源Object的完整路径，完整路径中不能包含Bucket名称，例如srcdir/scrobject.txt。
            var sourceObject = $"{_ossOptions.Path.TrimPath()}/{oldname}".TrimPath();

            // 填写目标Object的完整路径，完整路径中不能包含Bucket名称，例如destdir/destobject.txt。
            var targetObject = $"{_ossOptions.Path.TrimPath()}/{newname}".TrimPath();

            var client = GetClient();

            try
            {
                // 拷贝文件。
                var req = new CopyObjectRequest(sourceBucket, sourceObject, targetBucket, targetObject);
                var res = client.CopyObject(req);
                if (res?.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("文件重命名失败");
                }

                // 删除旧的文件
                Delete(oldname);
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
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(OSS_REGION, CommandLineArgument.ArgumentType.String, Strings.OSSBackend.OSSRegionDescriptionShort, Strings.OSSBackend.OSSRegionDescriptionLong),
                    new CommandLineArgument(OSS_ACCESS_KEY_ID, CommandLineArgument.ArgumentType.String, Strings.OSSBackend.OSSAccessKeyIdDescriptionShort, Strings.OSSBackend.OSSAccessKeyIdDescriptionLong),
                    new CommandLineArgument(OSS_ACCESS_KEY_SECRET, CommandLineArgument.ArgumentType.Password, Strings.OSSBackend.OSSAccessKeySecretDescriptionShort, Strings.OSSBackend.OSSAccessKeySecretDescriptionLong),
                    new CommandLineArgument(OSS_BUCKET_NAME, CommandLineArgument.ArgumentType.String, Strings.OSSBackend.OSSBucketNameDescriptionShort, Strings.OSSBackend.OSSBucketNameDescriptionLong),
                    new CommandLineArgument(OSS_ENDPOINT, CommandLineArgument.ArgumentType.String, Strings.OSSBackend.OSSEndpointDescriptionShort, Strings.OSSBackend.OSSEndpointDescriptionLong)
                });
            }
        }

        public string[] DNSName => null;
    }
}