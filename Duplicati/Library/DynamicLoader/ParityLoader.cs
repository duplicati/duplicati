// Copyright (C) 2026, The Duplicati Team
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
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Parity;

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// Loads all parity modules dynamically and exposes a list of those
    /// </summary>
    public class ParityLoader
    {
        /// <summary>
        /// Implementation overrides specific to parity
        /// </summary>
        private class ParityLoaderSub : DynamicLoader<IParity>
        {
            /// <summary>
            /// Returns the filename extension, which is also the key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The file extension used by the module</returns>
            protected override string GetInterfaceKey(IParity item)
            {
                return item.FilenameExtension;
            }

            /// <summary>
            /// Returns the subfolders searched for parity modules
            /// </summary>
            protected override string[] Subfolders => ["parity"];

            /// <summary>
            /// The built-in modules
            /// </summary>
            protected override IEnumerable<IParity> BuiltInModules => ParityModules.BuiltInParityModules;

            /// <summary>
            /// Instanciates a specific parity module, given the file extension and options
            /// </summary>
            /// <param name="fileExtension">The file extension to create the instance for</param>
            /// <param name="options">The options to pass to the instance constructor</param>
            /// <returns>The instanciated parity module or null if the file extension is not supported</returns>
            public IParity GetModule(string fileExtension, Dictionary<string, string> options)
            {
                if (string.IsNullOrEmpty(fileExtension))
                    throw new ArgumentNullException(nameof(fileExtension));

                LoadInterfaces();

                lock (m_lock)
                {
                    if (m_interfaces.ContainsKey(fileExtension))
                        return (IParity)Activator.CreateInstance(m_interfaces[fileExtension].GetType(), options);
                    else
                        return null;
                }
            }

            /// <summary>
            /// Gets the supported commands for a certain key
            /// </summary>
            /// <param name="key">The key to find commands for</param>
            /// <returns>The supported commands or null if the key was not found</returns>
            public IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string key)
            {
                if (string.IsNullOrEmpty(key))
                    throw new ArgumentNullException(nameof(key));

                LoadInterfaces();

                lock (m_lock)
                {
                    IParity b;
                    if (m_interfaces.TryGetValue(key, out b) && b != null)
                        return GetSupportedCommandsCached(b).ToList();
                    else
                        return null;
                }
            }
        }

        /// <summary>
        /// The static instance used to access parity module information
        /// </summary>
        private static readonly ParityLoaderSub _parityLoader = new ParityLoaderSub();

        #region Public static API

        /// <summary>
        /// Gets a list of loaded parity modules, the instances can be used to extract interface information, not used to interact with the module.
        /// </summary>
        public static IParity[] Modules { get { return _parityLoader.Interfaces; } }

        /// <summary>
        /// Gets a list of keys supported
        /// </summary>
        public static string[] Keys { get { return _parityLoader.Keys; } }

        /// <summary>
        /// Gets the supported commands for a given parity module
        /// </summary>
        /// <param name="key">The parity module to find the commands for</param>
        /// <returns>The supported commands or null if the key is not supported</returns>
        public static IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string key)
        {
            return _parityLoader.GetSupportedCommands(key);
        }

        /// <summary>
        /// Instanciates a specific parity module, given the file extension and options
        /// </summary>
        /// <param name="fileextension">The file extension to create the instance for</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        /// <returns>The instanciated parity module or null if the file extension is not supported</returns>
        public static IParity GetModule(string fileextension, Dictionary<string, string> options)
        {
            return _parityLoader.GetModule(fileextension, options);
        }
        #endregion

        /// <summary>
        /// Removes all parity modules except the specified ones
        /// </summary>
        /// <param name="allowedModules">The module keys to keep, comma-separated</param>
        public static void OnlyAllowModules(string allowedModules)
            => _parityLoader.RemoveAllModulesExcept(allowedModules);

    }
}
