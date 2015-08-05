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
using System;using Duplicati.Library.Interface;using System.Collections.Generic;using System.Linq;

namespace Duplicati.Library.Backend.OpenStack
{
    public class SwiftConfig : IWebModule
    {        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.Providers;        private static readonly string DEFAULT_CONFIG_TYPE_STR = Enum.GetName(typeof(ConfigType), DEFAULT_CONFIG_TYPE);        private const string KEY_CONFIGTYPE = "openstack-config";        public enum ConfigType        {            Providers,        }
        public SwiftConfig()
        {
        }        public System.Collections.Generic.IDictionary<string, string> Execute(System.Collections.Generic.IDictionary<string, string> options)        {            string k;            options.TryGetValue(KEY_CONFIGTYPE, out k);            if (string.IsNullOrWhiteSpace(k))                k = DEFAULT_CONFIG_TYPE_STR;            ConfigType ct;            if (!Enum.TryParse<ConfigType>(k, true, out ct))                ct = DEFAULT_CONFIG_TYPE;            switch (ct)            {                default:                    return OpenStack.OpenStackStorage.KNOWN_OPENSTACK_PROVIDERS.ToDictionary((x) => x.Key, (y) => y.Value);            }        }        public string Key { get { return "openstack-getconfig"; } }        public string DisplayName { get { return "OpenStack configuration module"; } }        public string Description { get { return "Exposes OpenStack configuration as a web module"; } }        public System.Collections.Generic.IList<ICommandLineArgument> SupportedCommands        {            get            {                return new List<ICommandLineArgument>(new ICommandLineArgument[] {                    new CommandLineArgument(KEY_CONFIGTYPE, CommandLineArgument.ArgumentType.Enumeration, "The config to get", "Provides different config values", DEFAULT_CONFIG_TYPE_STR, Enum.GetNames(typeof(ConfigType)))                });            }        }
    }
}

