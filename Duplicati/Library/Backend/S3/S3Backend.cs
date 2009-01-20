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

namespace Duplicati.Library.Backend
{
    public class S3 : IBackendInterface
    {
        private string m_awsID;
        private string m_awsKey;
        private string m_url;
        private string m_host;
        private string m_bucket;
        private string m_prefix;
        private Affirma.ThreeSharp.CallingFormat m_format;
        private bool m_euBuckets;

        Dictionary<string, string> m_options;

        public S3()
        {
        }

        public S3(string url, Dictionary<string, string> options)
        {
            Uri u = new Uri(url);

            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                if (u.UserInfo.IndexOf(":") >= 0)
                {
                    m_awsID = u.UserInfo.Substring(0, u.UserInfo.IndexOf(":"));
                    m_awsKey = u.UserInfo.Substring(u.UserInfo.IndexOf(":") + 1);
                }
                else
                {
                    m_awsID = u.UserInfo;
                    if (options.ContainsKey("aws_secret_access_key"))
                        m_awsKey = options["aws_secret_access_key"];
                    else if (options.ContainsKey("ftp_password"))
                        m_awsKey = options["ftp_password"];
                }
            }
            else
            {
                if (options.ContainsKey("aws_access_key_id"))
                    m_awsID = options["aws_access_key_id"];
                if (options.ContainsKey("aws_secret_access_key"))
                    m_awsKey = options["aws_secret_access_key"];
            }

            if (string.IsNullOrEmpty(m_awsID))
                throw new Exception("No Amazon S3 userID given");
            if (string.IsNullOrEmpty(m_awsKey))
                throw new Exception("No Amazon S3 secret key given");


            m_prefix = "";

            m_host = u.Host;
            m_format = options.ContainsKey("s3-use-new-style") ? Affirma.ThreeSharp.CallingFormat.SUBDOMAIN : Affirma.ThreeSharp.CallingFormat.REGULAR;
            m_euBuckets = options.ContainsKey("s3-european-buckets");

            if (m_host.ToLower() == "s3.amazonaws.com")
            {
                m_bucket = u.PathAndQuery;

                if (m_bucket.Contains("/"))
                {
                    m_prefix = m_bucket.Substring(m_bucket.IndexOf("/") + 1);
                    m_bucket = m_bucket.Substring(0, m_bucket.IndexOf("/"));
                }

            }
            else 
            {
                //Vanity style, do a CNAME lookup
                if (!m_host.ToLower().EndsWith(".s3.amazonaws.com"))
                {
                    System.Net.IPHostEntry ent = System.Net.Dns.GetHostEntry(m_host);
                    List<string> entries = new List<string>();
                    entries.AddRange(ent.Aliases);

                    foreach (string s in entries)
                        if (s.ToLower().EndsWith(".s3.amazonaws.com"))
                        {
                            m_host = s;
                            break;
                        }
                }

                if (m_host.ToLower().EndsWith(".s3.amazonaws.com"))
                {
                    m_format = Affirma.ThreeSharp.CallingFormat.SUBDOMAIN;
                    m_bucket = m_host.Substring(0, m_host.Length - ".s3.amazonaws.com".Length);
                    m_host = "s3.amazonaws.com";
                    m_prefix = u.PathAndQuery;
                    
                }
                else
                    throw new Exception("Unable to determine the bucket name for host: " + m_url);
            }


            m_options = options;
            m_url = url;
            m_prefix = m_prefix.Trim();
            if (m_prefix.Length != 0 && !m_prefix.EndsWith("/"))
                m_prefix += "/";
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return "Amazon S3 based"; }
        }

        public string ProtocolKey
        {
            get { return "s3"; }
        }

        public List<FileEntry> List()
        {

            try
            {
                S3Wrapper con = CreateRequest();

                List<FileEntry> lst = con.ListBucket(m_bucket, m_prefix);
                for (int i = 0; i < lst.Count; i++)
                    lst[i].Name = lst[i].Name.Substring(m_prefix.Length);
                return lst;
            }
            catch
            {
                return new List<FileEntry>();
            }
        }

        public void Put(string remotename, string localname)
        {
            S3Wrapper con = CreateRequest();
            try
            {
                con.AddFileObject(m_bucket, GetFullKey(remotename), localname);
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Status == System.Net.WebExceptionStatus.ProtocolError)
                {
                    if (wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        //Perhaps the bucket needs to be created?
                        try
                        {
                            con.AddBucket(m_bucket);
                            con.AddFileObject(m_bucket, m_prefix + remotename, localname);
                            return;
                        }
                        catch
                        {
                        }

                        throw;
                    }
                    else
                        throw;
                }
            }
        }

        public void Get(string remotename, string localname)
        {
            S3Wrapper con = CreateRequest();
            con.GetFileObject(m_bucket, GetFullKey(remotename), localname);
        }

        public void Delete(string remotename)
        {
            S3Wrapper con = CreateRequest();
            con.DeleteObject(m_bucket, GetFullKey(remotename));
        }

        #endregion

        private S3Wrapper CreateRequest()
        {
            return new S3Wrapper(m_awsID, m_awsKey, m_format, m_euBuckets);
        }

        private string GetFullKey(string name)
        {
            //Url encode special chars, but keep slashes
            return System.Web.HttpUtility.UrlEncode(m_prefix + name).Replace("%2f", "/");
        }

    }
}
