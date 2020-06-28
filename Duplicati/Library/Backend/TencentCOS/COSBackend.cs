using COSXML;
using COSXML.Auth;
using COSXML.Model.Bucket;
using COSXML.Model.Object;
using COSXML.Model.Tag;
using COSXML.Utils;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Tencent COS
    /// https://cloud.tencent.com/document/product/436
    /// https://cloud.tencent.com/document/product/436/32869
    /// </summary>
    public class COS : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<COS>();

        private const string COS_APP_ID = "cos-app-id";
        private const string COS_REGION = "cos-region";
        private const string COS_SECRET_ID = "cos-secret-id";
        private const string COS_SECRET_KEY = "cos-secret-key";
        private const string COS_BUCKET = "cos-bucket";

        private const bool COS_USE_SSL = true;
        private const int CONNECTION_TIMEOUT_MS = 1000 * 60 * 60;
        private const int READ_WRITE_TIMEOUT_MS = 1000 * 60 * 60;
        private const int KEY_DURATION_SECOND = 60 * 60;

        private readonly CosOptions _cosOptions;
        private readonly string m_prefix;

        private CosXml _cosXml;

        /// <summary>
        /// 腾讯云对象存储配置
        /// </summary>
        public class CosOptions
        {
            /// <summary>
            /// 腾讯云账户的账户标识
            /// </summary>
            public string Appid { get; set; }
            /// <summary>
            /// 云 API 密钥 SecretId
            /// </summary>
            public string SecretId { get; set; }
            /// <summary>
            /// 云 API 密钥 SecretKey
            /// </summary>
            public string SecretKey { get; set; }
            /// <summary>
            /// 存储桶地域 ap-guangzhou ap-hongkong
            /// https://cloud.tencent.com/document/product/436/6224
            /// </summary>
            public string Region { get; set; }
            /// <summary>
            /// 存储桶，格式：BucketName-APPID
            /// </summary>
            public string Bucket { get; set; }
            /// <summary>
            /// 存储桶中路径或子文件夹
            /// </summary>
            public string Path { get; set; }
        }

        public COS()
        {
        }

        public COS(string url, Dictionary<string, string> options)
        {
            _cosOptions = new CosOptions();

            var uri = new Utility.Uri(url);
            m_prefix = uri.HostAndPath?.Trim()?.Trim('/')?.Trim('\\');

            if (!string.IsNullOrEmpty(m_prefix))
            {
                m_prefix += "/";
                _cosOptions.Path = m_prefix;
            }

            if (options.ContainsKey(COS_APP_ID))
            {
                _cosOptions.Appid = options[COS_APP_ID];
            }

            if (options.ContainsKey(COS_REGION))
            {
                _cosOptions.Region = options[COS_REGION];
            }

            if (options.ContainsKey(COS_SECRET_ID))
            {
                _cosOptions.SecretId = options[COS_SECRET_ID];
            }

            if (options.ContainsKey(COS_SECRET_KEY))
            {
                _cosOptions.SecretKey = options[COS_SECRET_KEY];
            }

            if (options.ContainsKey(COS_BUCKET))
            {
                _cosOptions.Bucket = options[COS_BUCKET];
            }
        }

        CosXml GetCosXml()
        {
            //初始化 CosXmlConfig 
            string appid = _cosOptions.Appid;//设置腾讯云账户的账户标识 APPID
            string region = _cosOptions.Region; //设置一个默认的存储桶地域

            CosXmlConfig config = new CosXmlConfig.Builder()
              .SetConnectionTimeoutMs(CONNECTION_TIMEOUT_MS)  //设置连接超时时间，单位毫秒，默认45000ms
              .SetReadWriteTimeoutMs(READ_WRITE_TIMEOUT_MS)  //设置读写超时时间，单位毫秒，默认45000ms
              .IsHttps(COS_USE_SSL)  //设置默认 HTTPS 请求
              .SetAppid(appid)  //设置腾讯云账户的账户标识 APPID
              .SetRegion(region)  //设置一个默认的存储桶地域
                                  //.SetDebugLog(true)  //显示日志
              .Build();  //创建 CosXmlConfig 对象

            //方式1， 永久密钥
            string secretId = _cosOptions.SecretId; //"云 API 密钥 SecretId";
            string secretKey = _cosOptions.SecretKey; //"云 API 密钥 SecretKey";
            long durationSecond = KEY_DURATION_SECOND;  //每次请求签名有效时长，单位为秒

            //初始化 QCloudCredentialProvider，COS SDK 中提供了3种方式：永久密钥、临时密钥、自定义
            QCloudCredentialProvider cosCredentialProvider = new DefaultQCloudCredentialProvider(secretId, secretKey, durationSecond);

            CosXml cosXml = new CosXmlServer(config, cosCredentialProvider);
            return cosXml;
        }

        public IEnumerable<IFileEntry> List()
        {
            bool isTruncated = true;
            string filename = null;

            while (isTruncated)
            {
                _cosXml = GetCosXml();
                string bucket = _cosOptions.Bucket;

                GetBucketRequest request = new GetBucketRequest(bucket);

                //设置签名有效时长
                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), 600);

                if (!string.IsNullOrEmpty(filename))
                    request.SetMarker(filename);

                //获取 m_prefix/ 下的对象
                if (!string.IsNullOrEmpty(m_prefix))
                    request.SetPrefix(m_prefix);

                //执行请求
                GetBucketResult result = _cosXml.GetBucket(request);

                //bucket的相关信息
                ListBucket info = result.listBucket;

                isTruncated = result.listBucket.isTruncated;
                filename = result.listBucket.nextMarker;

                foreach (var item in info.contentsList)
                {
                    var last = DateTime.Parse(item.lastModified);

                    var fileName = item.key;
                    if (!string.IsNullOrWhiteSpace(m_prefix))
                    {
                        fileName = fileName.Substring(m_prefix.Length);

                        if (fileName.StartsWith("/", StringComparison.Ordinal))
                        {
                            fileName = fileName.Trim('/').Trim('/');
                        }
                    }

                    yield return new FileEntry(fileName, item.size, last, last);
                }
            }
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                return PutAsync(remotename, fs, cancelToken);
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
                _cosXml = GetCosXml();
                string bucket = _cosOptions.Bucket; //存储桶，格式：BucketName-APPID
                string key = GetFullKey(remotename); //对象在存储桶中的位置，即称对象键

                DeleteObjectRequest request = new DeleteObjectRequest(bucket, key);

                //设置签名有效时长
                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), 600);

                //执行请求
                DeleteObjectResult result = _cosXml.DeleteObject(request);

                //请求成功
                //result.GetResultInfo()
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

        public void Test()
        {
            var json = JsonConvert.SerializeObject(_cosOptions);
            try
            {
                _cosXml = GetCosXml();
                string bucket = _cosOptions.Bucket;
                HeadBucketRequest request = new HeadBucketRequest(bucket);
                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), 600);
                HeadBucketResult result = _cosXml.HeadBucket(request);

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

        public void CreateFolder()
        {
            // cos no folders need to be created
        }

        public void Dispose()
        {
            if (_cosXml != null)
                _cosXml = null;
        }

        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                _cosXml = GetCosXml();
                string bucket = _cosOptions.Bucket;
                string key = GetFullKey(remotename);

                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                stream.Seek(0, SeekOrigin.Begin);

                PutObjectRequest request = new PutObjectRequest(bucket, key, bytes);
                //设置签名有效时长
                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), KEY_DURATION_SECOND);
                //执行请求
                PutObjectResult result = _cosXml.PutObject(request);
                //对象的 eTag
                //string eTag = result.eTag;
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

        public void Get(string remotename, Stream stream)
        {
            //下载返回 bytes 数据
            try
            {
                CosXml cosXml = GetCosXml();
                string bucket = _cosOptions.Bucket;
                string key = GetFullKey(remotename);

                GetObjectBytesRequest request = new GetObjectBytesRequest(bucket, key);

                //设置签名有效时长
                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), KEY_DURATION_SECOND);

                //执行请求
                GetObjectBytesResult result = cosXml.GetObject(request);

                //获取内容
                byte[] bytes = result.content;

                Stream ms = new MemoryStream(bytes);
                Utility.Utility.CopyStream(ms, stream);

                //请求成功
                //Console.WriteLine(result.GetResultInfo());
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

        public void Rename(string oldname, string newname)
        {
            try
            {
                CosXml cosXml = GetCosXml();
                string sourceAppid = _cosOptions.Appid; //账号 appid
                string sourceBucket = _cosOptions.Bucket; //"源对象所在的存储桶
                string sourceRegion = _cosOptions.Region; //源对象的存储桶所在的地域
                string sourceKey = GetFullKey(oldname); //源对象键
                                                        //构造源对象属性
                CopySourceStruct copySource = new CopySourceStruct(sourceAppid, sourceBucket,
                  sourceRegion, sourceKey);

                string bucket = _cosOptions.Bucket; //存储桶，格式：BucketName-APPID
                string key = GetFullKey(newname); //对象在存储桶中的位置，即称对象键
                CopyObjectRequest request = new CopyObjectRequest(bucket, key);
                //设置签名有效时长
                request.SetSign(TimeUtils.GetCurrentTime(TimeUnit.SECONDS), KEY_DURATION_SECOND);
                //设置拷贝源
                request.SetCopySource(copySource);
                //设置是否拷贝还是更新,此处是拷贝
                request.SetCopyMetaDataDirective(COSXML.Common.CosMetaDataDirective.COPY);
                //执行请求
                CopyObjectResult result = cosXml.CopyObject(request);

                //请求成功
                //Console.WriteLine(result.GetResultInfo());

                // 删除
                Delete(oldname);
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

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(COS_APP_ID, CommandLineArgument.ArgumentType.String, Strings.COSBackend.COSAccountDescriptionShort,Strings.COSBackend.COSAccountDescriptionLong),
                    new CommandLineArgument(COS_REGION, CommandLineArgument.ArgumentType.String, Strings.COSBackend.COSLocationDescriptionShort,Strings.COSBackend.COSLocationDescriptionLong),
                    new CommandLineArgument(COS_SECRET_ID, CommandLineArgument.ArgumentType.String,  Strings.COSBackend.COSAPISecretIdDescriptionShort,Strings.COSBackend.COSAPISecretIdDescriptionLong),
                    new CommandLineArgument(COS_SECRET_KEY, CommandLineArgument.ArgumentType.Password,  Strings.COSBackend.COSAPISecretKeyDescriptionShort,Strings.COSBackend.COSAPISecretKeyDescriptionLong),
                    new CommandLineArgument(COS_BUCKET, CommandLineArgument.ArgumentType.String,Strings.COSBackend.COSBucketDescriptionShort,Strings.COSBackend.COSBucketDescriptionLong),
                });
            }
        }

        public string[] DNSName => null;

        private string GetFullKey(string name)
        {
            if (string.IsNullOrWhiteSpace(m_prefix))
                return name;
            return m_prefix + name;
        }
    }
}
