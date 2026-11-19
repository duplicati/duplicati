// Copyright (C) 2026, The Duplicati Team
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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using Duplicati.StreamUtil;

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Backend.Box
{
    public class BoxBackend : IStreamingBackend, IRenameEnabledBackend, IFolderEnabledBackend, IFolderDeleteEnabledBackend
    {
        private static readonly string TOKEN_URL = AuthIdOptionsHelper.GetOAuthLoginUrl("box.com", null);
        private const string REALLY_DELETE_OPTION = "box-delete-from-trash";

        private const string BOX_API_URL = "https://api.box.com/2.0";
        private const string BOX_UPLOAD_URL = "https://upload.box.com/api/2.0/files";

        private const int PAGE_SIZE = 200;

        /// <summary>
        /// The Box.com error code reported when the name is already used by an item
        /// </summary>
        private const string NAME_IN_USE_CODE = "item_name_in_use";
        /// <summary>
        /// The number of times to look for a folder that Box reports as existing
        /// </summary>
        private const int CONFLICT_LOOKUP_ATTEMPTS = 3;
        /// <summary>
        /// The delay to use between the lookups for a folder that Box reports as existing
        /// </summary>
        private static readonly TimeSpan CONFLICT_LOOKUP_DELAY = TimeSpan.FromSeconds(1);

        private readonly BoxHelper _oAuthHelper;
        private readonly string _path;
        private readonly bool _deleteFromTrash;

        private string? _currentFolder;
        /// <summary>
        /// The ids of the files of one folder, by name. Box addresses a file by its id, and
        /// two folders can each have a file of the same name, so the cache is only used for
        /// the folder it holds (<see cref="_fileCacheFolderPath"/>). Box does not allow two
        /// items in a folder whose names differ only in case, so names are matched that way.
        /// </summary>
        private readonly Dictionary<string, string> _fileCache = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// The absolute path of the folder the file cache holds, or null when it holds none.
        /// It is set once the folder has been listed to the end.
        /// </summary>
        private string? _fileCacheFolderPath;
        /// <summary>
        /// The id of the folder the file cache holds
        /// </summary>
        private string? _fileCacheFolderId;
        private readonly TimeoutOptionsHelper.Timeouts _timeouts;

        private class BoxHelper : OAuthHelperHttpClient
        {
            private readonly TimeoutOptionsHelper.Timeouts _timeouts;
            public BoxHelper(AuthIdOptionsHelper.AuthIdOptions authId, TimeoutOptionsHelper.Timeouts timeouts, HttpClient? httpClient = null)
                : base(authId.AuthId, "box.com", authId.OAuthUrl, httpClient)
            {
                AutoAuthHeader = true;
                _timeouts = timeouts;
                _httpClient.Timeout = Timeout.InfiniteTimeSpan;
            }
            public override async Task AttemptParseAndThrowExceptionAsync(Exception ex, HttpResponseMessage? responseContext, CancellationToken cancellationToken)
            {
                if (ex is not HttpRequestException || responseContext == null)
                    return;

                if (responseContext is { StatusCode: HttpStatusCode.TooManyRequests })
                    throw new TooManyRequestException(responseContext.Headers.RetryAfter);

                await using var stream = await responseContext.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                var rawData = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, ct => reader.ReadToEndAsync(ct)).ConfigureAwait(false);
                ErrorResponse? errorResponse = null;
                try { errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(rawData); }
                catch { }

                errorResponse ??= new ErrorResponse { Status = (int)responseContext.StatusCode, Code = "Unknown", Message = rawData };
                throw new BoxException(errorResponse, ex);
            }

            public async Task<T> PutAndGetJsonDataAsync<T>(string url, object item, CancellationToken cancellationToken)
            {
                var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));

                return await GetJsonDataAsync<T>(
                    url,
                    cancellationToken,
                    request =>
                    {
                        request.Method = HttpMethod.Put;
                        request.Content = new ByteArrayContent(data);
                        request.Content.Headers.Add("Content-Length", data.Length.ToString());
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    }
                ).ConfigureAwait(false);
            }
        }

        public BoxBackend()
        {
            _oAuthHelper = null!;
            _path = null!;
            _timeouts = null!;
        }

        public BoxBackend(string url, Dictionary<string, string?> options)
            : this(url, options, null)
        {
        }

        /// <summary>
        /// Creates a backend that uses the supplied <see cref="HttpClient"/>,
        /// so the Box API responses can be stubbed in tests
        /// </summary>
        /// <param name="url">The backend url</param>
        /// <param name="options">The options to use</param>
        /// <param name="httpClient">The client to use, or null to create one</param>
        internal BoxBackend(string url, Dictionary<string, string?> options, HttpClient? httpClient)
        {
            var uri = new RelaxedUri(url);

            _path = Util.AppendDirSeparator(uri.HostAndPath, "/");

            var authid = AuthIdOptionsHelper.Parse(options)
                .RequireCredentials(TOKEN_URL);

            _deleteFromTrash = Utility.Utility.ParseBoolOption(options, REALLY_DELETE_OPTION);
            _timeouts = TimeoutOptionsHelper.Parse(options);

            _oAuthHelper = new BoxHelper(authid, _timeouts, httpClient);

        }

        private async Task<string> GetCurrentFolderWithCacheAsync(CancellationToken cancelToken)
        {
            if (_currentFolder == null)
                return await GetCurrentFolderAsync(false, cancelToken).ConfigureAwait(false);

            return _currentFolder;
        }

        private async Task<string> GetCurrentFolderAsync(bool create, CancellationToken cancelToken)
        {
            var id = await GetFolderIdAsync(_path, create, cancelToken).ConfigureAwait(false);
            return _currentFolder = id;
        }

        private async Task<string> GetFolderIdAsync(string path, bool create, CancellationToken cancelToken)
        {
            var parentid = "0";

            foreach (var p in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var el = await FindFolderAsync(parentid, p, cancelToken).ConfigureAwait(false);
                if (el == null)
                {
                    if (!create)
                        throw new FolderMissingException();

                    try
                    {
                        el = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => _oAuthHelper.PostAndGetJsonDataAsync<ListFolderResponse>(
                            $"{BOX_API_URL}/folders",
                            new CreateItemRequest
                            {
                                Name = p,
                                Parent = new IDReference { ID = parentid }
                            },
                            ct
                        )).ConfigureAwait(false);
                    }
                    catch (BoxException bex) when (bex.Error.Status == (int)HttpStatusCode.Conflict && bex.Error.Code == NAME_IN_USE_CODE)
                    {
                        // The name is taken, so the folder is there even though the
                        // listing did not report it
                        el = await ResolveExistingFolderAsync(parentid, p, bex, cancelToken).ConfigureAwait(false);
                        if (el == null)
                            throw;
                    }
                }

                parentid = el.ID;
                if (string.IsNullOrWhiteSpace(parentid))
                    throw new InvalidDataException($"Invalid folder ID for {p} in {path}");
            }

            return parentid;
        }

        /// <summary>
        /// Finds a folder by name in the given folder
        /// </summary>
        /// <param name="parentid">The id of the folder to look in</param>
        /// <param name="name">The name of the folder to find</param>
        /// <param name="cancelToken">The cancellation token to use</param>
        /// <returns>The folder, or null if there is no such folder</returns>
        private async Task<MiniFolder?> FindFolderAsync(string parentid, string name, CancellationToken cancelToken)
            // Box.com does not allow two items in a folder to have names that differ
            // only in casing, and reports the name as used when they do, so the names
            // are compared the same way here
            => (MiniFolder?)await PagedFileListResponse(parentid, true, cancelToken)
                .FirstOrDefaultAsync(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase), cancellationToken: cancelToken)
                .ConfigureAwait(false);

        /// <summary>
        /// Finds a folder that Box reports as already using the requested name,
        /// either from the conflicting items in the error or by listing the parent again
        /// </summary>
        /// <param name="parentid">The id of the folder to look in</param>
        /// <param name="name">The name of the folder to find</param>
        /// <param name="error">The error reporting the name as taken</param>
        /// <param name="cancelToken">The cancellation token to use</param>
        /// <returns>The folder, or null if it could not be found</returns>
        private async Task<MiniFolder?> ResolveExistingFolderAsync(string parentid, string name, BoxException error, CancellationToken cancelToken)
        {
            // Box reports the canonical name, which can differ in case from the requested one
            var conflicts = error.Error.GetConflictingItems()
                .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var conflictingFolder = conflicts.FirstOrDefault(x => x.Type == "folder" && !string.IsNullOrWhiteSpace(x.ID));
            if (conflictingFolder != null)
                return conflictingFolder;

            // Something that is not a folder uses the name, so no folder can have
            // it and listing again will not produce one
            if (conflicts.Count > 0)
                return null;

            // Only the name was reported as taken, so look for the folder again,
            // allowing for the listing to catch up
            for (var attempt = 1; attempt <= CONFLICT_LOOKUP_ATTEMPTS; attempt++)
            {
                if (attempt > 1)
                    await Task.Delay(Utility.Utility.GetRetryDelay(CONFLICT_LOOKUP_DELAY, attempt - 1, true), cancelToken).ConfigureAwait(false);

                var el = await FindFolderAsync(parentid, name, cancelToken).ConfigureAwait(false);
                if (el != null)
                    return el;
            }

            return null;
        }

        private IFileEntry ParseEntry(FileEntity n)
        {
            var fe = new FileEntry(n.Name);
            if (n.Type == "folder")
            {
                fe.IsFolder = true;
                if (!fe.Name.EndsWith("/"))
                    fe.Name += "/";
            }
            else
            {
                fe.IsFolder = false;
                fe.Size = n.Size;
            }

            if (n.ModifiedAt != default)
                fe.LastModification = fe.LastAccess = n.ModifiedAt.ToUniversalTime();

            return fe;
        }

        /// <summary>
        /// Gets the absolute path for a given relative path
        /// </summary>
        /// <param name="path">The relative path local path, with backslashes or forward slashes</param>
        /// <returns>The absolute URL path</returns>
        private string GetAbsolutePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return _path;

            var p = path.Replace(Path.DirectorySeparatorChar, '/').Trim('/');
            if (string.IsNullOrWhiteSpace(_path) || _path == "/")
                return "/" + p;
            else
                return Util.AppendDirSeparator(_path, "/") + p;
        }

        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var fullPath = GetAbsolutePath(path);
            string folderId;
            try
            {
                folderId = await GetFolderIdAsync(fullPath, false, cancellationToken).ConfigureAwait(false);
            }
            catch (FolderMissingException)
            {
                yield break;
            }

            await foreach (var n in ListAndCacheAsync(NormalizeFolderPath(fullPath), folderId, cancellationToken).ConfigureAwait(false))
            {
                yield return ParseEntry(n);
            }
        }

        public async Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
        {
            var parts = GetAbsolutePath(path).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return new FileEntry(string.Empty) { IsFolder = true };

            var parentPath = string.Join("/", parts.Take(parts.Length - 1));
            var name = parts.Last();

            string parentId;
            try
            {
                parentId = await GetFolderIdAsync(parentPath, false, cancellationToken).ConfigureAwait(false);
            }
            catch (FolderMissingException)
            {
                return null;
            }

            var item = await PagedFileListResponse(parentId, false, cancellationToken)
                .FirstOrDefaultAsync(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase), cancellationToken)
                .ConfigureAwait(false);

            if (item == null) return null;

            return ParseEntry(item);
        }

        /// <summary>
        /// The absolute path of the backend folder, as the file cache names a folder
        /// </summary>
        private string RootFolderPath => NormalizeFolderPath(_path);

        /// <summary>
        /// Brings the absolute path of a folder into the form the file cache names a folder by
        /// </summary>
        /// <param name="path">The absolute path of the folder</param>
        /// <returns>The path without leading, trailing or repeated separators</returns>
        private static string NormalizeFolderPath(string path)
            => string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries));

        /// <summary>
        /// Splits a remote name into the folder the file is in and the name of the file.
        /// A name without a path is a file in the backend folder.
        /// </summary>
        /// <param name="remotename">The remote name, which may contain a path below the backend folder</param>
        /// <returns>The absolute path of the folder, and the name of the file in it</returns>
        private (string FolderPath, string Name) SplitRemoteName(string remotename)
        {
            if (string.IsNullOrWhiteSpace(remotename))
                throw new FileMissingException();

            if (!remotename.Contains('/'))
                return (RootFolderPath, remotename);

            var parts = GetAbsolutePath(remotename).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new FileMissingException();

            return (string.Join("/", parts[..^1]), parts[^1]);
        }

        /// <summary>
        /// Whether the file cache holds the given folder. Box matches names without regard
        /// to case, so two paths that differ only in case are the same folder.
        /// </summary>
        /// <param name="folderPath">The absolute path of the folder, normalized</param>
        /// <returns>True if the cache holds the folder</returns>
        private bool IsCachedFolder(string folderPath)
            => _fileCacheFolderPath != null && IsSameFolder(_fileCacheFolderPath, folderPath);

        /// <summary>
        /// Whether two normalized folder paths name the same Box folder
        /// </summary>
        /// <param name="a">The first path</param>
        /// <param name="b">The second path</param>
        /// <returns>True if the paths name the same folder</returns>
        private static bool IsSameFolder(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Empties the file cache, so the next file operation lists the folder again
        /// </summary>
        private void ClearFileCache()
        {
            _fileCache.Clear();
            _fileCacheFolderPath = null;
            _fileCacheFolderId = null;
        }

        /// <summary>
        /// Lists a folder and keeps the ids of its files in the file cache. The cache holds
        /// the folder once the listing has been read to the end; a listing that is not read
        /// to the end leaves the cache holding no folder.
        /// </summary>
        /// <param name="folderPath">The absolute path of the folder, normalized</param>
        /// <param name="folderId">The id of the folder</param>
        /// <param name="cancelToken">The cancellation token to use</param>
        /// <returns>The items in the folder</returns>
        private async IAsyncEnumerable<FileEntity> ListAndCacheAsync(string folderPath, string folderId, [EnumeratorCancellation] CancellationToken cancelToken)
        {
            ClearFileCache();

            await foreach (var f in PagedFileListResponse(folderId, false, cancelToken).ConfigureAwait(false))
            {
                if (f.Type == "file" && f.Name != null && f.ID != null)
                    _fileCache[f.Name] = f.ID;

                yield return f;
            }

            _fileCacheFolderPath = folderPath;
            _fileCacheFolderId = folderId;
        }

        /// <summary>
        /// Gets the id of a folder that has to exist
        /// </summary>
        /// <param name="folderPath">The absolute path of the folder, normalized</param>
        /// <param name="cancelToken">The cancellation token to use</param>
        /// <returns>The id of the folder</returns>
        private async Task<string> GetExistingFolderIdAsync(string folderPath, CancellationToken cancelToken)
        {
            // Finding a folder lists every folder on the way to it, so the folder
            // the file cache holds is not looked up again
            if (IsCachedFolder(folderPath) && _fileCacheFolderId != null)
                return _fileCacheFolderId;

            return folderPath == RootFolderPath
                ? await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false)
                : await GetFolderIdAsync(folderPath, false, cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Makes the file cache hold the files of a folder, by listing the folder
        /// </summary>
        /// <param name="folderPath">The absolute path of the folder, normalized</param>
        /// <param name="refresh">Whether the folder is listed again when the cache already holds it</param>
        /// <param name="cancelToken">The cancellation token to use</param>
        /// <returns>The id of the folder</returns>
        private async Task<string> LoadFolderAsync(string folderPath, bool refresh, CancellationToken cancelToken)
        {
            var folderId = await GetExistingFolderIdAsync(folderPath, cancelToken).ConfigureAwait(false);
            if (refresh || !IsCachedFolder(folderPath))
                await ListAndCacheAsync(folderPath, folderId, cancelToken).LastOrDefaultAsync(cancelToken).ConfigureAwait(false);

            return folderId;
        }

        private async Task<string> GetFileIdAsync(string name, CancellationToken cancelToken)
        {
            var (folderPath, fileName) = SplitRemoteName(name);

            if (IsCachedFolder(folderPath) && _fileCache.TryGetValue(fileName, out var cached))
                return cached;

            // The file can have been made after the folder was listed, so the folder
            // is listed again before the file is reported as missing
            await LoadFolderAsync(folderPath, true, cancelToken).ConfigureAwait(false);

            if (_fileCache.TryGetValue(fileName, out var id))
                return id;

            throw new FileMissingException();
        }

        private async IAsyncEnumerable<FileEntity> PagedFileListResponse(string parentid, bool onlyfolders, [EnumeratorCancellation] CancellationToken cancelToken)
        {
            var offset = 0;

            while (true)
            {
                var resp = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken, ct => _oAuthHelper.GetJsonDataAsync<ShortListResponse>($"{BOX_API_URL}/folders/{parentid}/items?limit={PAGE_SIZE}&offset={offset}&fields=name,size,modified_at", ct)).ConfigureAwait(false);

                if (resp.Entries == null || resp.Entries.Length == 0)
                    break;

                foreach (var f in resp.Entries)
                {
                    if (string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.ID))
                        continue;

                    // No sort order is requested, so a non-folder entry does not
                    // mean the remaining entries are non-folders as well
                    if (onlyfolders && f.Type != "folder")
                        continue;

                    yield return f;
                }

                // Fewer entries than requested may be returned while more remain,
                // so continue after the entries that were actually received
                offset += resp.Entries.Length;

                if (offset >= resp.TotalCount)
                    break;
            }
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            // A name with a path is a file in a folder below the backend folder. The
            // folder has to exist, as the backend folder has to.
            var (folderPath, fileName) = SplitRemoteName(remotename);
            var folderId = await LoadFolderAsync(folderPath, false, cancelToken).ConfigureAwait(false);
            var createreq = new CreateItemRequest
            {
                Name = fileName,
                Parent = new IDReference
                {
                    ID = folderId
                }
            };

            var existing = _fileCache.TryGetValue(fileName, out var existingId);

            var multipartForm = new MultipartFormDataContent();

            try
            {
                string url;

                if (existing)
                    url = $"{BOX_UPLOAD_URL}/{existingId}/content";
                else
                {
                    url = $"{BOX_UPLOAD_URL}/content";
                    multipartForm.Add(JsonContent.Create(createreq), "attributes");
                }

                using var timeoutStream = stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false, TimeoutMode.Gap);
                multipartForm.Add(new StreamContent(timeoutStream), "file", fileName);

                var res = (await _oAuthHelper.PostMultipartAndGetJsonDataAsync<FileList>(url, cancelToken, multipartForm)).Entries?.FirstOrDefault();
                if (res == null || string.IsNullOrWhiteSpace(res.ID))
                    throw new InvalidDataException("No file ID returned after upload");
                _fileCache[fileName] = res.ID;
            }
            catch
            {
                ClearFileCache();
                throw;
            }
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var fileId = await GetFileIdAsync(remotename, cancelToken).ConfigureAwait(false);
            using var request = await _oAuthHelper.CreateRequestAsync($"{BOX_API_URL}/files/{fileId}/content", HttpMethod.Get, cancelToken).ConfigureAwait(false);
            using var resp = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => _oAuthHelper.GetResponseAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);
            await using var responseStream = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => resp.Content.ReadAsStreamAsync(ct)).ConfigureAwait(false);
            using var ts = responseStream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false, TimeoutMode.Call);
            await Utility.Utility.CopyStreamAsync(ts, stream, cancelToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var currentFolder = await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false);
            await foreach (var n in ListAndCacheAsync(RootFolderPath, currentFolder, cancelToken).ConfigureAwait(false))
                yield return ParseEntry(n);
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await using FileStream fs = File.OpenRead(filename);
            await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await using FileStream fs = File.Create(filename);
            await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            var fileId = await GetFileIdAsync(remotename, cancelToken).ConfigureAwait(false);
            try
            {
                using (var request = await _oAuthHelper.CreateRequestAsync($"{BOX_API_URL}/files/{fileId}", HttpMethod.Delete, cancelToken).ConfigureAwait(false))
                using (var r = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => _oAuthHelper.GetResponseAsync(request, HttpCompletionOption.ResponseContentRead, ct)).ConfigureAwait(false))
                {
                }

                if (_deleteFromTrash)
                {
                    using (var request = await _oAuthHelper.CreateRequestAsync($"{BOX_API_URL}/files/{fileId}/trash", HttpMethod.Delete, cancelToken).ConfigureAwait(false))
                    using (var r = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => _oAuthHelper.GetResponseAsync(request, HttpCompletionOption.ResponseContentRead, ct)).ConfigureAwait(false))
                    {
                    }
                }

                // The cache holds the folder of the file, and the file is no longer in it
                _fileCache.Remove(SplitRemoteName(remotename).Name);
            }
            catch
            {
                ClearFileCache();
                throw;
            }
        }

        public async Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
        {
            var (oldFolderPath, oldFileName) = SplitRemoteName(oldname);
            var (newFolderPath, newFileName) = SplitRemoteName(newname);
            var fileId = await GetFileIdAsync(oldname, cancellationToken).ConfigureAwait(false);
            try
            {
                // A new name in another folder moves the file there; the folder has to exist
                var request = new CreateItemRequest { Name = newFileName };
                if (!IsSameFolder(newFolderPath, oldFolderPath))
                    request.Parent = new IDReference { ID = await GetExistingFolderIdAsync(newFolderPath, cancellationToken).ConfigureAwait(false) };

                await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, ct => _oAuthHelper.PutAndGetJsonDataAsync<FileEntity>(
                    $"{BOX_API_URL}/files/{fileId}",
                    request,
                    ct
                )).ConfigureAwait(false);

                // The cache holds the folder the file was in
                _fileCache.Remove(oldFileName);
                if (IsSameFolder(newFolderPath, oldFolderPath))
                    _fileCache[newFileName] = fileId;
            }
            catch
            {
                ClearFileCache();
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task DeleteFolderRecursiveAsync(string path, CancellationToken cancellationToken)
        {
            BackendExtensions.ValidateFolderDeletePath(path);

            string folderId;
            try
            {
                folderId = await GetFolderIdAsync(GetAbsolutePath(path), false, cancellationToken).ConfigureAwait(false);
            }
            catch (FolderMissingException)
            {
                // Already gone, which is the outcome the caller asked for
                return;
            }

            // The file cache can hold the folder, or one below it
            ClearFileCache();

            // Box deletes the whole tree before it answers, which can take as long as a listing
            using (var request = await _oAuthHelper.CreateRequestAsync($"{BOX_API_URL}/folders/{folderId}?recursive=true", HttpMethod.Delete, cancellationToken).ConfigureAwait(false))
            using (var r = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancellationToken, ct => _oAuthHelper.GetResponseAsync(request, HttpCompletionOption.ResponseContentRead, ct)).ConfigureAwait(false))
            {
            }

            if (_deleteFromTrash)
            {
                using (var request = await _oAuthHelper.CreateRequestAsync($"{BOX_API_URL}/folders/{folderId}/trash", HttpMethod.Delete, cancellationToken).ConfigureAwait(false))
                using (var r = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancellationToken, ct => _oAuthHelper.GetResponseAsync(request, HttpCompletionOption.ResponseContentRead, ct)).ConfigureAwait(false))
                {
                }
            }
        }

        /// <inheritdoc/>
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken)
            => this.TestBackendAsync(alsoWrite, cancelToken);

        /// <inheritdoc/>
        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            return GetCurrentFolderAsync(true, cancellationToken);
        }

        /// <inheritdoc/>
        public string DisplayName => Strings.Box.DisplayName;

        /// <inheritdoc/>
        public string ProtocolKey => "box";

        /// <inheritdoc/>
        // Box does not allow names that differ only by casing
        public bool HasCaseSensitivePaths => false;

        /// <inheritdoc/>
        public bool SupportsStreaming => true;

        /// <inheritdoc/>
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            .. AuthIdOptionsHelper.GetOptions(TOKEN_URL),
            new CommandLineArgument(REALLY_DELETE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Box.ReallydeleteShort, Strings.Box.ReallydeleteLong),
            .. TimeoutOptionsHelper.GetOptions()
        ];

        /// <inheritdoc/>
        public string Description => Strings.Box.Description;

        /// <inheritdoc/>
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] {
            new System.Uri(BOX_API_URL).Host,
            new System.Uri(BOX_UPLOAD_URL).Host
        }.Distinct().WhereNotNullOrWhiteSpace().ToArray());

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
