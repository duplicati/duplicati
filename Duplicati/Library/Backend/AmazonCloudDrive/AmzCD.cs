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
using System.Collections.Generic;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Backend.AmazonCloudDrive
{
    public class AmzCD : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string LABELS_OPTION = "amzcd-labels";
        private const string DELAY_OPTION = "amzcd-consistency-delay";

        private const string DEFAULT_LABELS = "duplicati,backup";
        private const string DEFAULT_DELAY = "30s";

        private const int PAGE_SIZE = 200;

        private const string CLOUDRIVE_MASTER_URL = "https://drive.amazonaws.com/drive/v1/account/endpoint";
        private const string CACHE_FILE_NAME_TEMPLATE = "dupl-amzcd-endp-{0}-cache.txt";
        private static readonly TimeSpan KEEP_CACHE_FILE_TIME = TimeSpan.FromDays(5);

        private const string CONTENT_KIND_FOLDER = "FOLDER";
        private const string CONTENT_KIND_FILE = "FILE";

        private EndpointInfo m_endPointInfo;
        private ResourceModel m_curdir;

        private readonly string m_path;
        private readonly string[] m_labels;

        private readonly string m_authid;
        private readonly OAuthHelper m_oauth;
        private Dictionary<string, string> m_filecache;
        private readonly string m_userid;
        private readonly TimeSpan m_delayTimeSpan;

        private static readonly object m_waitUntilLock;
        private static Dictionary<string, DateTime> m_waitUntilAuthId;
        private static Dictionary<string, DateTime> m_waitUntilRemotename;

        static AmzCD()
        {
            m_waitUntilLock = new object();
            m_waitUntilAuthId = new Dictionary<string, DateTime>();
            m_waitUntilRemotename = new Dictionary<string, DateTime>();
        }

        public AmzCD()
        {
        }

        public AmzCD(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            m_path = Util.AppendDirSeparator(uri.HostAndPath, "/");

            if (options.ContainsKey(AUTHID_OPTION))
                m_authid = options[AUTHID_OPTION];

            string labels = DEFAULT_LABELS;
            if (options.ContainsKey(LABELS_OPTION))
                labels = options[LABELS_OPTION];

            string delay = DEFAULT_DELAY;
            if (options.ContainsKey(DELAY_OPTION))
                delay = options[DELAY_OPTION];
            
            if (!string.IsNullOrWhiteSpace(labels))
                m_labels = labels.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);

            if (string.IsNullOrWhiteSpace(delay))
                m_delayTimeSpan = new TimeSpan(0);
            else
                m_delayTimeSpan = Library.Utility.Timeparser.ParseTimeSpan(delay);

            m_oauth = new OAuthHelper(m_authid, this.ProtocolKey) { AutoAuthHeader = true };
            m_userid = m_authid.Split(new [] {":"}, StringSplitOptions.RemoveEmptyEntries).First();
        }

        private static void CleanWaitUntil()
        {
            lock (m_waitUntilLock)
            {
                DateTime now = DateTime.Now;
                m_waitUntilRemotename = m_waitUntilRemotename.Where(pair => pair.Value > now)
                                 .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
        }

        private DateTime GetWaitUntil(string remotename)
        {
            lock (m_waitUntilLock)
            {
                CleanWaitUntil();

                DateTime result;
                if (string.IsNullOrEmpty(remotename))
                {
                    if (m_waitUntilAuthId.TryGetValue(m_authid, out result))
                        return result;
                }
                else
                {
                    if (m_waitUntilRemotename.TryGetValue(remotename, out result))
                        return result;
                }

                return DateTime.MinValue;
            }
        }

        private void SetWaitUntil(string remotename, DateTime value)
        {
            lock (m_waitUntilLock)
            {
                DateTime oldValue;

                if (!string.IsNullOrEmpty(remotename))
                {
                    if (!m_waitUntilRemotename.TryGetValue(remotename, out oldValue) || value > oldValue)
                        m_waitUntilRemotename[remotename] = value;
                }

                if (!m_waitUntilAuthId.TryGetValue(m_authid, out oldValue) || value > oldValue)
                    m_waitUntilAuthId[m_authid] = value;
            }
        }

        private void EnforceConsistencyDelay(string remotename)
        {
            var wait = GetWaitUntil(remotename) - DateTime.Now;

            if (wait.Ticks > 0)
                System.Threading.Thread.Sleep(wait);
        }

        private string CacheFilePath { get { return SystemIO.IO_OS.PathCombine(Utility.TempFolder.SystemTempPath, string.Format(CACHE_FILE_NAME_TEMPLATE, m_userid)); } }
                
        private void RefreshMetadataAndContentUrl()
        {
            try
            {
                if (File.Exists(CacheFilePath) && (DateTime.Now - File.GetLastWriteTime(CacheFilePath)) < KEEP_CACHE_FILE_TIME)
                {                 
                    m_endPointInfo = JsonConvert.DeserializeObject<EndpointInfo>(File.ReadAllText(CacheFilePath));
                    return;
                }
            }
            catch
            {
            }

            m_endPointInfo = m_oauth.GetJSONData<EndpointInfo>(CLOUDRIVE_MASTER_URL);

            try
            {
                if (m_endPointInfo.CustomerExists && m_endPointInfo.ContentUrl != null && m_endPointInfo.MetadataUrl != null)
                    File.WriteAllText(CacheFilePath, JsonConvert.SerializeObject(m_endPointInfo));
            }
            catch
            {
            }
        }

        public string ContentUrl
        {
            get
            {
                if (m_endPointInfo == null)
                    RefreshMetadataAndContentUrl();
                while (m_endPointInfo.ContentUrl.EndsWith("/", StringComparison.Ordinal))
                    m_endPointInfo.ContentUrl = m_endPointInfo.ContentUrl.Substring(0, m_endPointInfo.ContentUrl.Length - 1);
                return m_endPointInfo.ContentUrl;
            }
        }

        public string MetadataUrl
        {
            get
            {
                if (m_endPointInfo == null)
                    RefreshMetadataAndContentUrl();
                while (m_endPointInfo.MetadataUrl.EndsWith("/", StringComparison.Ordinal))
                    m_endPointInfo.MetadataUrl = m_endPointInfo.MetadataUrl.Substring(0, m_endPointInfo.MetadataUrl.Length - 1);
                return m_endPointInfo.MetadataUrl;
            }
        }

        private Dictionary<string, string> FileCache
        {
            get
            {
                if (m_filecache == null)
                    List();

                return m_filecache;
            }
        }

        private ResourceModel GetCurrentDirectory(bool createMissing, string altpath = null)
        {
            var rootResponse = m_oauth.GetJSONData<ListResponse>(string.Format("{0}/nodes?filters=isRoot:true", MetadataUrl));
            var parent = rootResponse.Data.First();
            var curpath = new List<string>();
            foreach(var p in (altpath ?? m_path).Split(new string[] {"/"}, StringSplitOptions.RemoveEmptyEntries))
            {
                var requestUrl = string.Format("{0}/nodes?filters=kind:{1}%20and%20parents:{2}%20and%20name:{3}", MetadataUrl, CONTENT_KIND_FOLDER, Utility.Uri.UrlEncode(parent.ID), Utility.Uri.UrlPathEncode(EscapeFiltersValue(p)));
                var self = m_oauth.GetJSONData<ListResponse>(requestUrl);
                if (self == null || self.Count == 0 || self.Data == null || self.Data.Length == 0)
                {
                    if (!createMissing)
                        throw new FolderMissingException(string.Format("Unable to find folder {0} in {1}", p, "/" + string.Join("/", curpath)));

                    // Create the folder
                    var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new CreateItemRequest() {
                        Name = p,
                        Parents = new string[] { parent.ID },
                        Kind = CONTENT_KIND_FOLDER,
                        Labels = m_labels
                    }));

                    parent = m_oauth.GetJSONData<ResourceModel>(
                        string.Format("{0}/nodes", MetadataUrl), 

                        // Setup
                        req =>
                        {
                            req.Method = "POST";
                            req.ContentLength = data.Length;
                            req.ContentType = "application/json";
                        },

                        // Post the folder entry
                        req =>
                        {
                            using(var rs = req.GetRequestStream())
                                rs.Write(data, 0, data.Length);
                        }
                    );
                    SetWaitUntil(null, DateTime.Now + m_delayTimeSpan);
                }
                else if (self != null && self.Count > 1)
                    throw new UserInformationException(Strings.AmzCD.MultipleEntries(p, "/" + string.Join("/", curpath)), "AmzCDMultipleEntries");
                else
                    parent = self.Data.First();
            }

            return parent;
        }

        private ResourceModel CurrentDirectory
        {
            get
            {
                if (m_curdir == null)
                    m_curdir = GetCurrentDirectory(false);

                return m_curdir;
            }
        }

        private string GetFileID(string name)
        {
            var recent = m_filecache == null;

            string id;
            FileCache.TryGetValue(name, out id);
            if (!string.IsNullOrWhiteSpace(id))
                return id;

            // Reset to make sure it is actually missing
            if (!recent)
            {
                List();

                FileCache.TryGetValue(name, out id);
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
            
            throw new FileMissingException();
        }
        
        private static System.Text.RegularExpressions.Regex FILTERS_VALUE_ESCAPECHAR = new System.Text.RegularExpressions.Regex(@"[+\-&|!(){}\[\]^'""~*?:\\ ]", System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string EscapeFiltersValue(string value)
        {
            return FILTERS_VALUE_ESCAPECHAR.Replace(value, (m) => {
                return @"\" + m.Value;
            });
        }

        #region IStreamingBackend implementation

        public void Put(string remotename, System.IO.Stream stream)
        {
            EnforceConsistencyDelay(remotename);

            var overwrite = FileCache.ContainsKey(remotename);
            var fileid = overwrite ? m_filecache[remotename] : null;

            var url = string.Format(overwrite ? "{0}/nodes/{1}/content?suppress=deduplication" : "{0}/nodes?suppress=deduplication", ContentUrl, fileid);

            var createreq = new CreateItemRequest() {
                Name = remotename,
                Kind = CONTENT_KIND_FILE,
                Labels = m_labels,
                Parents = new string[] { CurrentDirectory.ID }
            };

            try
            {
                var item = m_oauth.PostMultipartAndGetJSONData<ResourceModel>(
                    url,

                    req =>
                    {
                        req.Method = overwrite ? "PUT" : "POST";
                    },

                    new MultipartItem(createreq, "metadata"),
                    new MultipartItem(stream, "content", remotename)

                );

                if (m_filecache != null)
                    m_filecache[item.Name] = item.ID;
            }
            catch(Exception ex)
            {
                #if DEBUG
                if (ex is WebException)
                    using(var sr = new StreamReader((ex as WebException).Response.GetResponseStream()))
                        Console.WriteLine(sr.ReadToEnd());
                #endif

                m_filecache = null;
                throw;
            }
            finally
            {
                SetWaitUntil(remotename, DateTime.Now + m_delayTimeSpan);
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            EnforceConsistencyDelay(remotename);

            using (var resp = m_oauth.GetResponse(string.Format("{0}/nodes/{1}/content", ContentUrl, GetFileID(remotename))))
            using(var rs = Library.Utility.AsyncHttpRequest.TrySetTimeout(resp.GetResponseStream()))
                Utility.Utility.CopyStream(rs, stream);
        }

        #endregion

        #region IBackend implementation

        public IEnumerable<IFileEntry> List()
        {
            EnforceConsistencyDelay(null);

            var query = string.Format("{0}/nodes?filters=parents:{1}&limit={2}", MetadataUrl, Utility.Uri.UrlEncode(CurrentDirectory.ID), PAGE_SIZE);
            var res = new List<IFileEntry>();
            string nextToken = null;
            m_filecache = null;
            var cache = new Dictionary<string, string>();

            do
            {
                var lst = m_oauth.GetJSONData<ListResponse>(query + (string.IsNullOrWhiteSpace(nextToken) ? "" : ("&startToken=" + nextToken)));
                if (lst.Data != null)
                {
                    foreach(var n in lst.Data)
                    {

                        if (string.Equals(CONTENT_KIND_FOLDER, n.Kind, StringComparison.OrdinalIgnoreCase))
                            res.Add(new FileEntry(n.Name) { IsFolder = true });
                        else if (string.Equals(CONTENT_KIND_FILE, n.Kind, StringComparison.OrdinalIgnoreCase))
                        {
                            cache[n.Name] = n.ID;

                            if (n.ContentProperties == null)
                                res.Add(new FileEntry(n.Name) {
                                    LastAccess = n.LastModified,
                                    LastModification = n.LastModified
                                });
                            else
                                res.Add(new FileEntry(n.Name, n.ContentProperties.Size, n.LastModified, n.LastModified));
                        }
                    }
                }

                nextToken = lst.NextToken;

                // Contrary to the documentation, nextToken is null when the set is done
                if (lst.Count == 0)
                    break;

                // Docs say to check for empty response ...
                //if (lst.Count < PAGE_SIZE)
                //    break;
            } while(nextToken != null);

            m_filecache = cache;
            return res;

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
            EnforceConsistencyDelay(remotename);

            try
            {
                using (m_oauth.GetResponse(string.Format("{0}/trash/{1}", MetadataUrl, GetFileID(remotename)), null, "PUT"))
                {
                }

                m_filecache.Remove(remotename);
            }
            catch
            {
                m_filecache = null;
            }
            SetWaitUntil(remotename, DateTime.Now + m_delayTimeSpan);
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            EnforceConsistencyDelay(null);
            GetCurrentDirectory(true);            
        }

        public string DisplayName
        {
            get
            {
                return Strings.AmzCD.DisplayName;
            }
        }

        public string ProtocolKey
        {
            get
            {
                return "amzcd";
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.AmzCD.AuthidShort, Strings.AmzCD.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("amzcd"))),
                    new CommandLineArgument(LABELS_OPTION, CommandLineArgument.ArgumentType.String, Strings.AmzCD.LabelsShort, Strings.AmzCD.LabelsLong, DEFAULT_LABELS),
                    new CommandLineArgument(DELAY_OPTION, CommandLineArgument.ArgumentType.Timespan, Strings.AmzCD.DelayShort, Strings.AmzCD.DelayLong, DEFAULT_DELAY),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.AmzCD.Description;
            }
        }

        public string[] DNSName
        {
            get 
            {
                var contentUrl = string.Empty;
                var metdataUrl = string.Empty;

                if (m_endPointInfo != null)
                {
                    if (!string.IsNullOrWhiteSpace(m_endPointInfo.ContentUrl))
                        contentUrl = new Uri(m_endPointInfo.ContentUrl).Host;
                    if (!string.IsNullOrWhiteSpace(m_endPointInfo.MetadataUrl))
                        metdataUrl = new Uri(m_endPointInfo.MetadataUrl).Host;
                }
                
                return new string[] { 
                    new Uri(CLOUDRIVE_MASTER_URL).Host, 
                    contentUrl, 
                    metdataUrl 
                }; 
            }
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion

        #region IRenameEnabledBackend

        public void Rename(string oldname, string newname)
        {
            EnforceConsistencyDelay(oldname);

            var id = GetFileID(oldname);

            var data = System.Text.Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(new CreateItemRequest() { Name = newname })
            );

            try
            {
                var resp = m_oauth.GetJSONData<ResourceModel>(
                    string.Format("{0}/nodes/{1}", MetadataUrl, id),
                    req =>
                    {
                        req.Method = "PATCH";
                        req.ContentType = "application/json";
                        req.ContentLength = data.Length;
                    },

                    req =>
                    {
                        using (var rs = req.GetRequestStream())
                            rs.Write(data, 0, data.Length);
                    }
                );

                m_filecache.Remove(oldname);
                m_filecache[newname] = resp.ID;
            }
            catch
            {
                m_filecache = null;
                throw;
            }
            finally
            {
                SetWaitUntil(oldname, DateTime.Now + m_delayTimeSpan);
                SetWaitUntil(newname, DateTime.Now + m_delayTimeSpan);
            }
        }

        #endregion

        #region JSON Classes

        private class ListResponse
        {
            [JsonProperty("count")]
            public long Count { get; set; }
            [JsonProperty("nextToken")]
            public string NextToken { get; set; }
            [JsonProperty("data")]
            public ResourceModel[] Data { get; set; }
        }

        private class ResourceModel
        {
            [JsonProperty("id")]
            public string ID { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("kind")]
            public string Kind { get; set; }
            [JsonProperty("version")]
            public long Version { get; set; }
            [JsonProperty("modifiedDate")]
            public DateTime LastModified { get; set; }
            [JsonProperty("createdDate")]
            public DateTime CreatedDate { get; set; }
            [JsonProperty("labels")]
            public string[] Labels { get; set; }
            [JsonProperty("description")]
            public string Description { get; set; }
            [JsonProperty("createdBy")]
            public string CreatedBy { get; set; }
            [JsonProperty("parents")]
            public string[] Parents { get; set; }
            [JsonProperty("status")]
            public string Status { get; set; }
            [JsonProperty("contentProperties")]
            public ContentProperties ContentProperties { get; set; }
        }

        private class ContentProperties
        {
            [JsonProperty("version")]
            public long Version { get; set; }
            [JsonProperty("md5")]
            public string MD5 { get; set; }
            [JsonProperty("size")]
            public long Size { get; set; }

            [JsonProperty("contentType")]
            public string ContentType { get; set; }
            [JsonProperty("extension")]
            public string Extension { get; set; }
            [JsonProperty("contentDate")]
            public DateTime ContentDate { get; set; }
        }

        private class EndpointInfo
        {
            [JsonProperty("customerExists")]
            public bool CustomerExists { get; set; }
            [JsonProperty("contentUrl")]
            public string ContentUrl { get; set; }
            [JsonProperty("metadataUrl")]
            public string MetadataUrl { get; set; }
        }

        private class CreateItemRequest
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("kind")]
            public string Kind { get; set; }
            [JsonProperty("labels", NullValueHandling = NullValueHandling.Ignore)]
            public string[] Labels { get; set; } 
            [JsonProperty("properties", NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, string> Properties { get; set; } 
            [JsonProperty("parents")]
            public string[] Parents { get; set; } 
        }

        #endregion
    }
}

