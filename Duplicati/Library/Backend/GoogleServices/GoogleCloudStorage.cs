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
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;

namespace Duplicati.Library.Backend.GoogleCloudStorage
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class GoogleCloudStorage : IBackend, IStreamingBackend, IRenameEnabledBackend
    {
        private static readonly string TOKEN_URL = OAuthHelperHttpClient.OAUTH_LOGIN_URL("gcs");
        private const string PROJECT_OPTION = "gcs-project";

        private const string LOCATION_OPTION = "gcs-location";
        private const string STORAGECLASS_OPTION = "gcs-storage-class";

        private readonly string m_bucket;
        private readonly string m_prefix;
        private readonly string? m_project;
        private readonly OAuthHelperHttpClient m_oauth;

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

            m_oauth = new OAuthHelperHttpClient(authId.AuthId!, this.ProtocolKey);
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
                        Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, async ct =>
                        {
                            using var req = await m_oauth.CreateRequestAsync(url, HttpMethod.Get, ct).ConfigureAwait(false);
                            req.Headers.Add("Accept", "application/json");

                            using var resp = await m_oauth.GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                            resp.EnsureSuccessStatusCode();

                            return await resp.Content.ReadFromJsonAsync<ListBucketResponse>(ct).ConfigureAwait(false)
                                   ?? throw new Exception("Failed to parse response");
                        })).ConfigureAwait(false);

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
        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            using var req = await m_oauth.CreateRequestAsync(WebApi.GoogleCloudStorage.DeleteUrl(m_bucket, Library.Utility.Uri.UrlPathEncode(m_prefix + remotename)), HttpMethod.Delete, cancelToken);

            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct
                =>
            {
                using var resp = m_oauth.GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                resp.Result.EnsureSuccessStatusCode();
            }).ConfigureAwait(false);
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            if (string.IsNullOrEmpty(m_project))
                throw new UserInformationException(Strings.GoogleCloudStorage.ProjectIDMissingError(PROJECT_OPTION), "GoogleCloudStorageMissingProjectID");

            using var req = await m_oauth.CreateRequestAsync(WebApi.GoogleCloudStorage.CreateFolderUrl(m_project), HttpMethod.Post, cancelToken);
            req.Content = JsonContent.Create(new CreateBucketRequest
            {
                name = m_bucket,
                location = m_location,
                storageClass = m_storage_class
            });

            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
            {
                using var resp = await m_oauth.GetResponseAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                var res = await resp.Content.ReadFromJsonAsync<BucketResourceItem>(ct).ConfigureAwait(false);
                if (res == null)
                    throw new Exception("Create folder succeeded, but no data was returned");
            }).ConfigureAwait(false);
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
            var res = await GoogleCommon.ChunkedUploadWithResumeAsync<BucketResourceItem, BucketResourceItem>(m_oauth, item, url, stream, m_timeouts.ShortTimeout, m_timeouts.ReadWriteTimeout, cancelToken, HttpMethod.Post).ConfigureAwait(false);

            if (res == null)
                throw new Exception("Upload succeeded, but no data was returned");
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                var url = WebApi.GoogleCloudStorage.GetUrl(m_bucket, Utility.Uri.UrlPathEncode(m_prefix + remotename));
                using var req = await m_oauth.CreateRequestAsync(url, HttpMethod.Get, cancelToken);
                using var resp = await Library.Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_oauth.GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);
                using var source = await Library.Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => resp.Content.ReadAsStreamAsync(cancelToken)).ConfigureAwait(false);

                using (var ts = source.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
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

        public async Task RenameAsync(string oldname, string newname, CancellationToken cancelToken)
        {
            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new BucketResourceItem
            {
                name = m_prefix + newname,
            }));

            var req = await m_oauth.CreateRequestAsync(WebApi.GoogleCloudStorage.RenameUrl(m_bucket, Utility.Uri.UrlPathEncode(m_prefix + oldname)), HttpMethod.Patch, cancelToken).ConfigureAwait(false);
            req.Content = JsonContent.Create(new BucketResourceItem
            {
                name = m_prefix + newname,
            });

            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
            {
                using var resp = await m_oauth.GetResponseAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await resp.Content.ReadFromJsonAsync<BucketResourceItem>(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        #region IDisposable implementation
        public void Dispose()
        {

        }
        #endregion
    }
}

