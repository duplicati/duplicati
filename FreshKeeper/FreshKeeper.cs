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
    /// FreshKeeper is an LGPL update system for .Net
    /// </summary>
    public class FreshKeeper
    {
        /// <summary>
        /// The configuration file
        /// </summary>
        private string m_configFile;

        /// <summary>
        /// The configuration object
        /// </summary>
        private Config m_config = null;

        /// <summary>
        /// Information about the last update check
        /// </summary>
        private LastCheck m_lastCheck = null;

        /// <summary>
        /// The event type used to signal an update
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="update">The update found</param>
        public delegate void UpdateavailableEvent(FreshKeeper sender, Update update);

        /// <summary>
        /// The event type used to signal an error
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="ex">The exception that occured</param>
        public delegate void UpdateErrorEvent(FreshKeeper sender, Exception ex);

        /// <summary>
        /// An event that is raised when an update is available
        /// </summary>
        public UpdateavailableEvent Updateavailable;

        /// <summary>
        /// An event that is raised when an error occurs while checking for updates
        /// </summary>
        public UpdateErrorEvent UpdateError;

        /// <summary>
        /// The list of available updates
        /// </summary>
        private List<Update> m_updates = null;

        /// <summary>
        /// Constructs a new FreshKeeper instance
        /// </summary>
        public FreshKeeper()
        {
            m_configFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "FreshKeeper.xml");
        }

        /// <summary>
        /// Constructs a new FreshKeeper instance
        /// </summary>
        /// <param name="configFile">The configuration file to use</param>
        public FreshKeeper(string configFile)
        {
            m_configFile = configFile;
        }

        /// <summary>
        /// Gets or sets the configuration item
        /// </summary>
        public Config Config
        {
            get { return m_config; }
            set { m_config = value; }
        }

        /// <summary>
        /// Initiates an syncronous check for updates
        /// </summary>
        /// <param name="force">A value indicating if the duration and user-enabled check should be bypassed</param>
        public void CheckForUpdates(bool force)
        {
            try
            {
                if (m_config == null)
                {
                    System.Xml.Serialization.XmlSerializer src = new System.Xml.Serialization.XmlSerializer(typeof(Config));
                    using (System.IO.FileStream fs = new System.IO.FileStream(m_configFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                        m_config = (Config)src.Deserialize(fs);
                }

                //This throws an exception if somethings broken
                m_config.CheckValid();

                if (!m_config.Enabled && !force)
                    return;

                if (m_lastCheck == null)
                {
                    string file = m_config.ApplicationName + ".xml";
                    foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                        file = file.Replace(c, '-');

                    file = System.IO.Path.Combine(System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreshKeeper"), file);

                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(file)))
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));

                    if (System.IO.File.Exists(file))
                    {
                        System.Xml.Serialization.XmlSerializer srl = new System.Xml.Serialization.XmlSerializer(typeof(LastCheck));
                        using (System.IO.FileStream fs = new System.IO.FileStream(m_configFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                            m_lastCheck = (LastCheck)srl.Deserialize(fs);
                    }
                    else
                        m_lastCheck = new LastCheck();
                }

                if (Duplicati.Library.Core.Timeparser.ParseTimeInterval(m_config.CheckInterval, m_lastCheck.Time) > DateTime.Now)
                    return;

                Random r = new Random();
                string url = m_config.Urls[r.Next(0, m_config.Urls.Length)];

                System.Net.WebClient wc = new System.Net.WebClient();
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                doc.PreserveWhitespace = true; //Make sure we don't alter the document
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream(wc.DownloadData(url)))
                    doc.Load(ms);

                string hash = doc["UpdateList"].Attributes["SignedHash"].Value;
                doc["UpdateList"].Attributes["SignedHash"].Value = "";

                System.Security.Cryptography.RSACryptoServiceProvider rsa = new System.Security.Cryptography.RSACryptoServiceProvider();
                rsa.FromXmlString(m_config.PublicKey);

                UpdateList lst = null;

                using(System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    doc.Save(ms);
                    if (!rsa.VerifyData(ms.ToArray(), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA1"), Convert.FromBase64String(hash)))
                        throw new Exception("Failed to verify signature");
                    ms.Position = 0;
                    System.Xml.Serialization.XmlSerializer sr = new System.Xml.Serialization.XmlSerializer(typeof(UpdateList));
                    lst = (UpdateList)sr.Deserialize(ms);
                    lst.SignedHash = hash;
                }

                if (lst == null || lst.Updates == null || lst.Updates.Length == 0)
                    return;

                Update newest = lst.Updates[0];

                foreach(Update u in lst.Updates)
                    if (u.Version > newest.Version && (!u.BugfixUpdate || (u.BugfixUpdate && m_config.NotifyOnRevisionChange)))
                        newest = u;

                if (newest.Version > m_config.LocalVersion)
                    if (Updateavailable != null)
                        Updateavailable(this, newest);
            }
            catch (Exception ex)
            {
                RaiseErrorEvent(ex);
                return;
            }
        }

        private void RaiseErrorEvent(Exception ex)
        {
            if (UpdateError != null)
                UpdateError(this, ex);
        }
    }
}
