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
using Newtonsoft.Json;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend.Sia
{
    public class Sia : IBackend
    {
        private const string SIA_PASSWORD = "sia-password";
        private const string SIA_TARGETPATH = "sia-targetpath";
        private const string SIA_REDUNDANCY = "sia-redundancy";

        private readonly string m_apihost;
        private readonly int m_apiport;
        private readonly string m_targetpath;
        private readonly float m_redundancy;
        private readonly string? m_authorization;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Sia()
        {
            m_apihost = null!;
            m_targetpath = null!;
            m_timeouts = null!;
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Sia(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);

            m_apihost = uri.Host;
            m_apiport = uri.Port;
            m_targetpath = uri.Path;
            m_timeouts = TimeoutOptionsHelper.Parse(options);

            m_redundancy = 1.5F;
            var redundancyString = options.GetValueOrDefault(SIA_REDUNDANCY);
            if (!string.IsNullOrWhiteSpace(redundancyString))
                m_redundancy = (float)decimal.Parse(redundancyString, CultureInfo.InvariantCulture);

            if (m_apiport <= 0)
                m_apiport = 9980;

            var targetPathString = options.GetValueOrDefault(SIA_TARGETPATH);
            if (!string.IsNullOrWhiteSpace(targetPathString))
                m_targetpath = targetPathString;

            while (m_targetpath.Contains("//"))
                m_targetpath = m_targetpath.Replace("//", "/");
            while (m_targetpath.StartsWith("/", StringComparison.Ordinal))
                m_targetpath = m_targetpath.Substring(1);
            while (m_targetpath.EndsWith("/", StringComparison.Ordinal))
                m_targetpath = m_targetpath.Remove(m_targetpath.Length - 1);

            if (m_targetpath.Length == 0)
                m_targetpath = "backup";

            m_authorization = options.ContainsKey(SIA_PASSWORD) && !string.IsNullOrEmpty(options[SIA_PASSWORD])
                ? "Basic " + Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(":" + options[SIA_PASSWORD]))
                : null;
        }

        private HttpWebRequest CreateRequest(string endpoint)
        {
            string baseurl = "http://" + m_apihost + ":" + m_apiport;
            var req = (HttpWebRequest)WebRequest.Create(baseurl + endpoint);

            if (m_authorization != null)
            {
                // Manually set Authorization header, since System.Net.NetworkCredential ignores credentials with empty usernames
                req.Headers.Add("Authorization", m_authorization);
            }

            req.KeepAlive = false;
            req.UserAgent = string.Format("Sia-Agent (Duplicati SIA client {0})", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

            return req;
        }

        private async Task<string> GetResponseBodyOnError(string context, WebException wex, CancellationToken cancellationToken)
        {
            var response = wex.Response as HttpWebResponse;
            if (response is null)
                return $"{context} failed with error: {wex.Message}";

            var body = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, _ =>
            {
                using (var data = response.GetResponseStream())
                using (var reader = new StreamReader(data))
                    return reader.ReadToEnd();
            }).ConfigureAwait(false);
            return string.Format("{0} failed, response: {1}", context, body);
        }

        public class SiaFile
        {
            [JsonProperty("siapath")]
            public string? Siapath { get; set; }
            [JsonProperty("available")]
            public bool Available { get; set; }
            [JsonProperty("filesize")]
            public long Filesize { get; set; }
            [JsonProperty("uploadprogress")]
            public float Uploadprogress { get; set; }
            [JsonProperty("redundancy")]
            public float Redundancy { get; set; }
        }

        public class SiaFileList
        {
            [JsonProperty("files")]
            public SiaFile[]? Files { get; set; }
        }

        public class SiaDownloadFile
        {
            [JsonProperty("siapath")]
            public string? Siapath { get; set; }
            [JsonProperty("destination")]
            public string? Destination { get; set; }
            [JsonProperty("filesize")]
            public long Filesize { get; set; }
            [JsonProperty("received")]
            public long Received { get; set; }
            [JsonProperty("starttime")]
            public string? Starttime { get; set; }
            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        public class SiaDownloadList
        {
            [JsonProperty("downloads")]
            public SiaDownloadFile[]? Files { get; set; }
        }

        private async Task<SiaFileList> GetFiles(CancellationToken cancelToken)
        {
            // Remove warning until this is rewritten to use HttpClient
            await Task.CompletedTask;

            var fl = new SiaFileList();
            var endpoint = "/renter/files";

            try
            {
                var req = CreateRequest(endpoint);
                req.Method = WebRequestMethods.Http.Get;

                var areq = new AsyncHttpRequest(req);

                using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => (HttpWebResponse)areq.GetResponse()).ConfigureAwait(false))
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300)
                        throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);

                    var serializer = new JsonSerializer();

                    using (var rs = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => areq.GetResponseStream()).ConfigureAwait(false))
                    using (var sr = new StreamReader(rs))
                    using (var jr = new JsonTextReader(sr))
                        fl = serializer.Deserialize<SiaFileList>(jr)
                            ?? throw new Exception("Failed to deserialize response");
                }
            }
            catch (WebException wex)
            {
                throw new Exception(await GetResponseBodyOnError(endpoint, wex, cancelToken).ConfigureAwait(false));
            }
            return fl;
        }

        private async Task<bool> IsUploadComplete(string siafilename, CancellationToken cancelToken)
        {
            var fl = await GetFiles(cancelToken).ConfigureAwait(false);
            if (fl.Files == null)
                return false;

            foreach (var f in fl.Files)
            {
                if (f.Siapath == siafilename)
                {
                    if (f.Available == true && f.Redundancy >= m_redundancy /* && f.Uploadprogress >= 100 */ )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private async Task<SiaDownloadList> GetDownloads(CancellationToken cancelToken)
        {
            var fl = new SiaDownloadList();
            string endpoint = "/renter/downloads";

            try
            {
                var req = CreateRequest(endpoint);
                req.Method = WebRequestMethods.Http.Get;

                var areq = new AsyncHttpRequest(req);

                using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _ => (HttpWebResponse)areq.GetResponse()).ConfigureAwait(false))
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300)
                        throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);

                    var serializer = new JsonSerializer();

                    using (var rs = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => areq.GetResponseStream()))
                    using (var sr = new StreamReader(rs))
                    using (var jr = new JsonTextReader(sr))
                        fl = serializer.Deserialize<SiaDownloadList>(jr)
                            ?? throw new Exception("Failed to deserialize response");
                }
            }
            catch (WebException wex)
            {
                throw new Exception(await GetResponseBodyOnError(endpoint, wex, cancelToken).ConfigureAwait(false));
            }
            return fl;
        }

        private async Task<bool> IsDownloadComplete(string siafilename, string localname, CancellationToken cancelToken)
        {
            var fl = await GetDownloads(cancelToken).ConfigureAwait(false);
            if (fl.Files == null)
                return false;

            foreach (var f in fl.Files)
            {
                if (f.Siapath == siafilename)
                {
                    if (f.Error != "")
                    {
                        throw new Exception("failed to download " + siafilename + "err: " + f.Error);
                    }
                    if (f.Filesize == f.Received)
                    {
                        try
                        {
                            // Sia seems to keep the file open/locked for a while, make sure we can open it
                            using (var fs = new FileStream(localname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                fs.Close();
                        }
                        catch (IOException)
                        {
                            return false;
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        #region IBackend Members

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            // Dummy method, Sia doesn't have folders
            return Task.CompletedTask;
        }

        public string DisplayName => Strings.Sia.DisplayName;

        public string ProtocolKey => "sia";

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            SiaFileList fl;
            try
            {
                fl = await GetFiles(cancelToken).ConfigureAwait(false);
            }
            catch (WebException wex)
            {
                throw new Exception("failed to call /renter/files " + wex.Message);
            }

            if (fl.Files != null)
            {
                foreach (var f in fl.Files)
                {
                    // Sia returns a complete file list, but we're only interested in files that are
                    // in our target path
                    if (f.Siapath != null && f.Siapath.StartsWith(m_targetpath, StringComparison.Ordinal))
                    {
                        var fe = new FileEntry(f.Siapath.Substring(m_targetpath.Length + 1))
                        {
                            Size = f.Filesize,
                            IsFolder = false
                        };
                        yield return fe;
                    }
                }
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            var endpoint = "";
            var siafile = m_targetpath + "/" + remotename;

            try
            {
                endpoint = string.Format("/renter/upload/{0}/{1}?source={2}",
                    m_targetpath,
                    Utility.Uri.UrlEncode(remotename).Replace("+", "%20"),
                    Utility.Uri.UrlEncode(filename).Replace("+", "%20")
                );

                var req = CreateRequest(endpoint);
                req.Method = WebRequestMethods.Http.Post;

                var areq = new AsyncHttpRequest(req);

                using (var resp = (HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300)
                        throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);

                    while (!await IsUploadComplete(siafile, cancelToken).ConfigureAwait(false))
                        await Task.Delay(5000, cancelToken).ConfigureAwait(false);
                }
            }
            catch (WebException wex)
            {
                throw new Exception(await GetResponseBodyOnError(endpoint, wex, cancelToken).ConfigureAwait(false));
            }
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            string endpoint = "";
            string siafile = m_targetpath + "/" + remotename;
            string tmpfilename = localname + ".tmp";

            try
            {
                endpoint = string.Format("/renter/download/{0}/{1}?destination={2}",
                    m_targetpath,
                    Utility.Uri.UrlEncode(remotename).Replace("+", "%20"),
                    Utility.Uri.UrlEncode(tmpfilename).Replace("+", "%20")
                );
                var req = CreateRequest(endpoint);
                req.Method = WebRequestMethods.Http.Get;

                var areq = new AsyncHttpRequest(req);

                using (var resp = (HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300)
                        throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);

                    while (!await IsDownloadComplete(siafile, localname, cancelToken).ConfigureAwait(false))
                        await Task.Delay(5000, cancelToken).ConfigureAwait(false);

                    File.Copy(tmpfilename, localname, true);
                    try
                    {
                        File.Delete(tmpfilename);
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw new Exception(await GetResponseBodyOnError(endpoint, wex, cancelToken).ConfigureAwait(false));
            }
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            string endpoint = "";

            try
            {
                endpoint = string.Format("/renter/delete/{0}/{1}",
                    m_targetpath,
                    Utility.Uri.UrlEncode(remotename).Replace("+", "%20")
                );
                var req = CreateRequest(endpoint);
                req.Method = WebRequestMethods.Http.Post;

                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, _ =>
                {
                    var areq = new AsyncHttpRequest(req);
                    using (var resp = (HttpWebResponse)areq.GetResponse())
                    {
                        int code = (int)resp.StatusCode;
                        if (code < 200 || code >= 300)
                            throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);
                    }
                }).ConfigureAwait(false);
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw new Exception(await GetResponseBodyOnError(endpoint, wex, cancellationToken).ConfigureAwait(false));
            }
        }


        public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>([
                    new CommandLineArgument(SIA_TARGETPATH, CommandLineArgument.ArgumentType.String, Strings.Sia.SiaPathDescriptionShort, Strings.Sia.SiaPathDescriptionLong, "/backup"),
                    new CommandLineArgument(SIA_PASSWORD, CommandLineArgument.ArgumentType.Password, Strings.Sia.SiaPasswordShort, Strings.Sia.SiaPasswordLong, null),
                    new CommandLineArgument(SIA_REDUNDANCY, CommandLineArgument.ArgumentType.Decimal, Strings.Sia.SiaRedundancyDescriptionShort, Strings.Sia.SiaRedundancyDescriptionLong, "1.5"),
                    .. TimeoutOptionsHelper.GetOptions()
                        .Where(x => x.Name != TimeoutOptionsHelper.READ_WRITE_TIMEOUT_OPTION)
                ]);

        public string Description => Strings.Sia.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { new System.Uri(m_apihost).Host });

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion


    }


}
