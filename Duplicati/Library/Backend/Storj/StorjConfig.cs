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
using Duplicati.Library.Localization.Short;
using System;
using System.Collections.Generic;

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

        public string DisplayName { get { return LC.L("Storj DCS configuration module"); } }

        public string Description { get { return LC.L("Expose Storj DCS configuration as a web module"); } }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>([
                    new CommandLineArgument(KEY_CONFIGTYPE, CommandLineArgument.ArgumentType.Enumeration, LC.L("The config to get"), LC.L("Provide different config values"), DEFAULT_CONFIG_TYPE_STR, Enum.GetNames(typeof(ConfigType)))

                ]);
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
