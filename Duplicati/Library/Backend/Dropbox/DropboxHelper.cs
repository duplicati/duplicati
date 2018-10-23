using System;
using System.IO;
using System.Net;

using Duplicati.Library.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.Backend
{
    public class DropboxHelper : OAuthHelper
    {
        internal const string API_URL = "https://api.dropboxapi.com/2";
        internal const string CONTENT_API_URL = "https://content.dropboxapi.com/2";
        private const int DROPBOX_MAX_CHUNK_UPLOAD = 10 * 1024 * 1024; // 10 MB max upload
        private const string API_ARG_HEADER = "DROPBOX-API-arg";

        public DropboxHelper(string accessToken)
            : base(accessToken, "dropbox")
        {
            base.AutoAuthHeader = true;
            base.AccessTokenOnly = true;
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
            var url = string.Format("{0}/files/list_folder/continue", API_URL);

            try
            {
                return PostAndGetJSONData<ListFolderResult>(url, lfca);
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

        public FileMetaData UploadFile(String path, Stream stream)
        {
            // start a session
            var ussa = new UploadSessionStartArg();

            var chunksize = (int)Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, stream.Length);

            var url = string.Format("{0}/files/upload_session/start", CONTENT_API_URL);
            var req = CreateRequest(url, "POST");
            req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(ussa);
            req.ContentType = "application/octet-stream";
            req.ContentLength = chunksize;
            req.Timeout = 200000;

            var areq = new AsyncHttpRequest(req);

            byte[] buffer = new byte[Utility.Utility.DEFAULT_BUFFER_SIZE];

            ulong globalBytesRead = 0;
            using (var rs = areq.GetRequestStream())
            {
                int bytesRead = 0;
                do
                {
                    bytesRead = stream.Read(buffer, 0, Math.Min((int)Utility.Utility.DEFAULT_BUFFER_SIZE, chunksize));
                    globalBytesRead += (ulong)bytesRead;
                    rs.Write(buffer, 0, bytesRead);

                }
                while (bytesRead > 0 && globalBytesRead < (ulong)chunksize);                
            }

            var ussr = ReadJSONResponse<UploadSessionStartResult>(areq); // pun intended

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
                url = string.Format("{0}/files/upload_session/append_v2", CONTENT_API_URL);

                chunksize = (int)Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, (long)remaining);

                req = CreateRequest(url, "POST");
                req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(usaa);
                req.ContentType = "application/octet-stream";
                req.ContentLength = chunksize;
                req.Timeout = 200000;

                areq = new AsyncHttpRequest(req);

                int bytesReadInRequest = 0;
                using (var rs = areq.GetRequestStream())
                {
                    int bytesRead = 0;
                    do
                    {
                        bytesRead = stream.Read(buffer, 0, Math.Min(chunksize, (int)Utility.Utility.DEFAULT_BUFFER_SIZE));
                        bytesReadInRequest += bytesRead;
                        globalBytesRead += (ulong)bytesRead;
                        rs.Write(buffer, 0, bytesRead);

                    }
                    while (bytesRead > 0 && bytesReadInRequest < chunksize);
                }

                using (var response = GetResponse(areq))
                using (var sr = new StreamReader(response.GetResponseStream()))
                    sr.ReadToEnd();
            }

            // finish session and commit
            try
            {
                var usfa = new UploadSessionFinishArg();
                usfa.cursor.session_id = ussr.session_id;
                usfa.cursor.offset = (ulong)globalBytesRead;
                usfa.commit.path = path;

                url = string.Format("{0}/files/upload_session/finish", CONTENT_API_URL);
                req = CreateRequest(url, "POST");
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
                var pa = new PathArg() { path = path };
                var url = string.Format("{0}/files/download", CONTENT_API_URL);
                var req = CreateRequest(url, "POST");
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
                var url = string.Format("{0}/files/delete", API_URL);
                using (var response = GetResponse(url, pa))
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
            if (ex is WebException)
            {
                string json = string.Empty;

                try
                {
                    using (var sr = new StreamReader(((WebException)ex).Response.GetResponseStream()))
                        json = sr.ReadToEnd();
                }
                catch { }

                // Special mapping for exceptions:
                //    https://www.dropbox.com/developers-v1/core/docs

                if (((WebException)ex).Response is HttpWebResponse)
                {
                    var httpResp = ((WebException)ex).Response as HttpWebResponse;

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
                        ThrowAuthException(json, ex);
                    if ((int)httpResp.StatusCode == 429 || (int)httpResp.StatusCode == 507)
                        ThrowOverQuotaError();
                }

                throw new DropboxException() { errorJSON = JObject.Parse(json) };
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
        public bool close { get; set; }
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
        public string path_lower { get; set; }
        public string path_display { get; set; }
        public string id { get; set; }

        public string server_modified { get; set; }
        public string rev { get; set; }
        public ulong size { get; set; }

        public bool IsFile { get { return tag == "file"; } }

    }

    public class FileMetaData : MetaData
    {

    }
}
