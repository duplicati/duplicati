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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;

using Newtonsoft.Json;

using Duplicati.Library.Backend.GoogleServices;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.GoogleDrive
{
    public class GoogleDrive : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string DISABLE_TEAMDRIVE_OPTION = "googledrive-disable-teamdrive";

        private const string FOLDER_MIMETYPE = "application/vnd.google-apps.folder";
        private const string DRIVE_API_UPLOAD_URL = "https://www.googleapis.com/upload/drive/v2";
        // private const string DRIVE_API_URL = "https://www.googleapis.com/drive/v2";

        private readonly string m_path;
        private bool m_useTeamDrive = true;

        private readonly OAuthHelper m_oauth;
        private string m_currentFolderId;
        private Dictionary<string, GoogleDriveFolderItem[]> m_filecache;

        public GoogleDrive()
        {
        }

        public GoogleDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_path = uri.HostAndPath;
            if (!m_path.EndsWith("/", StringComparison.Ordinal))
                m_path += "/";

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            if (options.ContainsKey(DISABLE_TEAMDRIVE_OPTION))
                m_useTeamDrive = !Library.Utility.Utility.ParseBoolOption(options, DISABLE_TEAMDRIVE_OPTION);

            m_oauth = new OAuthHelper(authid, this.ProtocolKey) { AutoAuthHeader = true };
            m_filecache = new Dictionary<string, GoogleDriveFolderItem[]>();
        }

        private string GetFolderId(string path, bool autocreate = false)
        {
            var curparent = GetAboutInfo().rootFolderId;
            var curdisplay = "/";

            foreach (var p in path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var res = ListFolder(curparent, true, p).ToArray();

                if (res.Length == 0)
                {
                    if (!autocreate)
                        throw new FolderMissingException();

                    curparent = CreateFolder(p, curparent).id;
                }
                else if (res.Length > 1)
                    throw new UserInformationException(Strings.GoogleDrive.MultipleEntries(p, curdisplay), "GoogleDriveMultipleEntries");
                else
                    curparent = res[0].id;

                curdisplay += p + "/";
            }

            return curparent;
        }

        private string CurrentFolderId
        {
            get
            {
                if (string.IsNullOrEmpty(m_currentFolderId))
                    m_currentFolderId = GetFolderId(m_path);

                return m_currentFolderId;
            }
        }

        private GoogleDriveFolderItem[] GetFileEntries(string remotename, bool throwMissingException = true)
        {
            GoogleDriveFolderItem[] entries;
            m_filecache.TryGetValue(remotename, out entries);

            if (entries != null)
                return entries;

            entries = ListFolder(CurrentFolderId, false, remotename).ToArray();

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

        public void Put(string remotename, System.IO.Stream stream)
        {
            try
            {
                // Figure out if we update or create the file
                if (m_filecache.Count == 0)
                    foreach (var file in List()) { /* Enumerate the full listing */ }

                GoogleDriveFolderItem[] files;
                m_filecache.TryGetValue(remotename, out files);

                string fileId = null;
                if (files != null)
                {
                    if (files.Length == 1)
                        fileId = files[0].id;
                    else
                        Delete(remotename);
                }

                var isUpdate = !string.IsNullOrWhiteSpace(fileId);

                var values = new NameValueCollection {
                    { WebApi.GoogleDrive.QueryParam.UploadType,
                        WebApi.GoogleDrive.QueryValue.Resumable } };
                PrepareFileUploadUrl(Library.Utility.Uri.UrlPathEncode(fileId), values);

                var url = isUpdate ?
                    PrepareFileUploadUrl(Library.Utility.Uri.UrlPathEncode(fileId), values) :
                    PrepareFileUploadUrl(values);

                var item = new GoogleDriveFolderItem()
                {
                    title = remotename,
                    description = remotename,
                    mimeType = "application/octet-stream",
                    labels = new GoogleDriveFolderItemLabels { hidden = true },
                    parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference() { id = CurrentFolderId } }
                };

                var res = GoogleCommon.ChunckedUploadWithResume<GoogleDriveFolderItem, GoogleDriveFolderItem>(m_oauth, item, url, stream, isUpdate ? "PUT" : "POST");
                m_filecache[remotename] = new GoogleDriveFolderItem[] { res };
            }
            catch
            {
                m_filecache.Clear();
                throw;
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            // Prevent repeated download url lookups
            if (m_filecache.Count == 0)
                foreach (var file in List()) { /* Enumerate the full listing */ }

            var fileId = GetFileEntries(remotename).OrderByDescending(x => x.createdDate).First().id;

            var url = PrepareFileQueryUrl(fileId, new NameValueCollection{
                { WebApi.GoogleDrive.QueryParam.Alt, WebApi.GoogleDrive.QueryValue.Media }
            });
            var req = m_oauth.CreateRequest(url);
            var areq = new AsyncHttpRequest(req);
            using (var resp = (HttpWebResponse)areq.GetResponse())
            using (var rs = areq.GetResponseStream())
                Duplicati.Library.Utility.Utility.CopyStream(rs, stream);
        }

        #endregion

        #region IBackend implementation

        public IEnumerable<IFileEntry> List()
        {
            bool success = false;
            try
            {
                m_filecache.Clear();

                // For now, this class assumes that List() fully populates the file cache
                foreach (var n in ListFolder(CurrentFolderId))
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
                foreach (var fileid in from n in GetFileEntries(remotename) select n.id)
                {
                    var url = PrepareFileQueryUrl(Library.Utility.Uri.UrlPathEncode(fileid), SupportsTeamDriveParam());
                    m_oauth.GetJSONData<object>(url, x =>
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

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            m_filecache.Clear();
            m_currentFolderId = GetFolderId(m_path, true);
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

        public System.Collections.Generic.IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION,
                                            CommandLineArgument.ArgumentType.Password,
                                            Strings.GoogleDrive.AuthidShort,
                                            Strings.GoogleDrive.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("googledrive"))),
                    new CommandLineArgument(DISABLE_TEAMDRIVE_OPTION,
                                            CommandLineArgument.ArgumentType.Boolean,
                                            Strings.GoogleDrive.DisableTeamDriveShort,
                                            Strings.GoogleDrive.DisableTeamDriveLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.GoogleDrive.Description;
            }
        }

        #endregion

        #region IQuotaEnabledBackend implementation
        public IQuotaInfo Quota
        {
            get
            {
                try
                {
                    GoogleDriveAboutResponse about = this.GetAboutInfo();
                    return new QuotaInfo(about.quotaBytesTotal ?? -1, about.quotaBytesUsed ?? -1);
                }
                catch
                {
                    return null;
                }
            }
        }

        public string[] DNSName
        {
            get { return new string[] { new System.Uri(WebApi.GoogleDrive.Url.DRIVE).Host, new System.Uri(WebApi.GoogleDrive.Url.UPLOAD).Host }; }
        }

        #endregion

        #region IRenameEnabledBackend implementation
        public void Rename(string oldname, string newname)
        {
            try
            {
                var files = GetFileEntries(oldname, true);
                if (files.Length > 1)
                    throw new UserInformationException(string.Format(Strings.GoogleDrive.MultipleEntries(oldname, m_path)), "GoogleDriveMultipleEntries");

                var newfile = JsonConvert.DeserializeObject<GoogleDriveFolderItem>(JsonConvert.SerializeObject(files[0]));
                newfile.title = newname;
                newfile.parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference() { id = CurrentFolderId } };

                var url = PrepareFileQueryUrl(Library.Utility.Uri.UrlPathEncode(files[0].id));
                var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(newfile));

                var nf = m_oauth.GetJSONData<GoogleDriveFolderItem>(url, x =>
                {
                    x.Method = "PUT";
                    x.ContentLength = data.Length;
                    x.ContentType = "application/json; charset=UTF-8";
                }, x =>
                {
                    using (var rs = x.GetRequestStream())
                        rs.Write(data, 0, data.Length);
                });

                m_filecache[newname] = new GoogleDriveFolderItem[] { nf };
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

        private NameValueCollection SupportsTeamDriveParam()
        {
            return m_useTeamDrive ? new NameValueCollection {
                { WebApi.GoogleDrive.QueryParam.SupportsTeamDrive,
                    WebApi.GoogleDrive.QueryValue.True }
            } : null;
        }

        private NameValueCollection IncludeTeamDriveParam()
        {
            return m_useTeamDrive ? new NameValueCollection {
                { WebApi.GoogleDrive.QueryParam.IncludeTeamDrive,
                    WebApi.GoogleDrive.QueryValue.True } } : null;
        }

        private string PrepareFileQueryUrl(NameValueCollection values)
        {
            return Library.Utility.Uri.UriBuilder(WebApi.GoogleDrive.Url.DRIVE, WebApi.GoogleDrive.Path.File, values);
        }

        private string PrepareFileQueryUrl(string fileId, NameValueCollection values = null)
        {
            var path = WebApi.GoogleDrive.Path.File;
            path += "/" + fileId;

            return Library.Utility.Uri.UriBuilder(WebApi.GoogleDrive.Url.DRIVE, path, values);
        }

        private string PrepareFileUploadUrl(string fileId, NameValueCollection values)
        {
            var path = WebApi.GoogleDrive.Path.File;
            path += "/" + fileId;
            return Library.Utility.Uri.UriBuilder(DRIVE_API_UPLOAD_URL, path, values);
        }

        private string PrepareFileUploadUrl(NameValueCollection values)
        {
            var path = WebApi.GoogleDrive.Path.File;
            return Library.Utility.Uri.UriBuilder(DRIVE_API_UPLOAD_URL, path, values);
        }

        private class GoogleDriveParentReference
        {
            public string kind { get; set; }
            public string id { get; set; }
            public string selfLink { get; set; }
            public string parentLink { get; set; }
            public bool? isRoot { get; set; }
        }

        private class GoogleDriveListResponse
        {
            public string kind { get; set; }
            public string etag { get; set; }
            public string selfLink { get; set; }
            public string nextPageToken { get; set; }
            public string nextLink { get; set; }
            public GoogleDriveFolderItem[] items { get; set; }
        }

        private class GoogleDriveFolderItemLabels
        {
            public bool starred { get; set; }
            public bool hidden { get; set; }
            public bool thrashed { get; set; }
            public bool restricted { get; set; }
            public bool viewed { get; set; }
        }

        private class GoogleDriveFolderItem
        {
            public string kind { get; set; }
            public string id { get; set; }
            public string etag { get; set; }
            public string selfLink { get; set; }
            public string title { get; set; }
            public string description { get; set; }
            public string mimeType { get; set; }

            public GoogleDriveFolderItemLabels labels { get; set; }

            public DateTime? createdDate { get; set; }
            public DateTime? modifiedDate { get; set; }

            public string downloadUrl { get; set; }

            public string originalFilename { get; set; }
            public string md5Checksum { get; set; }
            public long? fileSize { get; set; }
            public long? quotaBytesUsed { get; set; }

            public GoogleDriveParentReference[] parents { get; set; }
        }

        private class GoogleDriveAboutResponse
        {
            public string kind { get; set; }
            public string etag { get; set; }
            public string selfLink { get; set; }
            public string name { get; set; }
            public long? quotaBytesTotal { get; set; }
            public long? quotaBytesUsed { get; set; }
            public long? quotaBytesUsedAggregate { get; set; }
            public long? quotaBytesUsedInTrash { get; set; }
            public string quotaType { get; set; }
            public string rootFolderId { get; set; }
        }

        private IEnumerable<GoogleDriveFolderItem> ListFolder(string parentfolder, bool? onlyFolders = null, string name = null)
        {
            var fileQuery = new string[] {
                string.IsNullOrEmpty(name) ? null : string.Format("title = '{0}'", EscapeTitleEntries(name)),
                onlyFolders == null ? null : string.Format("mimeType {0}= '{1}'", onlyFolders.Value ? "" : "!", FOLDER_MIMETYPE),
                string.Format("'{0}' in parents", EscapeTitleEntries(parentfolder))
            };

            var queryParams = new NameValueCollection
            {
                {WebApi.GoogleDrive.QueryParam.File,
                    Library.Utility.Uri.UrlEncode(string.Join(" and ", fileQuery.Where(x => x != null)))},
            };
            queryParams.Add(SupportsTeamDriveParam());
            queryParams.Add(IncludeTeamDriveParam());

            while (true)
            {
                var url = PrepareFileQueryUrl(queryParams);
                var res = m_oauth.GetJSONData<GoogleDriveListResponse>(url);
                foreach (var n in res.items)
                    yield return n;

                var token = res.nextPageToken;
                if (string.IsNullOrWhiteSpace(token))
                    break;

                queryParams.Set(WebApi.GoogleDrive.QueryParam.PageToken, token);
            }
        }

        private GoogleDriveAboutResponse GetAboutInfo()
        {
            var url = Library.Utility.Uri.UriBuilder(WebApi.GoogleDrive.Url.DRIVE, WebApi.GoogleDrive.Path.About);
            return m_oauth.GetJSONData<GoogleDriveAboutResponse>(url);
        }

        private GoogleDriveFolderItem CreateFolder(string name, string parent)
        {
            var url = PrepareFileQueryUrl(SupportsTeamDriveParam());

            var folder = new GoogleDriveFolderItem()
            {
                title = name,
                description = name,
                mimeType = FOLDER_MIMETYPE,
                labels = new GoogleDriveFolderItemLabels { hidden = true },
                parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference() { id = parent } }
            };

            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(folder));

            return m_oauth.GetJSONData<GoogleDriveFolderItem>(url, x =>
            {
                x.Method = "POST";
                x.ContentType = "application/json; charset=UTF-8";
                x.ContentLength = data.Length;

            }, req =>
            {
                using (var rs = req.GetRequestStream())
                    rs.Write(data, 0, data.Length);
            });
        }
    }
}

