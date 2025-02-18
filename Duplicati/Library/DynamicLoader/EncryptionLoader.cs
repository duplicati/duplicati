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
using System.Linq;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// Loads all encryption modules dynamically and exposes a list of those
    /// </summary>
    public class EncryptionLoader
    {
        /// <summary>
        /// Implementation overrides specific to encryption
        /// </summary>
        private class EncryptionLoaderSub : DynamicLoader<IEncryption>
        {
            /// <summary>
            /// Returns the filename extension, which is also the key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The file extension used by the module</returns>
            protected override string GetInterfaceKey(IEncryption item)
            {
                return item.FilenameExtension;
            }

            /// <summary>
            /// Returns the subfolders searched for encryption modules
            /// </summary>
            protected override string[] Subfolders => ["encryption"];

            /// <summary>
            /// The built-in modules
            /// </summary>
            protected override IEnumerable<IEncryption> BuiltInModules => EncryptionModules.BuiltInEncryptionModules;

            /// <summary>
            /// Instanciates a specific encryption module, given the file extension and options
            /// </summary>
            /// <param name="fileExtension">The file extension to create the instance for</param>
            /// <param name="passphrase">The passphrase used to encrypt contents</param>
            /// <param name="options">The options to pass to the instance constructor</param>
            /// <returns>The instanciated encryption module or null if the file extension is not supported</returns>
            public IEncryption GetModule(string fileExtension, string passphrase, Dictionary<string, string> options)
            {
                if (string.IsNullOrEmpty(fileExtension))
                    throw new ArgumentNullException(nameof(fileExtension));

                LoadInterfaces();

                lock (m_lock)
                {
                    if (m_interfaces.ContainsKey(fileExtension))
                        return (IEncryption)Activator.CreateInstance(m_interfaces[fileExtension].GetType(), passphrase, options);
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
                    IEncryption b;
                    if (m_interfaces.TryGetValue(key, out b) && b != null)
                        return GetSupportedCommandsCached(b).ToList();
                    else
                        return null;
                }
            }
        }

        /// <summary>
        /// The static instance used to access encryption module information
        /// </summary>
        private static readonly EncryptionLoaderSub _encryptionLoader = new EncryptionLoaderSub();

        #region Public static API

        /// <summary>
        /// Gets a list of loaded encryption modules, the instances can be used to extract interface information, not used to interact with the module.
        /// </summary>
        public static IEncryption[] Modules { get { return _encryptionLoader.Interfaces; } }

        /// <summary>
        /// Gets a list of keys supported
        /// </summary>
        public static string[] Keys { get { return _encryptionLoader.Keys; } }

        /// <summary>
        /// Gets the supported commands for a given encryption module
        /// </summary>
        /// <param name="key">The encryption module to find the commands for</param>
        /// <returns>The supported commands or null if the key is not supported</returns>
        public static IReadOnlyList<ICommandLineArgument> GetSupportedCommands(string key)
        {
            return _encryptionLoader.GetSupportedCommands(key);
        }


        /// <summary>
        /// Instanciates a specific encryption module, given the file extension and options
        /// </summary>
        /// <param name="fileextension">The file extension to create the instance for</param>
        /// <param name="passphrase">The passphrase used to encrypt contents</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        /// <returns>The instanciated encryption module or null if the file extension is not supported</returns>
        public static IEncryption GetModule(string fileextension, string passphrase, Dictionary<string, string> options)
        {
            return _encryptionLoader.GetModule(fileextension, passphrase, options);
        }
        #endregion

    }
}
