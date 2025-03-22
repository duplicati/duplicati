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

namespace Duplicati.Library.Backend.GoogleServices
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the ServerSettings.
    public class GCSConfig : IWebModule
    {
        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.Locations;
        private static readonly string DEFAULT_CONFIG_TYPE_STR = DEFAULT_CONFIG_TYPE.ToString();
        private const string KEY_CONFIGTYPE = "gcs-config";

        public enum ConfigType
        {
            Locations,
            StorageClasses
        }
        public GCSConfig()
        {
        }

        #region IWebModule implementation

        public IDictionary<string, string?> Execute(IDictionary<string, string?> options)
        {
            options.TryGetValue(KEY_CONFIGTYPE, out var k);
            if (string.IsNullOrWhiteSpace(k))
                k = DEFAULT_CONFIG_TYPE_STR;

            if (!Enum.TryParse<ConfigType>(k, true, out var ct))
                ct = DEFAULT_CONFIG_TYPE;

            switch (ct)
            {
                case ConfigType.StorageClasses:
                    return WebApi.GoogleCloudStorage.KNOWN_GCS_STORAGE_CLASSES.ToDictionary((x) => x.Key, (y) => y.Value);
                default:
                    return WebApi.GoogleCloudStorage.KNOWN_GCS_LOCATIONS.ToDictionary((x) => x.Key, (y) => y.Value);
            }
        }

        public string Key => "gcs-getconfig";

        public string DisplayName => Strings.GCSConfig.DisplayName;

        public string Description => Strings.GCSConfig.Description;


        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(KEY_CONFIGTYPE, CommandLineArgument.ArgumentType.Enumeration, Strings.GCSConfig.ConfigTypeShort, Strings.GCSConfig.ConfigTypeLong, DEFAULT_CONFIG_TYPE_STR, Enum.GetNames(typeof(ConfigType)))
        ];

        #endregion
    }
}
