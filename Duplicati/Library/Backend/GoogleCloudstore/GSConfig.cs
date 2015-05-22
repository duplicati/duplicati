using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.GoogleCloudstore
{
    class GSConfig : IWebModule
    {
        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.Providers;
        private static readonly string DEFAULT_CONFIG_TYPE_STR = Enum.GetName(typeof(ConfigType), DEFAULT_CONFIG_TYPE);
        private const string KEY_CONFIGTYPE = "gs-config";

        public enum ConfigType
        {
            Providers
        }

        public GSConfig()
        {

        }
        #region IWebModule implementation

        public IDictionary<string, string> Execute(IDictionary<string, string> options)
        {
            return null;
            //string k;
            //options.TryGetValue(KEY_CONFIGTYPE, out k);
            //if (string.IsNullOrWhiteSpace(k))
            //    k = DEFAULT_CONFIG_TYPE_STR;

            //ConfigType ct;
            //if (!Enum.TryParse<ConfigType>(k, true, out ct))
            //    ct = DEFAULT_CONFIG_TYPE;

            //switch (ct)
            //{
            //    case ConfigType.RegionHosts:
            //        return S3.DEFAULT_S3_LOCATION_BASED_HOSTS.ToDictionary((x) => x.Key, (y) => y.Value);
            //    case ConfigType.Regions:
            //        return S3.KNOWN_S3_LOCATIONS.ToDictionary((x) => x.Key, (y) => y.Value);
            //    default:
            //        return S3.KNOWN_S3_PROVIDERS.ToDictionary((x) => x.Key, (y) => y.Value);
            //}
        }

        public string Key { get { return "gs-getconfig"; } }

        public string DisplayName { get { return "GS configuration module"; } }

        public string Description { get { return "Exposes GS configuration as a web module"; } }

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
