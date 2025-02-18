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
using System.Text;
using System.Linq;

namespace Duplicati.Library.Modules.Builtin
{
    public class HttpOptions : Duplicati.Library.Interface.IConnectionModule, IDisposable
    {
        private const string OPTION_DISABLE_EXPECT100 = "disable-expect100-continue";
        private const string OPTION_DISABLE_NAGLING = "disable-nagling";
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "accept-specified-ssl-hash";
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "accept-any-ssl-certificate";
        private const string OPTION_OAUTH_URL = "oauth-url";
        private const string OPTION_SSL_VERSIONS = "allowed-ssl-versions";

        private const string OPTION_BUFFER_REQUESTS = "http-enable-buffering";
        private const string OPTION_OPERATION_TIMEOUT = "http-operation-timeout";
        private const string OPTION_READWRITE_TIMEOUT = "http-readwrite-timeout";


        private bool m_useNagle;
        private bool m_useExpect;
        private System.Net.SecurityProtocolType m_securityProtocol;

        private bool m_dispose;

        private bool m_resetNagle;
        private bool m_resetExpect;
        private bool m_resetSecurity;

        /// <summary>
        /// The handle to the call-context http settings
        /// </summary>
        private IDisposable m_httpsettings;

        /// <summary>
        /// The handle to the call-context oauth settings
        /// </summary>
		private IDisposable m_oauthsettings;

        private static Dictionary<string, int> SecurityProtocols
        {
            get
            {
                var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var val in Enum.GetNames(typeof(System.Net.SecurityProtocolType)).Zip(Enum.GetValues(typeof(System.Net.SecurityProtocolType)).Cast<int>(), (x, y) => new KeyValuePair<string, int>(x, y)))
                    res[val.Key] = val.Value;

                return res;
            }
        }

        private static System.Net.SecurityProtocolType ParseSSLProtocols(string names)
        {
            var ptr = SecurityProtocols;
            var res = 0;
            foreach (var s in names.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
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
            get
            {
                var sslnames = SecurityProtocols.Select(x => x.Key).ToArray();
                var defaultssl = System.Net.SecurityProtocolType.SystemDefault.ToString();

                return new List<Duplicati.Library.Interface.ICommandLineArgument>(new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_DISABLE_EXPECT100, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DisableExpect100Short, Strings.HttpOptions.DisableExpect100Long, "false"),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_DISABLE_NAGLING, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DisableNagleShort, Strings.HttpOptions.DisableNagleLong, "false"),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.HttpOptions.DescriptionAcceptHashShort, Strings.HttpOptions.DescriptionAcceptHashLong2),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_ACCEPT_ANY_CERTIFICATE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DescriptionAcceptAnyCertificateShort, Strings.HttpOptions.DescriptionAcceptAnyCertificateLong),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_OAUTH_URL, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.HttpOptions.OauthurlShort, Strings.HttpOptions.OauthurlLong, OAuthHelper.DUPLICATI_OAUTH_SERVICE),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_SSL_VERSIONS, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Flags, Strings.HttpOptions.SslversionsShort, Strings.HttpOptions.SslversionsLong, defaultssl, null, sslnames),

                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_OPERATION_TIMEOUT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan, Strings.HttpOptions.OperationtimeoutShort, Strings.HttpOptions.OperationtimeoutLong),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_READWRITE_TIMEOUT, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan, Strings.HttpOptions.ReadwritetimeoutShort, Strings.HttpOptions.ReadwritetimeoutLong),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_BUFFER_REQUESTS, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.BufferrequestsShort, Strings.HttpOptions.BufferrequestsLong, "false"),
                });
            }
        }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            m_dispose = true;
            TimeSpan operationTimeout = new TimeSpan(0);
            TimeSpan readwriteTimeout = new TimeSpan(0);

            string timetmp;
            commandlineOptions.TryGetValue(OPTION_OPERATION_TIMEOUT, out timetmp);
            if (!string.IsNullOrWhiteSpace(timetmp))
                operationTimeout = Utility.Timeparser.ParseTimeSpan(timetmp);

            commandlineOptions.TryGetValue(OPTION_READWRITE_TIMEOUT, out timetmp);
            if (!string.IsNullOrWhiteSpace(timetmp))
                readwriteTimeout = Utility.Timeparser.ParseTimeSpan(timetmp);

            bool accepAllCertificates = Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_ACCEPT_ANY_CERTIFICATE);

            string certHash;
            commandlineOptions.TryGetValue(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, out certHash);

            m_httpsettings = Duplicati.Library.Utility.HttpContextSettings.StartSession(
                operationTimeout,
                readwriteTimeout,
                Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_BUFFER_REQUESTS),
                accepAllCertificates,
                certHash == null ? null : certHash.Split(new string[] { ",", ";" }, StringSplitOptions.RemoveEmptyEntries)
            );

            bool disableNagle = Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_DISABLE_NAGLING);
            bool disableExpect100 = Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_DISABLE_EXPECT100);

            // TODO: This is done to avoid conflicting settings,
            // but ideally, we should run each operation in a separate
            // app-domain to ensure that multiple invocations of this module
            // does not interfere, as the options are shared in the app-domain
            m_resetNagle = commandlineOptions.ContainsKey(OPTION_DISABLE_NAGLING);
            m_resetExpect = commandlineOptions.ContainsKey(OPTION_DISABLE_EXPECT100);
            m_resetSecurity = commandlineOptions.ContainsKey(OPTION_SSL_VERSIONS);

            m_useNagle = System.Net.ServicePointManager.UseNagleAlgorithm;
            m_useExpect = System.Net.ServicePointManager.Expect100Continue;
            m_securityProtocol = System.Net.ServicePointManager.SecurityProtocol;

            if (m_resetNagle)
                System.Net.ServicePointManager.UseNagleAlgorithm = !disableNagle;
            if (m_resetExpect)
                System.Net.ServicePointManager.Expect100Continue = !disableExpect100;

            string sslprotocol;
            commandlineOptions.TryGetValue(OPTION_SSL_VERSIONS, out sslprotocol);
            if (!string.IsNullOrWhiteSpace(sslprotocol) && m_resetSecurity)
                System.Net.ServicePointManager.SecurityProtocol = ParseSSLProtocols(sslprotocol);

            string url;
            commandlineOptions.TryGetValue(OPTION_OAUTH_URL, out url);
            if (!string.IsNullOrWhiteSpace(url))
                m_oauthsettings = OAuthContextSettings.StartSession(url);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_dispose)
            {
                m_dispose = false;
                if (m_resetNagle)
                    System.Net.ServicePointManager.UseNagleAlgorithm = m_useNagle;
                if (m_resetExpect)
                    System.Net.ServicePointManager.Expect100Continue = m_useExpect;
                if (m_resetSecurity)
                    System.Net.ServicePointManager.SecurityProtocol = m_securityProtocol;
            }

            if (m_httpsettings != null)
            {
                m_httpsettings.Dispose();
                m_httpsettings = null;
            }

            if (m_oauthsettings != null)
            {
                m_oauthsettings.Dispose();
                m_oauthsettings = null;
            }
        }

        #endregion
    }
}
