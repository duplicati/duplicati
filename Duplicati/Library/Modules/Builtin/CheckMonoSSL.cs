//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
#if __MonoCS__ 
using Mono.Security.X509;
#endif
using System.Collections.Generic;

namespace Duplicati.Library.Modules.Builtin
{
    public class CheckMonoSSL : Duplicati.Library.Interface.IGenericModule, Duplicati.Library.Interface.IWebModule
    {
        public CheckMonoSSL()
        {
        }

        private int CheckForInstalledCerts()
        {
#if __MonoCS__            
            var localcerts = X509StoreManager.LocalMachine.TrustedRoot.Certificates.Count;
            var usercerts = X509StoreManager.CurrentUser.TrustedRoot.Certificates.Count;
            var trusted = X509StoreManager.TrustedRootCertificates.Count;
            return localcerts + usercerts + trusted;
#else
            return 0;
#endif
        }

        #region IGenericModule implementation

        public string Key { get { return "check-mono-ssl"; } }
        public string DisplayName { get { return Strings.CheckMonoSSL.Displayname; } }
        public string Description { get { return Strings.CheckMonoSSL.Description; } }
        public bool LoadAsDefault { get { return true; } }
        public IList<Duplicati.Library.Interface.ICommandLineArgument> SupportedCommands { get { return null; } }

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            //Ensure the setup is valid, could throw an exception
            if (!commandlineOptions.ContainsKey("main-action") || !Library.Utility.Utility.IsMono)
                return;

            if (CheckForInstalledCerts() == 0)
                Console.WriteLine(Strings.CheckMonoSSL.ErrorMessage);
        }
        #endregion

        #region IWebModule implementation

        private const string KEY_CONFIGTYPE = "mono-ssl-config";
        private const ConfigType DEFAULT_CONFIG_TYPE = ConfigType.List;

        private enum ConfigType
        {
            List,
            Install
        }


        public IDictionary<string, string> Execute(IDictionary<string, string> options)
        {
            string k;
            options.TryGetValue(KEY_CONFIGTYPE, out k);

            ConfigType ct;
            if (!Enum.TryParse<ConfigType>(k, true, out ct))
                ct = DEFAULT_CONFIG_TYPE;

            var d = new Dictionary<string, string>();

            switch (ct)
            {
                case ConfigType.Install:

                    try
                    {
                        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "mozroots.exe");
                        var pi = new System.Diagnostics.ProcessStartInfo(path, "--import --sync --quiet");
                        pi.UseShellExecute = false;
                        var p = System.Diagnostics.Process.Start(pi);
                        p.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds);
                    }
                    catch(Exception ex)
                    {
                        d["error"] = ex.ToString();
                    }

                    d["count"] = CheckForInstalledCerts().ToString();
                    break;

                case ConfigType.List:
                default:
                    d["count"] = CheckForInstalledCerts().ToString();
                    d["mono"] = Library.Utility.Utility.IsMono.ToString();
                    d["message"] = Strings.CheckMonoSSL.ErrorMessage;
                    break;
            }

            return d;
        }

        #endregion
    }
}

