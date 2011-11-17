#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Library.Modules.Builtin
{
    public class HttpOptions : Duplicati.Library.Interface.IGenericModule, IDisposable
    {
        private const string OPTION_DISABLE_EXPECT100 = "disable-expect100-continue";
        private const string OPTION_DISABLE_NAGLING = "disable-nagling";
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "accept-specified-ssl-hash";
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "accept-any-ssl-certificate";

        private IDisposable m_certificateValidator;
        private bool m_useNagle;
        private bool m_useExpect;
        private bool m_dispose;

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
                return new List<Duplicati.Library.Interface.ICommandLineArgument>( new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_DISABLE_EXPECT100, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DisableExpect100Short, Strings.HttpOptions.DisableExpect100Long, "false"),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_DISABLE_NAGLING, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DisableNagleShort, Strings.HttpOptions.DisableNagleLong, "false"),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String, Strings.HttpOptions.DescriptionAcceptHashShort, Strings.HttpOptions.DescriptionAcceptHashLong),
                    new Duplicati.Library.Interface.CommandLineArgument(OPTION_ACCEPT_ANY_CERTIFICATE, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, Strings.HttpOptions.DescriptionAcceptAnyCertificateShort, Strings.HttpOptions.DescriptionAcceptAnyCertificateLong),
                }); 
            }
        }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            m_dispose = true;

            bool accepAllCertificates = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_ACCEPT_ANY_CERTIFICATE);

            string certHash;
            commandlineOptions.TryGetValue(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, out certHash);

            m_certificateValidator = new Library.Utility.SslCertificateValidator(accepAllCertificates, certHash);
            
            bool disableNagle = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_DISABLE_NAGLING);
            bool disableExpect100 = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_DISABLE_EXPECT100);

            m_useNagle = System.Net.ServicePointManager.UseNagleAlgorithm;
            m_useExpect = System.Net.ServicePointManager.Expect100Continue;

            System.Net.ServicePointManager.UseNagleAlgorithm = !disableNagle;
            System.Net.ServicePointManager.Expect100Continue = !disableExpect100;
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
                if (m_certificateValidator != null)
                    m_certificateValidator.Dispose();
                m_certificateValidator = null;
            }
        }

        #endregion
    }
}
