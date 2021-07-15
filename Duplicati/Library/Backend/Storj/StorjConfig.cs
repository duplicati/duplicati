using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Storj
{
    public class StorjConfig : IWebModule
    {
        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.Satellites;
        private const string KEY_CONFIGTYPE = "storj-config";
        private static readonly string DEFAULT_CONFIG_TYPE_STR = Enum.GetName(typeof(ConfigType), DEFAULT_CONFIG_TYPE);

        public enum ConfigType
        {
            Satellites,
            AuthenticationMethods
        }

        #region IWebModule implementation

        public string Key { get { return "storj-getconfig"; } }

        public string DisplayName { get { return "Storj DCS configuration module"; } }

        public string Description { get { return "Exposes Storj DCS configuration as a web module"; } }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(KEY_CONFIGTYPE, CommandLineArgument.ArgumentType.Enumeration, "The config to get", "Provides different config values", DEFAULT_CONFIG_TYPE_STR, Enum.GetNames(typeof(ConfigType)))

                }); 
            }
        }

        public IDictionary<string, string> Execute(IDictionary<string, string> options)
        {
            string k;
            options.TryGetValue(KEY_CONFIGTYPE, out k);
            if (string.IsNullOrWhiteSpace(k))
            {
                k = DEFAULT_CONFIG_TYPE_STR;
            }

            ConfigType ct;
            if (!Enum.TryParse<ConfigType>(k, true, out ct))
            {
                ct = DEFAULT_CONFIG_TYPE;
            }

            switch (ct)
            {
                case ConfigType.Satellites:
                    return Storj.KNOWN_STORJ_SATELLITES;
                case ConfigType.AuthenticationMethods:
                    return Storj.KNOWN_AUTHENTICATION_METHODS;
                default:
                    return Storj.KNOWN_STORJ_SATELLITES;
            }
        }
        #endregion
    }
}
