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
    /// Aliyun Drive
    /// Aliyun Drive is a fast, do not disturb, safe enough, easy to share the personal network disk, you are welcome to experience.
    /// 
    /// 阿里云盘
    /// 不限制上传速度、不限制下载速度
    /// 支持秒传
    /// https://www.alipan.com/
    /// https://www.yuque.com/aliyundrive/
    /// <see cref=""/>
    /// </summary>
    public class AliyunDrive : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        /// <summary>
        /// 日志
        /// Log
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<AliyunDrive>();

        /// <summary>
        /// 授权码 KEY
        /// Authorization Code Key
        /// </summary>
        private const string AUTHORIZATION_CODE = "aliyundrive-authorization-code";

        /// <summary>
        /// 授权码令牌
        /// Authorization Code Token
        /// </summary>
        private readonly string _token;

        /// <summary>
        /// 存储根目录
        /// Storage root directory
        /// File directory, suggested /apps/sync, note: the root path apps corresponds to "My Applications" folder in the drive
        /// </summary>
        private readonly string _path;

        private readonly RestClient _apiClient;
        private readonly HttpClient _uploadHttpClient;
        private readonly IMemoryCache _cache;
        private const string TOEKN_KEY = "TOKEN";

        // 备份盘 ID
        // Backup Drive ID
        private string _driveId;

        /// <summary>
        /// 所有云盘文件
        /// All cloud drive files
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFiles = new ConcurrentDictionary<string, AliyunDriveFileItem>();

        /// <summary>
        /// 所有云盘文件夹
        /// All cloud drive folders
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFolders = new ConcurrentDictionary<string, AliyunDriveFileItem>();

        // 是否已加载列表
        // Whether the list is loaded
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

            _apiClient = new RestClient(ProviderApiHelper.ALIYUNDRIVE_API_HOST);

            // 上传请求
            // 上传链接最大有效 1 小时
            // 设置 45 分钟超时
            // 在 HttpClient 中，一旦发送了第一个请求，就不能再更改其配置属性，如超时时间 (Timeout)。
            // 这是因为 HttpClient 被设计为可重用的，它的属性设置在第一个请求发出之后就被固定下来。

            // Upload request
            // Upload links are valid for up to 1 hour
            // Set a 45-minute timeout
            // In HttpClient, once the first request has been sent, you cannot change its configuration properties, such as Timeout.
            // This is because HttpClient is designed to be reusable, and its property Settings are fixed after the first request is made.

            _uploadHttpClient = new HttpClient();
            _uploadHttpClient.Timeout = TimeSpan.FromMinutes(45);

            _cache = new MemoryCache(new MemoryCacheOptions());

            Initialize();
        }

        public IEnumerable<IFileEntry> List()
        {
            var saveParentPath = _path.TrimPath();
            if (!_driveFolders.ContainsKey(saveParentPath))
            {
                throw new Exception("Folder creation failure");
            }

            var saveParentFileId = _driveFolders[saveParentPath].FileId;

            FetchAllFilesAsync(_driveId, saveParentFileId);

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
                throw new Exception("file does not exist");
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
                throw new Exception("file does not exist");
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
        /// Upload File
        /// </summary>
        /// <param name="remotename">Remote file name.</param>
        /// <param name="stream">File stream to be uploaded.</param>
        /// <param name="cancelToken">Cancellation token.</param>
        /// <returns></returns>
        public async Task UploadFile(string remotename, Stream stream, bool needPreHash = true)
        {
            try
            {
                // 文件名
                // File name
                var name = AliyunDriveHelper.EncodeFileName(remotename);

                // 计算保存存储目录
                // Calculate the storage save directory
                var saveParentPath = _path.TrimPath();
                if (!_driveFolders.ContainsKey(saveParentPath))
                {
                    throw new Exception("文件夹创建失败"); // Folder creation failed
                }

                // 存储目录 ID
                // Storage directory ID
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
                    check_name_mode = "ignore", // Overwrite file mode
                    size = fileSize
                };

                // 是否进行秒传处理
                // Whether to perform rapid upload
                var isRapidUpload = false;

                // 开启秒传
                // If the file is > 10kb then perform rapid upload calculation, otherwise do not
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

                    var upload_url = dataObj?.PartInfoList?.FirstOrDefault()?.UploadUrl;

                    stream.Seek(0, SeekOrigin.Begin);
                    var content = new StreamContent(stream);

                    var uploadRes = await _uploadHttpClient.PutAsync(upload_url, content);
                    if (!uploadRes.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
                    }

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
        /// Fetch file list (Rate limited to 4 QPS)
        /// </summary>
        /// <param name="driveId">Drive ID.</param>
        /// <param name="parentFileId">Parent File ID.</param>
        /// <param name="limit">Limit.</param>
        /// <param name="orderBy">Order by.</param>
        /// <param name="orderDirection">Order direction.</param>
        /// <param name="category">Category.</param>
        /// <param name="type">Type.</param>
        /// <param name="saveRootPath">Backup save directory, if matched return immediately.</param>
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
        /// Fetch and download remote file
        /// </summary>
        /// <param name="remotename">Remote file name.</param>
        /// <param name="fs">File stream to download to.</param>
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
        /// Folder creation
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
                throw new Exception("Folder creation failure");
            }
        }

        /// <summary>
        /// 获取当前有效的访问令牌
        /// Get the current valid access token
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
                    // Expire 5 minutes in advance
                    c.SetAbsoluteExpiration(TimeSpan.FromSeconds(secs - 60 * 5));

                    return token;
                });
            }
        }

        /// <summary>
        /// 初始化令牌
        /// Initialize token
        /// </summary>
        /// <returns></returns>
        public string InitToken()
        {
            // 重新获取令牌
            // Retrieve token again
            var data = ProviderApiHelper.RefreshToken(_token);
            if (data != null)
            {
                return data.AccessToken;
            }

            throw new Exception("Failed to initialize access token"); // Failed to initialize access token
        }

        /// <summary>
        /// 初始化作业（路径、云盘信息等）
        /// Initialize job (path, cloud drive information, etc.)
        /// </summary>
        /// <returns></returns>
        private void Initialize()
        {
            // 获取云盘信息
            // Get cloud drive information
            InitAliyunDriveInfo();

            // 初始化备份目录
            // Initialize backup directory
            InitBackupPath();
        }

        /// <summary>
        /// 获取用户 drive 信息
        /// Get user drive information
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
        /// Initialize backup directory
        /// </summary>
        /// <returns></returns>
        public void InitBackupPath()
        {
            // 首先加载根目录结构
            // 并计算需要保存的目录
            // Calculate and create the backup folder
            // If the backup folder does not exist
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
                            // Directory not found
                            searchParentFileId = CreateFolder(subPath, searchParentFileId);
                        }
                        else
                        {
                            if (searchParentFileId == "root")
                            {
                                // 当前目录在根路径
                                // Current directory is at the root path
                                _driveFolders.TryAdd($"{okPath.Name}".TrimPath(), okPath);
                            }
                            else
                            {
                                // 计算父级路径
                                // Calculate parent path
                                var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == searchParentFileId).First();
                                _driveFolders.TryAdd($"{parent.Key}/{okPath.Name}".TrimPath(), okPath);
                            }

                            searchParentFileId = okPath.FileId;
                        }
                    }
                }
            }
        }


        /// <summary>
        /// 创建文件夹（同名不覆盖）
        /// Create folder (do not overwrite if a folder with the same name exists)
        /// </summary>
        /// <param name="filePath">File path.</param>
        /// <param name="parentId">Parent ID.</param>
        /// <returns>File ID of the created folder.</returns>
        public string CreateFolder(string filePath, string parentId)
        {
            var name = AliyunDriveHelper.EncodeFileName(filePath);

            try
            {
                // 判断是否需要创建文件夹
                // Determine if a folder needs to be created
                if (parentId == "root")
                {
                    // 如果是根目录
                    // If it is the root directory
                    var path = $"{name}".TrimPath();
                    if (_driveFolders.ContainsKey(path))
                    {
                        return _driveFolders[path].FileId;
                    }
                }
                else
                {
                    // 如果是子目录
                    // If it is a subdirectory
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
                    check_name_mode = "refuse", // 同名不创建 (Do not create if a folder with the same name exists)
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
                        // Current directory is at the root path
                        // /{当前路径}/
                        _driveFolders.TryAdd($"{data.Name}".TrimPath(), data);
                    }
                    else
                    {
                        // 计算父级路径
                        // Calculate parent path
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

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            await UploadFile(remotename, stream);
        }
    }
}