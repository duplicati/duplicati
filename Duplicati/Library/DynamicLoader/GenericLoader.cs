using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// An interface class that provides easy loading of plugable setting controls
    /// </summary>
    public static class GenericLoader
    {
        /// <summary>
        /// Implementation overrides specific to encryption
        /// </summary>
        private class GenericLoaderSub : DynamicLoader<IGenericModule>
        {
            /// <summary>
            /// Returns the filename extension, which is also the key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The file extension used by the module</returns>
            protected override string GetInterfaceKey(IGenericModule item)
            {
                return item.Key;
            }

            /// <summary>
            /// Returns the subfolders searched for encryption modules
            /// </summary>
            protected override string[] Subfolders
            {
                get { return new string[] { "modules" }; }
            }
        }

        #region Public static API

        /// <summary>
        /// Gets a list of loaded settings controls, the instances can be used to extract interface information, not used to interact with the module.
        /// </summary>
        public static IGenericModule[] Modules { get { return new GenericLoaderSub().Interfaces; } }

        #endregion

    }
}
