using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

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
            protected override string[] Subfolders
            {
                get { return new string[] { "parity" }; }
            }

            /// <summary>
            /// Instanciates a specific parity module, given the file extension and options
            /// </summary>
            /// <param name="fileExtension">The file extension to create the instance for</param>
            /// <param name="redundancy_level">The redundancy level of parity file in percentage</param>
            /// <param name="options">The options to pass to the instance constructor</param>
            /// <returns>The instanciated encryption module or null if the file extension is not supported</returns>
            public IParity GetModule(string fileExtension, int redundancy_level, Dictionary<string, string> options)
            {
                if (string.IsNullOrEmpty(fileExtension))
                    throw new ArgumentNullException(nameof(fileExtension));

                LoadInterfaces();

                lock (m_lock)
                {
                    if (m_interfaces.ContainsKey(fileExtension))
                        return (IParity)Activator.CreateInstance(m_interfaces[fileExtension].GetType(), redundancy_level, options);
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
                    IParity b;
                    if (m_interfaces.TryGetValue(key, out b) && b != null)
                        return b.SupportedCommands;
                    else
                        return null;
                }
            }
        }

        private static readonly ParityLoaderSub _parityLoader = new ParityLoaderSub();

        #region Public static API

        /// <summary>
        /// Gets a list of loaded encryption modules, the instances can be used to extract interface information, not used to interact with the module.
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
        public static IList<ICommandLineArgument> GetSupportedCommands(string key)
        {
            return _parityLoader.GetSupportedCommands(key);
        }

        /// <summary>
        /// Instanciates a specific parity module, given the file extension and options
        /// </summary>
        /// <param name="fileextension">The file extension to create the instance for</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        /// <returns>The instanciated parity module or null if the file extension is not supported</returns>
        public static IParity GetModule(string fileextension, int redundancy_level, Dictionary<string, string> options)
        {
            return _parityLoader.GetModule(fileextension, redundancy_level, options);
        }
        #endregion
    }
}
