using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.Backend
{
    public class DropboxHelper : JSONWebHelper
    {
        private const string API_URL = "https://api.dropboxapi.com/2";
        private const string CONTENT_API_URL = "https://content.dropboxapi.com/2";
        private const int DROPBOX_MAX_CHUNK_UPLOAD = 10*1024 * 1024; // 1 MB max upload
        private String m_accessToken;
        private const string API_ARG_HEADER = "DROPBOX-API-arg";

        public DropboxHelper(String accessToken)
        {
            m_accessToken = accessToken;
        }

        public override HttpWebRequest CreateRequest(string url, string method = null)
        {
            HttpWebRequest req = base.CreateRequest(url, method);
            req.Headers["Authorization"] = String.Format("Bearer {0}", m_accessToken);
            return req;
        }


        public ListFolderResult ListFiles(string path)
        {
            PathArg pa = new PathArg();
            pa.path = path;

            var url = string.Format("{0}/files/list_folder", API_URL);

            try
            {
                ListFolderResult lfr = PostAndGetJSONData<ListFolderResult>(url, pa);
                return lfr;
            }
            catch (Exception ex)
            {

                handleDropboxException(ex);
                throw;
            }


        }
        public ListFolderResult ListFilesContinue(String cursor)
        {
            ListFolderContinueArg lfca = new ListFolderContinueArg();
            lfca.cursor = cursor;

            var url = string.Format("{0}/files/list_folder/continue", API_URL);

            try
            {
                ListFolderResult lfr = PostAndGetJSONData<ListFolderResult>(url, lfca);
                return lfr;
            }
            catch (Exception ex)
            {
                handleDropboxException(ex);
                throw;
            }
        }

        public FolderMetadata CreateFolder(String path)
        {
            PathArg pa = new PathArg();
            pa.path = path;

            var url = string.Format("{0}/files/create_folder", API_URL);
            try
            {
                FolderMetadata fm = PostAndGetJSONData<FolderMetadata>(url, pa);
                return fm;
            }
            catch (Exception ex)
            {
                handleDropboxException(ex);
                throw;
            }
        }

        public FileMetaData UploadFile(String path, Stream stream)
        {

            // start a session
            UploadSessionStartArg ussa = new UploadSessionStartArg();

            var url = string.Format("{0}/files/upload_session/start", CONTENT_API_URL);
            HttpWebRequest req = CreateRequest(url, "POST");
            req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(ussa);
            req.ContentType = "application/octet-stream";
            req.ContentLength = Math.Min(DROPBOX_MAX_CHUNK_UPLOAD,stream.Length);
            req.Timeout = 200000;

            var areq = new AsyncHttpRequest(req);

            byte[] buffer = new byte[Utility.Utility.DEFAULT_BUFFER_SIZE];

            UInt64 globalBytesRead = 0;
            using (var rs = areq.GetRequestStream())
            {
                int bytesRead = 0;
                do
                {
                    bytesRead = stream.Read(buffer, 0, (int)Utility.Utility.DEFAULT_BUFFER_SIZE);
                    globalBytesRead += (UInt64)bytesRead;
                    rs.Write(buffer, 0, bytesRead);

                }
                while (bytesRead > 0 && globalBytesRead < DROPBOX_MAX_CHUNK_UPLOAD);
                
            }

            //Console.WriteLine(((HttpWebResponse)areq.GetResponse()).StatusCode);

            UploadSessionStartResult ussr = ReadJSONResponse<UploadSessionStartResult>(areq); // pun intended

            // keep appending until finished
            // 1) read into buffer
            while (globalBytesRead < (UInt64)stream.Length)
            {


                UInt64 remaining = (UInt64)stream.Length - globalBytesRead;

                // start an append request
                UploadSessionAppendArg usaa = new UploadSessionAppendArg();
                usaa.cursor.session_id = ussr.session_id;
                usaa.cursor.offset = globalBytesRead;
                usaa.close = remaining < DROPBOX_MAX_CHUNK_UPLOAD;
                url = string.Format("{0}/files/upload_session/append_v2", CONTENT_API_URL);


                req = CreateRequest(url, "POST");
                req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(usaa);
                req.ContentType = "application/octet-stream";
                req.ContentLength = Math.Min(DROPBOX_MAX_CHUNK_UPLOAD, (long)remaining);
                req.Timeout = 200000;

                areq = new AsyncHttpRequest(req);

                UInt64 bytesReadInRequest = 0;
                using (var rs = areq.GetRequestStream())
                {
                    int bytesRead = 0;
                    do
                    {
                        bytesRead = stream.Read(buffer, 0, (int)Utility.Utility.DEFAULT_BUFFER_SIZE);
                        bytesReadInRequest += (UInt64)bytesRead;
                        globalBytesRead += (UInt64)bytesRead;
                        rs.Write(buffer, 0, bytesRead);

                    }
                    while (bytesRead > 0 && bytesReadInRequest < Math.Min(remaining, DROPBOX_MAX_CHUNK_UPLOAD));
                }
                HttpWebResponse response = GetResponse(areq);
                StreamReader sr = new StreamReader(response.GetResponseStream());
                sr.ReadToEnd();


            }

            // finish session and commit
            try
            {
                UploadSessionFinishArg usfa = new UploadSessionFinishArg();
                usfa.cursor.session_id = ussr.session_id;
                usfa.cursor.offset = (UInt64)globalBytesRead;
                usfa.commit.path = path;

                url = string.Format("{0}/files/upload_session/finish", CONTENT_API_URL);
                req = CreateRequest(url, "POST");
                req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(usfa);
                req.ContentType = "application/octet-stream";
                req.Timeout = 200000;

                areq = new AsyncHttpRequest(req);

                //using (var rs = areq.GetRequestStream())
                //{
                //    int bytesRead = 0;
                //    do
                //    {
                //        bytesRead = stream.Read(buffer, 0, (int) Utility.Utility.DEFAULT_BUFFER_SIZE);
                //        globalBytesRead += (UInt64)bytesRead;
                //        rs.Write(buffer, 0, bytesRead);

                //    } while (bytesRead > 0);
                //}
                FileMetaData fmd = ReadJSONResponse<FileMetaData>(areq);
                return fmd;
            }
            catch (Exception ex)
            {
                handleDropboxException(ex);
                throw;
            }



        }
        public void DownloadFile(string path, Stream fs)
        {
            try
            {
                PathArg pa = new PathArg();
                pa.path = path;

                var url = string.Format("{0}/files/download", CONTENT_API_URL);
                var req = CreateRequest(url, "POST");
                req.Headers[API_ARG_HEADER] = JsonConvert.SerializeObject(pa);
                
                HttpWebResponse response = GetResponse(req);
                Utility.Utility.CopyStream(response.GetResponseStream(), fs);
            }
            catch (Exception ex)
            {
                handleDropboxException(ex);
                throw;
            }

        }

        public void Delete(string path)
        {
            try
            {
                PathArg pa = new PathArg();
                pa.path = path;

                var url = string.Format("{0}/files/delete", API_URL);
                HttpWebResponse response = GetResponse(url, pa);
                new StreamReader(response.GetResponseStream()).ReadToEnd();
            }
            catch (Exception ex)
            {
                handleDropboxException(ex);
                throw;
            }
        }


        private static void handleDropboxException(Exception ex)
        {
            if (ex is WebException)
            {
                DropboxException de = new DropboxException();
                string errorBody = new StreamReader(((WebException)ex).Response.GetResponseStream()).ReadToEnd();
                de.errorJSON = JObject.Parse(errorBody);
                throw de;
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
        public UInt64 offset { get; set; }
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
        [Newtonsoft.Json.JsonProperty(".tag")]
        public string tag { get; set; }
        public string name { get; set; }
        public string path_lower { get; set; }
        public string path_display { get; set; }
        public string id { get; set; }

        public string server_modified { get; set; }
        public string rev { get; set; }
        public UInt64 size { get; set; }

        public bool IsFile { get { return tag == "file"; } }

    }

    public class FileMetaData : MetaData
    {

    }

}
