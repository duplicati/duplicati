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
    // For information on the Google Drive API see here: https://developers.google.com/drive/api/v3/reference/files?hl=en
    public class GoogleDrive : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string TEAMDRIVE_ID = "googledrive-teamdrive-id";
        private const string ACKNOWLEDGE_ABUSE_OPTION = "acknowledge-abuse";
        private const string FOLDER_MIMETYPE = "application/vnd.google-apps.folder";

        private readonly string m_path;
        private readonly string m_teamDriveID;
        private readonly bool m_acknowledgeAbuse;
        private readonly OAuthHelper m_oauth;
        private readonly Dictionary<string, GoogleDriveFolderItem[]> m_filecache;

        private string m_currentFolderId;

        public GoogleDrive()
        {
        }

        public GoogleDrive(string url, Dictionary<string, string> options)
        {
            Utility.Uri uri = new Utility.Uri(url);

            m_path = Util.AppendDirSeparator(uri.HostAndPath, "/");

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];

            if (options.ContainsKey(TEAMDRIVE_ID))
                m_teamDriveID = options[TEAMDRIVE_ID];

            m_acknowledgeAbuse = options.ContainsKey(ACKNOWLEDGE_ABUSE_OPTION);

            m_oauth = new OAuthHelper(authid, this.ProtocolKey) { AutoAuthHeader = true };
            m_filecache = new Dictionary<string, GoogleDriveFolderItem[]>();
        }

        private string GetFolderId(string path, bool autocreate = false)
        {
            string curparent = m_teamDriveID ?? GetAboutInfo().rootFolderId;
            StringBuilder curdisplay = new StringBuilder("/");

            foreach (string p in path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                GoogleDriveFolderItem[] res = ListFolder(curparent, true, p).ToArray();

                if (res.Length == 0)
                {
                    if (!autocreate)
                        throw new FolderMissingException();

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

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                // Figure out if we update or create the file
                if (m_filecache.Count == 0)
                    foreach (IFileEntry file in List()) { /* Enumerate the full listing */ }

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

                bool isUpdate = !string.IsNullOrWhiteSpace(fileId);

                string url = WebApi.GoogleDrive.PutUrl(fileId, m_teamDriveID != null);

                GoogleDriveFolderItem item = new GoogleDriveFolderItem
                {
                    title = remotename,
                    description = remotename,
                    mimeType = "application/octet-stream",
                    labels = new GoogleDriveFolderItemLabels { hidden = true },
                    parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference { id = CurrentFolderId } },
                    teamDriveId = m_teamDriveID
                };

                GoogleDriveFolderItem res = await GoogleCommon.ChunkedUploadWithResumeAsync<GoogleDriveFolderItem, GoogleDriveFolderItem>(m_oauth, item, url, stream, cancelToken, isUpdate ? "PUT" : "POST");
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
            bool retry = false;
            bool addAcknowledgeAbuseParameter = false;

            // Prevent repeated download url lookups
            if (m_filecache.Count == 0)
                foreach (IFileEntry file in List()) { /* Enumerate the full listing */ }

            string fileId = GetFileEntries(remotename).OrderByDescending(x => x.createdDate).First().id;

            do
            {
                try
                {
                    HttpWebRequest req = m_oauth.CreateRequest(WebApi.GoogleDrive.GetUrl(fileId, addAcknowledgeAbuseParameter));
                    AsyncHttpRequest areq = new AsyncHttpRequest(req);
                    using (HttpWebResponse resp = (HttpWebResponse)areq.GetResponse())
                    {
                        using (Stream rs = areq.GetResponseStream())
                            Duplicati.Library.Utility.Utility.CopyStream(rs, stream);
                    }
                    retry = false;
                }
                /*
                 * Catch any web exceptions and grab the error detail from the respose stream
                 */
                catch (WebException wex)
                {
                    using (Stream exstream = wex.Response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(exstream))
                        {
                            string error = reader.ReadToEnd();

                            /*
                             * Google Drive might error if it detects malware or spam in what it is downloading, if this
                             * is the case then add an "acknowledgeAbuse" parameter to avoid the error. This parameter can
                             * only be added for the specific file it's complaining about (unfortunately it can't generally
                             * be added the URL) so if you see the error then add the parameter and retry the operation. If
                             * it's some other error then pass the error detail up the line.
                             * 
                             * See here for more detail on the parameter: https://developers.google.com/drive/api/v3/reference/files/get?hl=en
                             */

                            if (retry == false && m_acknowledgeAbuse == true && error.Contains("This file has been identified as malware or spam and cannot be downloaded"))
                            {
                                addAcknowledgeAbuseParameter = true;
                                retry = true;
                                continue;
                            }
                            else
                                throw new WebException(error, wex, wex.Status, wex.Response);
                        }
                    }
                }
                catch (Exception)
                {
                    throw;
                }
            }
            while (retry == true);
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
                foreach (GoogleDriveFolderItem n in ListFolder(CurrentFolderId))
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
            try
            {
                foreach (string fileid in from n in GetFileEntries(remotename) select n.id)
                {
                    string url = WebApi.GoogleDrive.DeleteUrl(Library.Utility.Uri.UrlPathEncode(fileid), m_teamDriveID);
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

        public System.Collections.Generic.IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION,
                                            CommandLineArgument.ArgumentType.Password,
                                            Strings.GoogleDrive.AuthidShort,
                                            Strings.GoogleDrive.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("googledrive"))),
                    new CommandLineArgument(TEAMDRIVE_ID,
                                            CommandLineArgument.ArgumentType.String,
                                            Strings.GoogleDrive.TeamDriveIdShort,
                                            Strings.GoogleDrive.TeamDriveIdLong),
                    new CommandLineArgument(ACKNOWLEDGE_ABUSE_OPTION,
                                            CommandLineArgument.ArgumentType.Boolean,
                                            Strings.GoogleDrive.AcknowledgeAbuseShort,
                                            Strings.GoogleDrive.AcknowledgeAbuseLong, "false"),
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

        public string[] DNSName
        {
            get { return WebApi.GoogleDrive.Hosts(); }
        }

        #endregion

        #region IRenameEnabledBackend implementation
        public void Rename(string oldname, string newname)
        {
            try
            {
                GoogleDriveFolderItem[] files = GetFileEntries(oldname, true);
                if (files.Length > 1)
                    throw new UserInformationException(string.Format(Strings.GoogleDrive.MultipleEntries(oldname, m_path)),
                                                       "GoogleDriveMultipleEntries");

                using (CancellationTokenSource cToken = new CancellationTokenSource())
                {
                    Stream stream = new MemoryStream();
                    Get(oldname, stream);
                    PutAsync(newname, stream, cToken.Token).Wait(cToken.Token);
                    Delete(oldname);
                }

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

        private IEnumerable<GoogleDriveFolderItem> ListFolder(string parentfolder, bool? onlyFolders = null, string name = null)
        {
            string[] fileQuery = new string[] {
                string.IsNullOrEmpty(name) ? null : string.Format("title = '{0}'", EscapeTitleEntries(name)),
                onlyFolders == null ? null : string.Format("mimeType {0}= '{1}'", onlyFolders.Value ? "" : "!", FOLDER_MIMETYPE),
                string.Format("'{0}' in parents", EscapeTitleEntries(parentfolder)),
                "trashed=false"
            };

            string encodedFileQuery = Library.Utility.Uri.UrlEncode(string.Join(" and ", fileQuery.Where(x => x != null)));
            string url = WebApi.GoogleDrive.ListUrl(encodedFileQuery, m_teamDriveID);

            while (true)
            {
                GoogleDriveListResponse res = m_oauth.GetJSONData<GoogleDriveListResponse>(url);
                foreach (GoogleDriveFolderItem n in res.items)
                    yield return n;

                string token = res.nextPageToken;
                if (string.IsNullOrWhiteSpace(token))
                    break;

                url = WebApi.GoogleDrive.ListUrl(encodedFileQuery, m_teamDriveID, token);
            }
        }

        private GoogleDriveAboutResponse GetAboutInfo()
        {
            return m_oauth.GetJSONData<GoogleDriveAboutResponse>(WebApi.GoogleDrive.AboutInfoUrl());
        }

        private GoogleDriveFolderItem CreateFolder(string name, string parent)
        {
            GoogleDriveFolderItem folder = new GoogleDriveFolderItem()
            {
                title = name,
                description = name,
                mimeType = FOLDER_MIMETYPE,
                labels = new GoogleDriveFolderItemLabels { hidden = true },
                parents = new GoogleDriveParentReference[] { new GoogleDriveParentReference { id = parent } }
            };

            byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(folder));

            return m_oauth.GetJSONData<GoogleDriveFolderItem>(WebApi.GoogleDrive.CreateFolderUrl(m_teamDriveID), x =>
            {
                x.Method = "POST";
                x.ContentType = "application/json; charset=UTF-8";
                x.ContentLength = data.Length;

            }, req =>
            {
                using (Stream rs = req.GetRequestStream())
                    rs.Write(data, 0, data.Length);
            });
        }
    }
}

