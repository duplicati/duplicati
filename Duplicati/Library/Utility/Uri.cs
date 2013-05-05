//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Specialized;
using System.Web;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Represents a relaxed parsing of a URL
    /// </summary>
    public struct Uri
    {
        /// <summary>
        /// A very lax version of a URL parser
        /// </summary>
        private static System.Text.RegularExpressions.Regex URL_PARSER = new System.Text.RegularExpressions.Regex(@"(?<scheme>[^:]+)://((?<username>[^\:]+)(\:(?<password>.*))?\@)?(?<hostname>[^/\?\:]+)(\:(?<port>\d+))?(/(?<path>[^\?]*))?(\?(?<query>.+))?");

        /// <summary>
        /// The URL scheme, e.g. http
        /// </summary>
        public readonly string Scheme;
        /// <summary>
        /// The server name, e.g. www.example.com
        /// </summary>
        public readonly string Server;
        /// <summary>
        /// The server path, e.g. index.html
        /// </summary>
        public readonly string Path;
        /// <summary>
        /// The server port, e.g. 80, is -1 if using the default port
        /// </summary>
        public readonly int Port;
        /// <summary>
        /// The querystring, e.g. ?id=1
        /// </summary>
        public readonly string Query;
        /// <summary>
        /// The username, if any
        /// </summary>
        public readonly string Username;
        /// <summary>
        /// The password, if any
        /// </summary>
        public readonly string Password;
        
        /// <summary>
        /// The original URI.
        /// </summary>
        public readonly string OriginalUri;
        
        /// <summary>
        /// Cache for the query parameters.
        /// </summary>
        private NameValueCollection m_queryParams;
        
        /// <summary>
        /// Gets the paramters in the query string
        /// </summary>
        /// <value>The query parameters.</value>
        public NameValueCollection QueryParameters
        {
            get
            {
                if (m_queryParams == null)
                {
                    if (Query == null)
                        m_queryParams = new NameValueCollection();
                    else
                        m_queryParams = HttpUtility.ParseQueryString(Query);
                }
                
                return m_queryParams;
            }
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Utility.Utility+Uri"/> struct.
        /// </summary>
        /// <param name="url">The URL to parse</param>
        public Uri(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException("url");
            
            m_queryParams = null;
            this.OriginalUri = url;

            var m = URL_PARSER.Match(url);
            if (!m.Success || m.Length != url.Length)
            {
                if (url.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0)
                    try 
                    {
                        var fp = System.IO.Path.GetFullPath(url);
                        this.Scheme = "file";
                        this.Path = fp;
                        this.Port = -1;
                        this.Query = null;
                        this.Username = null;
                        this.Password = null;
                        return;
                    }
                    catch
                    {
                    }
                throw new ArgumentException(string.Format(Strings.Uri.UriParseError, url), url);
            }
                
            this.Scheme = m.Groups["scheme"].Value;
            this.Server = m.Groups["hostname"].Value;
            this.Path = m.Groups["path"].Success ? m.Groups["path"].Value : null;
            this.Query = m.Groups["query"].Success ? m.Groups["query"].Value : null;
            this.Username = m.Groups["username"].Success ? m.Groups["username"].Value : null;
            this.Password = m.Groups["password"].Success ? m.Groups["password"].Value : null;
            if (m.Groups["port"].Success)
                this.Port = int.Parse(m.Groups["port"].Value);
            else
                this.Port = -1;
        }
        
        /// <summary>
        /// Returns a <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Utility.Uri"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Utility.Uri"/>.</returns>
        public override string ToString ()
        {
            var s = Scheme + "://";
            if (Username != null)
            {
                s += Username;
                if (Password != null)
                    s += ":" + Password;
                s += "@";
            }
            s += Server;
            if (Port != -1)
                s += ":" + Port.ToString();
                
            if (Path != null)
                s += "/" + Path;
            if (Query != null)
                s += "?" + Path;

            return s;            
        }
    }
}

