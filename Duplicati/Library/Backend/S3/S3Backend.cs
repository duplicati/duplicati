#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
    public class S3 : IStreamingBackend, IBackendGUI
    {
        private string m_awsID;
        private string m_awsKey;
        private string m_url;
        private string m_host;
        private string m_bucket;
        private string m_prefix;
        private Affirma.ThreeSharp.CallingFormat m_format;
        private bool m_euBuckets;

        private const string S3_HOST  = "s3.amazonaws.com";

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
                    else if (options.ContainsKey("ftp-password"))
                        m_awsKey = options["ftp-password"];
                }
            }
            else
            {
                if (options.ContainsKey("ftp-username"))
                    m_awsID = options["ftp-username"];
                if (options.ContainsKey("ftp-password"))
                    m_awsKey = options["ftp-password"];

                if (options.ContainsKey("aws_access_key_id"))
                    m_awsID = options["aws_access_key_id"];
                if (options.ContainsKey("aws_secret_access_key"))
                    m_awsKey = options["aws_secret_access_key"];
            }

            if (string.IsNullOrEmpty(m_awsID))
                throw new Exception(Strings.S3Backend.NoAMZUserIDError);
            if (string.IsNullOrEmpty(m_awsKey))
                throw new Exception(Strings.S3Backend.NoAMZKeyError);


            m_prefix = "";

            m_host = u.Host;
            m_format = options.ContainsKey("s3-use-new-style") ? Affirma.ThreeSharp.CallingFormat.SUBDOMAIN : Affirma.ThreeSharp.CallingFormat.REGULAR;
            m_euBuckets = options.ContainsKey("s3-european-buckets");

            if (m_host.ToLower() == S3_HOST)
            {
                m_bucket = System.Web.HttpUtility.UrlDecode(u.PathAndQuery);

                if (m_bucket.StartsWith("/"))
                    m_bucket = m_bucket.Substring(1);

                if (m_bucket.Contains("/"))
                {
                    m_prefix = m_bucket.Substring(m_bucket.IndexOf("/") + 1);
                    m_bucket = m_bucket.Substring(0, m_bucket.IndexOf("/"));
                }

            }
            else 
            {
                //If it is vanity style, do a CNAME lookup
                if (!m_host.ToLower().EndsWith("." + S3_HOST))
                {
                    System.Net.IPHostEntry ent = System.Net.Dns.GetHostEntry(m_host);
                    foreach (string s in ent.Aliases)
                        if (s.EndsWith("." + S3_HOST, StringComparison.InvariantCultureIgnoreCase))
                        {
                            m_host = s;
                            break;
                        }
                }

                //Subdomain type lookup
                if (m_host.ToLower().EndsWith("." + S3_HOST))
                {
                    m_format = Affirma.ThreeSharp.CallingFormat.SUBDOMAIN;
                    m_bucket = m_host.Substring(0, m_host.Length - ("." + S3_HOST).Length);
                    m_host = S3_HOST;
                    m_prefix = System.Web.HttpUtility.UrlDecode(u.PathAndQuery);

                    if (m_prefix.StartsWith("/"))
                        m_prefix = m_prefix.Substring(1);
                }
                else
                    throw new Exception(string.Format(Strings.S3Backend.UnableToDecodeBucketnameError, m_url));
            }


            m_options = options;
            m_url = url;
            m_prefix = m_prefix.Trim();
            if (m_prefix.Length != 0 && !m_prefix.EndsWith("/"))
                m_prefix += "/";
        }


        private List<FileEntry> List(bool isTesting)
        {
            try
            {
                S3Wrapper con = CreateRequest();

                List<FileEntry> lst = con.ListBucket(m_bucket, m_prefix);
                for (int i = 0; i < lst.Count; i++)
                {
                    lst[i].Name = lst[i].Name.Substring(m_prefix.Length);

                    //Fix for a bug in Duplicati 1.0 beta 3 and earlier, where filenames are incorrectly prefixed with a slash
                    if (lst[i].Name.StartsWith("/") && !m_prefix.StartsWith("/"))
                        lst[i].Name = lst[i].Name.Substring(1);
                }
                return lst;
            }
            catch (Exception ex)
            {
                System.Net.WebException wex = ex as System.Net.WebException;
                Affirma.ThreeSharp.ThreeSharpException tex = ex as Affirma.ThreeSharp.ThreeSharpException;
                if (wex == null && tex != null)
                    wex = tex.InnerException as System.Net.WebException;

                //Catch "non-existing" buckets
                if (wex != null && wex.Status == System.Net.WebExceptionStatus.ProtocolError && wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
                    if (isTesting)
                        throw new Backend.FolderMissingException(wex);
                    else
                        return new List<FileEntry>();

                if (tex != null && tex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    if (isTesting)
                        throw new Backend.FolderMissingException(tex);
                    else
                        return new List<FileEntry>();

                throw;
            }
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.S3Backend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "s3"; }
        }

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public List<FileEntry> List()
        {
            return List(false);
        }

        public void Put(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Put(remotename, fs);
        }

        public void Put(string remotename, System.IO.Stream input)
        {
            S3Wrapper con = CreateRequest();
            try
            {
                con.AddFileStream(m_bucket, GetFullKey(remotename), input);
            }
			catch (Exception ex)
			{
				bool isBucketMissingError = false;
				System.Net.WebException wex = ex as System.Net.WebException;
				Affirma.ThreeSharp.ThreeSharpException tex = ex as Affirma.ThreeSharp.ThreeSharpException;
				if (wex == null && tex != null)
					wex = tex.InnerException as System.Net.WebException;
                if (wex != null && wex.Status == System.Net.WebExceptionStatus.ProtocolError && wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
					isBucketMissingError = true;
				if (!isBucketMissingError && tex != null && tex.StatusCode == System.Net.HttpStatusCode.NotFound)
					isBucketMissingError = true;
				
				if (isBucketMissingError)
                {
                    //Perhaps the bucket needs to be created?
                    try
                    {
                        con.AddBucket(m_bucket);
                        con.AddFileStream(m_bucket, GetFullKey(remotename), input);
                        return;
                    }
                    catch
                    {
                    }

                }

				throw;
            }
        }

        public void Get(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Get(remotename, fs);
        }

        public void Get(string remotename, System.IO.Stream output)
        {
            S3Wrapper con = CreateRequest();
            try
            {
                con.GetFileStream(m_bucket, GetFullKey(remotename), output);
            }
            catch
            {
                //This is a fix for the S3 backend prior to beta 3, where the filenames had a slash suffix
                bool fallbackFix = false;
                try
                {
                    if (!remotename.StartsWith("/"))
                        con.GetFileStream(m_bucket, GetFullKey("/" + remotename), output);
                    fallbackFix = true;
                }
                catch
                {
                }

                if (!fallbackFix)
                    throw;
            }
        }

        public void Delete(string remotename)
        {
            S3Wrapper con = CreateRequest();
            con.DeleteObject(m_bucket, GetFullKey(remotename));
        }

        public void Test()
        {
            List(true);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("aws_secret_access_key", CommandLineArgument.ArgumentType.Path, Strings.S3Backend.AMZKeyDescriptionShort, Strings.S3Backend.AMZKeyDescriptionLong, null, new string[] {"ftp-password"}, null),
                    new CommandLineArgument("aws_access_key_id", CommandLineArgument.ArgumentType.Path, Strings.S3Backend.AMZUserIDDescriptionShort, Strings.S3Backend.AMZUserIDDescriptionLong, null, new string[] {"ftp-username"}, null),
                    new CommandLineArgument("s3-use-new-style", CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.S3NewStyleDescriptionShort, Strings.S3Backend.S3NewStyleDescriptionLong, "true"),
                    new CommandLineArgument("s3-european-buckets", CommandLineArgument.ArgumentType.Boolean, Strings.S3Backend.S3EurobucketDescriptionShort, Strings.S3Backend.S3EurobucketDescriptionLong, "false"),
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, Strings.S3Backend.FTPPasswordDescriptionShort, Strings.S3Backend.FTPPasswordDescriptionLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.S3Backend.DescriptionFTPUsernameShort, Strings.S3Backend.DescriptionFTPUsernameLong)
                });

            }
        }

        public string Description
        {
            get
            {
                return Strings.S3Backend.Description;
            }
        }

        public void CreateFolder()
        {
            S3Wrapper con = CreateRequest();
            //S3 does not complain if the bucket already exists
            con.AddBucket(m_bucket);
        }
        
        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_options != null)
                m_options = null;
            if (m_awsID != null)
                m_awsID = null;
            if (m_awsKey != null)
                m_awsKey = null;
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


        #region IBackendGUI Members

        public string PageTitle
        {
            get { return S3UI.PageTitle; }
        }

        public string PageDescription
        {
            get { return S3UI.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new S3UI(options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((S3UI)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((S3UI)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return S3UI.GetConfiguration(guiOptions, commandlineOptions);
        }

        #endregion
    }
}
