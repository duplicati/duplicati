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
using System.Net;
using System.Text;

using Newtonsoft.Json;

using Duplicati.Library.Backend.GoogleServices;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;



namespace Duplicati.Library.Backend.GoogleCloudStorage
{
    public class GoogleCloudStorage : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string PROJECT_OPTION = "gcs-project";

        private const string LOCATION_OPTION = "gcs-location";
        private const string STORAGECLASS_OPTION = "gcs-storage-class";

        private string m_bucket;
        private string m_prefix;
        private string m_project;
        private readonly OAuthHelper m_oauth;

        private string m_location;
        private string m_storage_class;
        public GoogleCloudStorage()
        {
        }

        public GoogleCloudStorage(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_bucket = uri.Host;
            m_prefix = "/" + uri.Path;
            if (!m_prefix.EndsWith("/", StringComparison.Ordinal))
                m_prefix += "/";

            // For GCS we do not use a leading slash
            if (m_prefix.StartsWith("/", StringComparison.Ordinal))
                m_prefix = m_prefix.Substring(1);

            string authid;
            options.TryGetValue(AUTHID_OPTION, out authid);
            options.TryGetValue(PROJECT_OPTION, out m_project);
            options.TryGetValue(LOCATION_OPTION, out m_location);
            options.TryGetValue(STORAGECLASS_OPTION, out m_storage_class);

            if (string.IsNullOrEmpty(authid))
                throw new UserInformationException(Strings.GoogleCloudStorage.MissingAuthID(AUTHID_OPTION), "GoogleCloudStorageMissingAuthID");

            m_oauth = new OAuthHelper(authid, this.ProtocolKey);
            m_oauth.AutoAuthHeader = true;
        }


        private class ListBucketResponse
        {
            public string kind { get; set; }
            public string nextPageToken { get; set; }
            public string[] prefixes { get; set; }
            public BucketResourceItem[] items { get; set; }
        }

        private class BucketResourceItem
        {
            public string kind { get; set; }
            public string id { get; set; }
            public string selfLink { get; set; }
            public string name { get; set; }
            public string contentType { get; set; }
            public DateTime? updated { get; set; }
            public string storageClass { get; set; }
            public long? size { get; set; }
            public string md5Hash { get; set; }

            public string mediaLink { get; set; }
        }

        private class CreateBucketRequest
        {
            public string name { get; set; }
            public string location { get; set; }
            public string storageClass { get; set; }
        }

        private T HandleListExceptions<T>(Func<T> func)
        {
            try
            {
                return func();
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse && ((HttpWebResponse)wex.Response).StatusCode == HttpStatusCode.NotFound)
                    throw new FolderMissingException();
                else
                    throw;
            }
        }

        #region IBackend implementation
        public IEnumerable<IFileEntry> List()
        {
            var url = WebApi.GoogleCloudStorage.ListUrl(m_bucket, Utility.Uri.UrlEncode(m_prefix));
            while (true)
            {
                var resp = HandleListExceptions(() => m_oauth.ReadJSONResponse<ListBucketResponse>(url));

                if (resp.items != null)
                    foreach (var f in resp.items)
                    {
                        var name = f.name;
                        if (name.StartsWith(m_prefix, StringComparison.OrdinalIgnoreCase))
                            name = name.Substring(m_prefix.Length);
                        if (f.size == null)
                            yield return new FileEntry(name);
                        else if (f.updated == null)
                            yield return new FileEntry(name, f.size.Value);
                        else
                            yield return new FileEntry(name, f.size.Value, f.updated.Value, f.updated.Value);
                    }

                var token = resp.nextPageToken;
                if (string.IsNullOrWhiteSpace(token))
                    break;
                url = WebApi.GoogleCloudStorage.ListUrl(m_bucket, Utility.Uri.UrlEncode(m_prefix), token);
            };
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

            var req = m_oauth.CreateRequest(WebApi.GoogleCloudStorage.DeleteUrl(m_bucket, Library.Utility.Uri.UrlPathEncode(m_prefix + remotename)));
            req.Method = "DELETE";

            m_oauth.ReadJSONResponse<object>(req);
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            if (string.IsNullOrEmpty(m_project))
                throw new UserInformationException(Strings.GoogleCloudStorage.ProjectIDMissingError(PROJECT_OPTION), "GoogleCloudStorageMissingProjectID");

            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new CreateBucketRequest
            {
                name = m_bucket,
                location = m_location,
                storageClass = m_storage_class
            }));

            var req = m_oauth.CreateRequest(WebApi.GoogleCloudStorage.CreateFolderUrl(m_project));
            req.Method = "POST";
            req.ContentLength = data.Length;
            req.ContentType = "application/json; charset=UTF-8";

            var areq = new AsyncHttpRequest(req);

            using (var rs = areq.GetRequestStream())
                rs.Write(data, 0, data.Length);

            m_oauth.ReadJSONResponse<BucketResourceItem>(areq);
        }

        public string DisplayName
        {
            get { return Strings.GoogleCloudStorage.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "gcs"; }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                StringBuilder locations = new StringBuilder();
                StringBuilder storageClasses = new StringBuilder();

                foreach (KeyValuePair<string, string> s in WebApi.GoogleCloudStorage.KNOWN_GCS_LOCATIONS)
                    locations.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));
                foreach (KeyValuePair<string, string> s in WebApi.GoogleCloudStorage.KNOWN_GCS_STORAGE_CLASSES)
                    storageClasses.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(LOCATION_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.LocationDescriptionShort, Strings.GoogleCloudStorage.LocationDescriptionLong(locations.ToString())),
                    new CommandLineArgument(STORAGECLASS_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.StorageclassDescriptionShort, Strings.GoogleCloudStorage.StorageclassDescriptionLong(locations.ToString())),
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.GoogleCloudStorage.AuthidShort, Strings.GoogleCloudStorage.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("gcs"))),
                    new CommandLineArgument(PROJECT_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.ProjectDescriptionShort, Strings.GoogleCloudStorage.ProjectDescriptionLong),
                });
            }
        }
        public string Description
        {
            get { return Strings.GoogleCloudStorage.Description; }
        }

        public string[] DNSName
        {
            get
            {
                return new string[] { new System.Uri(WebApi.GoogleCloudStorage.Url.UPLOAD).Host,
                    new System.Uri(WebApi.GoogleCloudStorage.Url.API).Host };
            }
        }

        #endregion

        public void Put(string remotename, System.IO.Stream stream)
        {
            var queryParams = new NameValueCollection
            {
                { WebApi.Google.QueryParam.UploadType, WebApi.Google.QueryValue.Resumable }
            };
            var path = UrlPath.Create(WebApi.GoogleCloudStorage.Path.Bucket).Append(m_bucket).ToString();
            var url = Utility.Uri.UriBuilder(WebApi.GoogleCloudStorage.Url.UPLOAD, path, queryParams);

            var item = new BucketResourceItem() { name = m_prefix + remotename };

            var res = GoogleCommon.ChunckedUploadWithResume<BucketResourceItem, BucketResourceItem>(m_oauth, item, url, stream);

            if (res == null)
                throw new Exception("Upload succeeded, but no data was returned");
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            try
            {
                var queryParams = new NameValueCollection
                {
                    { WebApi.Google.QueryParam.Alt
                            , WebApi.Google.QueryValue.Media }
                };
                var path = WebApi.GoogleCloudStorage.BucketObjectPath(m_bucket, Library.Utility.Uri.UrlPathEncode(m_prefix + remotename));

                var url = Utility.Uri.UriBuilder(WebApi.GoogleCloudStorage.Url.API, path, queryParams);

                var req = m_oauth.CreateRequest(url);
                var areq = new AsyncHttpRequest(req);

                using (var resp = areq.GetResponse())
                using (var rs = areq.GetResponseStream())
                    Library.Utility.Utility.CopyStream(rs, stream);
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse && ((HttpWebResponse)wex.Response).StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException();
                else
                    throw;
            }

        }

        public void Rename(string oldname, string newname)
        {
            var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new BucketResourceItem
            {
                name = m_prefix + newname,
            }));

            var req = m_oauth.CreateRequest(WebApi.GoogleCloudStorage.RenameUrl(m_bucket, Utility.Uri.UrlPathEncode(m_prefix + oldname)));
            req.Method = "PATCH";
            req.ContentLength = data.Length;
            req.ContentType = "application/json; charset=UTF-8";

            var areq = new AsyncHttpRequest(req);
            using (var rs = areq.GetRequestStream())
                rs.Write(data, 0, data.Length);

            m_oauth.ReadJSONResponse<BucketResourceItem>(req);
        }

        #region IDisposable implementation
        public void Dispose()
        {

        }
        #endregion
    }
}

