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
    /// Represents a single update
    /// </summary>
    public class Update
    {
        private string m_applicationName;
        private Version m_version = new Version(0, 0);
        private string m_architecture;
        private string m_signedFileHash;
        private string[] m_urls;
        private DateTime m_releaseDate;
        private string m_caption = "";
        private string m_changelog;
        private bool m_bugfixUpdate;
        private bool m_securityUpdate;

        /// <summary>
        /// Constructs an empty update instance
        /// </summary>
        public Update()
        {
        }

        /// <summary>
        /// The name of the application
        /// </summary>
        public string ApplicationName
        {
            get { return m_applicationName; }
            set { m_applicationName = value; }
        }

        /// <summary>
        /// The release version number as a string
        /// </summary>
        [System.Xml.Serialization.XmlElement("Version")]
        public string VersionString
        {
            get { return m_version.ToString(); }
            set { m_version = new Version(value); }
        }

        /// <summary>
        /// The version that this entry represents
        /// </summary>
        [System.Xml.Serialization.XmlIgnore()]
        public Version Version
        {
            get { return m_version; }
            set { m_version = value; }
        }

        /// <summary>
        /// The architecture that this update is for
        /// </summary>
        public string Architecture
        {
            get { return m_architecture; }
            set { m_architecture = value; }
        }

        /// <summary>
        /// The signed hash for the update file
        /// </summary>
        public string SignedFileHash
        {
            get { return m_signedFileHash; }
            set { m_signedFileHash = value; }
        }

        /// <summary>
        /// A list of URL's where the update can be downloaded from
        /// </summary>
        public string[] Urls
        {
            get { return m_urls; }
            set { m_urls = value; }
        }

        /// <summary>
        /// The time and date where the update was released
        /// </summary>
        public DateTime ReleaseDate
        {
            get { return m_releaseDate; }
            set { m_releaseDate = value; }
        }

        /// <summary>
        /// A text caption for the update
        /// </summary>
        public string Caption
        {
            get { return m_caption; }
            set { m_caption = value; }
        }

        /// <summary>
        /// A complete changelog describing updates in this version
        /// </summary>
        public string Changelog
        {
            get { return m_changelog; }
            set { m_changelog = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if the update is considered a bugfix
        /// </summary>
        public bool BugfixUpdate
        {
            get { return m_bugfixUpdate; }
            set { m_bugfixUpdate = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if the update fixes a security issue
        /// </summary>
        public bool SecurityUpdate
        {
            get { return m_securityUpdate; }
            set { m_securityUpdate = value; }
        }

        /// <summary>
        /// Returns a textual description of the object
        /// </summary>
        /// <returns>A textual description of the object</returns>
        public override string ToString()
        {
            return m_version.ToString() + " - " + m_caption;
        }
    }
}
