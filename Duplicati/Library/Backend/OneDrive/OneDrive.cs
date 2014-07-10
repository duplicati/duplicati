using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend
{
    public class SkyDrive : IBackend, IStreamingBackend
    {
        private const string AUTHID_OPTION = "authid";

        private const string WLID_SERVICE = "https://duplicati-oauth-handler.appspot.com/refresh?authid={0}";
        private const string WLID_LOGIN = "https://duplicati-oauth-handler.appspot.com/";

        private const string WLID_SERVER = "https://apis.live.net/v5.0";
        private const string ROOT_FOLDER_ID = "me/skydrive";
        private const string FOLDER_TEMPLATE = "{0}/files";

        private static readonly string USER_AGENT = string.Format("Duplicati v{0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());

        private string m_authid;
        private string m_rootfolder;
        private string m_prefix;
        private string m_token;
        private DateTime m_tokenExpires = DateTime.UtcNow;
        private WLID_FolderItem m_folderid;

        private Dictionary<string, string> m_fileidCache = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

        public SkyDrive() { }

        public SkyDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            uri.RequireHost();
            
            m_rootfolder = uri.Host;
            m_prefix = "/" + uri.Path;
            if (!m_prefix.EndsWith("/"))
                m_prefix += "/";

            if (options.ContainsKey(AUTHID_OPTION))
                m_authid = options[AUTHID_OPTION];

            if (string.IsNullOrEmpty(m_authid))
                //throw new Exception(string.Format(Strings.SkyDrive.MissingAuthID, WLID_LOGIN));
                throw new Exception(string.Format(LC.L("You need an AuthID, you can get it from: {0}"), WLID_LOGIN));
        }

        private class WLID_Service_Response
        {
            public string access_token { get; set; }
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
            public DateTime created_time { get; set; }
            public DateTime updated_time { get; set; }
            public long size { get; set; }
        }

        private class WLID_CreateFolderData
        {
            public string name;
            public string description;
        }

        private T GetJSONData<T>(string url)
        {
            var req = (HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = USER_AGENT;

            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);

            using (var resp = (HttpWebResponse)areq.GetResponse())
            using (var rs = areq.GetResponseStream())
            using (var tr = new System.IO.StreamReader(rs))
            using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
                return new Newtonsoft.Json.JsonSerializer().Deserialize<T>(jr);
        }

        private string AccessToken
        {
            get
            {
                if (m_token == null || m_tokenExpires < DateTime.UtcNow)
                {
                    var res = GetJSONData<WLID_Service_Response>(string.Format(WLID_SERVICE, Library.Utility.Uri.UrlEncode(m_authid)));

                    m_tokenExpires = DateTime.UtcNow.AddSeconds(res.expires - 30);
                    m_token = res.access_token;
                }

                return m_token;
            }
        }

        private WLID_FolderItem FindFolder(string folder, string parentfolder = null)
        {
            if (string.IsNullOrWhiteSpace(parentfolder))
                parentfolder = ROOT_FOLDER_ID;
            
            var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, string.Format(FOLDER_TEMPLATE, parentfolder), Library.Utility.Uri.UrlEncode(AccessToken));
            var res = GetJSONData<WLID_DataItem>(url);

            if (res == null || res.data == null)
                return null;

            foreach(var r in res.data)
                if (string.Equals(r.name, folder, StringComparison.InvariantCultureIgnoreCase))
                    return r;

             return null;
        }

        private WLID_FolderItem FindFolders(bool autocreate)
        {
            var folders = (m_rootfolder + '/' + m_prefix).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (folders.Length == 0)
            {
                var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, ROOT_FOLDER_ID, Library.Utility.Uri.UrlEncode(AccessToken));
                return GetJSONData<WLID_FolderItem>(url);
            }

            WLID_FolderItem cur = null;
            foreach (var f in folders)
            {
                var n = FindFolder(f, cur == null ? null : cur.id);
                if (n == null)
                {
                    if (autocreate)
                    {
                        var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, cur == null ? ROOT_FOLDER_ID : cur.id, Library.Utility.Uri.UrlEncode(AccessToken));
                        var req = (HttpWebRequest)WebRequest.Create(url);
                        req.UserAgent = USER_AGENT;
                        req.Method = "POST";

                        var areq = new Utility.AsyncHttpRequest(req);

                        using (var ms = new System.IO.MemoryStream())
                        using (var sw = new System.IO.StreamWriter(ms))
                        {
                            new Newtonsoft.Json.JsonSerializer().Serialize(sw, new WLID_CreateFolderData() {
                                name = f,
                                description = LC.L("Autocreated folder")
                            });

                            sw.Flush();
                            ms.Position = 0;

                            req.ContentLength = ms.Length;
                            req.ContentType = "application/json";

                            using (var reqs = areq.GetRequestStream())
                                Utility.Utility.CopyStream(ms, reqs);
                        }

                        using (var resp = (HttpWebResponse)areq.GetResponse())
                        using (var rs = areq.GetResponseStream())
                        using (var tr = new System.IO.StreamReader(rs))
                        using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
                        {
                            if ((int)resp.StatusCode < 200 || (int)resp.StatusCode > 299)
                                throw new ProtocolViolationException(string.Format(LC.L("Unexpected error code: {0} - {1}"), resp.StatusCode, resp.StatusDescription));
                            cur = new Newtonsoft.Json.JsonSerializer().Deserialize<WLID_FolderItem>(jr);
                        }
                    }
                    else
                        throw new FolderMissingException(string.Format(LC.L("Missing the folder: {0}"), f));
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
                List();
                m_fileidCache.TryGetValue(name, out id);

                if (string.IsNullOrWhiteSpace(id) && throwIfMissing)
                    throw new System.IO.FileNotFoundException(LC.L("File not found"), name);
            }

            return id;
        }

        #region IBackend Members

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            FindFolders(true);
        }

        public string DisplayName
        {
            get { return LC.L("OneDrive"); }
        }

        public string ProtocolKey
        {
            get { return "onedrive"; }
        }

        public List<IFileEntry> List()
        {
            var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, string.Format(FOLDER_TEMPLATE, FolderID), Library.Utility.Uri.UrlEncode(AccessToken));

            var res = GetJSONData<WLID_DataItem>(url);
            var files = new List<IFileEntry>();

            m_fileidCache.Clear();

            if (res != null && res.data != null)
                foreach (var r in res.data)
                {
                    m_fileidCache.Add(r.name, r.id);

                    var fe = new FileEntry(r.name, r.size, r.updated_time, r.updated_time);
                    fe.IsFolder = string.Equals(r.type, "folder", StringComparison.InvariantCultureIgnoreCase);
                    files.Add(fe);
                }

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
            var id = GetFileID(remotename);
            var url = string.Format("{0}/{1}?access_token={2}", WLID_SERVER, id,  Library.Utility.Uri.UrlEncode(AccessToken));
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "DELETE";

            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = (HttpWebResponse)areq.GetResponse())
            {
                if ((int)resp.StatusCode < 200 || (int)resp.StatusCode > 299)
                    throw new ProtocolViolationException(string.Format(LC.L("Unexpected error code: {0} - {1}"), resp.StatusCode, resp.StatusDescription));
                m_fileidCache.Remove(remotename);
            }
        }        

        public IList<ICommandLineArgument> SupportedCommands
        {
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, LC.L("The authorization code"), string.Format(LC.L("The authorization token retrieved from {0}"), WLID_LOGIN)),
                });
            }
        }

        public string Description
        {
            get { return LC.L("Stores files on Microsoft OneDrive"); }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public void Put(string remotename, System.IO.Stream stream)
        {
            var url = string.Format("{0}/{1}/files/{2}?access_token={3}", WLID_SERVER, FolderID, Utility.Uri.UrlPathEncode(remotename),  Library.Utility.Uri.UrlEncode(AccessToken));
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
            using (var reqs = areq.GetRequestStream())
                Utility.Utility.CopyStream(stream, reqs);

            using (var resp = (HttpWebResponse)areq.GetResponse())
            using (var rs = areq.GetResponseStream())
            using (var tr = new System.IO.StreamReader(rs))
            using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
            {
                var nf = new Newtonsoft.Json.JsonSerializer().Deserialize<WLID_FolderItem>(jr);
                m_fileidCache[remotename] = nf.id;
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            var id = GetFileID(remotename);
            var url = string.Format("{0}/{1}/content?access_token={2}", WLID_SERVER, id,  Library.Utility.Uri.UrlEncode(AccessToken));
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = USER_AGENT;

            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = (HttpWebResponse)areq.GetResponse())
            using (var rs = areq.GetResponseStream())
                Utility.Utility.CopyStream(rs, stream);
        }

        #endregion
    }
}
