#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Duplicati.Library.Modules.Builtin
{
    public class HttpOptions : Duplicati.Library.Interface.IGenericModule, Duplicati.Library.Interface.IConnectionModule, IDisposable
    {
        private const string OPTION_DISABLE_EXPECT100 = "disable-expect100-continue";
        private const string OPTION_DISABLE_NAGLING = "disable-nagling";
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "accept-specified-ssl-hash";
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "accept-any-ssl-certificate";
        private const string OPTION_OAUTH_URL = "oauth-url";
        private const string OPTION_SSL_VERSIONS = "allowed-ssl-versions";

        private IDisposable m_certificateValidator;
        private bool m_useNagle;
        private bool m_useExpect;
        private bool m_dispose;
        private System.Net.SecurityProtocolType m_securityProtocol;

        // Copied from system reference
        [Flags]
        private enum CopySecurityProtocolType
        {
            Ssl3 = 48,
            Tls = 192,
            Tls11 = 768,
            Tls12 = 3072,
        }

        private static Dictionary<string, int> SecurityProtocols
        {
            get
            {
                var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var val in Enum.GetNames(typeof(CopySecurityProtocolType)).Zip(Enum.GetValues(typeof(CopySecurityProtocolType)).Cast<int>(), (x,y) => new KeyValuePair<string, int>(x, y)))
                    res[val.Key] = val.Value;

                foreach (var val in Enum.GetNames(typeof(System.Net.SecurityProtocolType)).Zip(Enum.GetValues(typeof(System.Net.SecurityProtocolType)).Cast<int>(), (x,y) => new KeyValuePair<string, int>(x, y)))
                    res[val.Key] = val.Value;

                return res;
            }
        }

        private static System.Net.SecurityProtocolType ParseSSLProtocols(string names)
        {
            var ptr = SecurityProtocols;
            var res = 0;
            foreach (var s in names.Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
                if (ptr.ContainsKey(s))
                    res = res | ptr[s];

            return (System.Net.SecurityProtocolType)res;
        }

        #region IGenericModule Members

        public string Key
        {
            get { return "http-options"; }
        }

        public string DisplayName
        {
            get { return Strings.HttpOptions.DisplayName; }
        }

        public string Description
        {
            get { return Strings.HttpOptions.Description; }
        }

        public bool LoadAsDefault
        {
            get { return true; }
        }

        public IList<Duplicati.Library.Interface.ICommandLineArgument> SupportedCommands
        {
            get { 
                var sslnames = SecurityProtocols.Select(x => x.Key).ToArray();
                var defaultssl = string.Join(",", Enum.GetValues(typeof(System.Net.SecurityProtocolType)).Cast<Enum>().Where(x => System.Net.ServicePointManager.SecurityProtocol.HasFlag(x)));

                return new List<Duplicati.Library.Interface.ICommandLineArgument>( new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_DISABLE_EXPECT100, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DisableExpect100Short, Strings.HttpOptions.DisableExpect100Long, "false"),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_DISABLE_NAGLING, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DisableNagleShort, Strings.HttpOptions.DisableNagleLong, "false"),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.HttpOptions.DescriptionAcceptHashShort, Strings.HttpOptions.DescriptionAcceptHashLong2),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_ACCEPT_ANY_CERTIFICATE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DescriptionAcceptAnyCertificateShort, Strings.HttpOptions.DescriptionAcceptAnyCertificateLong),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_OAUTH_URL, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.HttpOptions.OauthurlShort, Strings.HttpOptions.OauthurlLong, OAuthHelper.DUPLICATI_OAUTH_SERVICE),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_SSL_VERSIONS, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Flags, Strings.HttpOptions.SslversionsShort, Strings.HttpOptions.SslversionsLong, defaultssl, null, sslnames),
                }); 
            }
        }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            m_dispose = true;

            bool accepAllCertificates = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_ACCEPT_ANY_CERTIFICATE);

            string certHash;
            commandlineOptions.TryGetValue(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, out certHash);

            m_certificateValidator = new Library.Utility.SslCertificateValidator(accepAllCertificates, certHash == null ? null : certHash.Split(new string[] {",", ";"}, StringSplitOptions.RemoveEmptyEntries));
            
            bool disableNagle = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_DISABLE_NAGLING);
            bool disableExpect100 = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_DISABLE_EXPECT100);

            m_useNagle = System.Net.ServicePointManager.UseNagleAlgorithm;
            m_useExpect = System.Net.ServicePointManager.Expect100Continue;
            m_securityProtocol = System.Net.ServicePointManager.SecurityProtocol;

            System.Net.ServicePointManager.UseNagleAlgorithm = !disableNagle;
            System.Net.ServicePointManager.Expect100Continue = !disableExpect100;

            string sslprotocol;
            commandlineOptions.TryGetValue(OPTION_SSL_VERSIONS, out sslprotocol);
            if (!string.IsNullOrWhiteSpace(sslprotocol))
                System.Net.ServicePointManager.SecurityProtocol = ParseSSLProtocols(sslprotocol);

            string url;
            commandlineOptions.TryGetValue(OPTION_OAUTH_URL, out url);
            OAuthHelper.OAUTH_SERVER = url;

        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_dispose)
            {
                m_dispose = false;
                System.Net.ServicePointManager.UseNagleAlgorithm = m_useNagle;
                System.Net.ServicePointManager.Expect100Continue = m_useExpect;
                System.Net.ServicePointManager.SecurityProtocol = m_securityProtocol;
                if (m_certificateValidator != null)
                    m_certificateValidator.Dispose();
                m_certificateValidator = null;
                OAuthHelper.OAUTH_SERVER = null;
            }
        }

        #endregion
    }
}
