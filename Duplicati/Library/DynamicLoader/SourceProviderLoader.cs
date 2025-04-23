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
using Duplicati.Library.SourceProviders;
using Duplicati.Library.SourceProvider;
using System.Threading.Tasks;
using System.Threading;

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// Loads all SourceProviders dynamically and exposes a list of those
    /// </summary>
    public class SourceProviderLoader
    {
        /// <summary>
        /// Implementation overrides specific to SourceProviders
        /// </summary>
        private class SourceProviderLoaderSub : DynamicLoader<ISourceProviderModule>
        {
            /// <summary>
            /// Returns the protocol key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The protocol key</returns>
            protected override string GetInterfaceKey(ISourceProviderModule item)
            {
                return item.Key;
            }

            /// <summary>
            /// Returns the subfolders searched for SourceProviders
            /// </summary>
            protected override string[] Subfolders => ["SourceProviders"];

            /// <summary>
            /// The built-in modules
            /// </summary>
            protected override IEnumerable<ISourceProviderModule> BuiltInModules => SourceProviderModules.BuiltInSourceProviderModules;

            /// <summary>
            /// Instanciates a specific SourceProvider, given the url and options
            /// </summary>
            /// <param name="url">The url to create the instance for</param>
            /// <param name="options">The options to pass to the instance constructor</param>
            /// <returns>The instanciated SourceProvider or null if the url is not supported</returns>
            public ISourceProviderModule GetSourceProvider(string url, string mountPoint, Dictionary<string, string> options)
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
                            return (ISourceProviderModule)Activator.CreateInstance(m_interfaces[uri.Scheme].GetType(), url, mountPoint, newOpts);
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

                // TODO: The loading logic is replicated in the "GetSourceProvider" method, should be refactored
                lock (m_lock)
                {
                    ISourceProviderModule b;
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
        /// The static instance used to access SourceProvider information
        /// </summary>
        private static readonly SourceProviderLoaderSub _SourceProviderLoader = new SourceProviderLoaderSub();

        #region Public static API

        /// <summary>
        /// Gets a list of loaded SourceProviders, the instances can be used to extract interface information, not used to interact with the SourceProvider.
        /// </summary>
        public static ISourceProviderModule[] SourceProviders { get { return _SourceProviderLoader.Interfaces; } }

        /// <summary>
        /// Gets a list of keys supported
        /// </summary>
        public static string[] Keys { get { return _SourceProviderLoader.Keys; } }

        /// <summary>
        /// Gets the supported commands for a given SourceProvider
        /// </summary>
        /// <param name="url">The url to find the commands for</param>
        /// <returns>The supported commands or null if the url is not supported</returns>
        public static IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url));

            var commands = _SourceProviderLoader.GetSupportedCommands(url);
            if (commands != null)
                return commands;

            var backend = BackendLoader.GetBackend(url, []);
            if (backend is IFolderEnabledBackend folderBackend)
                commands = folderBackend.SupportedCommands.AsReadOnly();
            backend?.Dispose();

            return commands;
        }

        /// <summary>
        /// Instanciates a specific SourceProvider, given the url and options
        /// </summary>
        /// <param name="url">The url to create the instance for</param>
        /// <param name="mountPoint">The mount point to use</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The instanciated SourceProvider or null if the url is not supported</returns>
        public static async Task<ISourceProviderModule> GetSourceProvider(string url, string mountPoint, Dictionary<string, string> options, CancellationToken cancellationToken)
        {
            // Source providers are preferred over backends
            var provider = _SourceProviderLoader.GetSourceProvider(url, mountPoint, options);
            if (provider == null)
            {
                // See if there is a backend that can also be a source
                var backend = BackendLoader.GetBackend(url, options);
                if (backend is IFolderEnabledBackend folderBackend)
                    provider = new BackendSourceProvider(folderBackend, mountPoint);
                else
                    backend?.Dispose();
            }

            if (provider == null)
                return null;

            try
            {
                await provider.Initialize(cancellationToken).ConfigureAwait(false);
                return provider;
            }
            catch
            {
                provider.Dispose();
                throw;
            }

        }
        #endregion

        /// <summary>
        /// Adds a SourceProvider to the loader
        /// </summary>
        /// <param name="SourceProvider">The SourceProvider to add</param>
        public static void AddSourceProvider(ISourceProviderModule SourceProvider)
        {
            _SourceProviderLoader.AddModule(SourceProvider);
        }
    }
}
