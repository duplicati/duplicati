﻿//  Copyright (C) 2015, The Duplicati Team
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
using Duplicati.Library.Utility;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Duplicati.Library.Backend.GoogleServices;

namespace Duplicati.Library.Backend.GoogleDrive
{
    public class GoogleDrive : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string AUTHID_OPTION = "authid";

        private const string FOLDER_MIMETYPE = "application/vnd.google-apps.folder";
        private const string DRIVE_API_UPLOAD_URL = "https://www.googleapis.com/upload/drive/v2";
        private const string DRIVE_API_URL = "https://www.googleapis.com/drive/v2";

        private string m_path;

        private OAuthHelper m_oauth;
        private string m_currentFolderId;
        private Dictionary<string, GoogleDriveFolderItem[]> m_filecache;

        public GoogleDrive()
        {
        }

        public GoogleDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_path = uri.HostAndPath;
            if (!m_path.EndsWith("/"))
                m_path += "/";

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            m_oauth = new OAuthHelper(authid, this.ProtocolKey) { AutoAuthHeader = true };
            m_filecache = new Dictionary<string, GoogleDriveFolderItem[]>();
        }

        private string GetFolderId(string path, bool autocreate = false)
        {
            var curparent = GetAboutInfo().rootFolderId;
            var curdisplay = "/";

            foreach(var p in path.Split(new char[] {'/'}, StringSplitOptions.RemoveEmptyEntries))
            {
                var res = ListFolder(curparent, true, p).ToArray();

                if (res.Length == 0)
                {
                    if (!autocreate)
                        throw new FolderMissingException();

                    curparent = CreateFolder(p, curparent).id;
                }
                else if (res.Length > 1)
                    throw new UserInformationException(Strings.GoogleDrive.MultipleEntries(p, curdisplay));
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

            var list = ListFolder(CurrentFolderId, false, remotename).ToArray();

            if (list == null || list.Length == 0)
            {
                if (throwMissingException)
                    throw new FileMissingException();
                else
                    return null;
            }
                    
            return m_filecache[remotename] = list;
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
                    List();

                GoogleDriveFolderItem[] files;
                m_filecache.TryGetValue(remotename, out files);

                string fileid = null;
                if (files != null)
                {
                    if (files.Length == 1)
                        fileid = files[0].id;
                    else
                        Delete(remotename);
                }

                var isUpdate = !string.IsNullOrWhiteSpace(fileid);

                var url = isUpdate ?
                    string.Format("{0}/files/{1}?uploadType=resumable", DRIVE_API_UPLOAD_URL, Library.Utility.Uri.UrlPathEncode(fileid)) :
                    string.Format("{0}/files?uploadType=resumable", DRIVE_API_UPLOAD_URL);

                var item = new GoogleDriveFolderItem() {
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
                List();

            var fileid = GetFileEntries(remotename).OrderByDescending(x => x.createdDate).First().id;

            var req = m_oauth.CreateRequest(string.Format("{0}/files/{1}?alt=media", DRIVE_API_URL, fileid));
            var areq = new AsyncHttpRequest(req);
            using(var resp = (HttpWebResponse)areq.GetResponse())
            using(var rs = areq.GetResponseStream())
                Duplicati.Library.Utility.Utility.CopyStream(rs, stream);
        }

        #endregion

        #region IBackend implementation

        public List<IFileEntry> List()
        {
            try
            {
                var res = new List<IFileEntry>();
                m_filecache.Clear();

                foreach(var n in ListFolder(CurrentFolderId))
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
                        fe.IsFolder = FOLDER_MIMETYPE.Equals(n.mimeType, StringComparison.InvariantCultureIgnoreCase);
                        res.Add(fe);

                        if (!fe.IsFolder)
                        {
                            GoogleDriveFolderItem[] lst;
                            if (!m_filecache.TryGetValue(fe.Name, out lst))
                                m_filecache[fe.Name] = new GoogleDriveFolderItem[] { n };
                            else
                            {
                                Array.Resize(ref lst, lst.Length + 1);
                                lst[lst.Length - 1] = n;
                            }
                        }
                    }
                }

                return res;
            }
            catch
            {
                m_filecache.Clear();

                throw;
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
                foreach(var fileid in from n in GetFileEntries(remotename) select n.id)
                {
                    var url = string.Format("{0}/files/{1}", DRIVE_API_URL, Library.Utility.Uri.UrlPathEncode(fileid));

                    m_oauth.GetJSONData<object>(url, x => {
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
            List();
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
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.GoogleDrive.AuthidShort, Strings.GoogleDrive.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("googledrive"))),
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
        public long TotalQuotaSpace
        {
            get
            {
                try { return GetAboutInfo().quotaBytesTotal ?? -1; }
                catch { }

                return -1;
            }
        }

        public long FreeQuotaSpace
        {
            get
            {
                try { return GetAboutInfo().quotaBytesUsed ?? -1; }
                catch { }

                return -1;
            }
        }
        #endregion

        #region IRenameEnabledBackend implementation
        public void Rename(string oldname, string newname)
        {
            try
            {
                var files = GetFileEntries(oldname, true);
                if (files.Length > 1)
                    throw new UserInformationException(string.Format(Strings.GoogleDrive.MultipleEntries(oldname, m_path)));

                var newfile = JsonConvert.DeserializeObject<GoogleDriveFolderItem>(JsonConvert.SerializeObject(files[0]));
                newfile.title = newname;
                newfile.parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference() { id = CurrentFolderId } };

                var url = string.Format("{0}/files/{1}", DRIVE_API_UPLOAD_URL, Library.Utility.Uri.UrlPathEncode(files[0].id));
                var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(newfile));

                var nf = m_oauth.GetJSONData<GoogleDriveFolderItem>(url, x => {
                    x.Method = "PUT";
                    x.ContentLength = data.Length;
                    x.ContentType = "application/json; charset=UTF-8";
                }, x => {
                    
                    using(var rs = x.GetRequestStream())
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
            var p = new string[] {
                string.IsNullOrEmpty(name) ? null : string.Format("title = '{0}'", EscapeTitleEntries(name)),
                onlyFolders == null ? null : string.Format("mimeType {0}= '{1}'", onlyFolders.Value ? "" : "!", FOLDER_MIMETYPE),
                string.Format("'{0}' in parents", EscapeTitleEntries(parentfolder))
            };

            var url = string.Format("{0}/files?q={1}", DRIVE_API_URL, Library.Utility.Uri.UrlEncode(string.Join(" and ", p.Where(x => x != null))));
            var token = string.Empty;

            do
            {
                var res = m_oauth.GetJSONData<GoogleDriveListResponse>(url + (string.IsNullOrWhiteSpace(token) ? "" : "&pageToken=" + Library.Utility.Uri.UrlEncode(token)));
                foreach (var n in res.items)
                    yield return n;

                token = res.nextPageToken;
            } while (!string.IsNullOrWhiteSpace(token));
        }

        private GoogleDriveAboutResponse GetAboutInfo()
        {
            var url = string.Format("{0}/about", DRIVE_API_URL);
            return m_oauth.GetJSONData<GoogleDriveAboutResponse>(url);
        }

        private GoogleDriveFolderItem CreateFolder(string name, string parent)
        {
            var url = string.Format("{0}/files", DRIVE_API_URL);
            var folder = new GoogleDriveFolderItem() {
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

            }, req => {
                
                using(var rs = req.GetRequestStream())
                    rs.Write(data, 0, data.Length);
                
            });

        }
    }
}

