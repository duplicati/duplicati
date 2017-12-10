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
    /// Loads all compression modules dynamically and exposes a list of those
    /// </summary>
    public class CompressionLoader
    {
        /// <summary>
        /// Implementation overrides specific to compression
        /// </summary>
        private class CompressionLoaderSub : DynamicLoader<ICompression>
        {
            /// <summary>
            /// Returns the filename extension, which is also the key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The file extension used by the module</returns>
            protected override string GetInterfaceKey(ICompression item)
            {
                return item.FilenameExtension;
            }

            /// <summary>
            /// Returns the subfolders searched for compression modules
            /// </summary>
            protected override string[] Subfolders
            {
                get { return new string[] { "compression" }; }
            }

            /// <summary>
            /// Instanciates a specific compression module, given the file extension and options
            /// </summary>
            /// <param name="fileExtension">The file extension to create the instance for</param>
            /// <param name="filename">The filename used to compress/decompress contents</param>
            /// <param name="options">The options to pass to the instance constructor</param>
            /// <returns>The instanciated encryption module or null if the file extension is not supported</returns>
            public ICompression GetModule(string fileExtension, string filename, Dictionary<string, string> options)
            {
                if (string.IsNullOrEmpty(fileExtension))
                    throw new ArgumentNullException("fileExtension");

                LoadInterfaces();
                
                lock (m_lock)
                {
                    if (m_interfaces.ContainsKey(fileExtension))
                        return (ICompression)Activator.CreateInstance(m_interfaces[fileExtension].GetType(), filename, options);
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
                    throw new ArgumentNullException("key");

                LoadInterfaces();

                lock (m_lock)
                {
                    ICompression b;
                    if (m_interfaces.TryGetValue(key, out b) && b != null)
                        return b.SupportedCommands;
                    else
                        return null;
                }
            }
        }

        /// <summary>
        /// The static instance used to access compression module information
        /// </summary>
        private static CompressionLoaderSub _compressionLoader = new CompressionLoaderSub();

        #region Public static API

        /// <summary>
        /// Gets a list of loaded compression modules, the instances can be used to extract interface information, not used to interact with the module.
        /// </summary>
        public static ICompression[] Modules { get { return _compressionLoader.Interfaces; } }

        /// <summary>
        /// Gets a list of keys supported
        /// </summary>
        public static string[] Keys { get { return _compressionLoader.Keys; } }

        /// <summary>
        /// Gets the supported commands for a given compression module
        /// </summary>
        /// <param name="key">The compression module to find the commands for</param>
        /// <returns>The supported commands or null if the key is not supported</returns>
        public static IList<ICommandLineArgument> GetSupportedCommands(string key)
        {
            return _compressionLoader.GetSupportedCommands(key);
        }


        /// <summary>
        /// Instanciates a specific compression module, given the file extension and options
        /// </summary>
        /// <param name="fileextension">The file extension to create the instance for</param>
        /// <param name="filename">The filename of the file used to compress/decompress contents</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        /// <returns>The instanciated compression module or null if the file extension is not supported</returns>
        public static ICompression GetModule(string fileextension, string filename, Dictionary<string, string> options)
        {
            return _compressionLoader.GetModule(fileextension, filename, options);
        }
        #endregion

    }
}
