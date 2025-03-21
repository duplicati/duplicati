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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend
{
    public class Idrivee2Backend : IBackend, IStreamingBackend, IFolderEnabledBackend
    {
        // TODO: Non-standard naming, should be access-key-id and access-key-secret
        private const string AUTH_USERNAME_OPTION = "access_key_id";
        private const string AUTH_PASSWORD_OPTION = "access_key_secret";
        private readonly string m_prefix;
        private readonly string m_bucket;

        private IS3Client? m_s3Client;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;
        private readonly AuthOptionsHelper.AuthOptions m_auth;
        private readonly Dictionary<string, string?> m_options;

        public Idrivee2Backend()
        {
            m_bucket = null!;
            m_prefix = null!;
            m_timeouts = null!;
            m_auth = null!;
            m_options = null!;
        }

        public Idrivee2Backend(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);
            m_bucket = uri.Host;
            m_prefix = uri.Path;
            m_prefix = m_prefix.Trim();
            if (m_prefix.Length != 0)
                m_prefix = Util.AppendDirSeparator(m_prefix, "/");

            m_timeouts = TimeoutOptionsHelper.Parse(options);
            m_auth = AuthOptionsHelper.ParseWithAlias(options, uri, AUTH_USERNAME_OPTION, AUTH_PASSWORD_OPTION);
            if (!m_auth.HasUsername)
                throw new UserInformationException(Strings.Idrivee2Backend.NoKeyIdError, "Idrivee2NoKeyId");
            if (!m_auth.HasPassword)
                throw new UserInformationException(Strings.Idrivee2Backend.NoKeySecretError, "Idrivee2NoKeySecret");

            m_options = options;
        }

        public async Task<string> GetRegionEndpointAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = System.Net.WebRequestMethods.Http.Get;

                var areq = new Utility.AsyncHttpRequest(req);

                using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, _ => (System.Net.HttpWebResponse)areq.GetResponse()).ConfigureAwait(false))
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new Exception("Failed to fetch region endpoint");
                    using (var s = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, _ => areq.GetResponseStream()))
                    using (var t = s.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
                    {
                        using (var reader = new StreamReader(t))
                        {
                            string endpoint = reader.ReadToEnd();
                            return endpoint;
                        }
                    }
                }
            }
            catch (System.Net.WebException wex)
            {
                //Convert to better exception
                throw new Exception("Failed to fetch region endpoint", wex);
            }
        }

        #region IBackend Members

        public string DisplayName => Strings.Idrivee2Backend.DisplayName;

        public string ProtocolKey => "e2";

        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            await foreach (IFileEntry file in con.ListBucketAsync(m_bucket, m_prefix, false, cancelToken).ConfigureAwait(false))
                yield return file;
        }

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken);
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            await con.AddFileStreamAsync(m_bucket, GetFullKey(remotename), input, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
        {
            using (var fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None))
                await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
        {
            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            await con.GetFileStreamAsync(m_bucket, GetFullKey(remotename), output, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            await con.DeleteObjectAsync(m_bucket, GetFullKey(remotename), cancellationToken).ConfigureAwait(false);
        }

        public IList<ICommandLineArgument> SupportedCommands =>
        [
            .. S3AwsClient.GetAwsExtendedOptions(),
            new CommandLineArgument(AUTH_USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.Idrivee2Backend.KeyIDDescriptionShort, Strings.Idrivee2Backend.KeyIDDescriptionLong,null, [AuthOptionsHelper.AuthUsername], null),
            new CommandLineArgument(AUTH_PASSWORD_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Idrivee2Backend.KeySecretDescriptionShort, Strings.Idrivee2Backend.KeySecretDescriptionLong, null, [AuthOptionsHelper.AuthPassword ], null),
            .. TimeoutOptionsHelper.GetOptions(),
        ];

        public string Description => Strings.Idrivee2Backend.Description;

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            //S3 does not complain if the bucket already exists
            await con.AddBucketAsync(m_bucket, cancelToken).ConfigureAwait(false);
        }

        #endregion

        #region IRenameEnabledBackend Members

        public async Task Rename(string source, string target, CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            await con.RenameFileAsync(m_bucket, GetFullKey(source), GetFullKey(target), cancelToken);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            m_s3Client?.Dispose();
            m_s3Client = null;
        }

        #endregion

        private async Task<IS3Client> GetConnection(CancellationToken cancellationToken)
        {
            if (m_s3Client == null)
            {
                (var accessKeyId, var accessKeySecret) = m_auth.GetCredentials();
                // TODO: Do not make blocking calls in the constructor
                var host = await GetRegionEndpointAsync("https://api.idrivee2.com/api/service/get_region_end_point/" + accessKeyId, cancellationToken).ConfigureAwait(false);
                m_s3Client = new S3AwsClient(accessKeyId, accessKeySecret, null, host, null, true, false, m_timeouts, m_options);
            }

            return m_s3Client;
        }

        public async Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            var dnshost = con.GetDnsHost();
            return string.IsNullOrWhiteSpace(dnshost)
                ? []
                : [dnshost];
        }

        private string GetFullKey(string? name)
            //AWS SDK encodes the filenames correctly
            => $"{m_prefix}{name}";

        /// <inheritdoc/>
        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var filterPath = GetFullKey(path);
            if (!string.IsNullOrWhiteSpace(filterPath))
                filterPath = Util.AppendDirSeparator(filterPath, "/");

            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            await foreach (var files in con.ListBucketAsync(m_bucket, filterPath, true, cancellationToken).ConfigureAwait(false))
                yield return files;
        }

        /// <inheritdoc/>
        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(null);
    }
}
