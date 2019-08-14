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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.GoogleDrive
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class GoogleDrive : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string DRIVE_ID = "googledrive-teamdrive-id";
        private const string FOLDER_MIMETYPE = "application/vnd.google-apps.folder";

        private readonly string m_rootPath;
        private readonly string m_driveID;
        private readonly OAuthHelper m_oauth;

        private readonly Dictionary<string, GoogleDriveFolderItem[]> m_fileCache = new Dictionary<string, GoogleDriveFolderItem[]>();
        private static readonly Dictionary<string, string> m_folderCache = new Dictionary<string, string>();

        private string m_rootFolderId;

        public GoogleDrive()
        {
        }

        public GoogleDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_rootPath = Util.AppendDirSeparator(uri.HostAndPath, "/");

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
            {
                authid = options[AUTHID_OPTION];
            }

            if (options.ContainsKey(DRIVE_ID))
            {
                m_driveID = options[DRIVE_ID];
            }

            m_oauth = new OAuthHelper(authid, this.ProtocolKey) { AutoAuthHeader = true };
        }

        private string GetFolderId(string path, bool autoCreate)
        {
            lock (m_folderCache)
            {
                if (m_folderCache.ContainsKey(path))
                {
                    return m_folderCache[path];
                }
            }

            var curparent = m_driveID ?? GetAboutInfo().rootFolderId;
            var curdisplay = new StringBuilder("/");

            foreach (var p in path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                GoogleDriveFolderItem[] res = GetRemoteListResponse(curparent, true, p).ToArray();

                if (res.Length == 0)
                {
                    if (!autoCreate)
                    {
                        throw new FolderMissingException();
                    }

                    curparent = CreateFolder(p, curparent).id;
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

            lock (m_folderCache)
            {
                m_folderCache[path] = curparent;
            }

            return curparent;
        }

        private string GetRootFolderId
        {
            get
            {
                if (m_rootFolderId != null) return m_rootFolderId;

                m_rootFolderId = GetFolderId(m_rootPath, false);

                return m_rootFolderId;
            }
        }

        private GoogleDriveFolderItem[] GetFileEntries(string remotename, bool throwMissingException = true)
        {
            GoogleDriveFolderItem[] entries;

            m_fileCache.TryGetValue(remotename, out entries);

            if (entries != null)
            {
                return entries;
            }

            entries = GetRemoteListResponse(GetRootFolderId, false, remotename).ToArray();

            if (entries.Length != 0)
            {
                return m_fileCache[remotename] = entries;
            }
            if (throwMissingException)
            {
                throw new FileMissingException();
            }

            return null;
        }

        private IEnumerable<GoogleDriveFolderItem> GetRemoteListResponse(string parentfolder, bool? onlyFolders = null, string name = null)
        {
            var fileQuery = new[] {
                string.IsNullOrEmpty(name) ? null : $"title = '{EscapeTitleEntries(name)}'",
                onlyFolders == null ? null : $"mimeType {(onlyFolders.Value ? "" : "!")}= '{FOLDER_MIMETYPE}'",
                $"'{EscapeTitleEntries(parentfolder)}' in parents",
                "trashed=false"
            };

            var encodedFileQuery = Library.Utility.Uri.UrlEncode(string.Join(" and ", fileQuery.Where(x => x != null)));
            var url = WebApi.GoogleDrive.ListUrl(encodedFileQuery, m_driveID);

            while (true)
            {
                var res = m_oauth.GetJSONData<GoogleDriveListResponse>(url);
                foreach (var n in res.items)
                {
                    yield return n;
                }

                var token = res.nextPageToken;
                if (string.IsNullOrWhiteSpace(token))
                {
                    break;
                }

                url = WebApi.GoogleDrive.ListUrl(encodedFileQuery, m_driveID, token);
            }
        }

        private GoogleDriveAboutResponse GetAboutInfo()
        {
            return m_oauth.GetJSONData<GoogleDriveAboutResponse>(WebApi.GoogleDrive.AboutInfoUrl());
        }

        private GoogleDriveFolderItem CreateFolder(string name, string parent)
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

            return m_oauth.GetJSONData<GoogleDriveFolderItem>(WebApi.GoogleDrive.CreateFolderUrl(m_driveID), x =>
            {
                x.Method = "POST";
                x.ContentType = "application/json; charset=UTF-8";
                x.ContentLength = data.Length;

            }, req =>
            {
                using (var rs = req.GetRequestStream())
                {
                    rs.Write(data, 0, data.Length);
                }
            });
        }

        private static string EscapeTitleEntries(string title)
        {
            return title.Replace("'", "\\'");
        }

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

        #region IStreamingBackend implementation

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            string targetFolderId;
            string targetFolderPath = GetPath($"{m_rootPath}{remotename}");
            string targetFilename = GetFilename(remotename);

            lock (m_folderCache)
            {
                if (m_folderCache.Count == 0 || !m_folderCache.ContainsKey(targetFolderPath))
                {
                    targetFolderId = GetFolderId(targetFolderPath, true);
                }
                else
                {
                    targetFolderId = m_folderCache[targetFolderPath];
                }
            }

            // Figure out if we update or create the file
            string fileId = null;
            if (m_fileCache.ContainsKey(remotename))
            {
                m_fileCache.TryGetValue(remotename, out var files);
                if (files != null)
                {
                    if (files.Length == 1)
                    {
                        fileId = files[0].id;
                    }
                    else
                    {
                        Delete(remotename);
                    }
                }
            }

            var isUpdate = !string.IsNullOrWhiteSpace(fileId);

            var url = WebApi.GoogleDrive.PutUrl(fileId, m_driveID != null);

            var item = new GoogleDriveFolderItem
            {
                title = targetFilename,
                description = targetFilename,
                mimeType = "application/octet-stream",
                labels = new GoogleDriveFolderItemLabels { hidden = true },
                parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference { id = targetFolderId } },
                driveId = m_driveID
            };

            var res = await GoogleCommon.ChunkedUploadWithResumeAsync<GoogleDriveFolderItem, GoogleDriveFolderItem>(m_oauth, item, url, stream, cancelToken, isUpdate ? "PUT" : "POST");
            m_fileCache[remotename] = new GoogleDriveFolderItem[] { res };
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            // Prevent repeated download url lookups
            if (m_fileCache.Count == 0)
            {
                foreach (var file in List()) { /* Enumerate the full listing */ }
            }

            var fileId = GetFileEntries(remotename).OrderByDescending(x => x.createdDate).First().id;
            var req = m_oauth.CreateRequest(WebApi.GoogleDrive.GetUrl(fileId));
            var areq = new AsyncHttpRequest(req);

            using (var resp = (HttpWebResponse)areq.GetResponse())
            {
                using (var rs = areq.GetResponseStream())
                {
                    Duplicati.Library.Utility.Utility.CopyStream(rs, stream);
                }
            }
        }

        #endregion

        #region IBackend implementation

        public IEnumerable<IFileEntry> List()
        {
            List<IFileEntry> foundFiles = new List<IFileEntry>();

            bool success = false;

            try
            {
                lock (m_folderCache)
                {
                    m_folderCache.Clear();
                }
                m_fileCache.Clear();

                foundFiles = GetRemoteList("", GetRootFolderId);

                success = true;
            }
            finally
            {
                // If the enumeration either failed or didn't complete, clear the file cache.
                // This way, other operations which require a fully populated file cache will see an empty one and can populate it themselves.
                if (!success)
                {
                    lock (m_folderCache)
                    {
                        m_folderCache.Clear();
                    }
                    m_fileCache.Clear();
                }
            }

            return foundFiles;
        }

        private List<IFileEntry> GetRemoteList(string path, string folderId)
        {
            List<IFileEntry> foundFiles = new List<IFileEntry>();

            var allItems = from n in GetRemoteListResponse(folderId)
                           select new RemoteFileEntry
                           {
                               Name = string.IsNullOrEmpty(path) ? n.title : $"{path}/{n.title}",
                               Size = n.fileSize ?? n.fileSize ?? 0,
                               LastModification = n.modifiedDate ?? n.modifiedDate ?? default,
                               LastAccess = n.modifiedDate ?? n.modifiedDate ?? default,
                               IsFolder = FOLDER_MIMETYPE.Equals(n.mimeType, StringComparison.OrdinalIgnoreCase),
                               Path = path,
                               ID = n.id
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
                    m_fileCache[item.Name] = new GoogleDriveFolderItem[] { new GoogleDriveFolderItem { title = item.Name, id = item.ID } };
                }

                foundFiles.Add(item);

                if (item.IsFolder)
                {
                    foundFiles.AddRange(GetRemoteList(string.IsNullOrEmpty(path) ? item.Name : $"{path}/{item.Name}", item.ID));
                }
            }

            return foundFiles;
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
            {
                return PutAsync(remotename, fs, cancelToken);
            }
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
            {
                Get(remotename, fs);
            }
        }

        public void Delete(string remotename)
        {
            try
            {
                foreach (var fileid in from n in GetFileEntries(remotename) select n.id)
                {
                    var url = WebApi.GoogleDrive.DeleteUrl(Library.Utility.Uri.UrlPathEncode(fileid), m_driveID);
                    m_oauth.GetJSONData<object>(url, x =>
                    {
                        x.Method = "DELETE";
                    });
                }

                m_fileCache.Remove(remotename);
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
            lock (m_folderCache)
            {
                m_folderCache.Clear();
            }
            m_fileCache.Clear();
            m_rootFolderId = GetFolderId(m_rootPath, true);
        }

        public System.Collections.Generic.IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION,
                                            CommandLineArgument.ArgumentType.Password,
                                            Strings.GoogleDrive.AuthidShort,
                                            Strings.GoogleDrive.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("googledrive"))),
                    new CommandLineArgument(DRIVE_ID,
                                            CommandLineArgument.ArgumentType.String,
                                            Strings.GoogleDrive.DriveIdShort,
                                            Strings.GoogleDrive.DriveIdLong),
                });

        public string DisplayName => Strings.GoogleDrive.DisplayName;

        public string ProtocolKey => "googledrive";

        public string Description => Strings.GoogleDrive.Description;

        #endregion

        #region IQuotaEnabledBackend implementation
        public IQuotaInfo Quota
        {
            get
            {
                try
                {
                    GoogleDriveAboutResponse about = this.GetAboutInfo();
                    return new QuotaInfo(about.quotaBytesTotal ?? -1, about.quotaBytesTotal - about.quotaBytesUsed ?? -1);
                }
                catch
                {
                    return null;
                }
            }
        }

        public string[] DNSName => WebApi.GoogleDrive.Hosts();

        #endregion

        #region IRenameEnabledBackend implementation
        public void Rename(string oldname, string newname)
        {
            try
            {
                var files = GetFileEntries(oldname, true);
                if (files.Length > 1)
                {
                    throw new UserInformationException(string.Format(Strings.GoogleDrive.MultipleEntries(oldname, m_rootPath)), "GoogleDriveMultipleEntries");
                }

                using (var cToken = new CancellationTokenSource())
                {
                    Stream stream = new MemoryStream();
                    Get(oldname, stream);
                    PutAsync(newname, stream, cToken.Token).Wait(cToken.Token);
                    Delete(oldname);
                }

                m_fileCache.Remove(oldname);
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
            public string driveId { get; set; }
            public GoogleDriveParentReference[] parents { get; set; }
        }

        private class GoogleDriveAboutResponse
        {
            public long? quotaBytesTotal { get; set; }
            public long? quotaBytesUsed { get; set; }
            public string rootFolderId { get; set; }
        }

    }
}

