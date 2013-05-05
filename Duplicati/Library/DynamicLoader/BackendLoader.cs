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
using System.Collections.Specialized;
using System.Web;
using System.Linq;

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// Loads all backends dynamically and exposes a list of those
    /// </summary>
    public class BackendLoader
    {
        /// <summary>
        /// Implementation overrides specific to backends
        /// </summary>
        private class BackendLoaderSub : DynamicLoader<IBackend>
        {
            /// <summary>
            /// Returns the protocol key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The protocol key</returns>
            protected override string GetInterfaceKey(IBackend item)
            {
                return item.ProtocolKey;
            }

            /// <summary>
            /// Returns the subfolders searched for backends
            /// </summary>
            protected override string[] Subfolders
            {
                get { return new string[] {"backends"}; }
            }

            /// <summary>
            /// Parses the URL into components
            /// </summary>
            /// <param name="url">The url to parse, the parser will remove the querystring</param>
            /// <param name="scheme">The url scheme</param>
            /// <param name="extraOptions">Extra options from the query string</param>
            public static void ParseUrl(ref string url, out string scheme, out NameValueCollection extraOptions)
            {
                extraOptions = new NameValueCollection();
                
                //If possible, we avoid parsing the string as a URL to allow flexible string handling
                if (false && url.IndexOf("://") > 0)
                {
                    scheme = url.Substring(0, url.IndexOf("://"));
                    var ix = url.IndexOf('?');
                    if (ix > 0)
                    {
                        extraOptions = HttpUtility.ParseQueryString(url.Substring(ix));
                        url = url.Substring(0, ix);
                    }
                }
                else
                {
                    var uri = new Uri(url);
                    scheme = uri.Scheme.ToLower();
                    if (!string.IsNullOrEmpty(uri.Query))
                    {
                        extraOptions = HttpUtility.ParseQueryString(uri.Query);
                        url = url.Substring(0, url.Length - uri.Query.Length);
                    }
                }
            }

            /// <summary>
            /// Instanciates a specific backend, given the url and options
            /// </summary>
            /// <param name="url">The url to create the instance for</param>
            /// <param name="options">The options to pass to the instance constructor</param>
            /// <returns>The instanciated backend or null if the url is not supported</returns>
            public IBackend GetBackend(string url, Dictionary<string, string> options)
            {
                if (string.IsNullOrEmpty(url))
                    throw new ArgumentNullException("url");

                string scheme;
                NameValueCollection extraOptions;
                ParseUrl(ref url, out scheme, out extraOptions);
                
                LoadInterfaces();
                
                var newOpts = new Dictionary<string, string>(options);
                foreach(var key in extraOptions.AllKeys)
                    newOpts[key] = extraOptions[key];

                lock (m_lock)
                {
                    if (m_interfaces.ContainsKey(scheme))
                        return (IBackend)Activator.CreateInstance(m_interfaces[scheme].GetType(), url, newOpts);
                    else if (scheme.EndsWith("s"))
                    {
                        var tmpscheme = scheme.Substring(0, scheme.Length - 1);
                        if (m_interfaces.ContainsKey(tmpscheme))
                        {
                            var commands = m_interfaces[tmpscheme].SupportedCommands;
                            if (commands != null && (commands.Where(x =>
                                x.Name.Equals("use-ssl", StringComparison.InvariantCultureIgnoreCase) ||
                                (x.Aliases != null && x.Aliases.Where(y => y.Equals("use-ssl", StringComparison.InvariantCultureIgnoreCase)).Any())
                                ).Any()))
                            {
                                newOpts["use-ssl"] = "true";
                                return (IBackend)Activator.CreateInstance(m_interfaces[tmpscheme].GetType(), url, newOpts);
                            }
                        }
                    }
                    
                    return null;
                }
            }

            /// <summary>
            /// Gets the supported commands for a certain url
            /// </summary>
            /// <param name="url">The url to find commands for</param>
            /// <returns>The supported commands or null if the url scheme was not supported</returns>
            public IList<ICommandLineArgument> GetSupportedCommands(string url)
            {
                if (string.IsNullOrEmpty(url))
                    throw new ArgumentNullException("url");

                string scheme;
                NameValueCollection extraOptions;
                ParseUrl(ref url, out scheme, out extraOptions);

                LoadInterfaces();

                lock (m_lock)
                {
                    IBackend b;
                    if (m_interfaces.TryGetValue(scheme, out b) && b != null)
                        return b.SupportedCommands;
                    else if (scheme.EndsWith("s"))
                    {
                        var tmpscheme = scheme.Substring(0, scheme.Length - 1);
                        if (m_interfaces.ContainsKey(tmpscheme))
                            return m_interfaces[tmpscheme].SupportedCommands;
                    }
                    
                    return null;
                }
            }
            
            /// <summary>
            /// Gets the extra url commands encoded in the query string
            /// </summary>
            /// <param name="url">The url to extract commands from</param>
            /// <returns>The extra commands</returns>
            public NameValueCollection GetExtraCommands(string url)
            {
                if (string.IsNullOrEmpty(url))
                    throw new ArgumentNullException("url");

                string scheme;
                NameValueCollection extraOptions;
                ParseUrl(ref url, out scheme, out extraOptions);
                return extraOptions;

            }        
        }

        /// <summary>
        /// The static instance used to access backend information
        /// </summary>
        private static BackendLoaderSub _backendLoader = new BackendLoaderSub();

        #region Public static API
        
        /// <summary>
        /// Gets a list of loaded backends, the instances can be used to extract interface information, not used to interact with the backend.
        /// </summary>
        public static IBackend[] Backends { get { return _backendLoader.Interfaces; } }
        
        /// <summary>
        /// Gets a list of keys supported
        /// </summary>
        public static string[] Keys { get { return _backendLoader.Keys; } }
        
        /// <summary>
        /// Gets the supported commands for a given backend
        /// </summary>
        /// <param name="url">The url to find the commands for</param>
        /// <returns>The supported commands or null if the url is not supported</returns>
        public static IList<ICommandLineArgument> GetSupportedCommands(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException("url");

            return _backendLoader.GetSupportedCommands(url);
        }

        /// <summary>
        /// Gets the extra url-encoded commands for a given backend
        /// </summary>
        /// <param name="url">The backend to find the commands for</param>
        /// <returns>The extra supported commands</returns>
        public static IDictionary<string, string> GetExtraCommands(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException("url");

            var tmp = _backendLoader.GetExtraCommands(url);
            var dict = new Dictionary<string, string>();
            foreach(var k in tmp.AllKeys)
                dict[k] = tmp[k];
                
            return dict;
        }


        /// <summary>
        /// Instanciates a specific backend, given the url and options
        /// </summary>
        /// <param name="url">The url to create the instance for</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        /// <returns>The instanciated backend or null if the url is not supported</returns>
        public static IBackend GetBackend(string url, Dictionary<string, string> options)
        {
            return _backendLoader.GetBackend(url, options);
        }
        #endregion
    }
}
