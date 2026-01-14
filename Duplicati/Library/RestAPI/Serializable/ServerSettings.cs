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

using System.Collections.Generic;
using System.Linq;
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
            public DynamicModule(Library.Interface.IBackend backend)
            {
                this.Key = backend.ProtocolKey;
                this.Description = backend.Description;
                this.DisplayName = backend.DisplayName;
                this.Options = backend.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for compression module interface
            /// </summary>
            public DynamicModule(Library.Interface.ICompression module)
            {
                this.Key = module.FilenameExtension;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for encryption module interface
            /// </summary>
            public DynamicModule(Library.Interface.IEncryption module)
            {
                this.Key = module.FilenameExtension;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for generic module interface
            /// </summary>
            public DynamicModule(Library.Interface.IGenericModule module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for webmodule interface
            /// </summary>
            public DynamicModule(Library.Interface.IWebModule module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
                this.Lookups = module.GetLookups();
            }

            /// <summary>
            /// Constructor for sercretprovider interface
            /// </summary>
            public DynamicModule(Library.Interface.ISecretProvider module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for sourceprovider interface
            /// </summary>
            public DynamicModule(Library.Interface.ISourceProviderModule module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                this.Options = module.SupportedCommands?.ToArray() ?? [];
            }

            /// <summary>
            /// Constructor for restoredestinationprovider interface
            /// </summary>
            public DynamicModule(Library.Interface.IRestoreDestinationProviderModule module)
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
            public Library.Interface.ICommandLineArgument[] Options { get; private set; }
            /// <summary>
            /// The lookups supported by the module
            /// </summary>
            public IDictionary<string, IDictionary<string, string>> Lookups { get; private set; }
        }

        /// <summary>
        /// Gets all supported options
        /// </summary>
        public static Library.Interface.ICommandLineArgument[] Options
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
        /// The source provider modules known by the server
        /// </summary>
        public static IDynamicModule[] SourceProviderModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.SourceProviderLoader.SourceProviders
                     select new DynamicModule(n))
                     .Concat(
                        from n in Library.DynamicLoader.BackendLoader.Backends
                        where n is Library.Interface.IFolderEnabledBackend
                        select new DynamicModule(n))
                    .ToArray();
            }
        }

        /// <summary>
        /// The restore destination provider modules known by the server
        /// </summary>
        public static IDynamicModule[] RestoreDestinationProviderModules
        {
            get
            {
                return
                    (from n in Library.DynamicLoader.RestoreDestinationProviderLoader.ResotreDestinationProviders
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
    }
}
