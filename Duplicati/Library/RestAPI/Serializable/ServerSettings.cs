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
using System.Linq;
using Duplicati.Library.RestAPI;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.Server.Serializable
{
    /// <summary>
    /// The server config
    /// </summary>
    public static class ServerSettings
    {
        /// <summary>
        /// Shared implementation for reporting dynamic modules
        /// </summary>
        private class DynamicModule : IDynamicModule
        {
            /// <summary>
            /// Constructor for backend interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.IBackend backend)
            {
                this.Key = backend.ProtocolKey;
                this.Description = backend.Description;
                this.DisplayName = backend.DisplayName;
                this.Options = backend.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for compression module interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.ICompression module)
            {
                this.Key = module.FilenameExtension;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for encryption module interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.IEncryption module)
            {
                this.Key = module.FilenameExtension;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for generic module interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.IGenericModule module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for webmodule interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.IWebModule module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for sercretprovider interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.ISecretProvider module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }
            /// <summary>
            /// The module key
            /// </summary>
            public string Key { get; private set; }
            /// <summary>
            /// The localized module description
            /// </summary>
            public string Description { get; private set; }
            /// <summary>
            /// Gets the localized display name
            /// </summary>
            /// <value>The display name.</value>
            public string DisplayName { get; private set; }
            /// <summary>
            /// The options supported by the module
            /// </summary>
            public Duplicati.Library.Interface.ICommandLineArgument[] Options { get; private set; }
        }

        /// <summary>
        /// Gets all supported options
        /// </summary>
        public static Duplicati.Library.Interface.ICommandLineArgument[] Options
        {
            get
            {
                return new Duplicati.Library.Main.Options(new System.Collections.Generic.Dictionary<string, string>()).SupportedCommands.ToArray();
            }
        }

        /// <summary>
        /// The backend modules known by the server
        /// </summary>
        public static IDynamicModule[] BackendModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.BackendLoader.Backends
                     select new DynamicModule(n))
                    .ToArray();
            }
        }
        /// <summary>
        /// The encryption modules known by the server
        /// </summary>
        public static IDynamicModule[] EncryptionModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.EncryptionLoader.Modules
                     select new DynamicModule(n))
                    .ToArray();
            }
        }

        /// <summary>
        /// The compression modules known by the server
        /// </summary>
        public static IDynamicModule[] CompressionModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.CompressionLoader.Modules
                     select new DynamicModule(n))
                    .ToArray();
            }
        }

        /// <summary>
        /// The generic modules known by the server
        /// </summary>
        public static IDynamicModule[] GenericModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.GenericLoader.Modules
                     select new DynamicModule(n))
                    .ToArray();
            }
        }

        /// <summary>
        /// The web modules known by the server
        /// </summary>
        public static IDynamicModule[] WebModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.WebLoader.Modules
                     select new DynamicModule(n))
                    .ToArray();
            }
        }

        /// <summary>
        /// The web modules known by the server
        /// </summary>
        public static IDynamicModule[] ConnectionModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.GenericLoader.Modules
                     where n is Library.Interface.IConnectionModule
                     select new DynamicModule(n))
                    .ToArray();
            }
        }

        /// <summary>
        /// The server modules known by the server
        /// </summary>
        public static object[] ServerModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.GenericLoader.Modules
                     where n is Library.Interface.IGenericServerModule
                     select n)
                    .ToArray();
            }
        }

        /// <summary>
        /// The web modules known by the server
        /// </summary>
        public static IDynamicModule[] SecretProviderModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.SecretProviderLoader.Modules
                     select new DynamicModule(n))
                    .ToArray();
            }
        }

        /// <summary>
        /// The filters that are applied to all backups
        /// </summary>
        public static IFilter[] Filters
        {
            get { return FIXMEGlobal.DataConnection.Filters; }
        }

        /// <summary>
        /// The settings applied to all backups by default
        /// </summary>
        public static ISetting[] Settings
        {
            get { return FIXMEGlobal.DataConnection.Settings; }
        }
    }
}
