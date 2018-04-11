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
            /// Instanciates a specific backend, given the url and options
            /// </summary>
            /// <param name="url">The url to create the instance for</param>
            /// <param name="options">The options to pass to the instance constructor</param>
            /// <returns>The instanciated backend or null if the url is not supported</returns>
            public IBackend GetBackend(string url, Dictionary<string, string> options)
            {
                var uri = new Utility.Uri(url);
                
                LoadInterfaces();
                
                var newOpts = new Dictionary<string, string>(options);
                foreach (var key in uri.QueryParameters.AllKeys)
                    newOpts[key] = uri.QueryParameters[key];

                lock (m_lock)
                {
                    try
                    {
                        if (m_interfaces.ContainsKey(uri.Scheme))
                            return (IBackend)Activator.CreateInstance(m_interfaces[uri.Scheme].GetType(), url, newOpts);
                        else if (uri.Scheme.EndsWith("s", StringComparison.Ordinal))
                        {
                            var tmpscheme = uri.Scheme.Substring(0, uri.Scheme.Length - 1);
                            if (m_interfaces.ContainsKey(tmpscheme))
                            {
                                var commands = m_interfaces[tmpscheme].SupportedCommands;
                                if (commands != null && (commands.Where(x =>
                                x.Name.Equals("use-ssl", StringComparison.OrdinalIgnoreCase) ||
                                (x.Aliases != null && x.Aliases.Where(y => y.Equals("use-ssl", StringComparison.OrdinalIgnoreCase)).Any())
                                ).Any()))
                                {
                                    newOpts["use-ssl"] = "true";
                                    return (IBackend)Activator.CreateInstance(m_interfaces[tmpscheme].GetType(), url, newOpts);
                                }
                            }
                        }
                    }
                    catch (System.Reflection.TargetInvocationException tex)
                    {
                        // Unwrap exceptions for nicer display
                        if (tex.InnerException != null)
                            throw new Exception("Unwrapped TargetInvocationException", tex.InnerException);

                        throw;
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
                var uri = new Utility.Uri(url);

                LoadInterfaces();

                lock (m_lock)
                {
                    IBackend b;
                    if (m_interfaces.TryGetValue(uri.Scheme, out b) && b != null)
                        return b.SupportedCommands;
                    else if (uri.Scheme.EndsWith("s", StringComparison.Ordinal))
                    {
                        var tmpscheme = uri.Scheme.Substring(0, uri.Scheme.Length - 1);
                        if (m_interfaces.ContainsKey(tmpscheme))
                            return m_interfaces[tmpscheme].SupportedCommands;
                    }
                    
                    return null;
                }
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
                throw new ArgumentNullException(nameof(url));

            return _backendLoader.GetSupportedCommands(url);
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
