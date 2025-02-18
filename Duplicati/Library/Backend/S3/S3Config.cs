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
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;

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
                    return S3.DEFAULT_S3_LOCATION_BASED_HOSTS;
                case ConfigType.Regions:
                    return S3.KNOWN_S3_LOCATIONS;
                case ConfigType.StorageClasses:
                    return S3.KNOWN_S3_STORAGE_CLASSES;
                default:
                    return S3.KNOWN_S3_PROVIDERS;
            }
        }

        public string Key { get { return "s3-getconfig"; } }

        public string DisplayName { get { return LC.L("S3 configuration module"); } }

        public string Description { get { return LC.L("Expose S3 configuration as a web module"); } }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>([
                    new CommandLineArgument(KEY_CONFIGTYPE, CommandLineArgument.ArgumentType.Enumeration, LC.L("The config to get"), LC.L("Provide different config values"), DEFAULT_CONFIG_TYPE_STR, Enum.GetNames(typeof(ConfigType)))

                ]);
            }
        }

        #endregion
    }
}

