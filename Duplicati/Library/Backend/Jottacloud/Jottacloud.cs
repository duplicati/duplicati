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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class Jottacloud : IBackend, IStreamingBackend
    {
        public const string JFS_ROOT = "https://www.jottacloud.com/jfs";
        public const string JFS_ROOT_UP = "https://up.jottacloud.com/jfs"; // Separate host for uploading files
        public const string JFS_DEVICE = "Jotta";
        public const string API_VERSION = "2.2"; // Hard coded per October 2014
        public const string DateTimeFormat = "yyyy'-'MM'-'dd-'T'HH':'mm':'ssK";
        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

        private System.Net.NetworkCredential m_userInfo;
        private string m_path;
        private string m_url;
        private string m_url_up;

        public Jottacloud()
        {
        }

        public Jottacloud(string url, Dictionary<string, string> options)
        {
            var u = new Utility.Uri(url);
            m_path = u.HostAndPath;
            if (string.IsNullOrEmpty(m_path))
                throw new UserInformationException(Strings.Jottacloud.NoPathError);
            if (!m_path.EndsWith("/"))
                m_path += "/";
            if (!string.IsNullOrEmpty(u.Username))
            {
                m_userInfo = new System.Net.NetworkCredential();
                m_userInfo.UserName = u.Username;
                if (!string.IsNullOrEmpty(u.Password))
                    m_userInfo.Password = u.Password;
                else if (options.ContainsKey("auth-password"))
                    m_userInfo.Password = options["auth-password"];
            }
            else
            {
                if (options.ContainsKey("auth-username"))
                {
                    m_userInfo = new System.Net.NetworkCredential();
                    m_userInfo.UserName = options["auth-username"];
                    if (options.ContainsKey("auth-password"))
                        m_userInfo.Password = options["auth-password"];
                }
            }
            if (m_userInfo == null || string.IsNullOrEmpty(m_userInfo.UserName))
                throw new UserInformationException(Strings.Jottacloud.NoUsernameError);
            if (m_userInfo == null || string.IsNullOrEmpty(m_userInfo.Password))
                throw new UserInformationException(Strings.Jottacloud.NoPasswordError);
            //Bugfix, see http://connect.microsoft.com/VisualStudio/feedback/details/695227/networkcredential-default-constructor-leaves-domain-null-leading-to-null-object-reference-exceptions-in-framework-code
            if (m_userInfo != null)
                m_userInfo.Domain = "";
            m_url = JFS_ROOT + "/" + m_userInfo.UserName + "/" + JFS_DEVICE + "/" + m_path;
            m_url_up = JFS_ROOT_UP + "/" + m_userInfo.UserName + "/" + JFS_DEVICE + "/" + m_path;
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.Jottacloud.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "jottacloud"; }
        }

        public List<IFileEntry> List()
        {
            var req = CreateRequest("", "", false);
            req.Method = System.Net.WebRequestMethods.Http.Get;
            try
            {
                // Send request and load XML response.
                var doc = new System.Xml.XmlDocument();
                var areq = new Utility.AsyncHttpRequest(req);
                using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
                using (var rs = areq.GetResponseStream())
                    doc.Load(rs);

                // Handle XML response. Note that root element can be "mountPoint" or "folder", but the content is very similar.
                List<IFileEntry> files = new List<IFileEntry>();
                foreach (System.Xml.XmlNode xFolder in doc.SelectNodes("//folders/folder"))
                {
                    // Folders have only name in Jottacloud file system.
                    FileEntry fe = new FileEntry(xFolder.Attributes["name"].Value);
                    fe.IsFolder = true;
                    files.Add(fe);
                }
                foreach (System.Xml.XmlNode xFile in doc.SelectNodes("//files/file"))
                {
                    string name = xFile.Attributes["name"].Value;
                    // Normal files have "currentRevision", incomplete or corrupt files have "latestRevision" or "revision" instead.
                    System.Xml.XmlNode xRevision = xFile.SelectSingleNode("currentRevision");
                    if (xRevision != null)
                    {
                        System.Xml.XmlNode xNode = xRevision.SelectSingleNode("size");
                        long size;
                        if (xNode == null || !long.TryParse(xNode.InnerText, out size))
                            size = -1;
                        DateTime lastModified;
                        xNode = xRevision.SelectSingleNode("modified"); // There is also a timestamp for "updated"?
                        if (xNode == null || !DateTime.TryParseExact(xNode.InnerText, DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out lastModified))
                            lastModified = new DateTime();
                        FileEntry fe = new FileEntry(name, size, lastModified, lastModified);
                        files.Add(fe);
                    }
                }
                return files;
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response as System.Net.HttpWebResponse != null &&
                        ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound || (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict))
                    throw new Interface.FolderMissingException(Strings.Jottacloud.MissingFolderError(req.RequestUri.PathAndQuery, wex.Message), wex);

                throw;
            }
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
            // Assuming remotename specifies a file for deleting a folder we need a different query ("dlDir=true").
            System.Net.HttpWebRequest req = CreateRequest("", "dl=true", false);
            req.Method = System.Net.WebRequestMethods.Http.Post;
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
            { }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get 
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.Jottacloud.DescriptionAuthPasswordShort, Strings.Jottacloud.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.Jottacloud.DescriptionAuthUsernameShort, Strings.Jottacloud.DescriptionAuthUsernameLong),
                });
            }
        }

        public string Description
        {
            get { return Strings.Jottacloud.Description; }
        }

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            System.Net.HttpWebRequest req = CreateRequest("", "mkDir=true", false);
            req.Method = System.Net.WebRequestMethods.Http.Post;
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
            { }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        private System.Net.HttpWebRequest CreateRequest(string remotename, string queryparams, bool upload)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create((upload ? m_url_up : m_url) + Library.Utility.Uri.UrlEncode(remotename).Replace("+", "%20") + (string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? "" : "?" + queryparams));
            req.Credentials = m_userInfo;
            //We need this under Mono for some reason,
            // and it appears some servers require this as well
            req.PreAuthenticate = true; 

            req.KeepAlive = false;
            req.UserAgent = "Duplicati Jottacloud Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            req.Headers.Add("X-JottaAPIVersion", API_VERSION);

            return req;
        }

        #region IStreamingBackend Members

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            var req = CreateRequest(remotename, "mode=bin", false);
            req.Method = System.Net.WebRequestMethods.Http.Get;
            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
            using (var s = areq.GetResponseStream())
                Utility.Utility.CopyStream(s, stream, true, m_copybuffer);
        }

        public void Put(string remotename, System.IO.Stream stream)
        {

            if (stream.CanSeek)
            {
                // Pre-calculate MD5 hash, we need it in query parameter, in HTTP header and in POST message data!
                string md5Hash;
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                    md5Hash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty);
                long fileSize = stream.Position; // Assuming ComputeHash has processed the entire stream we should be at the end now.
                stream.Seek(0, System.IO.SeekOrigin.Begin); // Move stream back to 0, or specified offset, after the MD5 calculation has used it.
                // Create request, with query parater, and a few custom headers.
                var req = CreateRequest(remotename, "cphash="+md5Hash, true);
                req.Method = System.Net.WebRequestMethods.Http.Post;
                string fileTime = DateTime.Now.ToString("o"); // NB: Cheating by setting current time as created/modified timestamps
                req.Headers.Add("JMd5", md5Hash);
                req.Headers.Add("JCreated", fileTime);
                req.Headers.Add("JModified", fileTime);
                req.Headers.Add("X-Jfs-DeviceName", JFS_DEVICE);
                req.Headers.Add("JSize", fileSize.ToString());
                req.Headers.Add("jx_csid", "");
                req.Headers.Add("jx_lisence", "");

                // Prepare post data:
                // First three simple data sections: md5, modified time and created time.
                // Then a final section with the file contents. We prepare everything,
                // calculate the total size including the file, and then we write it
                // to the request. This way we can stream the file directly into the
                // request without copying the entire file into byte array first etc.
                string multipartBoundary = string.Format("----------{0:N}", Guid.NewGuid());
                byte[] multiPartContent = System.Text.Encoding.UTF8.GetBytes(
                    CreateMultiPartItem("md5", md5Hash, multipartBoundary) + "\r\n"
                  + CreateMultiPartItem("modified", fileTime, multipartBoundary) + "\r\n"
                  + CreateMultiPartItem("created", fileTime, multipartBoundary) + "\r\n"
                  + CreateMultiPartFileHeader("file", remotename, null, multipartBoundary) + "\r\n");
                byte[] multipartTerminator = System.Text.Encoding.UTF8.GetBytes("\r\n--" + multipartBoundary + "--\r\n");
                req.ContentType = "multipart/form-data; boundary=" + multipartBoundary;
                req.ContentLength = multiPartContent.Length + fileSize + multipartTerminator.Length;
                // Write post data request
                var areq = new Utility.AsyncHttpRequest(req);
                using (var rs = areq.GetRequestStream())
                {
                    rs.Write(multiPartContent, 0, multiPartContent.Length);
                    Utility.Utility.CopyStream(stream, rs, true, m_copybuffer);
                    rs.Write(multipartTerminator, 0, multipartTerminator.Length);
                }
                // Send request, and check response
                using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode != System.Net.HttpStatusCode.Created)
                        throw new System.Net.WebException(Strings.Jottacloud.FileUploadError, null, System.Net.WebExceptionStatus.ProtocolError, resp);
                }
            }
        }

        private string CreateMultiPartItem(string contentName, string contentValue, string boundary)
        {
            // Header and content. Append newline before next section, or footer section.
            return string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"\r\n\r\n{2}",
                boundary,
                contentName,  //TODO: URLEncod it? HttpUtility.UrlEncode(contentName, System.Text.Encoding.UTF8),
                contentValue);  //TODO: URLEncod it? HttpUtility.UrlEncode(contentValue, System.Text.Encoding.UTF8));
        }

        private string CreateMultiPartFileHeader(string contentName, string fileName, string fileType, string boundary)
        {
            // Header. Append newline and then file content.
            return string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\";\r\nContent-Type: {3}\r\n",
                boundary,
                contentName, //TODO: URLEncod it? HttpUtility.UrlEncode(contentName, System.Text.Encoding.UTF8),
                fileName,  //TODO: URLEncod it? HttpUtility.UrlEncode(fileName, System.Text.Encoding.UTF8),
                fileType ?? "application/octet-stream");
        }


        #endregion
    }
}
