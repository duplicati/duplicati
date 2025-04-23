// Copyright (C) 2025, The Duplicati Team
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

using System;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using System.Linq;
using System.Runtime.ExceptionServices;
using Duplicati.Library.Backends;

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
            protected override string[] Subfolders => ["backends"];

            /// <summary>
            /// The built-in modules
            /// </summary>
            protected override IEnumerable<IBackend> BuiltInModules => BackendModules.BuiltInBackendModules;

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
                                if (commands != null && (commands.Any(x =>
                                x.Name.Equals("use-ssl", StringComparison.OrdinalIgnoreCase) ||
                                (x.Aliases != null && x.Aliases.Any(y => y.Equals("use-ssl", StringComparison.OrdinalIgnoreCase)))
                                )))
                                {
                                    newOpts["use-ssl"] = "true";
                                    return (IBackend)Activator.CreateInstance(m_interfaces[tmpscheme].GetType(), url, newOpts);
                                }
                            }
                        }
                    }
                    catch (System.Reflection.TargetInvocationException tex)
                    {
                        if (tex.InnerException != null)
                        {
                            // Unwrap exceptions for nicer display. The ExceptionDispatchInfo class allows us to
                            // rethrow an exception without changing the stack trace.
                            ExceptionDispatchInfo.Capture(tex.InnerException).Throw();
                        }

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
            public IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string url)
            {
                var uri = new Utility.Uri(url);

                LoadInterfaces();

                // TODO: The loading logic is replicated in the "GetBackend" method, should be refactored
                lock (m_lock)
                {
                    IBackend b;
                    if (m_interfaces.TryGetValue(uri.Scheme, out b) && b != null)
                        return GetSupportedCommandsCached(b).ToList();
                    else if (uri.Scheme.EndsWith("s", StringComparison.Ordinal))
                    {
                        var tmpscheme = uri.Scheme.Substring(0, uri.Scheme.Length - 1);
                        if (m_interfaces.TryGetValue(tmpscheme, out b) && b != null)
                            return GetSupportedCommandsCached(b).ToList();
                    }

                    return null;
                }
            }
        }

        /// <summary>
        /// The static instance used to access backend information
        /// </summary>
        private static readonly BackendLoaderSub _backendLoader = new BackendLoaderSub();

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
        public static IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string url)
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

        /// <summary>
        /// Adds a backend to the loader
        /// </summary>
        /// <param name="backend">The backend to add</param>
        public static void AddBackend(IBackend backend)
        {
            _backendLoader.AddModule(backend);
        }
    }
}
