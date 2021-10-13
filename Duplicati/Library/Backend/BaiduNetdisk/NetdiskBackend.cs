using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Duplicati.Library.Backend.BaiduNetdisk
{
    /// <summary>
    /// 百度网盘说明
    /// 1、不开通百度会员会受到下载限速，上传不会限速
    /// 2、路径不支持表情等特殊字符（在本系统不会存在问题）
    /// 3、0 字节的文件，需要做特殊处理，直接调用创建文件接口
    /// 4、支持秒传：当文件在百度云存在时，可以秒传到百度云，秒传支持必须传递对应的 MD5 信息
    /// 5、关于分片上传大小文件限制
    /// 5.1 普通用户单个分片大小固定为4MB（文件大小如果小于4MB，无需切片，直接上传即可），单文件总大小上限为4G
    /// 5.2 普通会员用户单个分片大小上限为16MB，单文件总大小上限为10G
    /// 5.3 超级会员用户单个分片大小上限为32MB，单文件总大小上限为20G
    /// 6、注意文件上传接口返回的不是 json 格式
    /// 7、如果是 302 下载链接，是否会产生错误 TODO
    /// <see cref="https://pan.baidu.com/union/doc/"/>
    /// </summary>
    public class Netdisk : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        /// <summary>
        /// 日志
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<Netdisk>();

        /// <summary>
        /// 授权码 KEY
        /// </summary>
        private const string AUTHORIZATION_CODE = "baidunetdisk-authorization-code";

        /// <summary>
        /// 单个分片大小 KEY
        /// </summary>
        private const string BLOCK_SIZE = "baidunetdisk-blocksize";

        /// <summary>
        /// 授权码令牌
        /// 百度授权码, 请点击下发链接登录百度云, 获取授权码, 当前应用服务由 "天网中心" 免费提供; 也可自行申请接入百度云, 输入自已的授权码
        /// </summary>
        private readonly string _token;

        /// <summary>
        /// 存储根目录
        /// 文件目录, 建议 /apps/sync, 注意: 根路径 apps 在网盘中对应是 "我的应用程序" 文件夹
        /// </summary>
        private readonly string _path;

        /// <summary>
        /// 文件默认分块大小, 切片上传文件分块大小 4MB 16MB 32MB
        /// 单个分片大小4MB/16MB/32MB, 不可填写其他, 默认4MB, 普通会员16MB, 超级会员32MB
        /// </summary>
        private readonly int _defaultLength = 1024 * 1024 * 4;

        /// <summary>
        /// 校验段对应文件前256KB
        /// </summary>
        private readonly int _sliceLength = 1024 * 256;

        /// <summary>
        /// 网盘 host
        /// </summary>
        private readonly string panHost = "https://pan.baidu.com";

        /// <summary>
        /// 网盘上传 host
        /// </summary>
        private readonly string pcsHost = "https://d.pcs.baidu.com";

        public Netdisk() { }

        public Netdisk(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url?.Trim());
            var prefix = uri.HostAndPath?.Trim()?.Trim('/')?.Trim('\\');
            var ps = uri.QueryParameters;

            _path = $"/{prefix}";
            _token = ps.AllKeys.Contains(AUTHORIZATION_CODE) ? ps[AUTHORIZATION_CODE] : string.Empty;

            var sizeArr = new int[] { 4, 16, 32 };
            if (ps.AllKeys.Contains(BLOCK_SIZE) && int.TryParse(ps[BLOCK_SIZE], out int size) && sizeArr.Contains(size))
            {
                _defaultLength = 1024 * 1024 * size;
            }

            if (options.ContainsKey(AUTHORIZATION_CODE))
            {
                var value = options[AUTHORIZATION_CODE];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _token = value;
                }
            }

            if (options.ContainsKey(BLOCK_SIZE))
            {
                var value = options[BLOCK_SIZE];
                if (int.TryParse(value, out int bsize) && sizeArr.Contains(bsize))
                {
                    _defaultLength = 1024 * 1024 * bsize;
                }
            }
        }

        public IEnumerable<IFileEntry> List()
        {
            var all = new List<PanListItem>();
            var start = 0;
            var limit = 10000;
            int count;
            do
            {
                var listUrl = $"{panHost}/rest/2.0/xpan/file?method=list&access_token={_token}&dir={_path}&folder=0&start={start * limit}&limit={limit}";
                var client = new RestClient(listUrl)
                {
                    Timeout = -1
                };
                var request = new RestRequest(Method.GET);
                var response = client.Execute<PanListResult>(request);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"请求文件列表失败 {response.StatusCode}, {response.Data?.errno}, {response.Content}");
                }
                var list = response.Data?.list;

                count = list?.Count ?? 0;
                if (count > 0)
                {
                    all.AddRange(list);
                }
                start++;
            } while (count >= limit);

            foreach (var item in all)
            {
                var fileName = item.server_filename;
                var last = ToLocalTime(item.server_mtime);
                yield return new FileEntry(fileName, item.size, last, last);
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
            var json = JsonConvert.SerializeObject(new string[] { $"/{remotename?.Trim().Trim('/')}" });
            Manage(json, "delete");
        }

        public void Rename(string oldname, string newname)
        {
            var item = new
            {
                path = $"/{oldname?.Trim().Trim('/')}",
                newname = $"/{newname?.Trim().Trim('/')}"
            };
            var json = JsonConvert.SerializeObject(new List<dynamic>() { item });
            Manage(json, "rename");
        }

        public void Test()
        {
            Create(_path, 0, "", "", true);
        }

        public void CreateFolder()
        {
            Create(_path, 0, "", "", true);
        }

        public void Dispose()
        {

        }

        public string DisplayName => Strings.NetdiskBackend.DisplayName;

        public string Description => Strings.NetdiskBackend.Description;

        public string ProtocolKey => "baidunetdisk";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHORIZATION_CODE, CommandLineArgument.ArgumentType.String, Strings.NetdiskBackend.BaiduNetdiskAccountDescriptionShort,Strings.NetdiskBackend.BaiduNetdiskAccountDescriptionLong),
                    new CommandLineArgument(BLOCK_SIZE, CommandLineArgument.ArgumentType.String, Strings.NetdiskBackend.BaiduNetdiskBlockSizeDescriptionShort,Strings.NetdiskBackend.BaiduNetdiskBlockSizeDescriptionLong,"4"),
                });
            }
        }

        public string[] DNSName => new string[] { new Uri(panHost).Host };

        /// <summary>
        /// 管理文件
        /// </summary>
        /// <param name="filelist"></param>
        /// <param name="opera">copy, mover, rename, delete</param>
        public void Manage(string filelist, string opera)
        {
            var manageUrl = $"{panHost}/rest/2.0/xpan/file?method=filemanager&access_token={_token}&opera={opera}";
            var client = new RestClient(manageUrl)
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.POST);

            // 0:同步， 1 自适应，2异步
            request.AddParameter("async", 0);

            // 全局ondup,遇到重复文件的处理策略,
            // fail(默认，直接返回失败)、newcopy(重命名文件)、overwrite、skip
            request.AddParameter("ondup", "overwrite");

            /*
             * 待操作文件列表
             * copy/move:[{"path":"/测试目录/123456.docx","dest":"/测试目录/abc","newname":"11223.docx","ondup":"fail"}]
             * rename:[{path":"/测试目录/123456.docx","newname":test.docx"}]
             * delete:["/测试目录/123456.docx"]
             */
            request.AddParameter("filelist", filelist);

            var response = client.Execute<PanManageResult>(request);
            if (response.StatusCode != HttpStatusCode.OK || response.Data?.errno != 0)
            {
                throw new Exception($"管理文件请求失败 {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }
        }

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="remotename"></param>
        /// <param name="stream"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                var fileName = remotename;
                var fileFullPath = $"{_path}/{fileName}";
                var fileEncodeFullPath = HttpUtility.UrlPathEncode(fileFullPath);
                var fileSize = 0L;
                var fileContentMd5 = string.Empty;
                var fileSliceMd5 = string.Empty;

                fileSize = stream.Length;
                fileContentMd5 = Md5Hash(stream);
                stream.Position = 0;

                if (fileSize <= 0)
                {
                    // 直接创建文件
                    Create(fileFullPath, fileSize, "", JsonConvert.SerializeObject(new string[] { fileContentMd5 }));
                    return Task.CompletedTask;
                }

                if (fileSize >= _sliceLength)
                {
                    var buffer = new byte[_sliceLength];
                    var ms = new MemoryStream();
                    var bytesRead = stream.Read(buffer, 0, _sliceLength);
                    if (bytesRead == _sliceLength)
                    {
                        fileSliceMd5 = Md5Hash(buffer);
                    }
                    stream.Position = 0;
                }

                // 预上传是为了通知网盘有一个上传任务开始了，网盘返回uploadid来标识这个上传任务
                // 当文件已经在网盘存在时，可以实现秒传
                var precreateUrl = $"{panHost}/rest/2.0/xpan/file?method=precreate&access_token={_token}";
                var client = new RestClient(precreateUrl)
                {
                    Timeout = -1
                };
                var request = new RestRequest(Method.POST);

                // 上传后使用的文件绝对路径，需要urlencode
                // 注意：不要编码，否则会以编码后显示
                request.AddParameter("path", fileFullPath);

                // 文件或目录的大小，单位B，目录的话大小为0
                request.AddParameter("size", fileSize);

                // 是否目录，0 文件、1 目录
                request.AddParameter("isdir", 0);

                // 固定值1
                request.AddParameter("autoinit", 1);

                // 文件命名策略，默认0
                // 0 为不重命名，返回冲突
                // 1 为只要path冲突即重命名
                // 2 为path冲突且block_list不同才重命名
                // 3 为覆盖
                request.AddParameter("rtype", 3);

                // 在秒传情况下，不需要计算分块 md5
                // 计算分块数量，需要计算分块的数量，根据切片大小计算需要传递几个分块 md5，传空值即可
                var md5Count = Convert.ToInt32(Math.Ceiling((decimal)fileSize / _defaultLength));
                request.AddParameter("block_list", JsonConvert.SerializeObject(new string[md5Count].Select(c => string.Empty)));
                //request.AddParameter("block_list", "[\"\"]");

                // 文件MD5
                // content-md5和slice-md5都不为空时，接口会判断云端是否已存在相同文件，如果存在，返回的return_type=2，代表直接上传成功，无需请求后面的分片上传和创建文件接口
                request.AddParameter("content-md5", fileContentMd5);

                // 文件校验段的MD5，校验段对应文件前256KB
                if (!string.IsNullOrWhiteSpace(fileSliceMd5))
                {
                    request.AddParameter("slice-md5", fileSliceMd5);
                }

                // 客户端创建时间， 默认为当前时间戳
                request.AddParameter("local_ctime", GetUnixTimestamp());

                // 客户端修改时间，默认为当前时间戳
                request.AddParameter("local_mtime", GetUnixTimestamp());

                var response = client.Execute<PanPrecreateResult>(request);
                if (response.StatusCode != HttpStatusCode.OK || response.Data == null || response.Data?.errno != 0)
                {
                    throw new Exception($"预上传请求失败 {response.StatusCode}, {response.Data?.errno}, {response.Content}");
                }
                if (response.Data.return_type == 2)
                {
                    return Task.CompletedTask;
                }

                // 文件各分片MD5数组的json串
                var md5Json = JsonConvert.SerializeObject(new string[] { fileContentMd5 });

                // precreate接口下发的uploadid
                var uploadid = response.Data.uploadid;

                // 需要上传的分片序号，索引从0开始
                // block_list为空时，等价于[0]
                var parts = response.Data.block_list;
                if (parts.Count <= 0)
                {
                    parts.Add(0);
                }

                // 如果无须分片，则直接上传
                if (parts.Count == 1 && fileSize <= _defaultLength)
                {
                    var uploadUrl = $"{pcsHost}/rest/2.0/pcs/superfile2?method=upload&access_token={_token}&path={fileEncodeFullPath}&type=tmpfile&uploadid={uploadid}&partseq={0}";
                    var client2 = new RestClient(uploadUrl)
                    {
                        Timeout = -1
                    };
                    var request2 = new RestRequest(Method.POST);
                    stream.Position = 0;

                    var buffer = new byte[fileSize];
                    var bytesRead = stream.Read(buffer, 0, (int)fileSize);
                    if (bytesRead != (int)fileSize)
                    {
                        throw new Exception("读取文件流处理失败");
                    }

                    request2.AddFile("file", buffer, fileName);
                    var response2 = client2.Execute(request2);
                    if (response2.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"文件上传请求失败 {response2.StatusCode}, {response2.Content}");
                    }
                    // 验证 md5 是否与与切片 md5 相同
                    var data = JsonConvert.DeserializeObject<PanUploadResult>(response2.Content);
                    if (fileContentMd5 != data?.md5)
                    {
                        // 不一致
                        throw new Exception($"文件 MD5, 与返回 MD5 不一致, {fileContentMd5}, {data.md5}");
                    }
                }
                else
                {
                    var md5s = new SortedDictionary<int, string>();
                    foreach (var index in parts)
                    {
                        var uploadUrl = $"{pcsHost}/rest/2.0/pcs/superfile2?method=upload&access_token={_token}&path={fileEncodeFullPath}&type=tmpfile&uploadid={uploadid}&partseq={index}";
                        var client2 = new RestClient(uploadUrl)
                        {
                            Timeout = -1
                        };
                        var request2 = new RestRequest(Method.POST);
                        var position = stream.Seek(index * _defaultLength, SeekOrigin.Begin);
                        if (position >= 0)
                        {
                            var buffer = new byte[_defaultLength];
                            int bytesRead;
                            if ((bytesRead = stream.Read(buffer, 0, _defaultLength)) > 0)
                            {
                                using (var ms = new MemoryStream(buffer, 0, bytesRead))
                                {
                                    var bytes = ms.ToArray();
                                    request2.AddFile("file", bytes, fileName);
                                    var response2 = client2.Execute(request2);
                                    if (response2.StatusCode != HttpStatusCode.OK)
                                    {
                                        throw new Exception($"分块上传请求失败 {response2.StatusCode}, {response2.Content}");
                                    }
                                    // 验证 md5 是否与与切片 md5 相同
                                    var data = JsonConvert.DeserializeObject<PanUploadResult>(response2.Content);
                                    if (string.IsNullOrWhiteSpace(data?.md5) || md5s.ContainsKey(index))
                                    {
                                        throw new Exception($"返回 MD5 错误, {data?.md5}");
                                    }
                                    md5s.Add(index, data.md5);
                                }
                            }
                        }
                    }
                    md5Json = JsonConvert.SerializeObject(md5s.Values);
                }

                Create(fileFullPath, fileSize, uploadid, md5Json);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "Upload", ex, "上传文件处理失败: {0}", remotename);
                throw new Exception("上传文件处理失败：" + ex.Message, ex);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取并下载远程文件
        /// </summary>
        /// <param name="remotename"></param>
        /// <param name="fs"></param>
        public void Get(string remotename, Stream fs)
        {
            // 其他说明
            // 通过【列表类接口】获取文件的fsid。
            // 通过【取文件信息filemetas接口】获取文件的下载地址，即接口返回的dlink字段
            // 使用dlink下载文件
            // dlink有效期为8小时
            // 必需要设置User-Agent字段
            // dlink存在302跳转

            var key = GetRemoteKey(remotename);
            if (key <= 0)
            {
                throw new Exception("未查询到远程文件");
            }

            var getFileUrl = $"{panHost}/rest/2.0/xpan/multimedia?method=filemetas&access_token={_token}&dlink=1&fsids=[{key}]";
            var client = new RestClient(getFileUrl)
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.GET);
            var response = client.Execute<PanFileInfoResult>(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"获取远程文件下载地址失败 {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }

            var url = response.Data.list.FirstOrDefault(c => c.fs_id == key)?.dlink;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new Exception($"获取远程文件下载为空 {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }

            //var request2 = (HttpWebRequest)WebRequest.Create(url);
            ////var response2 = request2.GetResponse();
            ////fs = response2.GetResponseStream();
            //request2.UserAgent = "pan.baidu.com";
            //var response2 = request2.GetResponse();
            //var response3 = (HttpWebResponse)request2.GetResponse();
            //using (Stream resStream = response3.GetResponseStream())
            //{
            //    resStream.CopyTo(fs);
            //}

            // 下载链接
            var downloadUrl = $"{url}&access_token={_token}";
            try
            {
                var client2 = new RestClient(downloadUrl)
                {
                    Timeout = -1
                };
                var request2 = new RestRequest(Method.GET);
                request2.AddHeader("User-Agent", "pan.baidu.com");
                request2.ResponseWriter = responseStream =>
                {
                    using (responseStream)
                    {
                        responseStream.CopyTo(fs);
                    }
                };
                var response2 = client2.DownloadData(request2);
            }
            catch (Exception ex)
            {
                throw new Exception($"下载文件失败 {remotename}, {downloadUrl}", ex);
            }
        }

        /// <summary>
        /// 获取文件在百度的唯一标识
        /// 可以修改为请求列表时将key保存到本地
        /// </summary>
        /// <param name="remotename"></param>
        /// <returns></returns>
        public long GetRemoteKey(string remotename)
        {
            var searchFileUrl = $"{panHost}/rest/2.0/xpan/file?method=search&access_token={_token}&key={remotename}&dir={_path}&recursion=0";
            var client = new RestClient(searchFileUrl)
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.GET);
            var response = client.Execute<PanSearchResult>(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"查询文件失败, {remotename}, {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }
            var key = response?.Data?.list?.FirstOrDefault(c => c.server_filename == remotename)?.fs_id ?? 0;
            if (key <= 0)
            {
                throw new Exception($"查询文件失败，未查询到文件在百度云的唯一标识, {remotename}, {response.StatusCode}, {response.Content}");
            }
            return key;
        }

        /// <summary>
        /// 创建文件/创建文件夹
        /// </summary>
        /// <param name="fileFullPath"></param>
        /// <param name="fileSize"></param>
        /// <param name="uploadid"></param>
        /// <param name="md5Json"></param>
        public void Create(string fileFullPath, long fileSize, string uploadid, string md5Json, bool isFolder = false)
        {
            // 如果是创建0字节的文件，则直接调用此接口，不需要调用分块上传，且不需要填写 uploadid，注意要写 md5
            // 如果是创建文件夹，也使用此接口
            var createUrl = $"{panHost}/rest/2.0/xpan/file?method=create&access_token={_token}";
            var client = new RestClient(createUrl)
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.POST);

            // 上传后使用的文件绝对路径
            // 注意：不要编码，否则会以编码后显示
            request.AddParameter("path", fileFullPath);

            // 文件或目录的大小，必须要和文件真实大小保持一致
            request.AddParameter("size", fileSize);

            // 文件命名策略，默认1
            // 0 为不重命名，返回冲突
            // 1 为只要path冲突即重命名
            // 2 为path冲突且block_list不同才重命名
            // 3 为覆盖
            request.AddParameter("rtype", 3);

            // 是否目录，0 文件、1 目录
            request.AddParameter("isdir", isFolder ? 1 : 0);

            // uploadid，非空表示通过superfile2上传
            request.AddParameter("uploadid", uploadid);

            if (!isFolder)
            {
                // 文件各分片MD5的json串
                // MD5对应superfile2返回的md5，且要按照序号顺序排列
                request.AddParameter("block_list", md5Json);
            }

            // 是否需要多版本支持，默认：0，必填：否
            // 1为支持，0为不支持， 默认为0(带此参数会忽略重命名策略)
            //request.AddParameter("is_revision", 1);

            // 上传方式，必填：否
            // 1 手动、2 批量上传、3 文件自动备份
            // 4 相册自动备份、5 视频自动备份
            //request.AddParameter("mode", 1);

            // 客户端创建时间(精确到秒)，默认为当前时间戳
            request.AddParameter("local_ctime", GetUnixTimestamp());

            // 	客户端修改时间(精确到秒)，默认为当前时间戳
            request.AddParameter("local_mtime", GetUnixTimestamp());

            var response = client.Execute<PanCreateResult>(request);
            if (response.StatusCode != HttpStatusCode.OK || response.Data?.errno != 0)
            {
                throw new Exception($"创建文件/文件夹请求失败 {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }
        }

        /// <summary>
        /// 计算 MD5
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public string Md5Hash(byte[] bytes)
        {
            MD5CryptoServiceProvider md5Hasher = new MD5CryptoServiceProvider();
            byte[] data = md5Hasher.ComputeHash(bytes);
            StringBuilder sBuilder = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return sBuilder.ToString();
        }

        /// <summary>
        /// 计算 MD5
        /// </summary>
        /// <param name="ss"></param>
        /// <returns></returns>
        public string Md5Hash(Stream ss)
        {
            MD5CryptoServiceProvider md5Hasher = new MD5CryptoServiceProvider();
            byte[] data = md5Hasher.ComputeHash(ss);
            StringBuilder sBuilder = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return sBuilder.ToString();
        }

        /// <summary>
        /// 时间戳转本地时间
        /// </summary>
        /// <param name="unix"></param>
        /// <returns></returns>
        public DateTime ToLocalTime(long unix)
        {
            var dto = DateTimeOffset.FromUnixTimeSeconds(unix);
            return dto.ToLocalTime().DateTime;
        }

        /// <summary>
        /// 获取当前时间戳
        /// </summary>
        /// <returns></returns>
        public long GetUnixTimestamp()
        {
            var dto = new DateTimeOffset(DateTime.Now);
            return dto.ToUnixTimeSeconds();
        }
    }

    /// <summary>
    /// 预上传/秒传返回结果
    /// </summary>
    public class PanPrecreateFile
    {
        /// <summary>
        /// 错误码
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 文件在云端的唯一标识ID
        /// </summary>
        public string fs_id { get; set; }
        /// <summary>
        /// 文件名
        /// </summary>
        public string server_filename { get; set; }
        /// <summary>
        /// 文件大小，单位B
        /// </summary>
        public int size { get; set; }
        /// <summary>
        /// 分类类型, 1 视频 2 音频 3 图片 4 文档 5 应用 6 其他 7 种子
        /// </summary>
        public int category { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long request_id { get; set; }
        /// <summary>
        /// 上传后使用的文件绝对路径
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// 是否目录，0 文件、1 目录
        /// </summary>
        public int isdir { get; set; }
        /// <summary>
        /// 文件修改时间 uint64 1545969541
        /// </summary>
        public long mtime { get; set; }
        /// <summary>
        /// 文件创建时间 uint64 1545969541
        /// </summary>
        public long ctime { get; set; }
        /// <summary>
        /// 文件的MD5，只有提交文件时才返回，提交目录时没有该值
        /// </summary>
        public string md5 { get; set; }
    }

    /// <summary>
    /// 预上传/秒传返回结果
    /// </summary>
    public class PanPrecreateResult
    {
        /// <summary>
        /// 返回类型，1 文件在云端不存在、2 文件在云端已存在
        /// </summary>
        public int return_type { get; set; }
        /// <summary>
        /// 错误码 !=0, 则错误
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public PanPrecreateFile info { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long request_id { get; set; }
        /// <summary>
        /// 文件的绝对路径
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// 上传id
        /// </summary>
        public string uploadid { get; set; }
        /// <summary>
        /// 需要上传的分片序号，索引从0开始
        /// </summary>
        public List<int> block_list { get; set; } = new List<int>();
    }

    /// <summary>
    /// 上传文件返回结果
    /// </summary>
    public class PanUploadResult
    {
        /// <summary>
        /// 文件切片云端md5
        /// </summary>
        public string md5 { get; set; }
        /// <summary>
        /// 错误码 !=0, 则错误
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long request_id { get; set; }
    }

    /// <summary>
    /// 创建文件/文件夹
    /// </summary>
    public class PanCreateResult
    {
        /// <summary>
        /// 文件创建时间
        /// </summary>
        public long ctime { get; set; }
        /// <summary>
        /// 文件在云端的唯一标识ID
        /// </summary>
        public long fs_id { get; set; }
        /// <summary>
        /// 是否目录，0 文件、1 目录
        /// </summary>
        public int isdir { get; set; }
        /// <summary>
        /// 文件的MD5，只有提交文件时才返回，提交目录时没有该值
        /// </summary>
        public string md5 { get; set; }
        /// <summary>
        /// 文件修改时间
        /// </summary>
        public long mtime { get; set; }
        /// <summary>
        /// 上传后使用的文件绝对路径
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// 文件大小，单位B
        /// </summary>
        public long size { get; set; }
        /// <summary>
        /// 错误码
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 文件名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 文件名
        /// </summary>
        public string server_filename { get; set; }
        /// <summary>
        /// 分类类型, 1 视频 2 音频 3 图片 4 文档 5 应用 6 其他 7 种子
        /// </summary>
        public int category { get; set; }
    }

    /// <summary>
    /// 获取文件列表
    /// </summary>
    public class PanListItem
    {
        /// <summary>
        /// 文件类型，1 视频、2 音频、3 图片、4 文档、5 应用、6 其他、7 种子
        /// </summary>
        public int category { get; set; }
        /// <summary>
        /// 文件在云端的唯一标识ID
        /// </summary>
        public long fs_id { get; set; }
        /// <summary>
        /// 文件在服务器创建时间
        /// </summary>
        public long server_ctime { get; set; }
        /// <summary>
        /// 文件在客户端修改时间
        /// </summary>
        public long local_mtime { get; set; }
        /// <summary>
        /// 文件大小，单位B
        /// </summary>
        public long size { get; set; }
        /// <summary>
        /// 文件在服务器修改时间
        /// </summary>
        public long server_mtime { get; set; }
        /// <summary>
        /// 文件的绝对路径
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// 文件在客户端创建时间
        /// </summary>
        public long local_ctime { get; set; }
        /// <summary>
        /// 文件名称
        /// </summary>
        public string server_filename { get; set; }
        /// <summary>
        /// 文件的md5值，只有是文件类型时，该KEY才存在
        /// </summary>
        public string md5 { get; set; }
        /// <summary>
        /// 是否目录，0 文件、1 目录
        /// </summary>
        public int isdir { get; set; }
        /// <summary>
        /// 该目录是否存在子目录， 只有请求参数带WEB且该条目为目录时，该KEY才存在， 0为存在， 1为不存在
        /// </summary>
        public int dir_empty { get; set; }
    }

    /// <summary>
    /// 获取文件列表
    /// </summary>
    public class PanListResult
    {
        /// <summary>
        /// 0 成功
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string guid_info { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<PanListItem> list { get; set; } = new List<PanListItem>();
        /// <summary>
        /// 
        /// </summary>
        public long request_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long guid { get; set; }
    }

    /// <summary>
    /// 获取文件信息和下载链接
    /// </summary>
    public class PanFileInfoItem
    {
        /// <summary>
        /// 文件类型
        /// </summary>
        public int category { get; set; }
        /// <summary>
        /// 文件下载地址
        /// </summary>
        public string dlink { get; set; }
        /// <summary>
        /// 文件名
        /// </summary>
        public string filename { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long fs_id { get; set; }
        /// <summary>
        /// 是否是目录
        /// </summary>
        public int isdir { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string md5 { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long oper_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// 文件的服务器创建时间
        /// </summary>
        public long server_ctime { get; set; }
        /// <summary>
        /// 文件的服务修改时间
        /// </summary>
        public long server_mtime { get; set; }
        /// <summary>
        /// 文件大小
        /// </summary>
        public long size { get; set; }
    }

    /// <summary>
    /// 获取文件信息和下载链接
    /// </summary>
    public class PanFileInfoResult
    {
        /// <summary>
        /// 
        /// </summary>
        public string errmsg { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 文件信息列表
        /// </summary>
        public List<PanFileInfoItem> list { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string request_id { get; set; }
    }

    /// <summary>
    /// 搜索文件
    /// </summary>
    public class PanSearchListItem
    {
        /// <summary>
        /// 文件在云端的唯一标识
        /// </summary>
        public long fs_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string server_filename { get; set; }
        /// <summary>
        /// 文件大小
        /// </summary>
        public long size { get; set; }
        /// <summary>
        /// 文件在服务端修改时间
        /// </summary>
        public long server_mtime { get; set; }
        /// <summary>
        /// 文件在服务端创建时间
        /// </summary>
        public long server_ctime { get; set; }
        /// <summary>
        /// 文件在客户端修改时间
        /// </summary>
        public long local_mtime { get; set; }
        /// <summary>
        /// 文件在客户端创建时间
        /// </summary>
        public long local_ctime { get; set; }
        /// <summary>
        /// 是否是目录，0为否，1为是
        /// </summary>
        public int isdir { get; set; }
        /// <summary>
        /// 文件类型
        /// </summary>
        public int category { get; set; }
        /// <summary>
        /// 文件md5
        /// </summary>
        public string md5 { get; set; }
    }

    /// <summary>
    /// 搜索文件
    /// </summary>
    public class PanSearchResult
    {
        /// <summary>
        /// 
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 文件列表
        /// </summary>
        public List<PanSearchListItem> list { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long request_id { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<string> contentlist { get; set; }
        /// <summary>
        /// 是否还有下一页
        /// </summary>
        public int has_more { get; set; }
    }

    /// <summary>
    /// 管理文件
    /// </summary>
    public class PanManageResult
    {
        /// <summary>
        /// 错误码 !=0, 则错误
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<PanManageItem> info { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public long request_id { get; set; }
    }

    public class PanManageItem
    {
        /// <summary>
        /// 
        /// </summary>
        public int errno { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string path { get; set; }
    }
}
