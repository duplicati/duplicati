using Duplicati.Library.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    public class DropboxHelper : OAuthHelper
    {
        private const int DROPBOX_MAX_CHUNK_UPLOAD = 10 * 1024 * 1024; // 10 MB max upload
        private const string API_ARG_HEADER = "DROPBOX-API-arg";

        public DropboxHelper(string accessToken)
            : base(accessToken, "dropbox")
        {
            base.AutoAuthHeader = true;
            // Pre 2022 tokens are direct Dropbox tokens (no ':')
            // Post 2022-02-21 tokens are regular authid tokens (with a ':')
            base.AccessTokenOnly = !accessToken.Contains(":");
        }

        public ListFolderResult ListFiles(string path)
        {
            var pa = new PathArg
            {
                path = path
            };

            try
            {
                return PostAndGetJSONData<ListFolderResult>(WebApi.Dropbox.ListFilesUrl(), pa);
            }
            catch (Exception ex)
            {
                HandleDropboxException(ex, false);
                throw;
            }
        }

        public ListFolderResult ListFilesContinue(string cursor)
        {
            var lfca = new ListFolderContinueArg() { cursor = cursor };

            try
            {
                return PostAndGetJSONData<ListFolderResult>(WebApi.Dropbox.ListFilesContinueUrl(), lfca);
            }
            catch (Exception ex)
            {
                HandleDropboxException(ex, false);
                throw;
            }
        }

        public FolderMetadata CreateFolder(string path)
        {
            var pa = new PathArg() { path = path };

            try
            {
                return PostAndGetJSONData<FolderMetadata>(WebApi.Dropbox.CreateFolderUrl(), pa);
            }
            catch (Exception ex)
            {
                HandleDropboxException(ex, false);
                throw;
            }
        }

        public async Task<FileMetaData> UploadFileAsync(String path, Stream stream, CancellationToken cancelToken)
        {
            // start a session
            var ussa = new UploadSessionStartArg();

            var chunksize = (int)Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, stream.Length);

            var req = CreateRequest(WebApi.Dropbox.UploadSessionStartUrl(), "POST");
            req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(ussa);
            req.ContentType = "application/octet-stream";
            req.ContentLength = chunksize;
            req.Timeout = 200000;

            var areq = new AsyncHttpRequest(req);

            byte[] buffer = new byte[Utility.Utility.DEFAULT_BUFFER_SIZE];
            int sizeToRead = Math.Min((int)Utility.Utility.DEFAULT_BUFFER_SIZE, chunksize);

            ulong globalBytesRead = 0;
            using (var rs = areq.GetRequestStream())
            {
                int bytesRead = 0;
                do
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, sizeToRead, cancelToken).ConfigureAwait(false);
                    globalBytesRead += (ulong)bytesRead;
                    await rs.WriteAsync(buffer, 0, bytesRead, cancelToken).ConfigureAwait(false);
                }
                while (bytesRead > 0 && globalBytesRead < (ulong)chunksize);
            }

            var ussr = await ReadJSONResponseAsync<UploadSessionStartResult>(areq, cancelToken); // pun intended

            // keep appending until finished
            // 1) read into buffer
            while (globalBytesRead < (ulong)stream.Length)
            {
                var remaining = (ulong)stream.Length - globalBytesRead;

                // start an append request
                var usaa = new UploadSessionAppendArg();
                usaa.cursor.session_id = ussr.session_id;
                usaa.cursor.offset = globalBytesRead;
                usaa.close = remaining < DROPBOX_MAX_CHUNK_UPLOAD;

                chunksize = (int)Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, (long)remaining);

                req = CreateRequest(WebApi.Dropbox.UploadSessionAppendUrl(), "POST");
                req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(usaa);
                req.ContentType = "application/octet-stream";
                req.ContentLength = chunksize;
                req.Timeout = 200000;

                areq = new AsyncHttpRequest(req);

                int bytesReadInRequest = 0;
                sizeToRead = Math.Min(chunksize, (int)Utility.Utility.DEFAULT_BUFFER_SIZE);
                using (var rs = areq.GetRequestStream())
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

                using (var response = GetResponse(areq))
                using (var sr = new StreamReader(response.GetResponseStream()))
                    await sr.ReadToEndAsync().ConfigureAwait(false);
            }

            // finish session and commit
            try
            {
                var usfa = new UploadSessionFinishArg();
                usfa.cursor.session_id = ussr.session_id;
                usfa.cursor.offset = globalBytesRead;
                usfa.commit.path = path;

                req = CreateRequest(WebApi.Dropbox.UploadSessionFinishUrl(), "POST");
                req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(usfa);
                req.ContentType = "application/octet-stream";
                req.Timeout = 200000;

                return ReadJSONResponse<FileMetaData>(req);
            }
            catch (Exception ex)
            {
                HandleDropboxException(ex, true);
                throw;
            }
        }

        public void DownloadFile(string path, Stream fs)
        {
            try
            {
                var pa = new PathArg { path = path };

                var req = CreateRequest(WebApi.Dropbox.DownloadFilesUrl(), "POST");
                req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(pa);

                using (var response = GetResponse(req))
                    Utility.Utility.CopyStream(response.GetResponseStream(), fs);
            }
            catch (Exception ex)
            {
                HandleDropboxException(ex, true);
                throw;
            }
        }

        public void Delete(string path)
        {
            try
            {
                var pa = new PathArg() { path = path };
                using (var response = GetResponse(WebApi.Dropbox.DeleteUrl(), pa))
                using(var sr = new StreamReader(response.GetResponseStream()))
                    sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                HandleDropboxException(ex, true);
                throw;
            }
        }

        private void HandleDropboxException(Exception ex, bool filerequest)
        {
            if (ex is WebException exception)
            {
                string json = string.Empty;

                try
                {
                    using (var sr = new StreamReader(exception.Response.GetResponseStream()))
                        json = sr.ReadToEnd();
                }
                catch { }

                // Special mapping for exceptions:
                //    https://www.dropbox.com/developers-v1/core/docs

                if (exception.Response is HttpWebResponse httpResp)
                {
                    if (httpResp.StatusCode == HttpStatusCode.NotFound)
                    {
                        if (filerequest)
                            throw new Duplicati.Library.Interface.FileMissingException(json);
                        else
                            throw new Duplicati.Library.Interface.FolderMissingException(json);
                    }
                    if (httpResp.StatusCode == HttpStatusCode.Conflict)
                    {
                        //TODO: Should actually parse and see if something else happens
                        if (filerequest)
                            throw new Duplicati.Library.Interface.FileMissingException(json);
                        else
                            throw new Duplicati.Library.Interface.FolderMissingException(json);
                    }
                    if (httpResp.StatusCode == HttpStatusCode.Unauthorized)
                        ThrowAuthException(json, exception);
                    if ((int)httpResp.StatusCode == 429 || (int)httpResp.StatusCode == 507)
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
