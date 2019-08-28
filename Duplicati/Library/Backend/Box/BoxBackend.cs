#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Box
{
    // ReSharper disable once ClassNeverInstantiated.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class BoxBackend : IBackend, IStreamingBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<BoxBackend>();

        private const string AUTHID_OPTION = "authid";
        private const string REALLY_DELETE_OPTION = "box-delete-from-trash";

        private const string BOX_API_URL = "https://api.box.com/2.0";
        private const string BOX_UPLOAD_URL = "https://upload.box.com/api/2.0/files";

        private const int PAGE_SIZE = 200;

        private readonly BoxHelper m_oauth;
        private readonly string m_rootPath;
        private readonly bool m_deleteFromTrash;

        private static readonly Dictionary<string, string> m_folderCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> m_fileCache = new Dictionary<string, string>();

        private string m_rootFolderId;

        #region BoxHelper
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
                        using (var rs = Library.Utility.AsyncHttpRequest.TrySetTimeout(hs.GetResponseStream()))
                        using (var sr = new System.IO.StreamReader(rs))
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
                catch (Exception ex2)
                {
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "BoxErrorParser", ex2, "Failed to parse error from Box");
                }

                if (newex != null)
                    throw newex;
            }
        }
        #endregion

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public BoxBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public BoxBackend(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_rootPath = Util.AppendDirSeparator(uri.HostAndPath, "/");

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            m_deleteFromTrash = Library.Utility.Utility.ParseBoolOption(options, REALLY_DELETE_OPTION);

            m_oauth = new BoxHelper(authid);
        }

        private string GetRootFolderId
        {
            get
            {
                if (m_rootFolderId != null)
                {
                    return m_rootFolderId;
                }

                m_rootFolderId = GetFolderId(m_rootPath, false);

                lock (m_folderCache)
                {
                    m_folderCache[m_rootPath] = m_rootFolderId;
                }

                return m_rootFolderId;
            }
        }

        private string GetFolderId(string path, bool autocreate)
        {
            var parentid = "0";
            var currentPath = "";

            foreach (var p in path.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath += $"{p}/";
                var el = (MiniFolder)GetRemoteListResponse(parentid, true).FirstOrDefault(x => x.Name == p);
                if (el == null)
                {
                    if (!autocreate)
                    {
                        throw new FolderMissingException();
                    }

                    el = m_oauth.PostAndGetJSONData<ListFolderResponse>(
                        $"{BOX_API_URL}/folders",
                        new CreateItemRequest() { Name = p, Parent = new IDReference() { ID = parentid } }
                    );
                }

                parentid = el.ID;
            }

            return parentid;
        }

        private string GetFileID(string name)
        {
            if (m_fileCache.ContainsKey(name))
            {
                return m_fileCache[name];
            }

            // Make sure we enumerate this, otherwise the m_filecache is empty.
            GetRemoteList("", GetRootFolderId).Where(x => x.IsFolder == false);

            if (m_fileCache.ContainsKey(name)){
                return m_fileCache[name];}

            throw new FileMissingException();
        }

        private IEnumerable<RemoteFileEntry> GetRemoteList(string path, string folderId)
        {
            List<RemoteFileEntry> foundFiles = new List<RemoteFileEntry>();

            var allItems = from n in GetRemoteListResponse(folderId, false)
                select new RemoteFileEntry
                {
                    Name = string.IsNullOrEmpty(path) ? n.Name : $"{path}/{n.Name}",
                    Size = n.Size,
                    LastModification = n.ModifiedAt,
                    IsFolder = n.Type == "folder",
                    Path = path,
                    ID = n.ID
                };

            foreach (var item in allItems)
            {
                if (item.IsFolder)
                {
                    lock (m_folderCache)
                    {
                        m_folderCache[item.Name] = item.ID;
                    }
                }
                else
                {
                    m_fileCache[item.Name] = item.ID;
                }

                foundFiles.Add(item);

                if (item.IsFolder)
                {
                    foundFiles.AddRange(GetRemoteList(string.IsNullOrEmpty(path) ? item.Name : $"{path}/{item.Name}", item.ID));
                }
            }

            return foundFiles;
        }

        private IEnumerable<BoxFileEntity> GetRemoteListResponse(string parentid, bool onlyfolders)
        {
            var offset = 0;

            do
            {
                var resp = m_oauth.GetJSONData<ShortListResponse>($"{BOX_API_URL}/folders/{parentid}/items?limit={PAGE_SIZE}&offset={offset}&fields=name,size,modified_at");

                if (resp.Entries == null || resp.Entries.Length == 0)
                {
                    break;
                }

                foreach (BoxFileEntity f in resp.Entries)
                {
                    yield return f;
                }

                offset += PAGE_SIZE;

                if (offset >= resp.TotalCount)
                {
                    break;
                }

            } while (true);
        }

        #region IStreamingBackend implementation

        private string NormalizePath(string path)
        {
            return path.Replace(@"\", "/");
        }

        private string GetPath(string remotename)
        {
            var path = NormalizePath(remotename).Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            return path.Length > 1 ? NormalizePath(Path.Combine(path.Take(path.Length - 1).ToArray())) : string.Empty;
        }

        private string GetFilename(string remotename)
        {
            return NormalizePath(remotename).Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).Last();
        }

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            string targetFolderId;
            string targetFolderPath = GetPath($"{m_rootPath}{remotename}");
            string targetFilename = GetFilename(remotename);

            if (m_fileCache.Count == 0)
            {
                targetFolderId = GetFolderId(targetFolderPath, true);
                lock (m_folderCache)
                {
                    m_folderCache[targetFolderPath] = targetFolderId;
                }
            }
            else
            {
                lock (m_folderCache)
                {
                    targetFolderId = m_folderCache[targetFolderPath];
                }
            }

            var createreq = new CreateItemRequest()
            {
                Name = targetFilename,
                Parent = new IDReference()
                {
                    ID = targetFolderId
                }
            };
            
            var existing = m_fileCache.ContainsKey(remotename);

            try
            {
                string url;
                var items = new List<MultipartItem>(2);

                // Figure out if we update or create the file
                if (existing)
                {
                    url = $"{BOX_UPLOAD_URL}/{m_fileCache[remotename]}/content";
                }
                else
                {
                    url = $"{BOX_UPLOAD_URL}/content";
                    items.Add(new MultipartItem(createreq, "attributes"));
                }
                
                items.Add(new MultipartItem(stream, "file", targetFilename));

                var res = (await m_oauth.PostMultipartAndGetJSONDataAsync<FileList>(url, null, cancelToken, items.ToArray())).Entries.First();
                m_fileCache[remotename] = res.ID;
            }
            catch
            {
                lock (m_folderCache)
                {
                    m_folderCache.Clear();
                }
                m_fileCache.Clear();
                throw;
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            var id = GetFileID(remotename);
            using (var resp = m_oauth.GetResponse($"{BOX_API_URL}/files/{GetFileID(remotename)}/content"))
            using (var rs = Duplicati.Library.Utility.AsyncHttpRequest.TrySetTimeout(resp.GetResponseStream()))
                Library.Utility.Utility.CopyStream(rs, stream);
        }

        #endregion

        #region IBackend implementation

        public IEnumerable<IFileEntry> List()
        {
            return
                from n in GetRemoteList("", GetRootFolderId).Where(x => x.IsFolder == false)
                select new FileEntry { Name = n.Name, Size = n.Size, LastModification = n.LastModification, IsFolder = n.IsFolder };
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                return PutAsync(remotename, fs, cancelToken);
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
                using (var r = m_oauth.GetResponse($"{BOX_API_URL}/files/{fileid}", null, "DELETE"))
                {
                }

                if (m_deleteFromTrash)
                    using (var r = m_oauth.GetResponse($"{BOX_API_URL}/files/{fileid}/trash", null, "DELETE"))
                    {
                    }
            }
            catch
            {
                lock (m_folderCache)
                {
                    m_folderCache.Clear();
                }
                m_fileCache.Clear();
                throw;
            }
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            GetFolderId(m_rootPath, true);
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
            get
            {
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

        public string[] DNSName
        {
            get { return new string[] { new Uri(BOX_API_URL).Host, new Uri(BOX_UPLOAD_URL).Host }; }
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion

        public class RemoteFileEntry : FileEntry
        {
            public string ID { get; set; }
            public string Path { get; set; }
        }

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

        private class BoxFileEntity : MiniFolder
        {
            public BoxFileEntity() { Size = -1; }

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
            public BoxFileEntity[] Entries { get; set; }
            [JsonProperty("offset")]
            public long Offset { get; set; }
            [JsonProperty("limit")]
            public long Limit { get; set; }
        }

        private class UploadEmail
        {
            [JsonProperty("access")]
            public string Access { get; set; }
            [JsonProperty("email")]
            public string Email { get; set; }
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
            [JsonProperty("help_url")]
            public string HelpUrl { get; set; }
            [JsonProperty("message")]
            public string Message { get; set; }
            [JsonProperty("request_id")]
            public string RequestId { get; set; }

        }
    }
}

