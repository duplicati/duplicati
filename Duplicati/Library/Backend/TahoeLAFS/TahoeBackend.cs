#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace Duplicati.Library.Backend
{
    public class TahoeBackend : IBackend, IStreamingBackend
    {
        private string m_url;
        private bool m_useSSL = false;
        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

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

                return new TahoeEl() { nodetype = nodetype,  node = node };
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
            
            if (!u.Path.StartsWith("uri/URI:DIR2:") && !u.Path.StartsWith("uri/URI%3ADIR2%3A"))
                throw new UserInformationException(Strings.TahoeBackend.UnrecognizedUriError);

            m_useSSL = Utility.Utility.ParseBoolOption(options, "use-ssl");

            m_url = u.SetScheme(m_useSSL ? "https" : "http").SetQuery(null).SetCredentials(null, null).ToString();
            if (!m_url.EndsWith("/"))
                m_url += "/";
        }

        private System.Net.HttpWebRequest CreateRequest(string remotename, string queryparams)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(m_url + (Library.Utility.Uri.UrlEncode(remotename).Replace("+", "%20")) + (string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? "" : "?" + queryparams));

            req.KeepAlive = false;
            req.UserAgent = "Duplicati Tahoe-LAFS Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            return req;
        }

        #region IBackend Members

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            System.Net.HttpWebRequest req = CreateRequest("", "t=mkdir");
            req.Method = System.Net.WebRequestMethods.Http.Post;
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (areq.GetResponse())
            { }
        }

        public string DisplayName
        {
            get { return Strings.TahoeBackend.Displayname; }
        }

        public string ProtocolKey
        {
            get { return "tahoe"; }
        }

        public List<IFileEntry> List()
        {
            TahoeEl data;

            try
            {
                var req = CreateRequest("", "t=json");
                req.Method = System.Net.WebRequestMethods.Http.Get;

                var areq = new Utility.AsyncHttpRequest(req);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                    using (var rs = areq.GetResponseStream())
                    using (var sr = new System.IO.StreamReader(rs))
                    using (var jr = new Newtonsoft.Json.JsonTextReader(sr))
                    {
                        var jsr  =new Newtonsoft.Json.JsonSerializer();
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
                
            var files = new List<IFileEntry>();
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

                files.Add(fe);
            }

            return files;
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
                if (wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw;
            }
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

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public void Put(string remotename, System.IO.Stream stream)
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
                    Utility.Utility.CopyStream(stream, s, true, m_copybuffer);

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

        public void Get(string remotename, System.IO.Stream stream)
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
                    Utility.Utility.CopyStream(s, stream, true, m_copybuffer);
            }
        }

        #endregion
    }
}
