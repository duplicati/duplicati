using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Modules.Builtin
{
    public class HttpOptions : Duplicati.Library.Interface.IGenericModule, IDisposable
    {
        //TODO: Figure out if this is activated for the TestConnection/CreateFolder.

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

            string opt;
            commandlineOptions.TryGetValue(OPTION_ACCEPT_ANY_CERTIFICATE, out opt);

            bool accepAllCertificates = Utility.Utility.ParseBool(opt, false);
            string certHash;
            commandlineOptions.TryGetValue(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, out certHash);

            m_certificateValidator = new Library.Utility.SslCertificateValidator(accepAllCertificates, certHash);
            
            commandlineOptions.TryGetValue(OPTION_DISABLE_NAGLING, out opt);
            bool disableNagle = Utility.Utility.ParseBool(opt, false);

            commandlineOptions.TryGetValue(OPTION_DISABLE_EXPECT100, out opt);
            bool disableExpect100 = Utility.Utility.ParseBool(opt, false);

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
