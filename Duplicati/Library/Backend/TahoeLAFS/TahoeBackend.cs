#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Library.Backend
{
    public class TahoeBackend : IBackend, IStreamingBackend
    {
        private string m_url;
        private bool m_useSSL = false;

        public TahoeBackend()
        {
        }

        public TahoeBackend(string url, Dictionary<string, string> options)
        {
            //Validate URL
            var u = new Utility.Uri(url);
            u.RequireHost();
            
            if (!u.Path.StartsWith("uri/URI:DIR2:"))
                throw new Exception(Strings.TahoeBackend.UnrecognizedUriError);

            m_useSSL = Utility.Utility.ParseBoolOption(options, "use-ssl");

            m_url = u.SetScheme(m_useSSL ? "https" : "http").SetQuery(null).SetCredentials(null, null).ToString();
            if (!m_url.EndsWith("/"))
                m_url += "/";
        }

        private System.Net.HttpWebRequest CreateRequest(string remotename, string queryparams)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(m_url + (System.Web.HttpUtility.UrlEncode(remotename).Replace("+", "%20")) + (string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? "" : "?" + queryparams));

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
            LitJson.JsonData data;

            try
            {
                System.Net.HttpWebRequest req = CreateRequest("", "t=json");
                req.Method = System.Net.WebRequestMethods.Http.Get;

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                    //HACK: We need the LitJSON to use Invariant culture, otherwise it cannot parse doubles
                    System.Globalization.CultureInfo ci = System.Threading.Thread.CurrentThread.CurrentCulture;
                    try
                    {
                        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                        using (System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream()))
                            data = LitJson.JsonMapper.ToObject(sr);
                    }
                    finally
                    {
                        try { System.Threading.Thread.CurrentThread.CurrentCulture = ci; }
                        catch { }
                    }
                }

            }
            catch (System.Net.WebException wex)
            {
                //Convert to better exception
                if (wex.Response as System.Net.HttpWebResponse != null)
                    if ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict || (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new Interface.FolderMissingException(string.Format(Strings.TahoeBackend.MissingFolderError, m_url, wex.Message), wex);

                throw;
            }

            if (data.Count < 2 || !data[0].IsString || (string)data[0] != "dirnode")
                throw new Exception(string.Format(Strings.TahoeBackend.UnexpectedJsonFragmentType, data.Count < 1 ? "<null>" : data[0], "dirnode"));

            if (!data[1].IsObject)
                throw new Exception(string.Format(Strings.TahoeBackend.UnexpectedJsonFragmentType, data[1], "Json object"));

            if (!(data[1] as System.Collections.IDictionary).Contains("children") || !data[1]["children"].IsObject || !(data[1]["children"] is System.Collections.IDictionary))
                throw new Exception(string.Format(Strings.TahoeBackend.UnexpectedJsonFragmentType, data[1], "children"));

            List<IFileEntry> files = new List<IFileEntry>();
            foreach (string key in ((System.Collections.IDictionary)data[1]["children"]).Keys)
            {
                LitJson.JsonData entry = data[1]["children"][key];
                if (!entry.IsArray || entry.Count < 2 || !entry[0].IsString || !entry[1].IsObject)
                    continue;

                bool isDir = ((string)entry[0]) == "dirnode";
                bool isFile = ((string)entry[0]) == "filenode";

                if (!isDir && !isFile)
                    continue;

                FileEntry fe = new FileEntry(key);
                fe.IsFolder = isDir;

                if (((System.Collections.IDictionary)entry[1]).Contains("metadata"))
                {
                    LitJson.JsonData fentry = entry[1]["metadata"];
                    if (fentry.IsObject && ((System.Collections.IDictionary)fentry).Contains("tahoe"))
                    {
                        fentry = fentry["tahoe"];

                        if (fentry.IsObject && ((System.Collections.IDictionary)fentry).Contains("linkmotime"))
                        {
                            try { fe.LastModification = ((DateTime)(Library.Utility.Utility.EPOCH + TimeSpan.FromSeconds((double)fentry["linkmotime"])).ToLocalTime()); }
                            catch { }
                        }
                    }
                }

                if (((System.Collections.IDictionary)entry[1]).Contains("size"))
                {
                    try 
                    { 
                        if (entry[1]["size"].IsInt)
                            fe.Size = (int)entry[1]["size"]; 
                        else if (entry[1]["size"].IsLong)
                            fe.Size = (long)entry[1]["size"]; 
                    }
                    catch {}
                }

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
            System.Net.HttpWebRequest req = CreateRequest(remotename, "");
            req.Method = "DELETE";
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (areq.GetResponse())
            { }
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
                    Utility.Utility.CopyStream(stream, s);

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
                        throw new Interface.FolderMissingException(string.Format(Strings.TahoeBackend.MissingFolderError, m_url, wex.Message), wex);

                throw;
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            System.Net.HttpWebRequest req = CreateRequest(remotename, "");
            req.Method = System.Net.WebRequestMethods.Http.Get;

            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                using (System.IO.Stream s = resp.GetResponseStream())
                    Utility.Utility.CopyStream(s, stream);
            }
        }

        #endregion
    }
}
