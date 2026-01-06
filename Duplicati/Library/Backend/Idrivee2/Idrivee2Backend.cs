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

using System.Reflection;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Backend
{
    public class Idrivee2Backend : IStreamingBackend, IFolderEnabledBackend, ILockingBackend
    {
        // Non-standard naming managed with AuthOptionsHelper.ParseWithAlias
        private const string AUTH_USERNAME_OPTION = "access_key_id";
        private const string AUTH_PASSWORD_OPTION = "access_key_secret";

        /// <summary>
        /// Cached S3 client
        /// </summary>
        private IS3Client? _s3Client;

        /// <summary>
        /// The path prefix for all operations within the bucket
        /// </summary>
        private readonly string _prefix = null!;

        /// <summary>
        /// Bucked name
        /// </summary>
        private readonly string _bucket = null!;

        /// <summary>
        /// Lazy cached HttpClient
        /// </summary>
        private readonly Lazy<HttpClient> _httpClient = new(() =>
        {
            var client = HttpClientHelper.CreateClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        });

        /// <summary>
        /// The timeout options
        /// </summary>
        private readonly TimeoutOptionsHelper.Timeouts _timeouts;

        /// <summary>
        /// The authentication options
        /// </summary>
        private readonly AuthOptionsHelper.AuthOptions _auth;

        /// <summary>
        /// All options passed to the backend
        /// </summary>
        private readonly Dictionary<string, string?> _options;

        private const string S3_LOCK_MODE_OPTION = "s3-lock-mode";

        /// <inheritdoc />
        public Idrivee2Backend()
        {
            _timeouts = null!;
            _auth = null!;
            _options = null!;
        }

        /// <inheritdoc />
        public Idrivee2Backend(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);
            _bucket = uri.Host ?? "";
            _prefix = uri.Path;
            _prefix = _prefix.Trim();
            if (_prefix.Length != 0)
                _prefix = Util.AppendDirSeparator(_prefix, "/");

            _timeouts = TimeoutOptionsHelper.Parse(options);
            _auth = AuthOptionsHelper.ParseWithAlias(options, uri, AUTH_USERNAME_OPTION, AUTH_PASSWORD_OPTION);
            if (!_auth.HasUsername)
                throw new UserInformationException(Strings.Idrivee2Backend.NoKeyIdError, "Idrivee2NoKeyId");
            if (!_auth.HasPassword)
                throw new UserInformationException(Strings.Idrivee2Backend.NoKeySecretError, "Idrivee2NoKeySecret");

            _options = options;
        }

        /// <inheritdoc />
        private async Task<string> GetRegionEndpointAsync(string url, CancellationToken cancellationToken)
        {
            string result;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(
                    $"Duplicati Idrivee2 Client {Assembly.GetExecutingAssembly().GetName().Version?.ToString()}");

                // Complete all operations within the using scope
                using var resp = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken,
                        innerCancellationToken => _httpClient.Value.SendAsync(request, innerCancellationToken))
                    .ConfigureAwait(false);

                if (resp.StatusCode != System.Net.HttpStatusCode.OK)
                    throw new Exception("Failed to fetch region endpoint");

                await using var s = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var t = s.ObserveReadTimeout(_timeouts.ReadWriteTimeout);
                using var reader = new StreamReader(t);
                result = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to fetch region endpoint", ex);
            }

            return result;
        }

        /// <inheritdoc />
        public string DisplayName => Strings.Idrivee2Backend.DisplayName;

        /// <inheritdoc />
        public string ProtocolKey => "e2";

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            await foreach (IFileEntry file in con.ListBucketAsync(_bucket, _prefix, false, cancelToken).ConfigureAwait(false))
                yield return file;
        }

        /// <inheritdoc />
        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            await using FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read);
            await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            await con.AddFileStreamAsync(_bucket, GetFullKey(remotename), input, cancelToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
        {
            await using var fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None);
            await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
        {
            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            await con.GetFileStreamAsync(_bucket, GetFullKey(remotename), output, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<DateTime?> GetObjectLockUntilAsync(string remotename, CancellationToken cancellationToken)
        {
            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            return await con.GetObjectLockUntilAsync(_bucket, GetFullKey(remotename), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            await con.DeleteObjectAsync(_bucket, GetFullKey(remotename), cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetObjectLockUntilAsync(string remotename, DateTime lockUntilUtc, CancellationToken cancellationToken)
        {
            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            await con.SetObjectLockUntilAsync(_bucket, GetFullKey(remotename), lockUntilUtc, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            .. S3AwsClient.GetAwsExtendedOptions(),
            new CommandLineArgument(AUTH_USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.Idrivee2Backend.KeyIDDescriptionShort, Strings.Idrivee2Backend.KeyIDDescriptionLong,null, [AuthOptionsHelper.AuthUsernameOption], null),
            new CommandLineArgument(AUTH_PASSWORD_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Idrivee2Backend.KeySecretDescriptionShort, Strings.Idrivee2Backend.KeySecretDescriptionLong, null, [AuthOptionsHelper.AuthPasswordOption ], null),
            new CommandLineArgument(S3_LOCK_MODE_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.Idrivee2Backend.DescriptionLockModeShort, Strings.Idrivee2Backend.DescriptionLockModeLong, "governance", null, new string[] { "governance", "compliance" }),
            .. TimeoutOptionsHelper.GetOptions(),
        ];

        /// <inheritdoc />
        public string Description => Strings.Idrivee2Backend.Description;

        /// <inheritdoc />
        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

        /// <inheritdoc/>
        public bool SupportsStreaming => true;

        /// <inheritdoc />
        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            //S3 does not complain if the bucket already exists
            await con.AddBucketAsync(_bucket, cancelToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _s3Client?.Dispose();
            _s3Client = null;
            if (_httpClient.IsValueCreated) _httpClient.Value.Dispose();
        }

        private async Task<IS3Client> GetConnection(CancellationToken cancellationToken)
        {
            if (_s3Client != null) return _s3Client;

            var (accessKeyId, accessKeySecret) = _auth.GetCredentials();

            var host = await GetRegionEndpointAsync("https://api.idrivee2.com/api/service/get_region_end_point/" + accessKeyId, cancellationToken).ConfigureAwait(false);
            var lockMode = _options.GetValueOrDefault(S3_LOCK_MODE_OPTION, "governance") ?? "governance";
            _s3Client = new S3AwsClient(accessKeyId, accessKeySecret, null, host, null, true, false, false, _timeouts, _options, lockMode, null);

            return _s3Client;
        }

        /// <inheritdoc />
        public async Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        {
            var con = await GetConnection(cancelToken).ConfigureAwait(false);
            var dnsHost = con.GetDnsHost();
            return string.IsNullOrWhiteSpace(dnsHost)
                ? []
                : [dnsHost];
        }

        private string GetFullKey(string? name)
            //AWS SDK encodes the filenames correctly
            => $"{_prefix}{name}";

        /// <inheritdoc/>
        public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var filterPath = GetFullKey(path);
            if (!string.IsNullOrWhiteSpace(filterPath))
                filterPath = Util.AppendDirSeparator(filterPath, "/");

            var con = await GetConnection(cancellationToken).ConfigureAwait(false);
            await foreach (var files in con.ListBucketAsync(_bucket, filterPath, true, cancellationToken).ConfigureAwait(false))
                yield return files;
        }

        /// <inheritdoc/>
        public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry?>(null);
    }
}
