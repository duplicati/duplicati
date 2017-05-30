//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
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
            public DynamicModule(Duplicati.Library.Interface.IBackend backend)
            {
                this.Key = backend.ProtocolKey;
                this.Description = backend.Description;
                this.DisplayName = backend.DisplayName;
                if (backend.SupportedCommands != null)
                    this.Options = backend.SupportedCommands.ToArray();
            }
        
            /// <summary>
            /// Constructor for compression module interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.ICompression module)
            {
                this.Key = module.FilenameExtension;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                if (module.SupportedCommands != null)
                    this.Options = module.SupportedCommands.ToArray();
            }

            /// <summary>
            /// Constructor for encryption module interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.IEncryption module)
            {
                this.Key = module.FilenameExtension;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                if (module.SupportedCommands != null)
                    this.Options = module.SupportedCommands.ToArray();
            }

            /// <summary>
            /// Constructor for generic module interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.IGenericModule module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                if (module.SupportedCommands != null)
                    this.Options = module.SupportedCommands.ToArray();
            }

            /// <summary>
            /// Constructor for webmodule interface
            /// </summary>
            public DynamicModule(Duplicati.Library.Interface.IWebModule module)
            {
                this.Key = module.Key;
                this.Description = module.Description;
                this.DisplayName = module.DisplayName;
                if (module.SupportedCommands != null)
                    this.Options = module.SupportedCommands.ToArray();
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
        /// The filters that are applied to all backups
        /// </summary>
        public static IFilter[] Filters
        {
            get { return Program.DataConnection.Filters; }
        }
        
        /// <summary>
        /// The settings applied to all backups by default
        /// </summary>
        public static ISetting[] Settings
        { 
            get { return Program.DataConnection.Settings; }
        }
    }
}
