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
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.GoogleCloudStorage
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class GoogleCloudStorage : IBackend, IStreamingBackend, IRenameEnabledBackend, ITimeoutExemptBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string PROJECT_OPTION = "gcs-project";

        private const string LOCATION_OPTION = "gcs-location";
        private const string STORAGECLASS_OPTION = "gcs-storage-class";

        private readonly string m_bucket;
        private readonly string m_prefix;
        private readonly string m_project;
        private readonly OAuthHelper m_oauth;

        private readonly string m_location;
        private readonly string m_storage_class;

        public GoogleCloudStorage()
        {
        }

        public GoogleCloudStorage(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_bucket = uri.Host;
            m_prefix = Util.AppendDirSeparator("/" + uri.Path, "/");

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
            public string nextPageToken { get; set; }
            public BucketResourceItem[] items { get; set; }
        }

        private class BucketResourceItem
        {
            public string name { get; set; }
            public DateTime? updated { get; set; }
            public long? size { get; set; }
        }

        private class CreateBucketRequest
        {
            public string name { get; set; }
            public string location { get; set; }
            public string storageClass { get; set; }
        }

        private async Task<T> HandleListExceptions<T>(Func<Task<T>> func)
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                    throw new FolderMissingException();
                else
                    throw;
            }
        }

        #region IBackend implementation
        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var url = WebApi.GoogleCloudStorage.ListUrl(m_bucket, Utility.Uri.UrlEncode(m_prefix));
            while (true)
            {
                var resp = await HandleListExceptions(() => m_oauth.ReadJSONResponseAsync<ListBucketResponse>(url, cancelToken)).ConfigureAwait(false);

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
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }
        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {

            var req = m_oauth.CreateRequest(WebApi.GoogleCloudStorage.DeleteUrl(m_bucket, Library.Utility.Uri.UrlPathEncode(m_prefix + remotename)));
            req.Method = "DELETE";

            return m_oauth.ReadJSONResponseAsync<object>(req, cancelToken);
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
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
                await rs.WriteAsync(data, 0, data.Length, cancelToken);

            await m_oauth.ReadJSONResponseAsync<BucketResourceItem>(areq, cancelToken).ConfigureAwait(false);
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
                    new CommandLineArgument(STORAGECLASS_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.StorageclassDescriptionShort, Strings.GoogleCloudStorage.StorageclassDescriptionLong(storageClasses.ToString())),
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.GoogleCloudStorage.AuthidShort, Strings.GoogleCloudStorage.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("gcs"))),
                    new CommandLineArgument(PROJECT_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.ProjectDescriptionShort, Strings.GoogleCloudStorage.ProjectDescriptionLong),
                });
            }
        }
        public string Description
        {
            get { return Strings.GoogleCloudStorage.Description; }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(WebApi.GoogleCloudStorage.Hosts());

        #endregion

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var item = new BucketResourceItem { name = m_prefix + remotename };

            var url = WebApi.GoogleCloudStorage.PutUrl(m_bucket);
            var res = await GoogleCommon.ChunkedUploadWithResumeAsync<BucketResourceItem, BucketResourceItem>(m_oauth, item, url, stream, cancelToken).ConfigureAwait(false);

            if (res == null)
                throw new Exception("Upload succeeded, but no data was returned");
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                var url = WebApi.GoogleCloudStorage.GetUrl(m_bucket, Library.Utility.Uri.UrlPathEncode(m_prefix + remotename));
                var req = m_oauth.CreateRequest(url);
                var areq = new AsyncHttpRequest(req);

                using (var resp = areq.GetResponse())
                using (var rs = areq.GetResponseStream())
                    await Library.Utility.Utility.CopyStreamAsync(rs, stream, cancelToken).ConfigureAwait(false);
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException();
                else
                    throw;
            }
        }

        public async Task RenameAsync(string oldname, string newname, CancellationToken cancelToken)
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
                await rs.WriteAsync(data, 0, data.Length, cancelToken).ConfigureAwait(false);

            await m_oauth.ReadJSONResponseAsync<BucketResourceItem>(req, cancelToken).ConfigureAwait(false);
        }

        #region IDisposable implementation
        public void Dispose()
        {

        }
        #endregion
    }
}

