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
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class S3Config : IWebModule
    {
        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.Providers;
        private static readonly string DEFAULT_CONFIG_TYPE_STR = Enum.GetName(typeof(ConfigType), DEFAULT_CONFIG_TYPE);
        private const string KEY_CONFIGTYPE = "s3-config";

        public enum ConfigType
        {
            Providers,
            Regions,
            RegionHosts,
            StorageClasses
        }

        public S3Config()
        {
        }

        #region IWebModule implementation

        public IDictionary<string, string> Execute(IDictionary<string, string> options)
        {
            string k;
            options.TryGetValue(KEY_CONFIGTYPE, out k);
            if (string.IsNullOrWhiteSpace(k))
                k = DEFAULT_CONFIG_TYPE_STR;
            
            ConfigType ct;
            if (!Enum.TryParse<ConfigType>(k, true, out ct))
                ct = DEFAULT_CONFIG_TYPE;
             
            switch (ct)
            {
                case ConfigType.RegionHosts:
                    return S3.DEFAULT_S3_LOCATION_BASED_HOSTS.ToDictionary((x) => x.Key, (y) => y.Value);
                case ConfigType.Regions:
                    return S3.KNOWN_S3_LOCATIONS.ToDictionary((x) => x.Key, (y) => y.Value);
                case ConfigType.StorageClasses:
                    return S3.KNOWN_S3_STORAGE_CLASSES.ToDictionary((x) => x.Key, (y) => y.Value);
                default:
                    return S3.KNOWN_S3_PROVIDERS.ToDictionary((x) => x.Key, (y) => y.Value);
            }
        }

        public string Key { get { return "s3-getconfig"; } }

        public string DisplayName { get { return "S3 configuration module"; } }

        public string Description { get { return "Exposes S3 configuration as a web module"; } }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(KEY_CONFIGTYPE, CommandLineArgument.ArgumentType.Enumeration, "The config to get", "Provides different config values", DEFAULT_CONFIG_TYPE_STR, Enum.GetNames(typeof(ConfigType)))

                });
            }
        }

        #endregion
    }
}

