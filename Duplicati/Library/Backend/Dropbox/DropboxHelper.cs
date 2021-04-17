using System.Net.Http.Headers;
using System.Net.Http;
using System.Security.AccessControl;
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Dropbox
{
    public class DropboxHelper : OAuthHelper
    {
        private const int DROPBOX_MAX_CHUNK_UPLOAD = 10 * 1024 * 1024; // 10 MB max upload
        private const string API_ARG_HEADER = "DROPBOX-API-arg";

        public DropboxHelper(string accessToken)
            : base(accessToken, "dropbox")
        {
            base.AutoAuthHeader = true;
            base.AccessTokenOnly = true;
        }

        public async Task<ListFolderResult> ListFilesAsync(string path, CancellationToken cancelToken)
        {
            var pa = new PathArg
            {
                path = path
            };

            try
            {
                return await PostAndGetJSONDataAsync<ListFolderResult>(WebApi.Dropbox.ListFilesUrl(), pa, null, cancelToken);
            }
            catch (Exception ex)
            {
                await HandleDropboxExceptionAsync(ex, false, cancelToken);
                throw;
            }
        }

        public async Task<ListFolderResult> ListFilesContinueAsync(string cursor, CancellationToken cancelToken)
        {
            var lfca = new ListFolderContinueArg() { cursor = cursor };

            try
            {
                return await PostAndGetJSONDataAsync<ListFolderResult>(WebApi.Dropbox.ListFilesContinueUrl(), lfca, null, cancelToken);
            }
            catch (Exception ex)
            {
                await HandleDropboxExceptionAsync(ex, false, cancelToken);
                throw;
            }
        }

        public async Task<FolderMetadata> CreateFolderAsync(string path, CancellationToken cancelToken)
        {
            var pa = new PathArg() { path = path };

            try
            {
                return await PostAndGetJSONDataAsync<FolderMetadata>(WebApi.Dropbox.CreateFolderUrl(), pa, null, cancelToken);
            }
            catch (Exception ex)
            {
                await HandleDropboxExceptionAsync(ex, false, cancelToken);
                throw;
            }
        }

        public async Task<FileMetaData> UploadFileAsync(String path, Stream stream, CancellationToken cancelToken)
        {
            // start a session
            var ussa = new UploadSessionStartArg();

            var chunksize = (int)Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, stream.Length);
            long globalBytesRead = 0;

            var req = await CreateRequestAsync(WebApi.Dropbox.UploadSessionStartUrl(), "POST", cancelToken);
            req.Headers.Add(API_ARG_HEADER, JsonConvert.SerializeObject(ussa));

            var body = new StreamContent(new PartialStream(stream, globalBytesRead, chunksize));
            body.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            body.Headers.ContentLength = chunksize;

            req.Content = body;

            var tcs = new CancellationTokenSource(200000);
            if (cancelToken.CanBeCanceled)
                cancelToken.Register(() => tcs.Cancel());

            byte[] buffer = new byte[Utility.Utility.DEFAULT_BUFFER_SIZE];
            int sizeToRead = Math.Min((int)Utility.Utility.DEFAULT_BUFFER_SIZE, chunksize);

            var ussr = await ReadJSONResponseAsync<UploadSessionStartResult>(req, tcs.Token); // pun intended
            globalBytesRead += chunksize;

            // keep appending until finished
            // 1) read into buffer
            while (globalBytesRead < stream.Length)
            {
                var remaining = stream.Length - globalBytesRead;

                // start an append request
                var usaa = new UploadSessionAppendArg();
                usaa.cursor.session_id = ussr.session_id;
                usaa.cursor.offset = (ulong)globalBytesRead;
                usaa.close = remaining < DROPBOX_MAX_CHUNK_UPLOAD;

                chunksize = (int)Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, (long)remaining);

                req = await CreateRequestAsync(WebApi.Dropbox.UploadSessionAppendUrl(), "POST", cancelToken);
                req.Headers.Add(API_ARG_HEADER, JsonConvert.SerializeObject(usaa));

                body = new StreamContent(new PartialStream(stream, globalBytesRead, chunksize));
                body.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                body.Headers.ContentLength = chunksize;

                req.Content = body;

                tcs = new CancellationTokenSource(200000);
                if (cancelToken.CanBeCanceled)
                    cancelToken.Register(() => tcs.Cancel());

                int bytesReadInRequest = 0;
                sizeToRead = Math.Min(chunksize, (int)Utility.Utility.DEFAULT_BUFFER_SIZE);
                using (var rs = req.GetRequestStream())
                {
                    int bytesRead = 0;
                    do
                    {
                        bytesRead = await stream.ReadAsync(buffer, 0, sizeToRead, cancelToken).ConfigureAwait(false);
                        bytesReadInRequest += bytesRead;
                        globalBytesRead += (ulong)bytesRead;
                        await rs.WriteAsync(buffer, 0, bytesRead, cancelToken).ConfigureAwait(false);

                    }
                    while (bytesRead > 0 && bytesReadInRequest < chunksize);
                }

                using (var response = await GetResponseAsync(req, null, tcs.Token))
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                globalBytesRead += (uint)chunksize;
            }

            // finish session and commit
            try
            {
                var usfa = new UploadSessionFinishArg();
                usfa.cursor.session_id = ussr.session_id;
                usfa.cursor.offset = (ulong)globalBytesRead;
                usfa.commit.path = path;

                req = await CreateRequestAsync(WebApi.Dropbox.UploadSessionFinishUrl(), "POST", cancelToken);
                req.Headers.Add(API_ARG_HEADER, JsonConvert.SerializeObject(usfa));                
                //req.ContentType = "application/octet-stream";

                tcs = new CancellationTokenSource(200000);
                if (cancelToken.CanBeCanceled)
                    cancelToken.Register(() => tcs.Cancel());

                return await ReadJSONResponseAsync<FileMetaData>(req, tcs.Token);
            }
            catch (Exception ex)
            {
                await HandleDropboxExceptionAsync(ex, true, cancelToken);
                throw;
            }
        }

        public async Task DownloadFileAsync(string path, Stream fs, CancellationToken cancelToken)
        {
            try
            {
                var pa = new PathArg { path = path };

                var req = await CreateRequestAsync(WebApi.Dropbox.DownloadFilesUrl(), "POST", cancelToken);
                req.Headers.Add(API_ARG_HEADER, JsonConvert.SerializeObject(pa));

                using (var response = await GetResponseAsync(req, null, cancelToken))
                using (var rs = await response.Content.ReadAsStreamAsync())
                    await Utility.Utility.CopyStreamAsync(rs, fs, cancelToken);
            }
            catch (Exception ex)
            {
                await HandleDropboxExceptionAsync(ex, true, cancelToken);
                throw;
            }
        }

        public async Task DeleteAsync(string path, CancellationToken cancelToken)
        {
            try
            {
                var pa = new PathArg() { path = path };
                using (var response = await GetResponseAsync(WebApi.Dropbox.DeleteUrl(), pa, null, cancelToken))
                    await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                await HandleDropboxExceptionAsync(ex, true, cancelToken);
                throw;
            }
        }

        private async Task HandleDropboxExceptionAsync(Exception ex, bool filerequest, CancellationToken cancelToken)
        {
            if (ex is HttpRequestStatusException exception)
            {
                string json = string.Empty;

                try
                {
                    json = await exception.Response.Content.ReadAsStringAsync();
                }
                catch { }

                // Special mapping for exceptions:
                //    https://www.dropbox.com/developers-v1/core/docs

                if (exception.Response != null)
                {
                    if (exception.Response.StatusCode == HttpStatusCode.NotFound)
                    {
                        if (filerequest)
                            throw new Duplicati.Library.Interface.FileMissingException(json);
                        else
                            throw new Duplicati.Library.Interface.FolderMissingException(json);
                    }
                    if (exception.Response.StatusCode == HttpStatusCode.Conflict)
                    {
                        //TODO: Should actually parse and see if something else happens
                        if (filerequest)
                            throw new Duplicati.Library.Interface.FileMissingException(json);
                        else
                            throw new Duplicati.Library.Interface.FolderMissingException(json);
                    }
                    if (exception.Response.StatusCode == HttpStatusCode.Unauthorized)
                        ThrowAuthException(json, exception);
                    if ((int)exception.Response.StatusCode == 429 || (int)exception.Response.StatusCode == 507)
                        ThrowOverQuotaError();
                }


                JObject errJson = null;
                try
                {
                    errJson = JObject.Parse(json);
                }
                catch
                {
                }

                if (errJson != null)
                    throw new DropboxException() { errorJSON = errJson };
                else
                    throw new InvalidDataException($"Non-json response: {json}");
            }
        }
    }

    public class PartialStream : Stream
    {
        private readonly Stream m_source;
        private readonly long m_offset;
        private readonly long m_length;
        private long m_position;

        public PartialStream(Stream source, long offset, long length)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            m_offset = offset;
            m_length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => m_length;
        public override long Position { get => m_position; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var c = (int)Math.Min(count, m_length - m_position);
            var r = await m_source.ReadAsync(buffer, offset, c, cancellationToken);
            m_position += r;
            return r;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var c = (int)Math.Min(count, m_length - m_position);
            var r = m_source.Read(buffer, offset, c);
            m_position += r;
            return r;
        }


        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    public class DropboxException : Exception
    {
        public JObject errorJSON { get; set; }
    }

    public class PathArg
    {
        public string path { get; set; }
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
        public string session_id { get; set; }
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
        public string path { get; set; }
        public string mode { get; set; }
        public bool autorename { get; set; }
        public bool mute { get; set; }
    }


    public class UploadSessionStartResult
    {
        public string session_id { get; set; }
    }

    public class ListFolderResult
    {

        public MetaData[] entries { get; set; }

        public string cursor { get; set; }
        public bool has_more { get; set; }
    }

    public class ListFolderContinueArg
    {
        public string cursor { get; set; }
    }

    public class MetaData
    {
        [JsonProperty(".tag")]
        public string tag { get; set; }
        public string name { get; set; }
        public string server_modified { get; set; }
        public ulong size { get; set; }
        public bool IsFile { get { return tag == "file"; } }

        // While this is unused, the Dropbox API v2 documentation does not
        // declare this to be optional.
        // ReSharper disable once UnusedMember.Global
        public string id { get; set; }

        // While this is unused, the Dropbox API v2 documentation does not
        // declare this to be optional.
        // ReSharper disable once UnusedMember.Global
        public string rev { get; set; }
    }

    public class FileMetaData : MetaData
    {

    }
}
