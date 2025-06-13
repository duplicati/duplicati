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

using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Duplicati.Library.Backend
{
    public class DropboxHelper : OAuthHelperHttpClient
    {
        private const int DROPBOX_MAX_CHUNK_UPLOAD = 10 * 1024 * 1024; // 10 MB max upload
        private const string API_ARG_HEADER = "DROPBOX-API-arg";

        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        public DropboxHelper(AuthIdOptionsHelper.AuthIdOptions authId, TimeoutOptionsHelper.Timeouts timeouts)
            : base(authId.AuthId, "dropbox", authId.OAuthUrl)
        {
            m_timeouts = timeouts;
            base.AutoAuthHeader = true;
            // Pre 2022 tokens are direct Dropbox tokens (no ':')
            // Post 2022-02-21 tokens are regular authid tokens (with a ':')
            base.AccessTokenOnly = !authId.AuthId!.Contains(":");
        }

        public async Task<ListFolderResult> ListFiles(string path, CancellationToken cancelToken)
        {
            return await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken,
                async ct =>
                {
                    using var req = await CreateRequestAsync(WebApi.Dropbox.ListFilesUrl(), HttpMethod.Post, ct).ConfigureAwait(false);
                    req.Content = JsonContent.Create(new PathArg { path = path });
                    using var resp = await GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    return await resp.Content.ReadFromJsonAsync<ListFolderResult>(ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Failed to deserialize ListFolderResult");
                }).ConfigureAwait(false);
        }

        public async Task<ListFolderResult> ListFilesContinue(string cursor, CancellationToken cancelToken)
        {
            return await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken,
                async ct =>
                {
                    using var req = await CreateRequestAsync(WebApi.Dropbox.ListFilesContinueUrl(), HttpMethod.Post, ct).ConfigureAwait(false);
                    req.Content = JsonContent.Create(new ListFolderContinueArg() { cursor = cursor });
                    using var resp = await GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    return await resp.Content.ReadFromJsonAsync<ListFolderResult>(ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Failed to deserialize ListFolderResult");
                }).ConfigureAwait(false);
        }

        public async Task<FolderMetadata> CreateFolderAsync(string path, CancellationToken cancellationToken)
        {
            return await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken,
                async ct =>
                {
                    using var req = await CreateRequestAsync(WebApi.Dropbox.CreateFolderUrl(), HttpMethod.Post, ct).ConfigureAwait(false);
                    req.Content = JsonContent.Create(new PathArg() { path = path });
                    using var resp = await GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    return await resp.Content.ReadFromJsonAsync<FolderMetadata>(ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Failed to deserialize FolderMetadata");
                }).ConfigureAwait(false);
        }

        private async Task<HttpRequestMessage> CreateChunkRequestAsync<T>(string url, T arg, CancellationToken cancelToken)
        {
            var req = await CreateRequestAsync(url, HttpMethod.Post, cancelToken).ConfigureAwait(false);
            req.Headers.Add(API_ARG_HEADER, System.Text.Json.JsonSerializer.Serialize(arg));
            req.Options.Set(FileRequestOption, true);
            return req;
        }

        private async Task<TRes> UploadChunk<TRes>(Task<HttpRequestMessage> reqTask, Stream stream, long offset, long chunksize, CancellationToken cancelToken)
        {
            using var req = await reqTask.ConfigureAwait(false);
            using var ls = new ReadLimitLengthStream(stream, offset, Math.Min(chunksize, stream.Length - offset));
            using var ts = ls.ObserveWriteTimeout(m_timeouts.ReadWriteTimeout);

            req.Content = new StreamContent(ts);
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            using var resp = await GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, cancelToken).ConfigureAwait(false);

            if (typeof(TRes) == typeof(object))
                return default!;

            return await resp.Content.ReadFromJsonAsync<TRes>(cancelToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to deserialize UploadSessionStartResult");
        }


        public async Task<FileMetaData> UploadFileAsync(String path, Stream stream, CancellationToken cancelToken)
        {
            // start a session
            var chunksize = (int)Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, stream.Length);
            var uploadStartArgs = await UploadChunk<UploadSessionStartResult>(
                CreateChunkRequestAsync(WebApi.Dropbox.UploadSessionStartUrl(), new UploadSessionStartArg(), cancelToken),
                stream, 0, chunksize, cancelToken);

            var position = chunksize;

            // keep appending until finished
            // 1) read into buffer
            while (position < stream.Length)
            {
                var remaining = stream.Length - position;

                // start an append request
                var usaa = new UploadSessionAppendArg();
                usaa.cursor.session_id = uploadStartArgs.session_id;
                usaa.cursor.offset = (ulong)position;
                usaa.close = remaining < DROPBOX_MAX_CHUNK_UPLOAD;

                await UploadChunk<object>(
                    CreateChunkRequestAsync(WebApi.Dropbox.UploadSessionAppendUrl(), usaa, cancelToken),
                    stream, position, chunksize, cancelToken);
                position += chunksize;
            }

            // finish session and commit
            var usfa = new UploadSessionFinishArg();
            usfa.cursor.session_id = uploadStartArgs.session_id;
            usfa.cursor.offset = (ulong)stream.Length;
            usfa.commit.path = path;

            using var commitReq = await CreateChunkRequestAsync(WebApi.Dropbox.UploadSessionFinishUrl(), usfa, cancelToken).ConfigureAwait(false);
            commitReq.Options.Set(FileRequestOption, true);
            commitReq.Content = new ByteArrayContent(Array.Empty<byte>());
            commitReq.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            return await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken,
                async ct =>
                {
                    using var resp = await GetResponseAsync(commitReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    return await resp.Content.ReadFromJsonAsync<FileMetaData>(ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Failed to deserialize FileMetaData");
                }
            ).ConfigureAwait(false);
        }

        public async Task DownloadFileAsync(string path, Stream fs, CancellationToken cancelToken)
        {
            var req = await CreateRequestAsync(WebApi.Dropbox.DownloadFilesUrl(), HttpMethod.Post, cancelToken).ConfigureAwait(false);
            req.Options.Set(FileRequestOption, true);
            req.Headers.Add(API_ARG_HEADER, System.Text.Json.JsonSerializer.Serialize(new PathArg { path = path }));

            using (var response = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false))
            {
                using (var rs = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => response.Content.ReadAsStreamAsync(ct)))
                using (var ts = rs.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
                    await Utility.Utility.CopyStreamAsync(ts, fs, cancelToken).ConfigureAwait(false);
            }
        }

        public async Task DeleteAsync(string path, CancellationToken cancelToken)
        {
            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
            {
                using var req = await CreateRequestAsync(WebApi.Dropbox.DeleteUrl(), HttpMethod.Post, ct).ConfigureAwait(false);
                req.Options.Set(FileRequestOption, true);
                req.Content = JsonContent.Create(new PathArg { path = path });
                using var response = await GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private static readonly HttpRequestOptionsKey<bool> FileRequestOption = new HttpRequestOptionsKey<bool>("FileRequestOption");

        public override async Task AttemptParseAndThrowExceptionAsync(Exception ex, HttpResponseMessage? response, CancellationToken cancellationToken)
        {
            string json = string.Empty;

            if (response != null)
            {
                try
                {
                    json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch
                {
                    // If reading fails, continue with empty string
                }

                var isFileRequest = response.RequestMessage?.Options.TryGetValue(FileRequestOption, out var fileRequest) == true && fileRequest;

                // Map HTTP status codes
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                    case HttpStatusCode.Conflict:
                        if (isFileRequest)
                            throw new Interface.FileMissingException(json);
                        else
                            throw new Interface.FolderMissingException(json);

                    case HttpStatusCode.Unauthorized:
                        throw new Interface.UserInformationException(
                            Strings.Dropbox.AuthorizationFailure(json, OAuthLoginUrl),
                            "OAuthLoginError",
                            ex);

                    case (HttpStatusCode)429: // Too Many Requests
                    case (HttpStatusCode)507: // Insufficient Storage
                        throw new Interface.UserInformationException(
                            Strings.Dropbox.OverQuotaError(string.IsNullOrWhiteSpace(json) ? ex.Message : json),
                            "DropboxOverQuotaError",
                            ex);
                }
            }

            JObject? errJson = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(json))
                    errJson = JObject.Parse(json);
            }
            catch
            {
                // Parsing failed
            }

            if (errJson != null)
                throw new DropboxException(ex) { errorJSON = errJson };
            else if (response != null)
                throw new InvalidDataException($"Non-json response (code: {(int)response.StatusCode}, message: {response.ReasonPhrase}): {json}", ex);
            else
                throw new InvalidDataException($"Non-json response: {json}", ex);
        }
    }

    public class DropboxException : Exception
    {
        public DropboxException(Exception innerException)
            : base("Dropbox API error", innerException)
        {
        }
        public JObject? errorJSON { get; set; }
    }

    public class PathArg
    {
        public string? path { get; set; }
    }

    public class FolderMetadata : MetaData
    {

    }

    public class UploadSessionStartArg
    {
        // ReSharper disable once UnusedMember.Global
        // This is serialized into JSON and provided in the Dropbox request header.
        // A value of false indicates that the session should not be closed.
        public static bool close => false;
    }

    public class UploadSessionAppendArg
    {
        public UploadSessionAppendArg()
        {
            cursor = new UploadSessionCursor();
        }

        public UploadSessionCursor cursor { get; set; }
        public bool close { get; set; }
    }

    public class UploadSessionFinishArg
    {
        public UploadSessionFinishArg()
        {
            cursor = new UploadSessionCursor();
            commit = new CommitInfo();
        }

        public UploadSessionCursor cursor { get; set; }
        public CommitInfo commit { get; set; }
    }

    public class UploadSessionCursor
    {
        public string? session_id { get; set; }
        public ulong offset { get; set; }
    }

    public class CommitInfo
    {
        public CommitInfo()
        {
            mode = "overwrite";
            autorename = false;
            mute = true;
        }
        public string? path { get; set; }
        public string mode { get; set; }
        public bool autorename { get; set; }
        public bool mute { get; set; }
    }


    public class UploadSessionStartResult
    {
        public string? session_id { get; set; }
    }

    public class ListFolderResult
    {
        public MetaData[]? entries { get; set; }

        public string? cursor { get; set; }
        public bool has_more { get; set; }
    }

    public class ListFolderContinueArg
    {
        public string? cursor { get; set; }
    }

    public class MetaData
    {
        [JsonProperty(".tag")]
        public string? tag { get; set; }
        public string? name { get; set; }
        public string? server_modified { get; set; }
        public ulong size { get; set; }
        public bool IsFile { get { return tag == "file"; } }

        // While this is unused, the Dropbox API v2 documentation does not
        // declare this to be optional.
        // ReSharper disable once UnusedMember.Global
        public string? id { get; set; }

        // While this is unused, the Dropbox API v2 documentation does not
        // declare this to be optional.
        // ReSharper disable once UnusedMember.Global
        public string? rev { get; set; }
    }

    public class FileMetaData : MetaData
    {

    }
}
