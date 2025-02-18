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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    public class TahoeBackend : IBackend, IStreamingBackend
    {
        private readonly string m_url;
        private readonly bool m_useSSL = false;

        private class TahoeEl
        {
            public string nodetype { get; set; }
            public TahoeNode node { get; set; }
        }

        private class TahoeNode
        {
            public string rw_uri { get; set; }
            public string verify_uri { get; set; }
            public string ro_uri { get; set; }
            public Dictionary<string, TahoeEl> children { get; set; }
            public bool mutable { get; set; }
            public long size { get; set; }
            public TahoeMetadata metadata { get; set; }
        }

        private class TahoeMetadata
        {
            public TahoeStamps tahoe { get; set; }
        }

        private class TahoeStamps
        {
            public double linkmotime { get; set; }
            public double linkcrtime { get; set; }
        }

        private class TahoeElConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(TahoeEl);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var array = JArray.Load(reader);
                string nodetype = null;
                TahoeNode node = null;
                foreach (var token in array.Children())
                    if (token.Type == JTokenType.String)
                        nodetype = token.ToString();
                    else if (token.Type == JTokenType.Object)
                        node = token.ToObject<TahoeNode>(serializer);

                return new TahoeEl() { nodetype = nodetype, node = node };
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }


        public TahoeBackend()
        {
        }

        public TahoeBackend(string url, Dictionary<string, string> options)
        {
            //Validate URL
            var u = new Utility.Uri(url);
            u.RequireHost();

            if (!u.Path.StartsWith("uri/URI:DIR2:", StringComparison.Ordinal) && !u.Path.StartsWith("uri/URI%3ADIR2%3A", StringComparison.Ordinal))
                throw new UserInformationException(Strings.TahoeBackend.UnrecognizedUriError, "TahoeInvalidUri");

            m_useSSL = Utility.Utility.ParseBoolOption(options, "use-ssl");

            m_url = u.SetScheme(m_useSSL ? "https" : "http").SetQuery(null).SetCredentials(null, null).ToString();
            m_url = Util.AppendDirSeparator(m_url, "/");
        }

        private System.Net.HttpWebRequest CreateRequest(string remotename, string queryparams)
        {
            var req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(m_url + (Library.Utility.Uri.UrlEncode(remotename).Replace("+", "%20")) + (string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? "" : "?" + queryparams));

            req.KeepAlive = false;
            req.UserAgent = "Duplicati Tahoe-LAFS Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            return req;
        }

        #region IBackend Members

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var req = CreateRequest("", "t=mkdir");
            req.Method = System.Net.WebRequestMethods.Http.Post;
            var areq = new Utility.AsyncHttpRequest(req);
            using (areq.GetResponse())
            { }

            return Task.CompletedTask;
        }

        public string DisplayName
        {
            get { return Strings.TahoeBackend.Displayname; }
        }

        public string ProtocolKey
        {
            get { return "tahoe"; }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            // Remove warning until this is rewritten to use HttpClient
            await Task.CompletedTask;

            TahoeEl data;

            try
            {
                var req = CreateRequest("", "t=json");
                req.Method = System.Net.WebRequestMethods.Http.Get;

                var areq = new Utility.AsyncHttpRequest(req);
                using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    var code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                    using (var rs = areq.GetResponseStream())
                    using (var sr = new System.IO.StreamReader(rs))
                    using (var jr = new Newtonsoft.Json.JsonTextReader(sr))
                    {
                        var jsr = new Newtonsoft.Json.JsonSerializer();
                        jsr.Converters.Add(new TahoeElConverter());
                        data = jsr.Deserialize<TahoeEl>(jr);
                    }
                }
            }
            catch (System.Net.WebException wex)
            {
                //Convert to better exception
                if (wex.Response as System.Net.HttpWebResponse != null)
                    if ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict || (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new Interface.FolderMissingException(Strings.TahoeBackend.MissingFolderError(m_url, wex.Message), wex);

                throw;
            }

            if (data == null || data.node == null || data.nodetype != "dirnode")
                throw new Exception("Invalid folder listing response");

            foreach (var e in data.node.children)
            {
                if (e.Value == null || e.Value.node == null)
                    continue;

                bool isDir = e.Value.nodetype == "dirnode";
                bool isFile = e.Value.nodetype == "filenode";

                if (!isDir && !isFile)
                    continue;

                FileEntry fe = new FileEntry(e.Key);
                fe.IsFolder = isDir;

                if (e.Value.node.metadata != null && e.Value.node.metadata.tahoe != null)
                    fe.LastModification = Duplicati.Library.Utility.Utility.EPOCH + TimeSpan.FromSeconds(e.Value.node.metadata.tahoe.linkmotime);

                if (isFile)
                    fe.Size = e.Value.node.size;

                yield return fe;
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = System.IO.File.Create(filename))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                System.Net.HttpWebRequest req = CreateRequest(remotename, "");
                req.Method = "DELETE";
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (areq.GetResponse())
                { }
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw;
            }

            return Task.CompletedTask;
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("use-ssl", CommandLineArgument.ArgumentType.Boolean, Strings.TahoeBackend.DescriptionUseSSLShort, Strings.TahoeBackend.DescriptionUseSSLLong),
                });
            }
        }

        public string Description
        {
            get { return Strings.TahoeBackend.Description; }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { new Uri(m_url).Host });

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                System.Net.HttpWebRequest req = CreateRequest(remotename, "");
                req.Method = System.Net.WebRequestMethods.Http.Put;
                req.ContentType = "application/binary";

                try { req.ContentLength = stream.Length; }
                catch { }

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.IO.Stream s = areq.GetRequestStream())
                    await Utility.Utility.CopyStreamAsync(stream, s, true, cancelToken).ConfigureAwait(false);

                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
                }
            }
            catch (System.Net.WebException wex)
            {
                //Convert to better exception
                if (wex.Response as System.Net.HttpWebResponse != null)
                    if ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict || (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new Interface.FolderMissingException(Strings.TahoeBackend.MissingFolderError(m_url, wex.Message), wex);

                throw;
            }
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var req = CreateRequest(remotename, "");
            req.Method = System.Net.WebRequestMethods.Http.Get;

            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                using (var s = areq.GetResponseStream())
                    await Utility.Utility.CopyStreamAsync(s, stream, true, cancelToken).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
