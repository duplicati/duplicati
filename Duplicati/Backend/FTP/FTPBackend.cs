#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Backend
{
    public class FTPBackend : IBackendInterface
    {

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


        private Match MatchLine(string line)
        {
            Match m = null;
            foreach (Regex s in PARSEFORMATS)
                if ((m = s.Match(line)).Success)
                    return m;

            return null;
        }

        private FileEntry ParseLine(string line)
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
            get { return "FTP based"; }
        }

        public string ProtocolKey
        {
            get { return "ftp"; }
        }

        public List<FileEntry> List(string url, Dictionary<string, string> options)
        {
            System.Net.FtpWebRequest req = CreateRequest(url, options);
            req.Method = System.Net.WebRequestMethods.Ftp.ListDirectoryDetails;
            req.UseBinary = false;

            List<FileEntry> lst = new List<FileEntry>();
            using (System.IO.StreamReader sr = new System.IO.StreamReader(req.GetResponse().GetResponseStream()))
            while(sr.Peek() >= 0)
            {
                FileEntry f = ParseLine(sr.ReadLine());
                if (f != null)
                    lst.Add(f);
            }
            return lst;
        }

        public void Put(string url, Dictionary<string, string> options, System.IO.Stream stream)
        {
            System.Net.FtpWebRequest req = CreateRequest(url, options);
            req.Method = System.Net.WebRequestMethods.Ftp.UploadFile;
            Utility.CopyStream(stream, req.GetRequestStream(), true);
            req.GetResponse();
        }

        public System.IO.Stream Get(string url, Dictionary<string, string> options)
        {
            System.Net.FtpWebRequest req = CreateRequest(url, options);
            req.Method = System.Net.WebRequestMethods.Ftp.DownloadFile;
            return req.GetResponse().GetResponseStream();
        }

        public void Delete(string url, Dictionary<string, string> options)
        {
            System.Net.FtpWebRequest req = CreateRequest(url, options);
            req.Method = System.Net.WebRequestMethods.Ftp.DeleteFile;
            req.GetResponse();
        }

        #endregion

        private System.Net.FtpWebRequest CreateRequest(string url, Dictionary<string, string> options)
        {
            System.Net.FtpWebRequest req = (System.Net.FtpWebRequest)System.Net.FtpWebRequest.Create(url);
            //TODO: Read out password, etc. from options
            return req;
        }
    }
}
