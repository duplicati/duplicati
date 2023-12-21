using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 阿里云盘说明
    /// 1、不限制上传速度、不限制下载速度
    /// 2、支持秒传
    /// <see cref=""/>
    /// </summary>
    public class AliyunDrive : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        /// <summary>
        /// 日志
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<AliyunDrive>();

        /// <summary>
        /// 授权码 KEY
        /// </summary>
        private const string AUTHORIZATION_CODE = "aliyundrive-authorization-code";

        /// <summary>
        /// 授权码令牌
        /// </summary>
        private readonly string _token;

        /// <summary>
        /// 存储根目录
        /// 文件目录, 建议 /apps/sync, 注意: 根路径 apps 在网盘中对应是 "我的应用程序" 文件夹
        /// </summary>
        private readonly string _path;

        private readonly RestClient _apiClient;

        private readonly HttpClient _uploadHttpClient;

        private readonly IMemoryCache _cache;

        private const string TOEKN_KEY = "TOKEN";

        // 备份盘 ID
        private string _driveId;

        /// <summary>
        /// 所有云盘文件
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFiles = new ConcurrentDictionary<string, AliyunDriveFileItem>();

        /// <summary>
        /// 所有云盘文件夹
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFolders = new ConcurrentDictionary<string, AliyunDriveFileItem>();


        // 是否已加载列表
        private bool _isInitList = false;

        public AliyunDrive()
        { }

        public AliyunDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url?.Trim());
            var prefix = uri.HostAndPath?.Trim()?.Trim('/')?.Trim('\\');
            var ps = uri.QueryParameters;

            _path = $"/{prefix}";
            _token = ps.AllKeys.Contains(AUTHORIZATION_CODE) ? ps[AUTHORIZATION_CODE] : string.Empty;

            if (options.ContainsKey(AUTHORIZATION_CODE))
            {
                var value = options[AUTHORIZATION_CODE];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _token = value;
                }
            }

            // 接口请求
            _apiClient = new RestClient(ProviderApiHelper.ALIYUNDRIVE_API_HOST);

            // 上传请求
            // 上传链接最大有效 1 小时
            // 设置 45 分钟超时
            // 在 HttpClient 中，一旦发送了第一个请求，就不能再更改其配置属性，如超时时间 (Timeout)。
            // 这是因为 HttpClient 被设计为可重用的，它的属性设置在第一个请求发出之后就被固定下来。
            _uploadHttpClient = new HttpClient();
            _uploadHttpClient.Timeout = TimeSpan.FromMinutes(45);

            // 本地缓存
            _cache = new MemoryCache(new MemoryCacheOptions());

            // 初始化
            Initialize();
        }

        public IEnumerable<IFileEntry> List()
        {
            // 计算保存存储目录
            var saveParentPath = _path.TrimPath();
            if (!_driveFolders.ContainsKey(saveParentPath))
            {
                throw new Exception("文件夹创建失败");
            }

            // 存储目录 ID
            var saveParentFileId = _driveFolders[saveParentPath].FileId;

            FetchAllFilesAsync(_driveId, saveParentFileId);

            //SearchAllFilesAsync(_driveId);

            foreach (var item in _driveFolders)
            {
                var fileName = item.Value.Name;
                var last = item.Value.UpdatedAt.Value;
                var f = new FileEntry(fileName, item.Value.Size ?? 0, last.ToLocalTime().DateTime, last.ToLocalTime().DateTime);
                f.IsFolder = true;
                yield return f;
            }

            foreach (var item in _driveFiles)
            {
                var fileName = item.Value.Name;
                var last = item.Value.UpdatedAt.Value;
                yield return new FileEntry(fileName, item.Value.Size ?? 0, last.ToLocalTime().DateTime, last.ToLocalTime().DateTime);
            }

            _isInitList = true;
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
            if (!_isInitList)
            {
                var list = List().ToList();
            }

            var key = $"{_path}/{remotename}".TrimPath();
            if (!_driveFiles.TryGetValue(key, out var item) || item == null)
            {
                throw new Exception("文件不存在");
            }

            ProviderApiHelper.FileDelete(item.DriveId, item.FileId, AccessToken);
        }

        public void Rename(string oldname, string newname)
        {
            if (!_isInitList)
            {
                var list = List().ToList();
            }

            var key = $"{_path}/{oldname}".TrimPath();
            if (!_driveFiles.TryGetValue(key, out var item) || item == null)
            {
                throw new Exception("文件不存在");
            }

            ProviderApiHelper.FileUpdate(item.DriveId, item.FileId, newname, AccessToken);
        }

        public void Test()
        {
            CreateBackupPath(_path);
        }

        public void CreateFolder()
        {
            CreateBackupPath(_path);
        }

        public void Dispose()
        {
        }

        public string DisplayName => Strings.AliyunDriveBackend.DisplayName;

        public string Description => Strings.AliyunDriveBackend.Description;

        public string ProtocolKey => "aliyundrive";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHORIZATION_CODE, CommandLineArgument.ArgumentType.String, Strings.AliyunDriveBackend.AliyunDriveAccountDescriptionShort,Strings.AliyunDriveBackend.AliyunDriveAccountDescriptionLong),
                });
            }
        }

        public string[] DNSName => new string[] { new Uri(ProviderApiHelper.ALIYUNDRIVE_API_HOST).Host };

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="remotename"></param>
        /// <param name="stream"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public async Task UploadFile(string remotename, Stream stream, bool needPreHash = true)
        {
            try
            {
                // 文件名
                var name = AliyunDriveHelper.EncodeFileName(remotename);

                // 计算保存存储目录
                var saveParentPath = _path.TrimPath();
                if (!_driveFolders.ContainsKey(saveParentPath))
                {
                    throw new Exception("文件夹创建失败");
                }

                // 存储目录 ID
                var saveParentFileId = _driveFolders[saveParentPath].FileId;

                var request = new RestRequest("/adrive/v1.0/openFile/create", Method.POST);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", $"Bearer {AccessToken}");

                var fileSize = stream.Length;

                object body = new
                {
                    drive_id = _driveId,
                    parent_file_id = saveParentFileId,
                    name = name,
                    type = "file",
                    check_name_mode = "ignore", // 覆盖文件模式
                    size = fileSize
                };

                // 是否进行秒传处理
                var isRapidUpload = false;

                // 开启秒传
                // 如果文件 > 10kb 则进行秒传计算，否则不进行
                if (fileSize > 1024 * 10)
                {
                    isRapidUpload = true;
                }

                // 如果需要计算秒传
                if (isRapidUpload)
                {
                    if (fileSize > 1024 * 1024 && needPreHash)
                    {
                        // 如果文件超过 1mb 则进行预处理，判断是否可以进行妙传
                        var preHash = AliyunDriveHelper.GenerateStartSHA1(stream);
                        body = new
                        {
                            drive_id = _driveId,
                            parent_file_id = saveParentFileId,
                            name = name,
                            type = "file",
                            check_name_mode = "ignore",
                            size = fileSize,
                            pre_hash = preHash
                        };
                    }
                    else
                    {
                        // > 10kb 且 < 1mb 的文件直接计算 sha1
                        var proofCode = AliyunDriveHelper.GenerateProofCode(stream, fileSize, AccessToken);
                        var contentHash = AliyunDriveHelper.GenerateSHA1(stream);

                        body = new
                        {
                            drive_id = _driveId,
                            parent_file_id = saveParentFileId,
                            name = name,
                            type = "file",
                            check_name_mode = "ignore",
                            size = fileSize,
                            content_hash = contentHash,
                            content_hash_name = "sha1",
                            proof_version = "v1",
                            proof_code = proofCode
                        };
                    }
                }
                request.AddJsonBody(body);
                var response = _apiClient.Execute(request);

                // 如果需要秒传，并且需要预处理时
                // System.Net.HttpStatusCode.Conflict 注意可能不是 409
                if (isRapidUpload && needPreHash
                    && (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict)
                    && response.Content.Contains("PreHashMatched"))
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(response.Content);
                    if (data?.code == "PreHashMatched")
                    {
                        // 匹配成功，进行完整的秒传，不需要预处理
                        await UploadFile(remotename, stream, false);
                        return;
                    }
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var dataObj = JsonConvert.DeserializeObject<AliyunDriveOpenFileCreateResponse>(response.Content);

                    string drive_id = dataObj?.DriveId;
                    string file_id = dataObj?.FileId;
                    string upload_id = dataObj?.UploadId;

                    var rapid_upload = dataObj?.RapidUpload;
                    if (rapid_upload == true)
                    {
                        return;
                    }

                    string upload_url = dataObj?.PartInfoList?.FirstOrDefault()?.UploadUrl;

                    //using (HttpClient httpClient = new HttpClient())
                    //{
                    //// 读取文件作为字节流
                    //byte[] fileData = File.ReadAllBytes(filePath);

                    // 创建HttpContent
                    stream.Seek(0, SeekOrigin.Begin);
                    var content = new StreamContent(stream);

                    // 发送PUT请求
                    var uploadRes = await _uploadHttpClient.PutAsync(upload_url, content);
                    if (!uploadRes.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
                    }

                    // 检查请求是否成功
                    if (uploadRes.IsSuccessStatusCode)
                    {
                        var reqCom = new RestRequest("/adrive/v1.0/openFile/complete", Method.POST);
                        reqCom.AddHeader("Content-Type", "application/json");
                        reqCom.AddHeader("Authorization", $"Bearer {AccessToken}");
                        var body3 = new
                        {
                            drive_id = _driveId,
                            file_id = file_id,
                            upload_id = upload_id,
                        };
                        reqCom.AddJsonBody(body3);
                        var resCom = _apiClient.Execute<AliyunDriveFileItem>(reqCom);

                        if (resCom.StatusCode != HttpStatusCode.OK)
                        {
                            throw resCom.ErrorException ?? new Exception("上传文件失败");
                        }
                        //_log.LogInformation("上传标记完成 " + localFileInfo.Key);

                        // 将文件添加到上传列表
                        var data = resCom.Data; //  JsonSerializer.Deserialize<AliyunDriveFileItem>(response3.Content);
                        if (data.ParentFileId == "root")
                        {
                            // 当前目录在根路径
                            // /{当前路径}/
                            _driveFiles.TryAdd($"{data.Name}".TrimPath(), data);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == data.ParentFileId).First();
                            var path = $"{parent.Key}/{data.Name}".TrimPath();

                            // /{父级路径}/{当前路径}/
                            _driveFiles.TryAdd(path, data);
                        }
                    }
                    else
                    {
                        //_log.LogInformation($"Failed to upload the file. Status Code: {response.StatusCode}");
                    }
                    //}
                }
                else
                {
                    throw response.ErrorException;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 获取文件列表（限流 4 QPS）
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="parentFileId"></param>
        /// <param name="limit"></param>
        /// <param name="orderBy"></param>
        /// <param name="orderDirection"></param>
        /// <param name="category"></param>
        /// <param name="type"></param>
        /// <param name="saveRootPath">备份保存的目录，如果匹配到则立即返回</param>
        /// <returns></returns>
        public void FetchAllFilesAsync(
            string driveId,
            string parentFileId,
            int limit = 100,
            string orderBy = null,
            string orderDirection = null,
            string category = null,
            string type = "all")
        {
            try
            {
                var allItems = new List<AliyunDriveFileItem>();
                string marker = null;
                do
                {
                    var sw = new Stopwatch();
                    sw.Start();

                    var response = FetchFileListAsync(driveId, parentFileId, limit, marker, orderBy, orderDirection, category, type);
                    var responseData = JsonConvert.DeserializeObject<AliyunFileList>(response.Content);
                    if (responseData.Items.Count > 0)
                    {
                        allItems.AddRange(responseData.Items.ToList());
                    }
                    marker = responseData.NextMarker;

                    sw.Stop();

                    // 等待 250ms 以遵守限流策略
                    if (sw.ElapsedMilliseconds < 250)
                        Thread.Sleep((int)(250 - sw.ElapsedMilliseconds));

                } while (!string.IsNullOrEmpty(marker));

                foreach (var item in allItems)
                {
                    // 如果是文件夹，则递归获取子文件列表
                    if (item.Type == "folder")
                    {
                        // 如果是根目录
                        if (item.ParentFileId == "root")
                        {
                            _driveFolders.TryAdd($"{item.Name}".TrimPath(), item);
                        }
                        else
                        {
                            var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First();
                            _driveFolders.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        }

                        FetchAllFilesAsync(driveId, item.FileId, limit, orderBy, orderDirection, category, type);
                    }
                    else
                    {
                        // 如果是根目录的文件
                        if (item.ParentFileId == "root")
                        {
                            _driveFiles.TryAdd($"{item.Name}".TrimPath(), item);
                        }
                        else
                        {
                            // 构建文件路径作为字典的键
                            var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First();
                            _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private IRestResponse FetchFileListAsync(string driveId, string parentFileId, int limit, string marker, string orderBy, string orderDirection, string category, string type)
        {
            var request = new RestRequest("/adrive/v1.0/openFile/list", Method.POST);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");
            var body = new
            {
                drive_id = driveId,
                parent_file_id = parentFileId,
                limit,
                marker,
                order_by = orderBy,
                order_direction = orderDirection,
                category,
                type
            };
            request.AddJsonBody(body);

            return ExecuteWithRetryAsync(request);
        }

        private IRestResponse ExecuteWithRetryAsync(RestRequest request)
        {
            const int maxRetries = 5;
            int retries = 0;
            while (true)
            {
                var response = _apiClient.Execute(request);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return response;
                }
                else if ((int)response.StatusCode == 429)
                {
                    if (retries >= maxRetries)
                    {
                        throw new Exception("请求次数过多，已达到最大重试次数");
                    }

                    // 休眠 250ms
                    Thread.Sleep(250);
                    retries++;
                }
                else
                {
                    // 休眠 250ms
                    Thread.Sleep(250);
                    throw new Exception($"请求失败: {response.StatusCode}");
                }
            }
        }

        /// <summary>
        /// 获取并下载远程文件
        /// </summary>
        /// <param name="remotename"></param>
        /// <param name="fs"></param>
        public void Get(string remotename, Stream fs)
        {
            if (!_isInitList)
            {
                var list = List().ToList();
            }

            var key = $"{_path}/{remotename}".TrimPath();
            if (!_driveFiles.TryGetValue(key, out var item) || item == null)
            {
                throw new Exception("文件不存在");
            }

            var url = "";

            // 获取详情 url
            var request = new RestRequest("/adrive/v1.0/openFile/get", Method.POST);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");
            object body = new
            {
                drive_id = _driveId,
                file_id = item.FileId
            };
            request.AddJsonBody(body);
            var resInfo = _apiClient.Execute<AliyunDriveFileItem>(request);
            if (resInfo.StatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(resInfo.Data.Url))
            {
                url = resInfo.Data.Url;
            }
            else
            {
                throw resInfo.ErrorException;
            }

            using (var httpClient = new HttpClient())
            {
                try
                {
                    // 设置 45 分钟超时
                    httpClient.Timeout = TimeSpan.FromMinutes(45);

                    using (var response = httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result)
                    {
                        response.EnsureSuccessStatusCode();

                        // 读取响应流
                        using (var stream = response.Content.ReadAsStreamAsync().Result)
                        {
                            stream.CopyTo(fs);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        /// <summary>
        /// 创建文件/创建文件夹
        /// </summary>
        /// <param name="fileFullPath"></param>
        /// <param name="fileSize"></param>
        /// <param name="uploadid"></param>
        /// <param name="md5Json"></param>
        public void CreateBackupPath(string fileFullPath)
        {
            var saveParentPath = fileFullPath.TrimPath();
            if (!_driveFolders.ContainsKey(saveParentPath))
            {
                var savePaths = saveParentPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var savePathsParentFileId = "root";
                foreach (var subPath in savePaths)
                {
                    savePathsParentFileId = CreateFolder(subPath, savePathsParentFileId);
                }
            }

            if (!_driveFolders.ContainsKey(saveParentPath))
            {
                throw new Exception("文件夹创建失败");
            }
        }

        /// <summary>
        /// 获取当前有效的访问令牌
        /// </summary>
        public string AccessToken
        {
            get
            {
                return _cache.GetOrCreate(TOEKN_KEY, c =>
                {
                    var token = InitToken();

                    var secs = 7200;
                    if (secs <= 300 || secs > 7200)
                    {
                        secs = 7200;
                    }

                    // 提前 5 分钟过期
                    c.SetAbsoluteExpiration(TimeSpan.FromSeconds(secs - 60 * 5));

                    return token;
                });
            }
        }

        /// <summary>
        /// 初始化令牌
        /// </summary>
        /// <returns></returns>
        public string InitToken()
        {
            // 重新获取令牌
            var data = ProviderApiHelper.RefreshToken(_token);
            if (data != null)
            {
                return data.AccessToken;
            }

            throw new Exception("初始化访问令牌失败");
        }

        /// <summary>
        /// 初始化作业（路径、云盘信息等）
        /// </summary>
        /// <returns></returns>
        private void Initialize()
        {
            // 获取云盘信息
            InitAliyunDriveInfo();

            // 初始化备份目录
            InitBackupPath();
        }

        /// <summary>
        /// 获取用户 drive 信息
        /// </summary>
        /// <returns></returns>
        public void InitAliyunDriveInfo()
        {
            var request = new RestRequest("/adrive/v1.0/user/getDriveInfo", Method.POST);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");
            var response = _apiClient.Execute<AliyunDriveInfo>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = response.Data;

                _driveId = data.DefaultDriveId;

                if (!string.IsNullOrWhiteSpace(data.BackupDriveId))
                {
                    _driveId = data.BackupDriveId;
                }
                else if (!string.IsNullOrWhiteSpace(data.ResourceDriveId))
                {
                    _driveId = data.ResourceDriveId;
                }
            }
        }

        /// <summary>
        /// 初始化备份目录
        /// </summary>
        /// <returns></returns>
        public void InitBackupPath()
        {
            // 首先加载根目录结构
            // 并计算需要保存的目录
            // 计算/创建备份文件夹
            // 如果备份文件夹不存在
            var saveRootSubPaths = _path.Split('/').Select(c => c.Trim().Trim('/')).Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
            var searchParentFileId = "root";
            foreach (var subPath in saveRootSubPaths)
            {
                var request = new RestRequest("/adrive/v1.0/openFile/search", Method.POST);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", $"Bearer {AccessToken}");

                object body = new
                {
                    drive_id = _driveId,
                    query = $"parent_file_id='{searchParentFileId}' and type = 'folder' and name = '{subPath}'"
                };
                request.AddJsonBody(body);
                var response = _apiClient.Execute<AliyunFileList>(request);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (response.Data != null)
                    {
                        var okPath = response.Data.Items.FirstOrDefault(x => x.Name == subPath && x.Type == "folder" && x.ParentFileId == searchParentFileId);
                        if (okPath == null)
                        {
                            // 未找到目录
                            searchParentFileId = CreateFolder(subPath, searchParentFileId);
                        }
                        else
                        {
                            if (searchParentFileId == "root")
                            {
                                // 当前目录在根路径
                                // /{当前路径}/
                                _driveFolders.TryAdd($"{okPath.Name}".TrimPath(), okPath);
                            }
                            else
                            {
                                // 计算父级路径
                                var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == searchParentFileId).First();
                                var path = $"{parent.Key}/{okPath.Name}".TrimPath();

                                // /{父级路径}/{当前路径}/
                                _driveFolders.TryAdd(path, okPath);
                            }

                            searchParentFileId = okPath.FileId;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 创建文件夹（同名不覆盖）
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="parentId"></param>
        /// <returns></returns>
        public string CreateFolder(string filePath, string parentId)
        {
            var name = AliyunDriveHelper.EncodeFileName(filePath);

            try
            {
                // 判断是否需要创建文件夹
                if (parentId == "root")
                {
                    // 如果是根目录
                    var path = $"{name}".TrimPath();
                    if (_driveFolders.ContainsKey(path))
                    {
                        return _driveFolders[path].FileId;
                    }
                }
                else
                {
                    // 如果是子目录
                    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == parentId).First();
                    var path = $"{parent.Key}/{name}".TrimPath();
                    if (_driveFolders.ContainsKey(path))
                    {
                        return _driveFolders[path].FileId;
                    }
                }

                // v1 https://openapi.alipan.com/adrive/v1.0/openFile/create
                // v2 https://api.aliyundrive.com/adrive/v2/file/createWithFolders
                var request = new RestRequest("/adrive/v1.0/openFile/create", Method.POST);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", $"Bearer {AccessToken}");

                var body = new
                {
                    drive_id = _driveId,
                    parent_file_id = parentId,
                    name = name,
                    type = "folder",
                    check_name_mode = "refuse", // 同名不创建
                };

                request.AddJsonBody(body);
                var response = _apiClient.Execute<AliyunDriveFileItem>(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var data = response.Data;
                    data.Name = data.FileName;

                    if (parentId == "root")
                    {
                        // 当前目录在根路径
                        // /{当前路径}/
                        _driveFolders.TryAdd($"{data.Name}".TrimPath(), data);
                    }
                    else
                    {
                        // 计算父级路径
                        var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == parentId).First();
                        var path = $"{parent.Key}/{data.Name}".TrimPath();

                        // /{父级路径}/{当前路径}/
                        _driveFolders.TryAdd(path, data);
                    }

                    return data.FileId;
                }

                throw response.ErrorException;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 加载备份路径云盘文件
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="limit"></param>
        public void SearchAllFilesAsync(string driveId, int limit = 100)
        {
            try
            {
                var allItems = new List<AliyunDriveFileItem>();
                var marker = "";
                do
                {
                    var request = new RestRequest("/adrive/v1.0/openFile/search", Method.POST);
                    request.AddHeader("Content-Type", "application/json");
                    request.AddHeader("Authorization", $"Bearer {AccessToken}");
                    var body = new
                    {
                        drive_id = driveId,
                        limit = limit,
                        marker = marker,
                        query = ""
                    };
                    request.AddJsonBody(body);
                    var response = _apiClient.Execute<AliyunFileList>(request);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (response.Data?.Items.Count > 0)
                        {
                            allItems.AddRange(response.Data?.Items);
                        }
                        marker = response.Data.NextMarker;
                    }
                    else
                    {
                        throw response.ErrorException;
                    }

                    //// 等待 250ms 以遵守限流策略
                    //if (sw.ElapsedMilliseconds < _listRequestInterval)
                    //    await Task.Delay((int)(_listRequestInterval - sw.ElapsedMilliseconds));
                } while (!string.IsNullOrEmpty(marker));

                // 先加载文件夹
                LoadPath();

                void LoadPath(string parentFileId = "root")
                {
                    foreach (var item in allItems.Where(c => c.IsFolder).Where(c => c.ParentFileId == parentFileId))
                    {
                        // 如果是文件夹，则递归获取子文件列表
                        if (item.Type == "folder")
                        {
                            var keyPath = "";

                            // 如果是根目录
                            if (item.ParentFileId == "root")
                            {
                                keyPath = $"{item.Name}".TrimPath();
                            }
                            else
                            {
                                var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First();
                                keyPath = $"{parent.Key}/{item.Name}".TrimPath();
                            }

                            if (string.IsNullOrWhiteSpace(keyPath))
                            {
                                throw new Exception("路径异常");
                            }

                            // 路径必须符合根路径，否则跳过
                            var rootPath = _path.TrimPath();
                            if (keyPath == rootPath || keyPath.StartsWith($"{rootPath}/"))
                            {
                                _driveFolders.TryAdd(keyPath, item);

                                LoadPath(item.FileId);
                            }
                        }
                    }
                }

                // 再加载列表
                foreach (var item in allItems.Where(c => c.IsFile))
                {
                    // 如果是文件夹，则递归获取子文件列表
                    if (item.Type == "folder")
                    {
                        //// 如果是根目录
                        //if (item.ParentFileId == "root")
                        //{
                        //    _driveFolders.TryAdd($"{item.Name}".TrimPath(), item);
                        //}
                        //else
                        //{
                        //    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                        //    _driveFolders.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        //}
                    }
                    else
                    {
                        // 文件必须在备份路径中
                        var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(parent.Key))
                        {
                            _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);

                            //_log.LogInformation($"云盘文件加载中，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹，{item.Name}");
                        }
                        else
                        {
                        }

                        //// 如果是根目录的文件
                        //if (item.ParentFileId == "root")
                        //{
                        //    _driveFiles.TryAdd($"{item.Name}".TrimPath(), item);
                        //}
                        //else
                        //{
                        //    // 构建文件路径作为字典的键
                        //    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                        //    _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        //}
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            //_log.LogInformation($"云盘文件加载完成，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹。");
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            await UploadFile(remotename, stream);
        }
    }
}