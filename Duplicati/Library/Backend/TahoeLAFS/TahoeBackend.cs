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
using Newtonsoft.Json.Linq;
using System.Net;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend
{
    public class TahoeBackend : IBackend, IStreamingBackend
    {
        private readonly string m_url;
        private readonly bool m_useSSL = false;
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        private class TahoeEl
        {
            public string? nodetype { get; set; }
            public TahoeNode? node { get; set; }
        }

        private class TahoeNode
        {
            public string? rw_uri { get; set; }
            public string? verify_uri { get; set; }
            public string? ro_uri { get; set; }
            public Dictionary<string, TahoeEl>? children { get; set; }
            public bool mutable { get; set; }
            public long size { get; set; }
            public TahoeMetadata? metadata { get; set; }
        }

        private class TahoeMetadata
        {
            public TahoeStamps? tahoe { get; set; }
        }

        private class TahoeStamps
        {
            public double linkmotime { get; set; }
            public double linkcrtime { get; set; }
        }

        private class TahoeElConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
                => objectType == typeof(TahoeEl);

            public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                var array = JArray.Load(reader);
                string? nodetype = null;
                TahoeNode? node = null;
                foreach (var token in array.Children())
                    if (token.Type == JTokenType.String)
                        nodetype = token.ToString();
                    else if (token.Type == JTokenType.Object)
                        node = token.ToObject<TahoeNode>(serializer);

                return new TahoeEl() { nodetype = nodetype, node = node };
            }

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
                => throw new NotImplementedException();
        }


        public TahoeBackend()
        {
            m_url = null!;
            m_timeouts = null!;
        }

        public TahoeBackend(string url, Dictionary<string, string?> options)
        {
            //Validate URL
            var u = new Utility.Uri(url);
            u.RequireHost();

            if (!u.Path.StartsWith("uri/URI:DIR2:", StringComparison.Ordinal) && !u.Path.StartsWith("uri/URI%3ADIR2%3A", StringComparison.Ordinal))
                throw new UserInformationException(Strings.TahoeBackend.UnrecognizedUriError, "TahoeInvalidUri");

            // TODO: When upgrading to HttpClient, also support the certificate options
            var certOptions = SslOptionsHelper.Parse(options);
            m_useSSL = certOptions.UseSSL;

            m_url = u.SetScheme(m_useSSL ? "https" : "http").SetQuery(null).SetCredentials(null, null).ToString();
            m_url = Util.AppendDirSeparator(m_url, "/");
            m_timeouts = TimeoutOptionsHelper.Parse(options);
        }

        private HttpWebRequest CreateRequest(string remotename, string queryparams)
        {
            var req = (HttpWebRequest)HttpWebRequest.Create($"{m_url}{Utility.Uri.UrlEncode(remotename).Replace("+", "%20")}{(string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? "" : "?" + queryparams)}");

            req.KeepAlive = false;
            req.UserAgent = "Duplicati Tahoe-LAFS Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            return req;
        }

        #region IBackend Members

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var req = CreateRequest("", "t=mkdir");
            req.Method = WebRequestMethods.Http.Post;
            var areq = new AsyncHttpRequest(req);
            using (await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _ => areq.GetResponse()).ConfigureAwait(false))
            { }
        }

        public string DisplayName => Strings.TahoeBackend.Displayname;

        public string ProtocolKey => "tahoe";

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            TahoeEl data;

            try
            {
                var req = CreateRequest("", "t=json");
                req.Method = WebRequestMethods.Http.Get;

                var areq = new AsyncHttpRequest(req);
                using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => (HttpWebResponse)areq.GetResponse()).ConfigureAwait(false))
                {
                    var code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);

                    using (var rs = areq.GetResponseStream())
                    using (var sr = new StreamReader(rs))
                    using (var jr = new JsonTextReader(sr))
                    {
                        var jsr = new JsonSerializer();
                        jsr.Converters.Add(new TahoeElConverter());
                        data = jsr.Deserialize<TahoeEl>(jr)
                            ?? throw new Exception("Invalid folder listing response");
                    }
                }
            }
            catch (WebException wex)
                when (wex.Response is HttpWebResponse response
                    && (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.NotFound))
            {
                throw new FolderMissingException(Strings.TahoeBackend.MissingFolderError(m_url, wex.Message), wex);
            }

            if (data == null || data.node == null || data.nodetype != "dirnode")
                throw new Exception("Invalid folder listing response");

            foreach (var e in data.node.children ?? [])
            {
                if (e.Value == null || e.Value.node == null)
                    continue;

                var isDir = e.Value.nodetype == "dirnode";
                var isFile = e.Value.nodetype == "filenode";

                if (!isDir && !isFile)
                    continue;

                var fe = new FileEntry(e.Key);
                fe.IsFolder = isDir;

                if (e.Value.node.metadata != null && e.Value.node.metadata.tahoe != null)
                    fe.LastModification = Utility.Utility.EPOCH + TimeSpan.FromSeconds(e.Value.node.metadata.tahoe.linkmotime);

                if (isFile)
                    fe.Size = e.Value.node.size;

                yield return fe;
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
            try
            {
                var req = CreateRequest(remotename, "");
                req.Method = "DELETE";
                var areq = new AsyncHttpRequest(req);
                using (await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _ => areq.GetResponse()).ConfigureAwait(false))
                { }
            }
            catch (WebException wex)
                when (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new FileMissingException(wex);
            }
        }

        public IList<ICommandLineArgument> SupportedCommands => [
            .. SslOptionsHelper.GetSslOnlyOption(),
        ];

        public string Description => Strings.TahoeBackend.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { new System.Uri(m_url).Host });

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                var req = CreateRequest(remotename, "");
                req.Method = WebRequestMethods.Http.Put;
                req.ContentType = "application/binary";

                try { req.ContentLength = stream.Length; }
                catch { }

                var areq = new AsyncHttpRequest(req);
                using (var s = areq.GetRequestStream())
                using (var t = s.ObserveWriteTimeout(m_timeouts.ReadWriteTimeout))
                    await Utility.Utility.CopyStreamAsync(stream, t, true, cancelToken).ConfigureAwait(false);

                using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, _ => (HttpWebResponse)areq.GetResponse()).ConfigureAwait(false))
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300)
                        throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);
                }
            }
            catch (WebException wex)
                when (wex.Response is HttpWebResponse response
                    && (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.NotFound))
            {
                throw new FolderMissingException(Strings.TahoeBackend.MissingFolderError(m_url, wex.Message), wex);
            }
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var req = CreateRequest(remotename, "");
            req.Method = WebRequestMethods.Http.Get;

            var areq = new AsyncHttpRequest(req);
            using (var resp = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, _ => (HttpWebResponse)areq.GetResponse()).ConfigureAwait(false))
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300)
                    throw new WebException(resp.StatusDescription, null, WebExceptionStatus.ProtocolError, resp);

                using (var s = areq.GetResponseStream())
                using (var t = s.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
                    await Utility.Utility.CopyStreamAsync(t, stream, true, cancelToken).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
