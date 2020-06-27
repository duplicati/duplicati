using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;


namespace Duplicati.Library.Backend
{
    public class CosConfig : IWebModule
    {
        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.Providers;
        private static readonly string DEFAULT_CONFIG_TYPE_STR = Enum.GetName(typeof(ConfigType), DEFAULT_CONFIG_TYPE);
        private const string KEY_CONFIGTYPE = "cos-config";

        public enum ConfigType
        {
            Providers,
            Regions,
            RegionHosts,
            StorageClasses
        }

        public CosConfig()
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

            }

            return new Dictionary<string, string>() {
                { "EU", "s3.eu-west-1.amazonaws.com" }
            };
        }

        public string Key { get { return "cos-getconfig"; } }

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
