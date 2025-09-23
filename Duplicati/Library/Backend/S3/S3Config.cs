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

using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class S3Config : IWebModule
    {
        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.Providers;
        private static readonly string DEFAULT_CONFIG_TYPE_STR = DEFAULT_CONFIG_TYPE.ToString();
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

        public IDictionary<string, string> Execute(IDictionary<string, string?> options)
        {
            var ct = Utility.Utility.ParseEnumOption(options.AsReadOnly(), KEY_CONFIGTYPE, DEFAULT_CONFIG_TYPE);
            GetLookups().TryGetValue(ct.ToString(), out var dict);
            return dict ?? new Dictionary<string, string>();
        }

        public string Key => "s3-getconfig";

        public string DisplayName => Strings.S3Config.DisplayName;

        public string Description => Strings.S3Config.Description;

        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(KEY_CONFIGTYPE, CommandLineArgument.ArgumentType.Enumeration, Strings.S3Config.ConfigTypeShort, Strings.S3Config.ConfigTypeLong, DEFAULT_CONFIG_TYPE_STR, Enum.GetNames(typeof(ConfigType)))
        ];

        public IDictionary<string, IDictionary<string, string?>> GetLookups()
            => new Dictionary<string, IDictionary<string, string?>>()
            {
                { ConfigType.Providers.ToString(), S3.KNOWN_S3_PROVIDERS.ToDictionary((x) => x.Key, (y) => y.Value) },
                { ConfigType.Regions.ToString(), S3.KNOWN_S3_LOCATIONS.ToDictionary((x) => x.Key, (y) => y.Value) },
                { ConfigType.RegionHosts.ToString(), S3.DEFAULT_S3_LOCATION_BASED_HOSTS.ToDictionary((x) => x.Key, (y) => y.Value) },
                { ConfigType.StorageClasses.ToString(), S3.KNOWN_S3_STORAGE_CLASSES.ToDictionary((x) => x.Key, (y) => y.Value) },
            };

        #endregion
    }
}

