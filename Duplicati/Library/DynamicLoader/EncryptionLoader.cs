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
            protected override string[] Subfolders
            {
                get { return new string[] { "encryption" }; }
            }

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
            public IList<ICommandLineArgument> GetSupportedCommands(string key)
            {
                if (string.IsNullOrEmpty(key))
                    throw new ArgumentNullException(nameof(key));

                LoadInterfaces();

                lock (m_lock)
                {
                    IEncryption b;
                    if (m_interfaces.TryGetValue(key, out b) && b != null)
                        return b.SupportedCommands;
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
        public static IList<ICommandLineArgument> GetSupportedCommands(string key)
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
