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
    /// Baidu Netdisk Description
    /// 1、不开通百度会员会受到下载限速，上传不会限速
    /// 1. Without Baidu membership, download speeds are limited, but upload speeds are not restricted.
    /// 2、路径不支持表情等特殊字符（在本系统不会存在问题）
    /// 2. Paths do not support special characters such as emojis (no issues within this system).
    /// 3、0 字节的文件，需要做特殊处理，直接调用创建文件接口
    /// 3. Zero-byte files require special handling and should directly call the file creation interface.
    /// 4、支持秒传：当文件在百度云存在时，可以秒传到百度云，秒传支持必须传递对应的 MD5 信息
    /// 4. Supports rapid upload: When a file exists on Baidu Cloud, it can be rapidly uploaded, requiring the corresponding MD5 information.
    /// 5、关于分片上传大小文件限制
    /// 5. Regarding the file size limitations for chunked uploads
    /// 5.1 普通用户单个分片大小固定为4MB（文件大小如果小于4MB，无需切片，直接上传即可），单文件总大小上限为4G
    /// 5.1 For regular users, the size of each chunk is fixed at 4MB (files smaller than 4MB do not need to be chunked and can be uploaded directly), with a maximum file size of 4GB.
    /// 5.2 普通会员用户单个分片大小上限为16MB，单文件总大小上限为10G
    /// 5.2 For regular member users, the maximum size of each chunk is 16MB, with a maximum file size of 10GB.
    /// 5.3 超级会员用户单个分片大小上限为32MB，单文件总大小上限为20G
    /// 5.3 For super member users, the maximum size of each chunk is 32MB, with a maximum file size of 20GB.
    /// 6、注意文件上传接口返回的不是 json 格式
    /// 6. Note that the file upload interface does not return in JSON format.
    ///
    /// TODO
    /// 7、如果是 302 下载链接，是否会产生错误
    /// 7. If it is a 302 download link, will it produce an error?
    /// <see href="https://pan.baidu.com/union/doc/"/>
    /// </summary>
    public class Netdisk : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        /// <summary>
        /// 日志
        /// Logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<Netdisk>();

        /// <summary>
        /// 授权码 KEY
        /// Authorization Code KEY
        /// </summary>
        private const string AUTHORIZATION_CODE = "baidunetdisk-authorization-code";

        /// <summary>
        /// 单个分片大小 KEY
        /// Block Size KEY
        /// </summary>
        private const string BLOCK_SIZE = "baidunetdisk-blocksize";

        /// <summary>
        /// 授权码令牌
        /// Authorization token
        /// 百度授权码, 请点击下发链接登录百度云, 获取授权码, 当前应用服务由 "天网中心" 免费提供; 也可自行申请接入百度云, 输入自已的授权码
        /// Baidu authorization code. Please click the link to log in to Baidu Cloud and obtain the authorization code. The current application service is provided for free by "SkyNet Center"; you can also apply to join Baidu Cloud and enter your authorization code.
        /// </summary>
        private readonly string _token;

        /// <summary>
        /// 存储根目录
        /// Storage Root Directory
        /// 文件目录, 建议 /apps/sync, 注意: 根路径 apps 在网盘中对应是 "我的应用程序" 文件夹
        /// File directory, suggested /apps/sync. Note: The root path 'apps' corresponds to the "My Applications" folder in the net disk.
        /// </summary>
        private readonly string _path;

        /// <summary>
        /// 文件默认分块大小, 切片上传文件分块大小 4MB 16MB 32MB
        /// Default file chunk size, file chunk size for upload 4MB 16MB 32MB
        /// 单个分片大小4MB/16MB/32MB, 不可填写其他, 默认4MB, 普通会员16MB, 超级会员32MB
        /// Single chunk size 4MB/16MB/32MB, other sizes not permissible, default 4MB, regular members 16MB, super members 32MB
        /// </summary>
        private readonly int _defaultLength = 1024 * 1024 * 4;

        /// <summary>
        /// 校验段对应文件前256KB
        /// Corresponding to the first 256KB of the file for verification
        /// </summary>
        private readonly int _sliceLength = 1024 * 256;

        /// <summary>
        /// 网盘 host
        /// Netdisk host
        /// </summary>
        private readonly string panHost = "https://pan.baidu.com";

        /// <summary>
        /// 网盘上传 host
        /// Netdisk upload host
        /// </summary>
        private readonly string pcsHost = "https://d.pcs.baidu.com";

        public Netdisk()
        { }

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
                    throw new Exception($"Failed to request the file list {response.StatusCode}, {response.Data?.errno}, {response.Content}");
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
        /// Manage Files
        /// </summary>
        /// <param name="filelist">待操作的文件列表 List of files to be operated</param>
        /// <param name="opera">操作类型，如copy, move, rename, delete Operation type, such as copy, move, rename, delete</param>
        public void Manage(string filelist, string opera)
        {
            // 构造管理文件的URL Construct the URL for file management
            var manageUrl = $"{panHost}/rest/2.0/xpan/file?method=filemanager&access_token={_token}&opera={opera}";
            var client = new RestClient(manageUrl)
            {
                Timeout = -1
            };
            var request = new RestRequest(Method.POST);

            // 参数：异步标志 Parameters: asynchronous flag
            request.AddParameter("async", 0);

            // 参数：重复文件的处理策略 Parameters: strategy for handling duplicate files
            request.AddParameter("ondup", "overwrite");

            /*
             * 待操作文件列表
             * copy/move: [{"path":"/测试目录/123456.docx","dest":"/测试目录/abc","newname":"11223.docx","ondup":"fail"}]
             * rename: [{path":"/测试目录/123456.docx","newname":"test.docx"}]
             * delete: ["/测试目录/123456.docx"]
             */
            request.AddParameter("filelist", filelist);

            // 执行请求 Execute the request
            var response = client.Execute<PanManageResult>(request);
            if (response.StatusCode != HttpStatusCode.OK || response.Data?.errno != 0)
            {
                // 异常处理 Exception handling
                throw new Exception($"管理文件请求失败 {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }
        }

        /// <summary>
        /// 上传文件
        /// Upload File
        /// </summary>
        /// <param name="remotename">远程文件名 Remote file name</param>
        /// <param name="stream">文件流 File stream</param>
        /// <param name="cancelToken">取消令牌 Cancellation token</param>
        /// <returns>任务对象 Task object</returns>
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
        /// Retrieve and download a remote file
        /// </summary>
        /// <param name="remotename">远程文件名 Remote file name</param>
        /// <param name="fs">文件流，用于写入下载的数据 File stream for writing downloaded data</param>
        public void Get(string remotename, Stream fs)
        {
            // 其他说明
            // Other descriptions
            // 通过【列表类接口】获取文件的fsid。
            // Retrieve the file's fsid through the list interface.
            // 通过【取文件信息filemetas接口】获取文件的下载地址，即接口返回的dlink字段
            // Get the file's download address through the filemetas interface, i.e., the dlink field returned by the interface.
            // 使用dlink下载文件
            // Use dlink to download the file.
            // dlink有效期为8小时
            // The validity period of dlink is 8 hours.
            // 必需要设置User-Agent字段
            // It's necessary to set the User-Agent field.
            // dlink存在302跳转
            // dlink has a 302 redirection.

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
                throw new Exception($"Failed to obtain the remote file download address. Procedure {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }

            var url = response.Data.list.FirstOrDefault(c => c.fs_id == key)?.dlink;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new Exception($"Get remote file download is empty {response.StatusCode}, {response.Data?.errno}, {response.Content}");
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
                throw new Exception($"Failed to download file {remotename}, {downloadUrl}", ex);
            }
        }
        /// <summary>
        /// 获取文件在百度的唯一标识
        /// Obtain the unique identifier of the file in Baidu
        /// 可以修改为请求列表时将key保存到本地
        /// The key can be saved locally when requesting a list
        /// </summary>
        /// <param name="remotename">远程文件名 Remote file name</param>
        /// <returns>文件唯一标识 File unique identifier</returns>
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
                throw new Exception($"File query failure, {remotename}, {response.StatusCode}, {response.Data?.errno}, {response.Content}");
            }
            var key = response?.Data?.list?.FirstOrDefault(c => c.server_filename == remotename)?.fs_id ?? 0;
            if (key <= 0)
            {
                throw new Exception($"Failed to query a file because the unique identifier of the file in Baidu Cloud cannot be queried, {remotename}, {response.StatusCode}, {response.Content}");
            }
            return key;
        }


        /// <summary>
        /// 创建文件/创建文件夹
        /// Create File/Create Folder
        /// </summary>
        /// <param name="fileFullPath">文件的完整路径 Full path of the file</param>
        /// <param name="fileSize">文件大小 File size</param>
        /// <param name="uploadid">上传ID Upload ID</param>
        /// <param name="md5Json">MD5 JSON串 MD5 JSON string</param>
        /// <param name="isFolder">是否是文件夹 Whether it is a folder</param>
        public void Create(string fileFullPath, long fileSize, string uploadid, string md5Json, bool isFolder = false)
        {
            // 构造创建文件/文件夹的URL Construct the URL to create a file/folder
            // 文件或目录大小需与实际大小一致 The size of the file or directory must be consistent with the actual size
            // 文件命名策略 File naming strategy
            // 是否目录 Whether it's a directory
            // uploadid非空表示通过superfile2上传 Non-empty uploadid indicates uploading through superfile2
            // 如果是文件夹，则无需填写MD5 If it's a folder, no need to fill in MD5
            // 执行请求并处理异常 Execute the request and handle exceptions

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
        /// Calculate MD5
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
        /// Calculate MD5
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
        /// Timestamp to local time
        /// </summary>
        /// <param name="unix"></param>
        /// <returns></returns>
        public DateTime ToLocalTime(long unix)
        {
            var dto = DateTimeOffset.FromUnixTimeSeconds(unix);
            return dto.ToLocalTime().DateTime;
        }

        /// <summary>
        /// Gets the current timestamp
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
    /// Pre-upload/Rapid Upload Result
    /// </summary>
    public class PanPrecreateFile
    {
        /// <summary>
        /// 错误码
        /// Error code
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// 文件在云端的唯一标识ID
        /// Unique cloud ID of the file
        /// </summary>
        public string fs_id { get; set; }

        /// <summary>
        /// 文件名
        /// File name
        /// </summary>
        public string server_filename { get; set; }

        /// <summary>
        /// 文件大小，单位B
        /// File size, in bytes
        /// </summary>
        public int size { get; set; }

        /// <summary>
        /// 分类类型, 1 视频 2 音频 3 图片 4 文档 5 应用 6 其他 7 种子
        /// Category type, 1 for video, 2 for audio, 3 for image, 4 for document, 5 for application, 6 for others, 7 for torrent
        /// </summary>
        public int category { get; set; }

        /// <summary>
        /// Request ID
        /// </summary>
        public long request_id { get; set; }

        /// <summary>
        /// 上传后使用的文件绝对路径
        /// Absolute path of the file used after upload
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// 是否目录，0 文件、1 目录
        /// Whether it is a directory, 0 for file, 1 for directory
        /// </summary>
        public int isdir { get; set; }

        /// <summary>
        /// 文件修改时间 uint64 1545969541
        /// File modification time uint64 1545969541
        /// </summary>
        public long mtime { get; set; }

        /// <summary>
        /// 文件创建时间 uint64 1545969541
        /// File creation time uint64 1545969541
        /// </summary>
        public long ctime { get; set; }

        /// <summary>
        /// 文件的MD5，只有提交文件时才返回，提交目录时没有该值
        /// MD5 of the file, returned only when submitting files, not directories
        /// </summary>
        public string md5 { get; set; }
    }

    /// <summary>
    /// 预上传/秒传返回结果
    /// Pre-upload/Rapid Upload Result
    /// </summary>
    public class PanPrecreateResult
    {
        /// <summary>
        /// 返回类型，1 文件在云端不存在、2 文件在云端已存在
        /// Return type, 1 for file not existing in the cloud, 2 for file already existing in the cloud
        /// </summary>
        public int return_type { get; set; }

        /// <summary>
        /// 错误码 !=0, 则错误
        /// Error code, !=0 indicates an error
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// File information
        /// </summary>
        public PanPrecreateFile info { get; set; }

        /// <summary>
        /// Request ID
        /// </summary>
        public long request_id { get; set; }

        /// <summary>
        /// 文件的绝对路径
        /// Absolute path of the file
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// 上传id
        /// Upload ID
        /// </summary>
        public string uploadid { get; set; }

        /// <summary>
        /// 需要上传的分片序号，索引从0开始
        /// The index of the block to upload, starting from 0
        /// </summary>
        public List<int> block_list { get; set; } = new List<int>();
    }

    /// <summary>
    /// 上传文件返回结果
    /// Upload File Result
    /// </summary>
    public class PanUploadResult
    {
        /// <summary>
        /// 文件切片云端md5
        /// MD5 of the file chunk in the cloud
        /// </summary>
        public string md5 { get; set; }

        /// <summary>
        /// 错误码 !=0, 则错误
        /// Error code, !=0 indicates an error
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// Request ID
        /// </summary>
        public long request_id { get; set; }
    }

    /// <summary>
    /// 创建文件/文件夹
    /// Create File/Folder
    /// </summary>
    public class PanCreateResult
    {
        /// <summary>
        /// 文件创建时间
        /// File creation time
        /// </summary>
        public long ctime { get; set; }

        /// <summary>
        /// 文件在云端的唯一标识ID
        /// Unique cloud ID of the file
        /// </summary>
        public long fs_id { get; set; }

        /// <summary>
        /// 是否目录，0 文件、1 目录
        /// Whether it is a directory, 0 for file, 1 for directory
        /// </summary>
        public int isdir { get; set; }

        /// <summary>
        /// 文件的MD5，只有提交文件时才返回，提交目录时没有该值
        /// MD5 of the file, returned only when submitting files, not directories
        /// </summary>
        public string md5 { get; set; }

        /// <summary>
        /// 文件修改时间
        /// File modification time
        /// </summary>
        public long mtime { get; set; }

        /// <summary>
        /// 上传后使用的文件绝对路径
        /// Absolute path of the file used after upload
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// 文件大小，单位B
        /// File size, in bytes
        /// </summary>
        public long size { get; set; }

        /// <summary>
        /// 错误码
        /// Error code
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// 文件名
        /// File name
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// 文件名
        /// File name
        /// </summary>
        public string server_filename { get; set; }

        /// <summary>
        /// 分类类型, 1 视频 2 音频 3 图片 4 文档 5 应用 6 其他 7 种子
        /// Category type, 1 for video, 2 for audio, 3 for image, 4 for document, 5 for application, 6 for others, 7 for torrent
        /// </summary>
        public int category { get; set; }
    }

    /// <summary>
    /// 获取文件列表
    /// Get File List
    /// </summary>
    public class PanListItem
    {
        /// <summary>
        /// 文件类型，1 视频、2 音频、3 图片、4 文档、5 应用、6 其他、7 种子
        /// File type, 1 for video, 2 for audio, 3 for image, 4 for document, 5 for application, 6 for others, 7 for torrent
        /// </summary>
        public int category { get; set; }

        /// <summary>
        /// 文件在云端的唯一标识ID
        /// Unique cloud ID of the file
        /// </summary>
        public long fs_id { get; set; }

        /// <summary>
        /// 文件在服务器创建时间
        /// File creation time on the server
        /// </summary>
        public long server_ctime { get; set; }

        /// <summary>
        /// 文件在客户端修改时间
        /// File modification time on the client
        /// </summary>
        public long local_mtime { get; set; }

        /// <summary>
        /// 文件大小，单位B
        /// File size, in bytes
        /// </summary>
        public long size { get; set; }

        /// <summary>
        /// 文件在服务器修改时间
        /// File modification time on the server
        /// </summary>
        public long server_mtime { get; set; }

        /// <summary>
        /// 文件的绝对路径
        /// Absolute path of the file
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// 文件在客户端创建时间
        /// File creation time on the client
        /// </summary>
        public long local_ctime { get; set; }

        /// <summary>
        /// 文件名称
        /// File name
        /// </summary>
        public string server_filename { get; set; }

        /// <summary>
        /// 文件的md5值，只有是文件类型时，该KEY才存在
        /// MD5 of the file, only exists for file type
        /// </summary>
        public string md5 { get; set; }

        /// <summary>
        /// 是否目录，0 文件、1 目录
        /// Whether it is a directory, 0 for file, 1 for directory
        /// </summary>
        public int isdir { get; set; }

        /// <summary>
        /// 该目录是否存在子目录， 只有请求参数带WEB且该条目为目录时，该KEY才存在， 0为存在， 1为不存在
        /// Whether the directory has subdirectories, only exists for directory type when the request parameter includes WEB, 0 for exists, 1 for does not exist
        /// </summary>
        public int dir_empty { get; set; }
    }

    /// <summary>
    /// 获取文件列表
    /// Get File List
    /// </summary>
    public class PanListResult
    {
        /// <summary>
        /// 0 成功
        /// 0 Success
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// GUID information
        /// </summary>
        public string guid_info { get; set; }

        /// <summary>
        /// File list
        /// </summary>
        public List<PanListItem> list { get; set; } = new List<PanListItem>();

        /// <summary>
        /// Request ID
        /// </summary>
        public long request_id { get; set; }

        /// <summary>
        /// GUID
        /// </summary>
        public long guid { get; set; }
    }

    /// <summary>
    /// 获取文件信息和下载链接
    /// Get File Information and Download Link
    /// </summary>
    public class PanFileInfoItem
    {
        /// <summary>
        /// 文件类型
        /// File type
        /// </summary>
        public int category { get; set; }

        /// <summary>
        /// 文件下载地址
        /// File download link
        /// </summary>
        public string dlink { get; set; }

        /// <summary>
        /// 文件名
        /// File name
        /// </summary>
        public string filename { get; set; }

        /// <summary>
        /// Unique cloud ID of the file
        /// </summary>
        public long fs_id { get; set; }

        /// <summary>
        /// 是否是目录
        /// Whether it is a directory
        /// </summary>
        public int isdir { get; set; }

        /// <summary>
        /// MD5 of the file
        /// </summary>
        public string md5 { get; set; }

        /// <summary>
        /// Operation ID
        /// </summary>
        public long oper_id { get; set; }

        /// <summary>
        /// Absolute path of the file
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// 文件的服务器创建时间
        /// File creation time on the server
        /// </summary>
        public long server_ctime { get; set; }

        /// <summary>
        /// 文件的服务修改时间
        /// File modification time on the server
        /// </summary>
        public long server_mtime { get; set; }

        /// <summary>
        /// 文件大小
        /// File size
        /// </summary>
        public long size { get; set; }
    }

    /// <summary>
    /// 获取文件信息和下载链接
    /// Get File Information and Download Link
    /// </summary>
    public class PanFileInfoResult
    {
        /// <summary>
        /// Error message
        /// </summary>
        public string errmsg { get; set; }

        /// <summary>
        /// Error code, !=0 indicates an error
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// 文件信息列表
        /// List of file information
        /// </summary>
        public List<PanFileInfoItem> list { get; set; }

        /// <summary>
        /// Request ID
        /// </summary>
        public string request_id { get; set; }
    }

    /// <summary>
    /// 搜索文件
    /// Search File
    /// </summary>
    public class PanSearchListItem
    {
        /// <summary>
        /// 文件在云端的唯一标识
        /// Unique cloud ID of the file
        /// </summary>
        public long fs_id { get; set; }

        /// <summary>
        /// File path
        /// </summary>
        public string path { get; set; }

        /// <summary>
        /// File name on the server
        /// </summary>
        public string server_filename { get; set; }

        /// <summary>
        /// 文件大小
        /// File size
        /// </summary>
        public long size { get; set; }

        /// <summary>
        /// 文件在服务端修改时间
        /// File modification time on the server
        /// </summary>
        public long server_mtime { get; set; }

        /// <summary>
        /// 文件在服务端创建时间
        /// File creation time on the server
        /// </summary>
        public long server_ctime { get; set; }

        /// <summary>
        /// 文件在客户端修改时间
        /// File modification time on the client
        /// </summary>
        public long local_mtime { get; set; }

        /// <summary>
        /// 文件在客户端创建时间
        /// File creation time on the client
        /// </summary>
        public long local_ctime { get; set; }

        /// <summary>
        /// 是否是目录，0为否，1为是
        /// Whether it is a directory, 0 for no, 1 for yes
        /// </summary>
        public int isdir { get; set; }

        /// <summary>
        /// 文件类型
        /// File type
        /// </summary>
        public int category { get; set; }

        /// <summary>
        /// 文件md5
        /// File MD5
        /// </summary>
        public string md5 { get; set; }
    }

    /// <summary>
    /// 搜索文件
    /// Search File
    /// </summary>
    public class PanSearchResult
    {
        /// <summary>
        /// Error code, !=0 indicates an error
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// 文件列表
        /// List of files
        /// </summary>
        public List<PanSearchListItem> list { get; set; }

        /// <summary>
        /// Request ID
        /// </summary>
        public long request_id { get; set; }

        /// <summary>
        /// Content list
        /// </summary>
        public List<string> contentlist { get; set; }

        /// <summary>
        /// 是否还有下一页
        /// Whether there is a next page
        /// </summary>
        public int has_more { get; set; }
    }

    /// <summary>
    /// 管理文件
    /// Manage File
    /// </summary>
    public class PanManageResult
    {
        /// <summary>
        /// 错误码 !=0, 则错误
        /// Error code, !=0 indicates an error
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// File information
        /// </summary>
        public List<PanManageItem> info { get; set; }

        /// <summary>
        /// Request ID
        /// </summary>
        public long request_id { get; set; }
    }

    public class PanManageItem
    {
        /// <summary>
        /// Error code
        /// </summary>
        public int errno { get; set; }

        /// <summary>
        /// File path
        /// </summary>
        public string path { get; set; }
    }
}