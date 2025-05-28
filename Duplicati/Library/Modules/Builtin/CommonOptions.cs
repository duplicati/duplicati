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
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Modules.Builtin
{
    public class CommonOptions : Interface.IConnectionModule, IDisposable
    {
        private const string OPTION_OAUTH_URL = "oauth-url";

        /// <summary>
        /// The handle to the call-context oauth settings
        /// </summary>
		private IDisposable m_oauthsettings;

        #region IGenericModule Members

        public string Key
        {
            get { return "common-options"; }
        }

        public string DisplayName
        {
            get { return Strings.CommonOptions.DisplayName; }
        }

        public string Description
        {
            get { return Strings.CommonOptions.Description; }
        }

        public bool LoadAsDefault
        {
            get { return true; }
        }

        public IList<Interface.ICommandLineArgument> SupportedCommands
            => [
                .. TimeoutOptionsHelper.GetOptions(),
                .. SslOptionsHelper.GetCertOnlyOptions(),
                new Interface.CommandLineArgument(OPTION_OAUTH_URL, Interface.CommandLineArgument.ArgumentType.String, Strings.CommonOptions.OauthurlShort, Strings.CommonOptions.OauthurlLong, OAuthContextSettings.DUPLICATI_OAUTH_SERVICE),
            ];

        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            commandlineOptions.TryGetValue(OPTION_OAUTH_URL, out var url);
            if (!string.IsNullOrWhiteSpace(url))
                m_oauthsettings = OAuthContextSettings.StartSession(url);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            m_oauthsettings?.Dispose();
            m_oauthsettings = null;
        }

        #endregion
    }
}
