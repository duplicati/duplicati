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
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Duplicati.Library.Backend.GoogleCloudStorage
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class GoogleCloudStorage : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private static readonly string TOKEN_URL = OAuthHelper.OAUTH_LOGIN_URL("gcs");
        private const string PROJECT_OPTION = "gcs-project";

        private const string LOCATION_OPTION = "gcs-location";
        private const string STORAGECLASS_OPTION = "gcs-storage-class";

        private readonly string m_bucket;
        private readonly string m_prefix;
        private readonly string? m_project;
        private readonly OAuthHelper m_oauth;

        private readonly string? m_location;
        private readonly string? m_storage_class;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        public GoogleCloudStorage()
        {
            m_bucket = null!;
            m_prefix = null!;
            m_oauth = null!;
            m_timeouts = null!;
        }

        public GoogleCloudStorage(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);

            m_bucket = uri.Host;
            m_prefix = Util.AppendDirSeparator("/" + uri.Path, "/");

            // For GCS we do not use a leading slash
            if (m_prefix.StartsWith("/", StringComparison.Ordinal))
                m_prefix = m_prefix.Substring(1);

            var authId = AuthIdOptionsHelper.Parse(options)
                .RequireCredentials(TOKEN_URL);

            m_timeouts = TimeoutOptionsHelper.Parse(options);
            m_project = options.GetValueOrDefault(PROJECT_OPTION);
            m_location = options.GetValueOrDefault(LOCATION_OPTION);
            m_storage_class = options.GetValueOrDefault(STORAGECLASS_OPTION);

            m_oauth = new OAuthHelper(authId.AuthId!, this.ProtocolKey);
            m_oauth.AutoAuthHeader = true;
        }


        private class ListBucketResponse
        {
            public string? nextPageToken { get; set; }
            public BucketResourceItem[]? items { get; set; }
        }

        private class BucketResourceItem
        {
            public string? name { get; set; }
            public DateTime? updated { get; set; }
            public long? size { get; set; }
        }

        private class CreateBucketRequest
        {
            public string? name { get; set; }
            public string? location { get; set; }
            public string? storageClass { get; set; }
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
                var resp = await HandleListExceptions(() =>
                        Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, ct =>
                            m_oauth.ReadJSONResponseAsync<ListBucketResponse>(url, ct))
                        ).ConfigureAwait(false);

                if (resp.items != null)
                    foreach (var f in resp.items)
                    {
                        var name = f.name ?? "";
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
            using (var fs = File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = File.Create(filename))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }
        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            var req = m_oauth.CreateRequest(WebApi.GoogleCloudStorage.DeleteUrl(m_bucket, Library.Utility.Uri.UrlPathEncode(m_prefix + remotename)));
            req.Method = "DELETE";

            return Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                => m_oauth.ReadJSONResponseAsync<object>(req, ct));
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            if (string.IsNullOrEmpty(m_project))
                throw new UserInformationException(Strings.GoogleCloudStorage.ProjectIDMissingError(PROJECT_OPTION), "GoogleCloudStorageMissingProjectID");

            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new CreateBucketRequest
            {
                name = m_bucket,
                location = m_location,
                storageClass = m_storage_class
            }));

            var req = m_oauth.CreateRequest(WebApi.GoogleCloudStorage.CreateFolderUrl(m_project));
            req.Method = "POST";
            req.ContentLength = data.Length;
            req.ContentType = "application/json; charset=UTF-8";

            return Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
            {
                var areq = new AsyncHttpRequest(req);
                using (var rs = areq.GetRequestStream())
                    await rs.WriteAsync(data, 0, data.Length, ct);

                await m_oauth.ReadJSONResponseAsync<BucketResourceItem>(areq, ct).ConfigureAwait(false);
            });
        }

        public string DisplayName => Strings.GoogleCloudStorage.DisplayName;

        public string ProtocolKey => "gcs";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                StringBuilder locations = new StringBuilder();
                StringBuilder storageClasses = new StringBuilder();

                foreach (var s in WebApi.GoogleCloudStorage.KNOWN_GCS_LOCATIONS)
                    locations.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));
                foreach (var s in WebApi.GoogleCloudStorage.KNOWN_GCS_STORAGE_CLASSES)
                    storageClasses.AppendLine(string.Format("{0}: {1}", s.Key, s.Value));

                return [
                    new CommandLineArgument(LOCATION_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.LocationDescriptionShort, Strings.GoogleCloudStorage.LocationDescriptionLong(locations.ToString())),
                    new CommandLineArgument(STORAGECLASS_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.StorageclassDescriptionShort, Strings.GoogleCloudStorage.StorageclassDescriptionLong(storageClasses.ToString())),
                    .. AuthIdOptionsHelper.GetOptions(TOKEN_URL),
                    new CommandLineArgument(PROJECT_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleCloudStorage.ProjectDescriptionShort, Strings.GoogleCloudStorage.ProjectDescriptionLong),
                    .. TimeoutOptionsHelper.GetOptions(),
                ];
            }
        }
        public string Description => Strings.GoogleCloudStorage.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(WebApi.GoogleCloudStorage.Hosts());

        #endregion

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            var item = new BucketResourceItem { name = m_prefix + remotename };

            var url = WebApi.GoogleCloudStorage.PutUrl(m_bucket);
            var res = await GoogleCommon.ChunkedUploadWithResumeAsync<BucketResourceItem, BucketResourceItem>(m_oauth, item, url, stream, m_timeouts.ShortTimeout, m_timeouts.ReadWriteTimeout, cancelToken).ConfigureAwait(false);

            if (res == null)
                throw new Exception("Upload succeeded, but no data was returned");
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                var url = WebApi.GoogleCloudStorage.GetUrl(m_bucket, Utility.Uri.UrlPathEncode(m_prefix + remotename));
                var req = m_oauth.CreateRequest(url);
                var areq = new AsyncHttpRequest(req);

                using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _ => areq.GetResponse()).ConfigureAwait(false))
                using (var rs = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _ => areq.GetResponseStream()).ConfigureAwait(false))
                using (var ts = rs.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
                    await Utility.Utility.CopyStreamAsync(ts, stream, cancelToken).ConfigureAwait(false);
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException();
                else
                    throw;
            }
        }

        public Task RenameAsync(string oldname, string newname, CancellationToken cancelToken)
        {
            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new BucketResourceItem
            {
                name = m_prefix + newname,
            }));

            var req = m_oauth.CreateRequest(WebApi.GoogleCloudStorage.RenameUrl(m_bucket, Utility.Uri.UrlPathEncode(m_prefix + oldname)));
            req.Method = "PATCH";
            req.ContentLength = data.Length;
            req.ContentType = "application/json; charset=UTF-8";

            return Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
            {
                var areq = new AsyncHttpRequest(req);
                using (var rs = areq.GetRequestStream())
                    await rs.WriteAsync(data, 0, data.Length, ct).ConfigureAwait(false);

                await m_oauth.ReadJSONResponseAsync<BucketResourceItem>(req, ct).ConfigureAwait(false);
            });
        }

        #region IDisposable implementation
        public void Dispose()
        {

        }
        #endregion
    }
}

