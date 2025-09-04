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

using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using Uri = Duplicati.Library.Utility.Uri;

namespace Duplicati.Library.Backend.Box
{
    public class BoxBackend : IStreamingBackend
    {
        private static readonly string TOKEN_URL = AuthIdOptionsHelper.GetOAuthLoginUrl("box.com", null);
        private const string AUTHID_OPTION = "authid";
        private const string REALLY_DELETE_OPTION = "box-delete-from-trash";

        private const string BOX_API_URL = "https://api.box.com/2.0";
        private const string BOX_UPLOAD_URL = "https://upload.box.com/api/2.0/files";

        private const int PAGE_SIZE = 200;

        private readonly BoxHelper _oAuthHelper;
        private readonly string _path;
        private readonly bool _deleteFromTrash;

        private string? _currentFolder;
        private readonly Dictionary<string, string> _fileCache = new();
        private readonly TimeoutOptionsHelper.Timeouts _timeouts;

        private class BoxHelper : OAuthHelperHttpClient
        {
            private readonly TimeoutOptionsHelper.Timeouts _timeouts;
            public BoxHelper(AuthIdOptionsHelper.AuthIdOptions authId, TimeoutOptionsHelper.Timeouts timeouts)
                : base(authId.AuthId, "box.com", authId.OAuthUrl)
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
                throw new UserInformationException($"Box.com ErrorResponse: {errorResponse.Status} - {errorResponse.Code}: {errorResponse.Message}", "box.com");
            }
        }

        public BoxBackend()
        {
            _oAuthHelper = null!;
            _path = null!;
            _timeouts = null!;
        }

        public BoxBackend(string url, Dictionary<string, string?> options)
        {
            var uri = new Uri(url);

            _path = Util.AppendDirSeparator(uri.HostAndPath, "/");

            var authid = AuthIdOptionsHelper.Parse(options)
                .RequireCredentials(TOKEN_URL);

            _deleteFromTrash = Utility.Utility.ParseBoolOption(options, REALLY_DELETE_OPTION);
            _timeouts = TimeoutOptionsHelper.Parse(options);

            _oAuthHelper = new BoxHelper(authid, _timeouts);

        }

        private async Task<string> GetCurrentFolderWithCacheAsync(CancellationToken cancelToken)
        {
            if (_currentFolder == null)
                return await GetCurrentFolderAsync(false, cancelToken).ConfigureAwait(false);

            return _currentFolder;
        }

        private async Task<string> GetCurrentFolderAsync(bool create, CancellationToken cancelToken)
        {
            var parentid = "0";

            foreach (var p in _path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var el = (MiniFolder?)await PagedFileListResponse(parentid, true, cancelToken).FirstOrDefaultAsync(x => x.Name == p, cancellationToken: cancelToken).ConfigureAwait(false);
                if (el == null)
                {
                    if (!create)
                        throw new FolderMissingException();

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

                parentid = el.ID;
                if (string.IsNullOrWhiteSpace(parentid))
                    throw new InvalidDataException($"Invalid folder ID for {p} in {_path}");
            }

            return _currentFolder = parentid;
        }

        private async Task<string> GetFileIdAsync(string name, CancellationToken cancelToken)
        {
            if (_fileCache.TryGetValue(name, out var async))
                return async;

            // Make sure we enumerate this, otherwise the m_filecache is empty.
            var currentFolder = await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false);
            await PagedFileListResponse(currentFolder, false, cancelToken).LastOrDefaultAsync(cancellationToken: cancelToken).ConfigureAwait(false);

            if (_fileCache.TryGetValue(name, out var idAsync))
                return idAsync;

            throw new FileMissingException();
        }

        private async IAsyncEnumerable<FileEntity> PagedFileListResponse(string parentid, bool onlyfolders, [EnumeratorCancellation] CancellationToken cancelToken)
        {
            var offset = 0;
            var done = false;

            if (!onlyfolders)
                _fileCache.Clear();

            do
            {
                var resp = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken, ct => _oAuthHelper.GetJsonDataAsync<ShortListResponse>($"{BOX_API_URL}/folders/{parentid}/items?limit={PAGE_SIZE}&offset={offset}&fields=name,size,modified_at", ct)).ConfigureAwait(false);

                if (resp.Entries == null || resp.Entries.Length == 0)
                    break;

                foreach (var f in resp.Entries)
                {
                    if (string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.ID))
                        continue;

                    if (onlyfolders && f.Type != "folder")
                    {
                        done = true;
                        break;
                    }

                    if (!onlyfolders && f.Type == "file")
                        _fileCache[f.Name] = f.ID;

                    yield return f;
                }

                offset = offset + PAGE_SIZE;

                if (offset >= resp.TotalCount)
                    break;

            } while (!done);
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var currentFolder = await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false);
            var createreq = new CreateItemRequest
            {
                Name = remotename,
                Parent = new IDReference
                {
                    ID = currentFolder
                }
            };

            if (_fileCache.Count == 0)
                await PagedFileListResponse(currentFolder, false, cancelToken).LastOrDefaultAsync(cancelToken).ConfigureAwait(false);

            var existing = _fileCache.ContainsKey(remotename);

            var multipartForm = new MultipartFormDataContent();

            try
            {
                string url;

                if (existing)
                    url = $"{BOX_UPLOAD_URL}/{_fileCache[remotename]}/content";
                else
                {
                    url = $"{BOX_UPLOAD_URL}/content";
                    multipartForm.Add(JsonContent.Create(createreq), "attributes");
                }

                using var timeoutStream = stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);
                multipartForm.Add(new StreamContent(timeoutStream), "file", remotename);

                var res = (await _oAuthHelper.PostMultipartAndGetJsonDataAsync<FileList>(url, cancelToken, multipartForm)).Entries?.FirstOrDefault();
                if (res == null || string.IsNullOrWhiteSpace(res.ID))
                    throw new InvalidDataException("No file ID returned after upload");
                _fileCache[remotename] = res.ID;
            }
            catch
            {
                _fileCache.Clear();
                throw;
            }
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var fileId = await GetFileIdAsync(remotename, cancelToken).ConfigureAwait(false);
            using var request = await _oAuthHelper.CreateRequestAsync($"{BOX_API_URL}/files/{fileId}/content", HttpMethod.Get, cancelToken).ConfigureAwait(false);
            using var resp = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => _oAuthHelper.GetResponseAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);
            await using var responseStream = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken, ct => resp.Content.ReadAsStreamAsync(ct)).ConfigureAwait(false);
            using var ts = responseStream.ObserveReadTimeout(_timeouts.ReadWriteTimeout);
            await Utility.Utility.CopyStreamAsync(ts, stream, cancelToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var currentFolder = await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false);
            await foreach (var n in PagedFileListResponse(currentFolder, false, cancelToken).ConfigureAwait(false))
                yield return new FileEntry(n.Name, n.Size, n.ModifiedAt, n.ModifiedAt) { IsFolder = n.Type == "folder" };
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
            }
            catch
            {
                _fileCache.Clear();
                throw;
            }
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            return GetCurrentFolderAsync(true, cancellationToken);
        }

        public string DisplayName => Strings.Box.DisplayName;

        public string ProtocolKey => "box";

        public IList<ICommandLineArgument> SupportedCommands =>
        [
            .. AuthIdOptionsHelper.GetOptions(TOKEN_URL),
            new CommandLineArgument(REALLY_DELETE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Box.ReallydeleteShort, Strings.Box.ReallydeleteLong),
            .. TimeoutOptionsHelper.GetOptions()
        ];

        public string Description => Strings.Box.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] {
            new System.Uri(BOX_API_URL).Host,
            new System.Uri(BOX_UPLOAD_URL).Host
        }.Distinct().WhereNotNullOrWhiteSpace().ToArray());

        public void Dispose()
        {
        }
    }
}
