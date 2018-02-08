//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using Duplicati.Library.Interface;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net;

namespace Duplicati.Library.Backend.Box
{
    public class BoxBackend : IBackend, IStreamingBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string REALLY_DELETE_OPTION = "box-delete-from-trash";

        private const string BOX_API_URL = "https://api.box.com/2.0";
        private const string BOX_UPLOAD_URL = "https://upload.box.com/api/2.0/files";

        private const int PAGE_SIZE = 200;

        private BoxHelper m_oauth;
        private string m_path;
        private bool m_deleteFromTrash;

        private string m_currentfolder;
        private Dictionary<string, string> m_filecache;

        private class BoxHelper : OAuthHelper
        {
            public BoxHelper(string authid)
                : base(authid, "box.com")
            {
                AutoAuthHeader = true;
            }

            protected override void ParseException(Exception ex)
            {
                Exception newex = null;
                try
                {
                    if (ex is WebException && (ex as WebException).Response is HttpWebResponse)
                    {
                        string rawdata = null;
                        var hs = (ex as WebException).Response as HttpWebResponse;
                        using(var rs = Library.Utility.AsyncHttpRequest.TrySetTimeout(hs.GetResponseStream()))
                        using(var sr = new System.IO.StreamReader(rs))
                            rawdata = sr.ReadToEnd();

                        if (string.IsNullOrWhiteSpace(rawdata))
                            return;
                        
                        newex = new Exception("Raw message: " + rawdata);

                        var msg = JsonConvert.DeserializeObject<ErrorResponse>(rawdata);
                        newex = new Exception(string.Format("{0} - {1}: {2}", msg.Status, msg.Code, msg.Message));

                        /*if (msg.ContextInfo != null && msg.ContextInfo.Length > 0)
                            newex = new Exception(string.Format("{0} - {1}: {2}{3}{4}", msg.Status, msg.Code, msg.Message, Environment.NewLine, string.Join("; ", from n in msg.ContextInfo select n.Message)));
                        */
                    }
                }
                catch(Exception ex2)
                {
                    Console.WriteLine(ex2);
                }

                if (newex != null)
                    throw newex;                
            }
        }

        public BoxBackend()
        {
        }

        public BoxBackend(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_path = uri.HostAndPath;
            if (!m_path.EndsWith("/", StringComparison.Ordinal))
                m_path += "/";
            
            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            m_deleteFromTrash = Library.Utility.Utility.ParseBoolOption(options, REALLY_DELETE_OPTION);

            m_oauth = new BoxHelper(authid);
        }

        private string CurrentFolder
        {
            get
            {
                if (m_currentfolder == null)
                    GetCurrentFolder(false);
                
                return m_currentfolder;
            }
        }

        private void GetCurrentFolder(bool create)
        {
            var parentid = "0";

            foreach(var p in m_path.Split(new string[] {"/"}, StringSplitOptions.RemoveEmptyEntries))
            {
                var el = (MiniFolder)PagedFileListResponse(parentid, true).Where(x => x.Name == p).FirstOrDefault();
                if (el == null)
                {
                    if (!create)
                        throw new FolderMissingException();

                    el = m_oauth.PostAndGetJSONData<ListFolderResponse>(
                        string.Format("{0}/folders", BOX_API_URL),
                        new CreateItemRequest() { Name = p, Parent = new IDReference() { ID = parentid } }
                    );
                }

                parentid = el.ID;
            }

            m_currentfolder = parentid;
        }

        private string GetFileID(string name)
        {
            if (m_filecache != null && m_filecache.ContainsKey(name))
                return m_filecache[name];

            // Make sure we enumerate this, otherwise the m_filecache is not assigned
            PagedFileListResponse(CurrentFolder, false).LastOrDefault();

            if (m_filecache != null && m_filecache.ContainsKey(name))
                return m_filecache[name];

            throw new FileMissingException();
        }

        private IEnumerable<FileEntity> PagedFileListResponse(string parentid, bool onlyfolders)
        {
            var offset = 0;
            var done = false;

            if (!onlyfolders)
                m_filecache = null;
            
            var cache = onlyfolders ? null : new Dictionary<string, string>();
            do
            {
                var resp = m_oauth.GetJSONData<ShortListResponse>(string.Format("{0}/folders/{1}/items?limit={2}&offset={3}&fields=name,size,modified_at", BOX_API_URL, parentid, PAGE_SIZE, offset));

                if (resp.Entries == null || resp.Entries.Length == 0)
                    break;

                foreach(var f in resp.Entries)
                {
                    if (onlyfolders && f.Type != "folder")
                    {
                        done = true;
                        break;
                    }
                    else
                    {
                        if (!onlyfolders && f.Type == "file")
                            cache[f.Name] = f.ID;
                        
                        yield return f;
                    }
                }

                offset = offset + PAGE_SIZE;

                if (offset >= resp.TotalCount)
                    break;

            } while(!done);

            if (!onlyfolders)
                m_filecache = cache;
        }

        #region IStreamingBackend implementation

        public void Put(string remotename, System.IO.Stream stream)
        {
            var createreq = new CreateItemRequest() {
                Name = remotename,
                Parent = new IDReference() {
                    ID = CurrentFolder
                }
            };

            if (m_filecache == null)
                PagedFileListResponse(CurrentFolder, false);

            var existing = m_filecache.ContainsKey(remotename);

            try
            {
                FileEntity res;
                if (existing)
                {
                    res = m_oauth.PostMultipartAndGetJSONData<FileList>(
                        string.Format("{0}/{1}/content", BOX_UPLOAD_URL, m_filecache[remotename]),
                        new MultipartItem(stream, name: "file", filename: remotename)
                    ).Entries.First();
                }
                else
                {

                    res = m_oauth.PostMultipartAndGetJSONData<FileList>(
                        string.Format("{0}/content", BOX_UPLOAD_URL),
                        new MultipartItem(createreq, name: "attributes"),
                        new MultipartItem(stream, name: "file", filename: remotename)
                    ).Entries.First();
                }

                m_filecache[remotename] = res.ID;
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            using (var resp = m_oauth.GetResponse(string.Format("{0}/files/{1}/content", BOX_API_URL, GetFileID(remotename))))
            using(var rs = Duplicati.Library.Utility.AsyncHttpRequest.TrySetTimeout(resp.GetResponseStream()))
                Library.Utility.Utility.CopyStream(rs, stream);
        }

        #endregion

        #region IBackend implementation

        public System.Collections.Generic.IEnumerable<IFileEntry> List()
        {
            return
                from n in PagedFileListResponse(CurrentFolder, false)
                select (IFileEntry)new FileEntry(n.Name, n.Size, n.ModifiedAt, n.ModifiedAt) { IsFolder = n.Type == "folder" };
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
            var fileid = GetFileID(remotename);
            try
            {
                using(var r = m_oauth.GetResponse(string.Format("{0}/files/{1}", BOX_API_URL, fileid), null, "DELETE"))
                {
                }

                if (m_deleteFromTrash)
                    using(var r = m_oauth.GetResponse(string.Format("{0}/files/{1}/trash", BOX_API_URL, fileid), null, "DELETE"))
                    {
                    }
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            GetCurrentFolder(true);
        }

        public string DisplayName
        {
            get
            {
                return Strings.Box.DisplayName;
            }
        }

        public string ProtocolKey
        {
            get
            {
                return "box";
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Box.AuthidShort, Strings.Box.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("box.com"))),
                    new CommandLineArgument(REALLY_DELETE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Box.ReallydeleteShort, Strings.Box.ReallydeleteLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.Box.Description;
            }
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion

        private class MiniUser : IDReference
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("login")]
            public string Login { get; set; }
        }

        private class MiniFolder : IDReference
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("etag")]
            public string ETag { get; set; }
            [JsonProperty("sequence_id")]
            public string SequenceID { get; set; }
        }

        private class FileEntity : MiniFolder
        {
            public FileEntity() { Size = -1; }

            [JsonProperty("sha1")]
            public string SHA1 { get; set; }

            [JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
            public long Size { get; set; }
            [JsonProperty("modified_at", NullValueHandling = NullValueHandling.Ignore)]
            public DateTime ModifiedAt { get; set; }
        }

        private class FolderList
        {
            [JsonProperty("total_count")]
            public long TotalCount { get; set; }
            [JsonProperty("entries")]
            public MiniFolder[] Entries { get; set; }
        }

        private class FileList
        {
            [JsonProperty("total_count")]
            public long TotalCount { get; set; }
            [JsonProperty("entries")]
            public FileEntity[] Entries { get; set; }
            [JsonProperty("offset")]
            public long Offset { get; set; }
            [JsonProperty("limit")]
            public long Limit { get; set; }
        }

        private class SharePermissions
        {
            [JsonProperty("can_download")]
            public bool CanDownload { get; set; }
            [JsonProperty("can_preview")]
            public bool CanPreview { get; set; }
        }

        private class UploadEmail
        {
            [JsonProperty("access")]
            public string Access { get; set; }
            [JsonProperty("email")]
            public string Email { get; set; }
        }

        private class SharedLink
        {
            [JsonProperty("url")]
            public string Url { get; set; }
            [JsonProperty("download_url")]
            public string DownloadUrl { get; set; }
            [JsonProperty("vanity_url")]
            public string VanityUrl { get; set; }
            [JsonProperty("is_password_enabled")]
            public bool IsPasswordEnabled { get; set; }
            [JsonProperty("unshared_at")]
            public DateTime? UnsharedAt { get; set; }
            [JsonProperty("download_count")]
            public long DownloadCount { get; set; }
            [JsonProperty("preview_count")]
            public long PreviewCount { get; set; }
            [JsonProperty("access")]
            public string Access { get; set; }
            [JsonProperty("permissions")]
            public SharePermissions Permissions { get; set; }
         }

        private class ListFolderResponse : MiniFolder
        {
            [JsonProperty("created_at")]
            public DateTime CreatedAt { get; set; }
            [JsonProperty("modified_at")]
            public DateTime ModifiedAt { get; set; }
            [JsonProperty("description")]
            public string Description { get; set; }
            [JsonProperty("size")]
            public long Size { get; set; }

            [JsonProperty("path_collection")]
            public FolderList PathCollection { get; set; }

            [JsonProperty("created_by")]
            public MiniUser CreatedBy { get; set; }
            [JsonProperty("modified_by")]
            public MiniUser ModifiedBy { get; set; }
            [JsonProperty("owned_by")]
            public MiniUser OwnedBy { get; set; }

            [JsonProperty("shared_link")]
            public MiniUser SharedLink { get; set; }

            [JsonProperty("folder_upload_email")]
            public UploadEmail FolderUploadEmail { get; set; }

            [JsonProperty("parent")]
            public MiniFolder Parent { get; set; }

            [JsonProperty("item_status")]
            public string ItemStatus { get; set; }

            [JsonProperty("item_collection")]
            public FileList ItemCollection { get; set; }

        }

        private class OrderEntry
        {
            [JsonProperty("by")]
            public string By { get; set; }
            [JsonProperty("direction")]
            public string Direction { get; set; }
        }

        private class ShortListResponse : FileList
        {
            [JsonProperty("order")]
            public OrderEntry[] Order { get; set; }

        }

        private class IDReference
        {
            [JsonProperty("id")]
            public string ID { get; set; }
        }

        private class CreateItemRequest
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("parent")]
            public IDReference Parent { get; set; }
        }

        private class ErrorResponse
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("status")]
            public int Status { get; set; }
            [JsonProperty("code")]
            public string Code { get; set; }
            // Not working exactly his way ...
            //[JsonProperty("context_info")]
            //public ErrorItem[] ContextInfo { get; set; }
            [JsonProperty("help_url")]
            public string HelpUrl { get; set; }
            [JsonProperty("message")]
            public string Message { get; set; }
            [JsonProperty("request_id")]
            public string RequestId { get; set; }

        }

        private class ErrorItem
        {
            [JsonProperty("reason")]
            public string Reason { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("message")]
            public string Message { get; set; }
        }

    }
}

