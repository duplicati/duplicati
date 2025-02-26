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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
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
        private readonly string m_path;
        private readonly bool m_deleteFromTrash;

        private string m_currentfolder;
        private readonly Dictionary<string, string> m_filecache = new Dictionary<string, string>();

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
                    if (ex is WebException exception && exception.Response is HttpWebResponse hs)
                    {
                        string rawdata = null;
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

            m_path = Util.AppendDirSeparator(uri.HostAndPath, "/");

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            m_deleteFromTrash = Library.Utility.Utility.ParseBoolOption(options, REALLY_DELETE_OPTION);

            m_oauth = new BoxHelper(authid);
        }

        private async Task<string> GetCurrentFolderWithCacheAsync(CancellationToken cancelToken)
        {
            if (m_currentfolder == null)
                await GetCurrentFolderAsync(false, cancelToken).ConfigureAwait(false);

            return m_currentfolder;
        }

        private async Task GetCurrentFolderAsync(bool create, CancellationToken cancelToken)
        {
            var parentid = "0";

            foreach (var p in m_path.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var el = (MiniFolder)await PagedFileListResponse(parentid, true, cancelToken).FirstOrDefaultAsync(x => x.Name == p).ConfigureAwait(false);
                if (el == null)
                {
                    if (!create)
                        throw new FolderMissingException();

                    el = await m_oauth.PostAndGetJSONDataAsync<ListFolderResponse>(
                        string.Format("{0}/folders", BOX_API_URL),
                        cancelToken,
                        new CreateItemRequest() { Name = p, Parent = new IDReference() { ID = parentid } }
                    ).ConfigureAwait(false);
                }

                parentid = el.ID;
            }

            m_currentfolder = parentid;
        }

        private async Task<string> GetFileIDAsync(string name, CancellationToken cancelToken)
        {
            if (m_filecache.ContainsKey(name))
                return m_filecache[name];

            // Make sure we enumerate this, otherwise the m_filecache is empty.
            var currentFolder = await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false);
            await PagedFileListResponse(currentFolder, false, cancelToken).LastOrDefaultAsync().ConfigureAwait(false);

            if (m_filecache.ContainsKey(name))
                return m_filecache[name];

            throw new FileMissingException();
        }

        private async IAsyncEnumerable<FileEntity> PagedFileListResponse(string parentid, bool onlyfolders, [EnumeratorCancellation] CancellationToken cancelToken)
        {
            var offset = 0;
            var done = false;

            if (!onlyfolders)
                m_filecache.Clear();

            do
            {
                var resp = await m_oauth.GetJSONDataAsync<ShortListResponse>($"{BOX_API_URL}/folders/{parentid}/items?limit={PAGE_SIZE}&offset={offset}&fields=name,size,modified_at", cancelToken).ConfigureAwait(false);

                if (resp.Entries == null || resp.Entries.Length == 0)
                    break;

                foreach (var f in resp.Entries)
                {
                    if (onlyfolders && f.Type != "folder")
                    {
                        done = true;
                        break;
                    }
                    else
                    {
                        if (!onlyfolders && f.Type == "file")
                            m_filecache[f.Name] = f.ID;

                        yield return f;
                    }
                }

                offset = offset + PAGE_SIZE;

                if (offset >= resp.TotalCount)
                    break;

            } while (!done);
        }

        #region IStreamingBackend implementation

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var currentFolder = await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false);
            var createreq = new CreateItemRequest()
            {
                Name = remotename,
                Parent = new IDReference()
                {
                    ID = currentFolder
                }
            };

            if (m_filecache.Count == 0)
                await PagedFileListResponse(currentFolder, false, cancelToken).LastOrDefaultAsync().ConfigureAwait(false);

            var existing = m_filecache.ContainsKey(remotename);

            try
            {
                string url;
                var items = new List<MultipartItem>(2);

                if (existing)
                    url = $"{BOX_UPLOAD_URL}/{m_filecache[remotename]}/content";
                else
                {
                    url = $"{BOX_UPLOAD_URL}/content";
                    items.Add(new MultipartItem(createreq, "attributes"));
                }

                items.Add(new MultipartItem(stream, "file", remotename));

                var res = (await m_oauth.PostMultipartAndGetJSONDataAsync<FileList>(url, null, cancelToken, items.ToArray())).Entries.First();
                m_filecache[remotename] = res.ID;
            }
            catch
            {
                m_filecache.Clear();
                throw;
            }
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var fileId = await GetFileIDAsync(remotename, cancelToken).ConfigureAwait(false);
            using (var resp = await m_oauth.GetResponseAsync(string.Format("{0}/files/{1}/content", BOX_API_URL, fileId), cancelToken).ConfigureAwait(false))
            using (var rs = Duplicati.Library.Utility.AsyncHttpRequest.TrySetTimeout(resp.GetResponseStream()))
                await Library.Utility.Utility.CopyStreamAsync(rs, stream, cancelToken).ConfigureAwait(false);
        }

        #endregion

        #region IBackend implementation

        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var currentFolder = await GetCurrentFolderWithCacheAsync(cancelToken).ConfigureAwait(false);
            await foreach (var n in PagedFileListResponse(currentFolder, false, cancelToken).ConfigureAwait(false))
                yield return new FileEntry(n.Name, n.Size, n.ModifiedAt, n.ModifiedAt) { IsFolder = n.Type == "folder" };
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            var fileid = await GetFileIDAsync(remotename, cancelToken).ConfigureAwait(false);
            try
            {
                using (var r = await m_oauth.GetResponseAsync(string.Format("{0}/files/{1}", BOX_API_URL, fileid), cancelToken, null, "DELETE").ConfigureAwait(false))
                {
                }

                if (m_deleteFromTrash)
                    using (var r = await m_oauth.GetResponseAsync(string.Format("{0}/files/{1}/trash", BOX_API_URL, fileid), cancelToken, null, "DELETE").ConfigureAwait(false))
                    {
                    }
            }
            catch
            {
                m_filecache.Clear();
                throw;
            }
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            return GetCurrentFolderAsync(true, cancellationToken);
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

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new string[] {
            new System.Uri(BOX_API_URL).Host,
            new System.Uri(BOX_UPLOAD_URL).Host
        });

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

