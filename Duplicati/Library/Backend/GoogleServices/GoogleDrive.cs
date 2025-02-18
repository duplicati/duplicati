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

using Duplicati.Library.Backend.GoogleServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.GoogleDrive
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class GoogleDrive : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend, ITimeoutExemptBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string TEAMDRIVE_ID = "googledrive-teamdrive-id";
        private const string FOLDER_MIMETYPE = "application/vnd.google-apps.folder";

        private readonly string m_path;
        private readonly string m_teamDriveID;
        private readonly OAuthHelper m_oauth;
        private readonly Dictionary<string, GoogleDriveFolderItem[]> m_filecache;

        private string m_currentFolderId;

        public GoogleDrive()
        {
        }

        public GoogleDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_path = Util.AppendDirSeparator(uri.HostAndPath, "/");

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            if (options.ContainsKey(TEAMDRIVE_ID))
                m_teamDriveID = options[TEAMDRIVE_ID];

            m_oauth = new OAuthHelper(authid, this.ProtocolKey) { AutoAuthHeader = true };
            m_filecache = new Dictionary<string, GoogleDriveFolderItem[]>();
        }

        private async Task<string> GetFolderIdAsync(string path, bool autocreate, CancellationToken cancelToken)
        {
            var curparent = m_teamDriveID ?? (await GetAboutInfoAsync(cancelToken).ConfigureAwait(false)).rootFolderId;
            var curdisplay = new StringBuilder("/");

            foreach (var p in path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var res = await ListFolder(curparent, true, p, cancelToken).ToArrayAsync(cancelToken).ConfigureAwait(false);
                if (res.Length == 0)
                {
                    if (!autocreate)
                        throw new FolderMissingException();

                    curparent = (await CreateFolderAsync(p, curparent, cancelToken)).id;
                }
                else if (res.Length > 1)
                {
                    throw new UserInformationException(Strings.GoogleDrive.MultipleEntries(p, curdisplay.ToString()), "GoogleDriveMultipleEntries");
                }
                else
                {
                    curparent = res[0].id;
                }

                curdisplay.Append(p).Append("/");
            }

            return curparent;
        }

        private async Task<string> GetCurrentFolderIdAsync(CancellationToken cancelToken)
        {
            if (string.IsNullOrEmpty(m_currentFolderId))
                m_currentFolderId = await GetFolderIdAsync(m_path, false, cancelToken).ConfigureAwait(false);

            return m_currentFolderId;
        }

        private async Task<GoogleDriveFolderItem[]> GetFileEntriesAsync(string remotename, bool throwMissingException, CancellationToken cancelToken)
        {
            GoogleDriveFolderItem[] entries;
            m_filecache.TryGetValue(remotename, out entries);

            if (entries != null)
                return entries;

            var currentFolderId = await GetCurrentFolderIdAsync(cancelToken).ConfigureAwait(false);
            entries = await ListFolder(currentFolderId, false, remotename, cancelToken).ToArrayAsync(cancelToken).ConfigureAwait(false);

            if (entries == null || entries.Length == 0)
            {
                if (throwMissingException)
                    throw new FileMissingException();
                else
                    return null;
            }

            return m_filecache[remotename] = entries;
        }

        private static string EscapeTitleEntries(string title)
        {
            return title.Replace("'", "\\'");
        }

        #region IStreamingBackend implementation

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                // Figure out if we update or create the file
                if (m_filecache.Count == 0)
                    await foreach (var file in ListAsync(cancelToken).ConfigureAwait(false)) { /* Enumerate the full listing */ }

                GoogleDriveFolderItem[] files;
                m_filecache.TryGetValue(remotename, out files);

                string fileId = null;
                if (files != null)
                {
                    if (files.Length == 1)
                        fileId = files[0].id;
                    else
                        await DeleteAsync(remotename, cancelToken);
                }

                var isUpdate = !string.IsNullOrWhiteSpace(fileId);

                var url = WebApi.GoogleDrive.PutUrl(fileId, m_teamDriveID != null);
                var currentFolderId = await GetCurrentFolderIdAsync(cancelToken).ConfigureAwait(false);

                var item = new GoogleDriveFolderItem
                {
                    title = remotename,
                    description = remotename,
                    mimeType = "application/octet-stream",
                    labels = new GoogleDriveFolderItemLabels { hidden = true },
                    parents = [new GoogleDriveParentReference { id = currentFolderId }],
                    teamDriveId = m_teamDriveID
                };

                var res = await GoogleCommon.ChunkedUploadWithResumeAsync<GoogleDriveFolderItem, GoogleDriveFolderItem>(m_oauth, item, url, stream, cancelToken, isUpdate ? "PUT" : "POST").ConfigureAwait(false);
                m_filecache[remotename] = [res];
            }
            catch
            {
                m_filecache.Clear();
                throw;
            }
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            // Prevent repeated download url lookups
            if (m_filecache.Count == 0)
                await foreach (var file in ListAsync(cancelToken).ConfigureAwait(false)) { /* Enumerate the full listing */ }

            var fileId = (await GetFileEntriesAsync(remotename, true, cancelToken).ConfigureAwait(false)).OrderByDescending(x => x.createdDate).First().id;

            var req = m_oauth.CreateRequest(WebApi.GoogleDrive.GetUrl(fileId));
            var areq = new AsyncHttpRequest(req);
            using (var resp = (HttpWebResponse)areq.GetResponse())
            using (var rs = areq.GetResponseStream())
                await Duplicati.Library.Utility.Utility.CopyStreamAsync(rs, stream, cancelToken);
        }

        #endregion

        #region IBackend implementation

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)

        {
            bool success = false;
            try
            {
                m_filecache.Clear();

                // For now, this class assumes that List() fully populates the file cache
                var currentFolderId = await GetCurrentFolderIdAsync(cancelToken).ConfigureAwait(false);
                await foreach (var n in ListFolder(currentFolderId, null, null, cancelToken).ConfigureAwait(false))
                {
                    FileEntry fe = null;

                    if (n.fileSize == null)
                        fe = new FileEntry(n.title);
                    else if (n.modifiedDate == null)
                        fe = new FileEntry(n.title, n.fileSize.Value);
                    else
                        fe = new FileEntry(n.title, n.fileSize.Value, n.modifiedDate.Value, n.modifiedDate.Value);

                    if (fe != null)
                    {
                        fe.IsFolder = FOLDER_MIMETYPE.Equals(n.mimeType, StringComparison.OrdinalIgnoreCase);

                        if (!fe.IsFolder)
                        {
                            GoogleDriveFolderItem[] lst;
                            if (!m_filecache.TryGetValue(fe.Name, out lst))
                            {
                                m_filecache[fe.Name] = new GoogleDriveFolderItem[] { n };
                            }
                            else
                            {
                                Array.Resize(ref lst, lst.Length + 1);
                                lst[lst.Length - 1] = n;
                            }
                        }

                        yield return fe;
                    }
                }

                success = true;
            }
            finally
            {
                // If the enumeration either failed or didn't complete, clear the file cache.
                // This way, other operations which require a fully populated file cache will see an empty one and can populate it themselves.
                if (!success)
                {
                    m_filecache.Clear();
                }
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = System.IO.File.Create(filename))
                await GetAsync(remotename, fs, cancelToken);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                foreach (var fileid in from n in await GetFileEntriesAsync(remotename, true, cancelToken).ConfigureAwait(false) select n.id)
                {
                    var url = WebApi.GoogleDrive.DeleteUrl(Library.Utility.Uri.UrlPathEncode(fileid), m_teamDriveID);
                    await m_oauth.GetJSONDataAsync<object>(url, cancelToken, x =>
                    {
                        x.Method = "DELETE";
                    });
                }

                m_filecache.Remove(remotename);
            }
            catch
            {
                m_filecache.Clear();

                throw;
            }
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            m_filecache.Clear();
            m_currentFolderId = await GetFolderIdAsync(m_path, true, cancelToken).ConfigureAwait(false);
        }

        public string DisplayName
        {
            get
            {
                return Strings.GoogleDrive.DisplayName;
            }
        }

        public string ProtocolKey
        {
            get
            {
                return "googledrive";
            }
        }

        public System.Collections.Generic.IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION,
                                            CommandLineArgument.ArgumentType.Password,
                                            Strings.GoogleDrive.AuthidShort,
                                            Strings.GoogleDrive.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("googledrive"))),
                    new CommandLineArgument(TEAMDRIVE_ID,
                                            CommandLineArgument.ArgumentType.String,
                                            Strings.GoogleDrive.TeamDriveIdShort,
                                            Strings.GoogleDrive.TeamDriveIdLong),
                });

        public string Description
        {
            get
            {
                return Strings.GoogleDrive.Description;
            }
        }

        #endregion

        #region IQuotaEnabledBackend implementation
        public async Task<IQuotaInfo> GetQuotaInfoAsync(CancellationToken cancelToken)
        {
            try
            {
                var about = await this.GetAboutInfoAsync(cancelToken).ConfigureAwait(false);
                return new QuotaInfo(about.quotaBytesTotal ?? -1, about.quotaBytesTotal - about.quotaBytesUsed ?? -1);
            }
            catch
            {
                return null;
            }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(WebApi.GoogleDrive.Hosts());

        #endregion

        #region IRenameEnabledBackend implementation
        public async Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
        {
            try
            {
                var files = await GetFileEntriesAsync(oldname, true, cancellationToken).ConfigureAwait(false);
                if (files.Length > 1)
                    throw new UserInformationException(string.Format(Strings.GoogleDrive.MultipleEntries(oldname, m_path)),
                                                       "GoogleDriveMultipleEntries");

                using var stream = new MemoryStream();
                await GetAsync(oldname, stream, cancellationToken).ConfigureAwait(false);
                await PutAsync(newname, stream, cancellationToken).ConfigureAwait(false);
                await DeleteAsync(oldname, cancellationToken).ConfigureAwait(false);

                m_filecache.Remove(oldname);
            }
            catch
            {
                m_filecache.Clear();

                throw;
            }

        }
        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion

        private class GoogleDriveParentReference
        {
            public string id { get; set; }
        }

        private class GoogleDriveListResponse
        {
            public string nextPageToken { get; set; }
            public GoogleDriveFolderItem[] items { get; set; }
        }

        private class GoogleDriveFolderItemLabels
        {
            public bool hidden { get; set; }
        }

        private class GoogleDriveFolderItem
        {
            public string id { get; set; }
            public string title { get; set; }
            public string description { get; set; }
            public string mimeType { get; set; }
            public GoogleDriveFolderItemLabels labels { get; set; }
            public DateTime? createdDate { get; set; }
            public DateTime? modifiedDate { get; set; }
            public long? fileSize { get; set; }
            public string teamDriveId { get; set; }
            public GoogleDriveParentReference[] parents { get; set; }
        }

        private class GoogleDriveAboutResponse
        {
            public long? quotaBytesTotal { get; set; }
            public long? quotaBytesUsed { get; set; }
            public string rootFolderId { get; set; }
        }

        private async IAsyncEnumerable<GoogleDriveFolderItem> ListFolder(string parentfolder, bool? onlyFolders, string name, [EnumeratorCancellation] CancellationToken cancelToken)
        {
            var fileQuery = new string[] {
                string.IsNullOrEmpty(name) ? null : string.Format("title = '{0}'", EscapeTitleEntries(name)),
                onlyFolders == null ? null : string.Format("mimeType {0}= '{1}'", onlyFolders.Value ? "" : "!", FOLDER_MIMETYPE),
                string.Format("'{0}' in parents", EscapeTitleEntries(parentfolder)),
                "trashed=false"
            };

            var encodedFileQuery = Library.Utility.Uri.UrlEncode(string.Join(" and ", fileQuery.Where(x => x != null)));
            var url = WebApi.GoogleDrive.ListUrl(encodedFileQuery, m_teamDriveID);

            while (true)
            {
                var res = await m_oauth.GetJSONDataAsync<GoogleDriveListResponse>(url, cancelToken).ConfigureAwait(false);
                foreach (var n in res.items)
                    yield return n;

                var token = res.nextPageToken;
                if (string.IsNullOrWhiteSpace(token))
                    break;

                url = WebApi.GoogleDrive.ListUrl(encodedFileQuery, m_teamDriveID, token);
            }
        }

        private Task<GoogleDriveAboutResponse> GetAboutInfoAsync(CancellationToken cancelToken)
        {
            return m_oauth.GetJSONDataAsync<GoogleDriveAboutResponse>(WebApi.GoogleDrive.AboutInfoUrl(), cancelToken);
        }

        private Task<GoogleDriveFolderItem> CreateFolderAsync(string name, string parent, CancellationToken cancelToken)
        {
            var folder = new GoogleDriveFolderItem()
            {
                title = name,
                description = name,
                mimeType = FOLDER_MIMETYPE,
                labels = new GoogleDriveFolderItemLabels { hidden = true },
                parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference { id = parent } }
            };

            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(folder));

            return m_oauth.GetJSONDataAsync<GoogleDriveFolderItem>(
                WebApi.GoogleDrive.CreateFolderUrl(m_teamDriveID),
                cancelToken,
                x =>
                {
                    x.Method = "POST";
                    x.ContentType = "application/json; charset=UTF-8";
                    x.ContentLength = data.Length;

                }, async (req, ct) =>
                {
                    using (var rs = req.GetRequestStream())
                        await rs.WriteAsync(data, 0, data.Length, ct).ConfigureAwait(false);
                });
        }
    }
}

