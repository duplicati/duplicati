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

namespace FreshKeeper
{
    /// <summary>
    /// Represents the configuration of FreshKeeper
    /// </summary>
    public class Config
    {
        private string m_publicKey;
        private string m_applicationName;
        private Version m_localVersion;
        private string m_architecture;
        private string[] m_urls;
        
        private bool m_enabled;
        private string m_checkInterval;
        private bool m_notifyOnRevisionChange;

        /// <summary>
        /// Constructs an empty Config instance
        /// </summary>
        public Config()
        {
        }

        /// <summary>
        /// Determines if the configuration instance is valid
        /// </summary>
        public void CheckValid()
        {
            throw new MissingMethodException();
        }

        /// <summary>
        /// The public key used to verify the autenticity of the update
        /// </summary>
        public string PublicKey
        {
            get { return m_publicKey; }
            set { m_publicKey = value; }
        }

        /// <summary>
        /// Name of the application
        /// </summary>
        public string ApplicationName
        {
            get { return m_applicationName; }
            set { m_applicationName = value; }
        }

        /// <summary>
        /// The version that is installed locally
        /// </summary>
        public Version LocalVersion
        {
            get { return m_localVersion; }
            set { m_localVersion = value; }
        }

        /// <summary>
        /// The architecture of this installation
        /// </summary>
        public string Architecture
        {
            get { return m_architecture; }
            set { m_architecture = value; }
        }

        /// <summary>
        /// A list of URL's where the update manifest can be found
        /// </summary>
        public string[] Urls
        {
            get { return m_urls; }
            set { m_urls = value; }
        }

        /// <summary>
        /// A value that indicates if update checking is enabled
        /// </summary>
        public bool Enabled
        {
            get { return m_enabled; }
            set { m_enabled = false; }
        }

        /// <summary>
        /// A value indicating the minimum interval between update checking
        /// </summary>
        public string CheckInterval
        {
            get { return m_checkInterval; }
            set { m_checkInterval = value; }
        }

        /// <summary>
        /// A value indicating if minor revisions are considered updates
        /// </summary>
        public bool NotifyOnRevisionChange
        {
            get { return m_notifyOnRevisionChange; }
            set { m_notifyOnRevisionChange = value; }
        }
    }
}
