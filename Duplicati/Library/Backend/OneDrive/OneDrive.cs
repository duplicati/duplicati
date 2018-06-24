using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class OneDrive : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
		private static readonly string LOGTAG = Logging.Log.LogTagFromType<OneDrive>();

        private const string SERVICES_AGREEMENT = "https://www.microsoft.com/en-us/servicesagreement";
        private const string PRIVACY_STATEMENT = "https://privacy.microsoft.com/en-us/privacystatement";

        private const string AUTHID_OPTION = "authid";

        private const string WLID_SERVER = "https://apis.live.net/v5.0";
        private const string ROOT_FOLDER_ID = "me/skydrive";
        private const string FOLDER_TEMPLATE = "{0}/files";
        private const string ONEDRIVE_SERVICE_URL = "https://api.onedrive.com/v1.0";

        private const int FILE_LIST_PAGE_SIZE = 100;

        private const long BITS_FILE_SIZE_LIMIT = 1024 * 1024 * 15;
        private const long BITS_CHUNK_SIZE = 1024 * 1024 * 10;

        private static readonly string USER_AGENT = string.Format("Duplicati v{0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

        private readonly string m_rootfolder;
        private readonly string m_prefix;
        private string m_userid;
        private WLID_FolderItem m_folderid;

        private readonly OAuthHelper m_oauth;

        private readonly Dictionary<string, string> m_fileidCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

        public OneDrive() { }

        public OneDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_rootfolder = uri.Host;
            m_prefix = "/" + uri.Path;
            if (!m_prefix.EndsWith("/", StringComparison.Ordinal))
                m_prefix += "/";

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            m_oauth = new OAuthHelper(authid, this.ProtocolKey);
        }

        private class WLID_Service_Response
        {
            public string access_token { get; set; }
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public int expires { get; set; }
        }

        private class WLID_DataItem
        {
            public WLID_FolderItem[] data { get; set; }
        }

        private class WLID_FolderItem
        {
            public string id { get; set; }
            public string name { get; set; }
            public string description { get; set; }
            public string parent_id { get; set; }
            public string upload_location { get; set; }
            public string type { get; set; }
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public DateTime? created_time { get; set; }
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public DateTime? updated_time { get; set; }
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public long? size { get; set; }
        }

        private class WLID_CreateFolderData
        {
            public string name;
            public string description;
        }

        private class WLID_ContinuationResponse
        {
            [Newtonsoft.Json.JsonProperty("uploadUrl")]
            public string UploadUrl { get; set; }
            [Newtonsoft.Json.JsonProperty("expirationDateTime", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public DateTime Expires { get; set; } 
            [Newtonsoft.Json.JsonProperty("nextExpectedRanges")]
            public string[] NextRanges { get; set; }
        }

        private class WLID_UserInfo
        {
            public string id { get; set; }
            public string first_name { get; set; }
            public string last_name { get; set; }
            public string name { get; set; }
            public string gender { get; set; }
            public string locale { get; set; }
        }

        private class WLID_QuotaInfo
        {
            [Newtonsoft.Json.JsonProperty("quota")]
            public long? Quota { get; set; }
            [Newtonsoft.Json.JsonProperty("available")]
            public long? Available { get; set; }
        }

        private WLID_FolderItem FindFolder(string folder, string parentfolder = null)
        {
            if (string.IsNullOrWhiteSpace(parentfolder))
                parentfolder = ROOT_FOLDER_ID;
            
            var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, string.Format(FOLDER_TEMPLATE, parentfolder), Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
            var res = m_oauth.GetJSONData<WLID_DataItem>(url, x => x.UserAgent = USER_AGENT);

            if (res == null || res.data == null)
                return null;

            foreach(var r in res.data)
                if (string.Equals(r.name, folder, StringComparison.OrdinalIgnoreCase))
                    return r;

             return null;
        }

        private WLID_FolderItem FindFolders(bool autocreate)
        {
            var folders = (m_rootfolder + '/' + m_prefix).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (folders.Length == 0)
            {
                var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, ROOT_FOLDER_ID, Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
                return m_oauth.GetJSONData<WLID_FolderItem>(url, x => x.UserAgent = USER_AGENT);
            }

            WLID_FolderItem cur = null;
            foreach (var f in folders)
            {
                var n = FindFolder(f, cur == null ? null : cur.id);
                if (n == null)
                {
                    if (autocreate)
                    {
                        var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, cur == null ? ROOT_FOLDER_ID : cur.id, Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
                        var req = (HttpWebRequest)WebRequest.Create(url);
                        req.UserAgent = USER_AGENT;
                        req.Method = "POST";

                        var areq = new Utility.AsyncHttpRequest(req);

                        using (var ms = new System.IO.MemoryStream())
                        using (var sw = new System.IO.StreamWriter(ms))
                        {
                            new Newtonsoft.Json.JsonSerializer().Serialize(sw, new WLID_CreateFolderData() {
                                name = f,
                                description = Strings.OneDrive.AutoCreatedFolderLabel
                            });

                            sw.Flush();
                            ms.Position = 0;

                            req.ContentLength = ms.Length;
                            req.ContentType = "application/json";

                            using (var reqs = areq.GetRequestStream())
                                Utility.Utility.CopyStream(ms, reqs, true, m_copybuffer);
                        }

                        using (var resp = (HttpWebResponse)areq.GetResponse())
                        using (var rs = areq.GetResponseStream())
                        using (var tr = new System.IO.StreamReader(rs))
                        using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
                        {
                            if ((int)resp.StatusCode < 200 || (int)resp.StatusCode > 299)
                                throw new ProtocolViolationException(Strings.OneDrive.UnexpectedError(resp.StatusCode, resp.StatusDescription));
                            cur = new Newtonsoft.Json.JsonSerializer().Deserialize<WLID_FolderItem>(jr);
                        }
                    }
                    else
                        throw new FolderMissingException(Strings.OneDrive.MissingFolderError(f));
                }
                else
                    cur = n;
            }

            return cur;
        }

        private string FolderID
        {
            get
            {
                if (m_folderid == null)
                    m_folderid = FindFolders(false);

                return m_folderid.id;
            }
        }

        private string GetFileID(string name, bool throwIfMissing = true)
        {
            string id;
            m_fileidCache.TryGetValue(name, out id);
            if (string.IsNullOrWhiteSpace(id))
            {
                // Refresh the list of files, just in case
                foreach (IFileEntry file in List()) { /* We just need to iterate the whole list */ }
                m_fileidCache.TryGetValue(name, out id);

                if (string.IsNullOrWhiteSpace(id) && throwIfMissing)
                    throw new FileMissingException(Strings.OneDrive.FileNotFoundError(name));
            }

            return id;
        }

        private WLID_QuotaInfo GetQuotaInfo()
        {
            var url = string.Format("{0}/me/skydrive/quota?access_token={1}", WLID_SERVER, Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
            return m_oauth.GetJSONData<WLID_QuotaInfo>(url, x => x.UserAgent = USER_AGENT);
        }

        #region IBackend Members

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            FindFolders(true);
        }

        public string DisplayName
        {
            get { return Strings.OneDrive.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "onedrive"; }
        }

        public IEnumerable<IFileEntry> List()
        {
            int offset = 0;
            int count = FILE_LIST_PAGE_SIZE;
            int numFiles = 0;
            int filesOk = 0;
            int filesRepeated = 0;
            int iteration = 0;

            var files = new List<IFileEntry>();

            m_fileidCache.Clear();

            do
            {

                while (count == FILE_LIST_PAGE_SIZE)
                {
                    var url = string.Format("{0}/{1}?access_token={2}&limit={3}&offset={4}", WLID_SERVER, string.Format(FOLDER_TEMPLATE, FolderID), Library.Utility.Uri.UrlEncode(m_oauth.AccessToken), FILE_LIST_PAGE_SIZE, offset);
                    var res = m_oauth.GetJSONData<WLID_DataItem>(url);
                    
                    if (res != null && res.data != null)
                    {
                        count = res.data.Length;

                        // log
						Library.Logging.Log.WriteProfilingMessage(LOGTAG, "OneDriveListStats", "Iteration: {0:D} Offset: {1:D} Count: {2:D} TotalOK: {3:D} TotalRep: {4:D} TotalFiles: {5:D}", iteration, offset, count, filesOk, filesRepeated, numFiles);

                        foreach (var r in res.data)
                        {

                            if (m_fileidCache.ContainsKey(r.name))
                            {
                                filesRepeated++;
                            }
                            else
                            {
                                m_fileidCache.Add(r.name, r.id);

                                var fe = new FileEntry(r.name, r.size.Value, r.updated_time.Value, r.updated_time.Value);
                                fe.IsFolder = string.Equals(r.type, "folder", StringComparison.OrdinalIgnoreCase);
                                files.Add(fe);
                                
                                filesOk++;
                            }
                        }
                    }
                    else
                    {
                        count = 0;
                    }

                    if (iteration != 0 && filesOk == numFiles) return files;

                    offset += count;
                    
                }

                // Save total number of files in the first iteration
                if (iteration == 0)
                {
                    numFiles = offset;
                }

                filesRepeated = 0;
                iteration++;

                offset = 0;
                count = FILE_LIST_PAGE_SIZE;
            }
            while (filesOk != numFiles);

            return files;
        }

        public void Put(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                Put(remotename, fs);
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            try
            {
                var id = GetFileID(remotename);
                var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, id,  Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = USER_AGENT;
                req.Method = "DELETE";

                var areq = new Utility.AsyncHttpRequest(req);
                using (var resp = (HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new FileMissingException();

                    if ((int)resp.StatusCode < 200 || (int)resp.StatusCode > 299)
                        throw new ProtocolViolationException(Strings.OneDrive.UnexpectedError(resp.StatusCode, resp.StatusDescription));
                    m_fileidCache.Remove(remotename);
                }
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw;
            }
        }        
            

        public IList<ICommandLineArgument> SupportedCommands
        {
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.OneDrive.AuthidShort, Strings.OneDrive.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("onedrive"))),
                });
            }
        }

        public string Description
        {
            get { return Strings.OneDrive.Description(
                    "Microsoft Service Agreement",
                    SERVICES_AGREEMENT,
                    "Microsoft Online Privacy Statement", 
                    PRIVACY_STATEMENT
                    ); 
            }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        private string UserID
        {
            get
            {
                if (string.IsNullOrEmpty(m_userid))
                {
                    var url = string.Format("{0}/me?access_token={1}", WLID_SERVER, Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = USER_AGENT;
                    var areq = new Utility.AsyncHttpRequest(req);
                    
                    using(var resp = (HttpWebResponse)areq.GetResponse())
                    using(var rs = areq.GetResponseStream())
                    using(var tr = new System.IO.StreamReader(rs))
                    using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
                        m_userid = new Newtonsoft.Json.JsonSerializer().Deserialize<WLID_UserInfo>(jr).id;
                }

                return m_userid;
            }
        }

        #region IRenameEnabledBackend

        public void Rename(string oldname, string newname)
        {
            try
            {
                try
                {
                    var id = GetFileID(oldname);
                    var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, id, Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = USER_AGENT;
                    req.Method = "PUT";

                    var updateData = new WLID_FolderItem() { name = newname };
                    var data = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(updateData));
                    req.ContentLength = data.Length;
                    req.ContentType = "application/json; charset=UTF-8";
                    using (var requestStream = req.GetRequestStream())
                        requestStream.Write(data, 0, data.Length);

                    var areq = new Utility.AsyncHttpRequest(req);
                    using (var resp = (HttpWebResponse)areq.GetResponse())
                    {
                        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                            throw new FileMissingException();

                        if ((int)resp.StatusCode < 200 || (int)resp.StatusCode > 299)
                            throw new ProtocolViolationException(Strings.OneDrive.UnexpectedError(resp.StatusCode, resp.StatusDescription));

                        m_fileidCache[newname] = id;
                        m_fileidCache.Remove(oldname);
                    }
                }
                catch
                {
                    // Since we don't know the state of file IDs, clear the cache
                    m_fileidCache.Clear();

                    throw;
                }
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw;
            }
        }

        #endregion

        #region IQuotaEnabledBackend Members

        public IQuotaInfo Quota
        {
            get
            {
                WLID_QuotaInfo quota = this.GetQuotaInfo();
                return new QuotaInfo(quota.Quota ?? -1, quota.Available ?? -1);
            }
        }

        public long TotalQuotaSpace
        {
            get
            {
                return this.GetQuotaInfo().Quota ?? -1;
            }
        }

        public long FreeQuotaSpace
        {
            get
            {
                return this.GetQuotaInfo().Available ?? -1;
            }
        }

        public string[] DNSName
        {
            get { return new string[] { new Uri(WLID_SERVER).Host, new Uri(ONEDRIVE_SERVICE_URL).Host, string.IsNullOrWhiteSpace(m_userid) ? null : string.Format("cid-{0}.users.storage.live.com", m_userid) }; }
        }

        #endregion

        #region IStreamingBackend Members

        public void Put(string remotename, System.IO.Stream stream)
        {
            if (stream.Length > BITS_FILE_SIZE_LIMIT)
            {
                // Get extra info for BITS
                var uid = UserID;
                var fid = FolderID.Split('.')[2];

                // Create a session
                var url = string.Format("https://cid-{0}.users.storage.live.com/items/{1}/{2}?access_token={3}", uid, fid, Utility.Uri.UrlPathEncode(remotename), Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = USER_AGENT;
                req.Method = "POST";
                req.ContentType = "application/json";

                req.Headers.Add("X-Http-Method-Override", "BITS_POST");
                req.Headers.Add("BITS-Packet-Type", "Create-Session");
                req.Headers.Add("BITS-Supported-Protocols", "{7df0354d-249b-430f-820d-3d2a9bef4931}");
                req.ContentLength = 0;

                var areq = new Utility.AsyncHttpRequest(req);

                string sessionid;

                using(var resp = (HttpWebResponse)areq.GetResponse())
                {
                    var packtype = resp.Headers["BITS-Packet-Type"];
                    if (!packtype.Equals("Ack", StringComparison.OrdinalIgnoreCase))
                        throw new Exception(string.Format("Unable to create BITS transfer, got status: {0}", packtype));
                    
                    sessionid = resp.Headers["BITS-Session-Id"];
                }

                if (string.IsNullOrEmpty(sessionid))
                    throw new Exception("BITS session-id was missing");
                
                // Session is now created, start uploading chunks

                var offset = 0L;
                var retries = 0;

                while (offset < stream.Length)
                {
                    try
                    {
                        var bytesInChunk = Math.Min(BITS_CHUNK_SIZE, stream.Length - offset);

                        req = (HttpWebRequest)WebRequest.Create(url);
                        req.UserAgent = USER_AGENT;
                        req.Method = "POST";
                        req.Headers.Add("X-Http-Method-Override", "BITS_POST");
                        req.Headers.Add("BITS-Packet-Type", "Fragment");
                        req.Headers.Add("BITS-Session-Id", sessionid);
                        req.Headers.Add("Content-Range", string.Format("bytes {0}-{1}/{2}", offset, offset + bytesInChunk - 1, stream.Length));

                        req.ContentLength = bytesInChunk;

                        if (stream.Position != offset)
                            stream.Position = offset;
                        
                        areq = new Utility.AsyncHttpRequest(req);
                        var remaining = (int)bytesInChunk;
                        using(var reqs = areq.GetRequestStream())
                        {
                            int read;
                            while ((read = stream.Read(m_copybuffer, 0, Math.Min(m_copybuffer.Length, remaining))) != 0)
                            {
                                reqs.Write(m_copybuffer, 0, read);
                                remaining -= read;
                            }
                        }

                        using(var resp = (HttpWebResponse)areq.GetResponse())
                        {
                            if (resp.StatusCode != HttpStatusCode.OK)
                                throw new WebException("Invalid partial upload response", null, WebExceptionStatus.UnknownError, resp);
                        }

                        offset += bytesInChunk;
                        retries = 0;
                    }
                    catch (Exception ex)
                    {
                        var retry = false;

                        // If we get a 5xx error, or some network issue, we retry
                        if (ex is WebException && ((WebException)ex).Response is HttpWebResponse)
                        {
                            var code = (int)((HttpWebResponse)((WebException)ex).Response).StatusCode;
                            retry = code >= 500 && code <= 599;
                        }
                        else if (ex is System.Net.Sockets.SocketException || ex is System.IO.IOException || ex.InnerException is System.Net.Sockets.SocketException || ex.InnerException is System.IO.IOException)
                        {
                            retry = true;
                        }


                        // Retry with exponential backoff
                        if (retry && retries < 5)
                        {
                            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                            retries++;
                        }
                        else
                            throw;
                    }
                }

                // Transfer completed, now commit the upload and close the session

                req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = USER_AGENT;
                req.Method = "POST";
                req.Headers.Add("X-Http-Method-Override", "BITS_POST");
                req.Headers.Add("BITS-Packet-Type", "Close-Session");
                req.Headers.Add("BITS-Session-Id", sessionid);
                req.ContentLength = 0;

                areq = new Utility.AsyncHttpRequest(req);
                using(var resp = (HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode != HttpStatusCode.OK)
                        throw new Exception("Invalid partial upload commit response");
                }
            }
            else
            {
                var url = string.Format("{0}/{1}/files/{2}?access_token={3}", WLID_SERVER, FolderID, Utility.Uri.UrlPathEncode(remotename), Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = USER_AGENT;
                req.Method = "PUT";

                try
                {
                    req.ContentLength = stream.Length;
                }
                catch
                {
                }

                // Docs says not to set this ?
                //req.ContentType = "application/octet-stream";

                var areq = new Utility.AsyncHttpRequest(req);
                using(var reqs = areq.GetRequestStream())
                    Utility.Utility.CopyStream(stream, reqs, true, m_copybuffer);

                using(var resp = (HttpWebResponse)areq.GetResponse())
                using(var rs = areq.GetResponseStream())
                using(var tr = new System.IO.StreamReader(rs))
                using(var jr = new Newtonsoft.Json.JsonTextReader(tr))
                {
                    var nf = new Newtonsoft.Json.JsonSerializer().Deserialize<WLID_FolderItem>(jr);
                    m_fileidCache[remotename] = nf.id;
                }
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            var id = GetFileID(remotename);
            var url = string.Format("{0}/{1}/content?access_token={2}", WLID_SERVER, id,  Library.Utility.Uri.UrlEncode(m_oauth.AccessToken));
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = USER_AGENT;

            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = (HttpWebResponse)areq.GetResponse())
            using (var rs = areq.GetResponseStream())
                Utility.Utility.CopyStream(rs, stream, true, m_copybuffer);
        }

        #endregion
    }
}
