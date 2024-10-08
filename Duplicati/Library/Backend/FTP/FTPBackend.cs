// Copyright (C) 2024, The Duplicati Team
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
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class FTP : IBackend, IStreamingBackend
    {
        private System.Net.NetworkCredential m_userInfo;
        private readonly string m_url;

        private readonly bool m_useSSL = false;
        private readonly bool m_passiveMode = false;
        private readonly bool m_listVerify = true;

        public FTP()
        {
        }

        public FTP(string url, Dictionary<string, string> options)
        {
            //This can be made better by keeping a single ftp stream open,
            //unfortunately the .Net model does not allow this as the request is 
            //bound to a single url (path+file).
            //
            //To fix this, a thirdparty FTP library is required,
            //this would also allow a fix for the FTP servers
            //that only support SSL during authentication, not during transfers
            //
            //If you have experience with a stable open source .Net FTP library,
            //please let the Duplicati authors know

            var u = new Utility.Uri(url);
            u.RequireHost();
            string username = null;
            string password = null;

            if (!string.IsNullOrEmpty(u.Username))
            {
                username = u.Username;
            }
            else if (options.ContainsKey("auth-username"))
            {
                username = options["auth-username"];
            }

            if (!string.IsNullOrEmpty(u.Username) && !string.IsNullOrEmpty(u.Password))
            {
                password = u.Password;
            }
            else if (options.ContainsKey("auth-password"))
            {
                password = options["auth-password"];
            }

            m_userInfo = new System.Net.NetworkCredential
            {
                UserName = username,
                Password = password
            };

            //Bugfix, see http://connect.microsoft.com/VisualStudio/feedback/details/695227/networkcredential-default-constructor-leaves-domain-null-leading-to-null-object-reference-exceptions-in-framework-code
            if (m_userInfo != null)
                m_userInfo.Domain = "";

            m_url = u.SetScheme("ftp").SetQuery(null).SetCredentials(null, null).ToString();
            m_url = Util.AppendDirSeparator(m_url, "/");

            m_useSSL = Utility.Utility.ParseBoolOption(options, "use-ssl");

            m_listVerify = !Utility.Utility.ParseBoolOption(options, "disable-upload-verify");

            if (Utility.Utility.ParseBoolOption(options, "ftp-passive"))
            {
                m_passiveMode = true;
            }
            else m_passiveMode = !Utility.Utility.ParseBoolOption(options, "ftp-regular");
        }

        #region Regular expression to parse list lines
        //Regexps found here: http://www.dotnetfunda.com/articles/article125.aspx
        //Modified to allow hyphens in username and groupname
        internal readonly static Regex[] PARSEFORMATS =
        {
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+\d+\s+(?<groupname>\S+)\s+(?<username>\S+)\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{4})\s+(?<name>.+)"),
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+(?<groupname>\d+)\s+(?<username>\d+)\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{4})\s+(?<name>.+)"),
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+(?<groupname>\d+)\s+(?<username>\d+)\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{1,2}:\d{2})\s+(?<name>.+)"),
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+\d+\s+(?<groupname>\S+)\s+(?<username>\S+)\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{1,2}:\d{2})\s+(?<name>.+)"),
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})(\s+)(?<size>(\d+))(\s+)(?<ctbit>(\w+\s\w+))(\s+)(?<size2>(\d+))\s+(?<timestamp>\w+\s+\d+\s+\d{2}:\d{2})\s+(?<name>.+)"),
            new Regex(@"(?<timestamp>\d{2}\-\d{2}\-\d{2}\s+\d{2}:\d{2}[Aa|Pp][mM])\s+(?<dir>\<\w+\>){0,1}(?<size>\d+){0,1}\s+(?<name>.+)"),
            new Regex(@"([<timestamp>]*\d{2}\-\d{2}\-\d{2}\s+\d{2}:\d{2}[Aa|Pp][mM])\s+([<dir>]*\<\w+\>){0,1}([<size>]*\d+){0,1}\s+([<name>]*.+)")
        };
        #endregion

        private static Match MatchLine(string line)
        {
            Match m = null;
            foreach (Regex s in PARSEFORMATS)
                if ((m = s.Match(line)).Success)
                    return m;

            return null;
        }

        public static FileEntry ParseLine(string line)
        {
            Match m = MatchLine(line);
            if (m == null)
                return null;

            FileEntry f = new FileEntry(m.Groups["name"].Value);

            string time = m.Groups["timestamp"].Value;
            string dir = m.Groups["dir"].Value;

            if (dir != "" && dir != "-")
            {
                f.IsFolder = true;
            }
            else
            {
                f.Size = long.Parse(m.Groups["size"].Value);
            }

            if (DateTime.TryParse(time, out DateTime t))
            {
                f.LastAccess = f.LastModification = t;
            }

            return f;
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return Strings.FTPBackend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "ftp"; }
        }

        private T HandleListExceptions<T>(Func<T> func, System.Net.FtpWebRequest req)
        {
            T ret = default(T);
            Action action = () => ret = func();
            HandleListExceptions(action, req);
            return ret;
        }

        private void HandleListExceptions(Action action, System.Net.FtpWebRequest req)
        {
            try
            {
                action();
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response as System.Net.FtpWebResponse != null && (wex.Response as System.Net.FtpWebResponse).StatusCode == System.Net.FtpStatusCode.ActionNotTakenFileUnavailable)
                    throw new Interface.FolderMissingException(Strings.FTPBackend.MissingFolderError(req.RequestUri.PathAndQuery, wex.Message), wex);
                else
                    throw;
            }
        }

        public IEnumerable<IFileEntry> List()
        {
            return List("");
        }

        public IEnumerable<IFileEntry> List(string filename)
        {
            var req = CreateRequest(filename);
            req.Method = System.Net.WebRequestMethods.Ftp.ListDirectoryDetails;
            req.UseBinary = false;

            System.Net.WebResponse resp = null;
            System.IO.Stream rs = null;
            System.IO.StreamReader sr = null;

            try
            {
                HandleListExceptions(
                    () =>
                        {
                            var areq = new Utility.AsyncHttpRequest(req);
                            resp = areq.GetResponse();
                            rs = areq.GetResponseStream();
                            sr = new System.IO.StreamReader(new StreamReadHelper(rs));
                        },
                    req);

                string line;
                while ((line = HandleListExceptions(sr.ReadLine, req)) != null)
                {
                    FileEntry f = ParseLine(line);
                    if (f != null)
                        yield return f;
                }
            }
            finally
            {
                try
                {
                    if (sr != null)
                    {
                        sr.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        if (rs != null)
                        {
                            rs.Dispose();
                        }
                    }
                    finally
                    {
                        if (resp != null)
                        {
                            resp.Dispose();
                        }
                    }
                }
            }
        }

        public async Task PutAsync(string remotename, System.IO.Stream input, CancellationToken cancelToken)
        {
            System.Net.FtpWebRequest req = null;
            try
            {
                req = CreateRequest(remotename);
                req.Method = System.Net.WebRequestMethods.Ftp.UploadFile;
                req.UseBinary = true;

                long streamLen = -1;
                try { streamLen = input.Length; }
                catch { }

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.IO.Stream rs = areq.GetRequestStream(streamLen))
                    await Utility.Utility.CopyStreamAsync(input, rs, true, cancelToken).ConfigureAwait(false);

                if (m_listVerify)
                {
                    IEnumerable<IFileEntry> files = List(remotename);
                    foreach (var fe in files)
                        if (fe.Name.Equals(remotename) || fe.Name.EndsWith("/" + remotename, StringComparison.Ordinal) || fe.Name.EndsWith("\\" + remotename, StringComparison.Ordinal))
                        {
                            if (fe.Size < 0 || streamLen < 0 || fe.Size == streamLen)
                                return;

                            throw new Exception(Strings.FTPBackend.ListVerifySizeFailure(remotename, fe.Size, streamLen));
                        }

                    throw new Exception(Strings.FTPBackend.ListVerifyFailure(remotename, files.Select(n => n.Name)));
                }

            }
            catch (System.Net.WebException wex)
            {
                if (req != null && wex.Response as System.Net.FtpWebResponse != null && (wex.Response as System.Net.FtpWebResponse).StatusCode == System.Net.FtpStatusCode.ActionNotTakenFileUnavailable)
                    throw new Interface.FolderMissingException(Strings.FTPBackend.MissingFolderError(req.RequestUri.PathAndQuery, wex.Message), wex);
                else
                    throw;
            }
        }

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, System.IO.Stream output, CancellationToken cancelToken)
        {
            var req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Ftp.DownloadFile;
            req.UseBinary = true;

            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = areq.GetResponse())
            using (var rs = areq.GetResponseStream())
                await Utility.Utility.CopyStreamAsync(rs, output, false, cancelToken).ConfigureAwait(false);
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            System.Net.FtpWebRequest req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Ftp.DeleteFile;
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (areq.GetResponse())
            { }

            return Task.CompletedTask;
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("ftp-passive", CommandLineArgument.ArgumentType.Boolean, Strings.FTPBackend.DescriptionFTPPassiveShort, Strings.FTPBackend.DescriptionFTPPassiveLong, "false"),
                    new CommandLineArgument("ftp-regular", CommandLineArgument.ArgumentType.Boolean, Strings.FTPBackend.DescriptionFTPActiveShort, Strings.FTPBackend.DescriptionFTPActiveLong, "true"),
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.FTPBackend.DescriptionAuthPasswordShort, Strings.FTPBackend.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.FTPBackend.DescriptionAuthUsernameShort, Strings.FTPBackend.DescriptionAuthUsernameLong),
                    new CommandLineArgument("use-ssl", CommandLineArgument.ArgumentType.Boolean, Strings.FTPBackend.DescriptionUseSSLShort, Strings.FTPBackend.DescriptionUseSSLLong),
                    new CommandLineArgument("disable-upload-verify", CommandLineArgument.ArgumentType.Boolean, Strings.FTPBackend.DescriptionDisableUploadVerifyShort, Strings.FTPBackend.DescriptionDisableUploadVerifyLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.FTPBackend.Description;
            }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new string[] { new Uri(m_url).Host });

        public Task TestAsync(CancellationToken cancelToken)
        {
            this.TestList();
            return Task.CompletedTask;
        }

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            System.Net.FtpWebRequest req = CreateRequest("", true);
            req.Method = System.Net.WebRequestMethods.Ftp.MakeDirectory;
            req.KeepAlive = false;
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (areq.GetResponse())
            { }

            return Task.CompletedTask;
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            m_userInfo = null;
        }

        #endregion

        private System.Net.FtpWebRequest CreateRequest(string remotename)
        {
            return CreateRequest(remotename, false);
        }

        private System.Net.FtpWebRequest CreateRequest(string remotename, bool createFolder)
        {
            string url = m_url;
            if (createFolder && url.EndsWith("/", StringComparison.Ordinal))
                url = url.Substring(0, url.Length - 1);

            System.Net.FtpWebRequest req = (System.Net.FtpWebRequest)System.Net.WebRequest.Create(url + remotename);

            if (m_userInfo != null)
            {
                req.Credentials = m_userInfo;
            }

            req.KeepAlive = false;
            req.UsePassive = m_passiveMode;

            if (m_useSSL)
                req.EnableSsl = m_useSSL;

            return req;
        }

        /// <summary>
        /// Private helper class to fix a bug with the StreamReader
        /// </summary>
        private class StreamReadHelper : Utility.OverrideableStream
        {
            /// <summary>
            /// Once the stream has returned 0 as the read count it is disposed
            /// in the FtpRequest, and subsequent read requests will throw an ObjectDisposedException
            /// </summary>
            private bool m_empty = false;

            /// <summary>
            /// Basic initialization, just pass the stream to the super class
            /// </summary>
            /// <param name="stream"></param>
            public StreamReadHelper(System.IO.Stream stream)
                : base(stream)
            {
            }

            /// <summary>
            /// Override the read function to make sure that we only return less than the requested amount of data if the stream is exhausted
            /// </summary>
            /// <param name="buffer">The buffer to place data in</param>
            /// <param name="offset">The offset into the buffer to start at</param>
            /// <param name="count">The number of bytes to read</param>
            /// <returns>The number of bytes read</returns>
            public override int Read(byte[] buffer, int offset, int count)
            {
                int readCount = 0;
                int a;

                while (!m_empty && count > 0)
                {
                    a = base.Read(buffer, offset, count);
                    readCount += a;
                    count -= a;
                    offset += a;
                    m_empty = a == 0;
                }

                return readCount;
            }
        }
    }
}
