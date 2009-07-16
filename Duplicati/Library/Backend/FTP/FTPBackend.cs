#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using System.Text.RegularExpressions;

namespace Duplicati.Library.Backend
{
    public class FTP : IStreamingBackend
    {
        private System.Net.NetworkCredential m_userInfo;
        private string m_url;
        Dictionary<string, string> m_options;

        public FTP()
        {
        }

        public FTP(string url, Dictionary<string, string> options)
        {
            Uri u = new Uri(url);

            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                m_userInfo = new System.Net.NetworkCredential();
                if (u.UserInfo.IndexOf(":") >= 0)
                {
                    m_userInfo.UserName = u.UserInfo.Substring(0, u.UserInfo.IndexOf(":"));
                    m_userInfo.Password = u.UserInfo.Substring(u.UserInfo.IndexOf(":") + 1);
                }
                else
                {
                    m_userInfo.UserName = u.UserInfo;
                    if (options.ContainsKey("ftp-password"))
                        m_userInfo.Password = options["ftp-password"];
                }
            }
            else
            {
                if (options.ContainsKey("ftp-username"))
                {
                    m_userInfo = new System.Net.NetworkCredential();
                    m_userInfo.UserName = options["ftp-username"];
                    if (options.ContainsKey("ftp-password"))
                        m_userInfo.Password = options["ftp-password"];
                }
            }

            m_options = options;
            m_url = url;
            if (!m_url.EndsWith("/"))
                m_url += "/";
        }

        #region Regular expression to parse list lines
        //Regexps found here: http://www.dotnetfunda.com/articles/article125.aspx
        internal readonly static Regex[] PARSEFORMATS =
        {
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+\d+\s+\w+\s+\w+\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{4})\s+(?<name>.+)"),
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+\d+\s+\d+\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{4})\s+(?<name>.+)"),
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+\d+\s+\d+\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{1,2}:\d{2})\s+(?<name>.+)"),
            new Regex(@"(?<dir>[\-d])(?<permission>([\-r][\-w][\-xs]){3})\s+\d+\s+\w+\s+\w+\s+(?<size>\d+)\s+(?<timestamp>\w+\s+\d+\s+\d{1,2}:\d{2})\s+(?<name>.+)"),
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
            string permission = m.Groups["permission"].Value;
            string dir = m.Groups["dir"].Value;

            if (dir != "" && dir != "-")
                f.IsFolder = true;
            else
                f.Size = long.Parse(m.Groups["size"].Value);

            DateTime t;
            if (DateTime.TryParse(time, out t))
                f.LastAccess = f.LastModification = t;

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

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public List<FileEntry> List()
        {
            System.Net.FtpWebRequest req = CreateRequest("");
            req.Method = System.Net.WebRequestMethods.Ftp.ListDirectoryDetails;
            req.UseBinary = false;

            List<FileEntry> lst = new List<FileEntry>();
            using (System.Net.WebResponse resp = req.GetResponse())
            using (System.IO.Stream rs = resp.GetResponseStream())
            using (System.IO.StreamReader sr = new System.IO.StreamReader(rs))
                while(sr.Peek() >= 0)
                {
                    FileEntry f = ParseLine(sr.ReadLine());
                    if (f != null)
                        lst.Add(f);
                }

            return lst;
        }

        public void Put(string remotename, System.IO.Stream input)
        {
            System.Net.FtpWebRequest req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Ftp.UploadFile;
            req.UseBinary = true;

            using (System.IO.Stream rs = req.GetRequestStream())
                Core.Utility.CopyStream(input, rs, true);
        }

        public void Put(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Put(remotename, fs);
        }

        public void Get(string remotename, System.IO.Stream output)
        {
            System.Net.FtpWebRequest req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Ftp.DownloadFile;
            req.UseBinary = true;

            using (System.Net.WebResponse resp = req.GetResponse())
            using (System.IO.Stream rs = resp.GetResponseStream())
                Core.Utility.CopyStream(rs, output, false);
        }

        public void Get(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            System.Net.FtpWebRequest req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Ftp.DeleteFile;
            using (req.GetResponse())
            { }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("ftp-passive", CommandLineArgument.ArgumentType.Boolean, Strings.FTPBackend.DescriptionFTPPassiveShort, Strings.FTPBackend.DescriptionFTPPassiveLong, "false"),
                    new CommandLineArgument("ftp-regular", CommandLineArgument.ArgumentType.Boolean, Strings.FTPBackend.DescriptionFTPActiveShort, Strings.FTPBackend.DescriptionFTPActiveLong, "true"),
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, Strings.FTPBackend.DescriptionFTPPasswordShort, Strings.FTPBackend.DescriptionFTPPasswordLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.FTPBackend.DescriptionFTPUsernameShort, Strings.FTPBackend.DescriptionFTPUsernameLong),
                    new CommandLineArgument("integrated-authentication", CommandLineArgument.ArgumentType.Boolean, Strings.FTPBackend.DescriptionIntegratedAuthenticationShort, Strings.FTPBackend.DescriptionIntegratedAuthenticationLong),
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

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_userInfo != null)
                m_userInfo = null;
            if (m_options != null)
                m_options = null;
        }

        #endregion

        private System.Net.FtpWebRequest CreateRequest(string remotename)
        {
            System.Net.FtpWebRequest req = (System.Net.FtpWebRequest)System.Net.FtpWebRequest.Create(m_url + remotename);

            if (m_userInfo != null)
                req.Credentials = m_userInfo;
            req.KeepAlive = false;

            if (m_options.ContainsKey("ftp-passive"))
                req.UsePassive = true;
            if (m_options.ContainsKey("ftp-regular"))
                req.UsePassive = false;

            return req;
        }



    }
}
