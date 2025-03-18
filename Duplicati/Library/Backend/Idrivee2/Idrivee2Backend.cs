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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    public class Idrivee2Backend : IBackend, IStreamingBackend, IFolderEnabledBackend
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<Idrivee2Backend>();

        static Idrivee2Backend()
        {

        }

        private readonly string m_prefix;
        private readonly string m_bucket;

        private IS3Client m_s3Client;

        public Idrivee2Backend()
        {
        }

        public Idrivee2Backend(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            m_bucket = uri.Host;
            m_prefix = uri.Path;
            m_prefix = m_prefix.Trim();
            if (m_prefix.Length != 0)
            {
                m_prefix = Util.AppendDirSeparator(m_prefix, "/");
            }
            string accessKeyId = null;
            string accessKeySecret = null;

            if (options.ContainsKey("auth-username"))
                accessKeyId = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                accessKeySecret = options["auth-password"];

            if (options.ContainsKey("access_key_id"))
                accessKeyId = options["access_key_id"];
            if (options.ContainsKey("secret_access_key"))
                accessKeySecret = options["secret_access_key"];

            if (string.IsNullOrEmpty(accessKeyId))
                throw new UserInformationException(Strings.Idrivee2Backend.NoKeyIdError, "Idrivee2NoKeyId");
            if (string.IsNullOrEmpty(accessKeySecret))
                throw new UserInformationException(Strings.Idrivee2Backend.NoKeySecretError, "Idrivee2NoKeySecret");
            string host = GetRegionEndpoint("https://api.idrivee2.com/api/service/get_region_end_point/" + accessKeyId);


            m_s3Client = new S3AwsClient(accessKeyId, accessKeySecret, null, host, null, true, false, options);
        }

        public string GetRegionEndpoint(string url)
        {
            try
            {
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = System.Net.WebRequestMethods.Http.Get;

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);

                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new Exception("Failed to fetch region endpoint");
                    using (var s = areq.GetResponseStream())
                    {
                        using (var reader = new StreamReader(s))
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

        public string DisplayName
        {
            get { return Strings.Idrivee2Backend.DisplayName; }
        }

        public string ProtocolKey => "e2";

        public bool SupportsStreaming => true;

        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            await foreach (IFileEntry file in Connection.ListBucketAsync(m_bucket, m_prefix, false, cancelToken).ConfigureAwait(false))
                yield return file;
        }

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (FileStream fs = File.Open(localname, FileMode.Open, FileAccess.Read, FileShare.Read))
                await PutAsync(remotename, fs, cancelToken);
        }

        public async Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            await Connection.AddFileStreamAsync(m_bucket, GetFullKey(remotename), input, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
        {
            using (var fs = File.Open(localname, FileMode.Create, FileAccess.Write, FileShare.None))
                await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
        }

        public Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
        {
            return Connection.GetFileStreamAsync(m_bucket, GetFullKey(remotename), output, cancellationToken);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            return Connection.DeleteObjectAsync(m_bucket, GetFullKey(remotename), cancellationToken);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                var exts = S3AwsClient.GetAwsExtendedOptions();

                var normal = new ICommandLineArgument[] {
                    new CommandLineArgument("access_key_secret", CommandLineArgument.ArgumentType.Password, Strings.Idrivee2Backend.KeySecretDescriptionShort, Strings.Idrivee2Backend.KeySecretDescriptionLong, null, new[]{"auth-password"}, null),
                    new CommandLineArgument("access_key_id", CommandLineArgument.ArgumentType.String, Strings.Idrivee2Backend.KeyIDDescriptionShort, Strings.Idrivee2Backend.KeyIDDescriptionLong,null, new[]{"auth-username"}, null)
                };

                return normal.Union(exts).ToList();
            }
        }

        public string Description
        {
            get
            {
                return Strings.Idrivee2Backend.Description;
            }
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            //S3 does not complain if the bucket already exists
            return Connection.AddBucketAsync(m_bucket, cancelToken);
        }

        #endregion

        #region IRenameEnabledBackend Members

        public Task Rename(string source, string target, CancellationToken cancelToken)
        {
            return Connection.RenameFileAsync(m_bucket, GetFullKey(source), GetFullKey(target), cancelToken);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            m_s3Client?.Dispose();
            m_s3Client = null;
        }

        #endregion

        private IS3Client Connection => m_s3Client;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { m_s3Client.GetDnsHost() });

        private string GetFullKey(string name)
        {
            //AWS SDK encodes the filenames correctly
            return m_prefix + name;
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<IFileEntry> ListAsync(string path, CancellationToken cancellationToken)
        {
            var filterPath = GetFullKey(path);
            if (!string.IsNullOrWhiteSpace(filterPath))
                filterPath = Util.AppendDirSeparator(filterPath, "/");

            return m_s3Client.ListBucketAsync(m_bucket, filterPath, true, cancellationToken);
        }


        /// <inheritdoc/>
        public Task<IFileEntry> GetEntryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<IFileEntry>(null);
    }
}
