using Duplicati.Library.Backend.OpenStack;
using Duplicati.Library.Backend.Rapidgator.Model;
using Duplicati.Library.Backend.Rapidgator.Models;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Duplicati.Library.Backend.Rapidgator
{
    public class RapidgatorBackend : IStreamingBackend, IBackend, IDynamicModule, IDisposable
    {
        private const string API_URL = "https://rapidgator.net/api/v2/";
        private readonly NetworkCredential? _userInfo;
        private readonly string _folder;
        private readonly HttpClient _httpClient;
        private LoginResponse? _loginResponse;
        private readonly RapidgatorHttpClientHelper _clientHelper;
        private string? _folderId;

        public RapidgatorBackend()
        {
            _folder = string.Empty;
            _httpClient = null!;
            _clientHelper = null!;
        }

        public RapidgatorBackend(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);
            AuthOptionsHelper.AuthOptions authOptions = AuthOptionsHelper.Parse(options, uri);
            if (authOptions.HasUsername)
            {
                _userInfo = new NetworkCredential()
                {
                    UserName = authOptions.Username
                };
                if (authOptions.HasPassword)
                    _userInfo.Password = authOptions.Password;
            }
            _folder = uri.HostAndPath ?? "";
            _httpClient = HttpClientHelper.CreateClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
            _clientHelper = new RapidgatorHttpClientHelper(this, _httpClient);
        }

        public string DisplayName => Strings.Rapidgator.DisplayName;

        public string Description => Strings.Rapidgator.Description;

        public string ProtocolKey => "rapidgator";

        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            // Ensure we have the target folder id
            string folderId = await GetFolderIdAsync(true, _folder);

            int currentPage = 1;
            while (!cancelToken.IsCancellationRequested)
            {
                string url = $"{API_URL}folder/content?folder_id={folderId}&page={currentPage}&per_page=100";
                var response = await _clientHelper.GetJsonDataAsync<FolderInfoResponse>(url, cancelToken);

                if (response == null || response.Status != HttpStatusCode.OK || response.Response?.Folder == null)
                    throw new Exception("Failed to retrieve folder-content info!");

                var folder = response.Response.Folder;

                if (folder.Files != null)
                {
                    foreach (var file in folder.Files)
                    {
                        yield return file;
                    }
                }

                if (response.Response.Pager == null || response.Response.Pager.Current >= response.Response.Pager.Total)
                    break;

                currentPage++;
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await using var fs = File.OpenRead(filename);
            await PutAsync(remotename, fs, cancelToken);
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            string folderId = await GetFolderIdAsync(true, _folder);
            var fileInfos = await GetFileInfosAsync(stream);

            // Wrap the timeout-observed stream in a non-disposing wrapper so
            // disposing the MultipartFormDataContent / StreamContent on failure
            // doesn't close the original stream and allows retries.
            var nonDisposing = new NonDisposableStream(stream);

            UploadMultipartResponse uploadMultipartResponse = null!;
            string uploadId = null!;
            int currentTry = 0;
            await RetryHelper.Retry(async () =>
            {
                currentTry++;

                if (nonDisposing.CanSeek)
                {
                    try
                    {
                        nonDisposing.Position = 0;
                    }
                    catch
                    {
                    }
                }

                string url = $"{API_URL}file/upload?folder_id={folderId}&multipart=true&name={WebUtility.UrlEncode(remotename)}&hash={WebUtility.UrlEncode(fileInfos.Md5Hash)}&size={WebUtility.UrlEncode(fileInfos.FileSize.ToString())}";
                UploadResponse uploadRequestResponse = await _clientHelper.GetJsonDataAsync<UploadResponse>(url, cancelToken);
                if (uploadRequestResponse == null || uploadRequestResponse.Response.Upload.State != 0)
                    throw new Exception("Failed to get upload URL for uploading File!");

                uploadId = uploadRequestResponse.Response.Upload.UploadId;
                string uploadUrl = uploadRequestResponse.Response.Upload.Url;

                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StreamContent(nonDisposing), "file", remotename);
                    uploadMultipartResponse = await _clientHelper.PostMultipartAndGetJsonDataAsync<UploadMultipartResponse>(uploadUrl, cancelToken, content);
                    if (uploadMultipartResponse.Status == HttpStatusCode.InternalServerError)
                        throw new Exception("Failed to upload file (InternalServerError on Rapidgator)");
                }
            }, 5, TimeSpan.FromMilliseconds(10.0), cancelToken);

            RapidgatorUpload uploadInfo = uploadMultipartResponse.Response;
            if (uploadInfo == null)
                throw new Exception("Failed to upload file!");

            while (uploadInfo.State == 1)
            {
                await Task.Delay(100, cancelToken);
                string uploadUrl = $"{API_URL}file/upload_info?upload_id={uploadId}";
                UploadResponse uploadInfoResponse = await _clientHelper.GetJsonDataAsync<UploadResponse>(uploadUrl, cancelToken);
                uploadInfo = uploadInfoResponse.Response.Upload;
            }

            if (uploadInfo.State != 2)
                throw new Exception("Failed to upload file!");
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await using var fs = File.Create(filename);
            await GetAsync(remotename, fs, cancelToken);
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            RapidgatorFile? found = null;
            await foreach (var entry in ListAsync(cancelToken).WithCancellation(cancelToken))
            {
                if (entry.Name == remotename)
                {
                    found = entry as RapidgatorFile;
                    break;
                }
            }

            if (found == null)
                throw new FileMissingException();

            string url = $"{API_URL}file/download?file_id={found.FileId}";
            DownloadUrlResponse response = await _clientHelper.GetJsonDataAsync<DownloadUrlResponse>(url, cancelToken);
            if (response.Status != HttpStatusCode.OK)
                throw new FileMissingException();

            using var downloadStream = await _httpClient.GetStreamAsync(response.Response.DownloadUrl);
            await downloadStream.CopyToAsync(stream, cancellationToken: cancelToken);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            RapidgatorFile? found = null;
            await foreach (var entry in ListAsync(cancelToken).WithCancellation(cancelToken))
            {
                if (entry.Name == remotename)
                {
                    found = entry as RapidgatorFile;
                    break;
                }
            }

            if (found == null)
                throw new FileMissingException();

            string url = $"{API_URL}file/delete?file_id={found.FileId}";
            DeleteResultResponse response = await _clientHelper.GetJsonDataAsync<DeleteResultResponse>(url, cancelToken);
            if (response.Response.Result.Success != 1)
                throw new Exception("Failed to delete file!");
        }

        public async Task TestAsync(CancellationToken cancelToken)
        {
            string folderIdAsync = await GetFolderIdAsync(false, _folder);
        }

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            string folderIdAsync = await GetFolderIdAsync(true, _folder);
        }

        public IList<ICommandLineArgument> SupportedCommands =>
        [
            .. AuthOptionsHelper.GetOptions()
        ];

        public void Dispose()
        {
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        {
            return Task.FromResult(new[] { "rapidgator.net" });
        }

        private async Task<string> GetFolderIdAsync(
            bool createFolders,
            string folderPath,
            string? currentFolderId = null)
        {
            if (_folderId != null)
                return _folderId;
            string[] folderNames = folderPath.Split('/');
            string url = $"{API_URL}folder/info";
            if (currentFolderId != null)
                url = $"{url}?folder_id={currentFolderId}";
            FolderInfoResponse response = await _clientHelper.GetJsonDataAsync<FolderInfoResponse>(url, CancellationToken.None);
            if (response.Status != HttpStatusCode.OK || response.Response?.Folder == null)
                throw new Exception("Failed to retrieve folder info");
            string seekingFolder = folderNames[0];
            if (string.IsNullOrWhiteSpace(seekingFolder))
                return response.Response.Folder.FolderId;
            List<RapidgatorFolder> folders = response.Response.Folder.Folders;
            RapidgatorFolder? foundFolder = folders != null ? folders.FirstOrDefault<RapidgatorFolder>(f => f.Name == seekingFolder) : null;
            if (foundFolder == null)
            {
                if (!createFolders)
                    throw new FolderMissingException();
                foundFolder = await CreateFolderAsync(response.Response.Folder.FolderId, seekingFolder);
            }
            if (folderNames.Length > 1)
            {
                string folderIdAsync = await GetFolderIdAsync(createFolders, string.Join('/', ((IEnumerable<string>)folderNames).Skip(1)), foundFolder.FolderId);
                return folderIdAsync;
            }
            _folderId = foundFolder.FolderId;
            return _folderId;
        }

        private async Task<RapidgatorFolder> CreateFolderAsync(
            string? currentFolderId,
            string seekingFolder)
        {
            string url = "https://rapidgator.net/api/v2/folder/create?name=" + seekingFolder;
            if (currentFolderId != null)
                url = $"{url}&folder_id={currentFolderId}";
            FolderInfoResponse response = await _clientHelper.GetJsonDataAsync<FolderInfoResponse>(url, CancellationToken.None);
            if (response.Status != HttpStatusCode.OK || response.Response?.Folder == null)
                throw new Exception("Failed to retrieve folder info");
            RapidgatorFolder folder = response.Response.Folder;
            return folder;
        }

        internal async Task<string> GetAccessTokenAsync(CancellationToken cancelToken)
        {
            if (_loginResponse == null)
                await RequestLoginResponseAsync(cancelToken);
            return _loginResponse.Response.Token;
        }

        private async Task RequestLoginResponseAsync(CancellationToken cancelToken)
        {
            LoginResponse resp = await _httpClient.GetFromJsonAsync<LoginResponse>($"{API_URL}user/login?login={WebUtility.UrlEncode(_userInfo.UserName)}&password={WebUtility.UrlEncode(_userInfo.Password)}", cancelToken);
            if (resp == null || resp.Status != HttpStatusCode.OK || string.IsNullOrWhiteSpace(resp.Response?.Token))
                throw new Exception("Failed to authenticate with Rapidgator");
            _loginResponse = resp;
        }

        private async Task<(string Md5Hash, long FileSize)> GetFileInfosAsync(Stream stream)
        {
            using (MD5 md5 = MD5.Create())
            {
                long totalBytes = 0;
                byte[] buffer = new byte[81920];
                long? originalPosition = null;
                if (stream.CanSeek)
                {
                    originalPosition = stream.Position;
                    stream.Position = 0L;
                }
                try
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                        totalBytes += bytesRead;
                    }
                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    string hash = BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
                    return (hash, totalBytes);
                }
                finally
                {
                    if (originalPosition.HasValue)
                        stream.Position = originalPosition.Value;
                }
            }
        }
    }
}
