#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Library.Backend
{
    public partial class S3CommonOptions : UserControl
    {
        private const string ALLOW_SAVED_CREDENTIALS = "S3: Save Credentials";
        private const string SAVED_CREDENTIALS = "S3: Available Accounts";
        private const string DEFAULT_EU_BUCKET = "S3: Default Use Euro";

        private const string XML_ROOT = "root";
        private const string XML_ACCOUNT = "account";
        private const string XML_ACCOUNT_NAME = "name";
        private const string XML_ACCOUNT_CODE = "code";

        private IDictionary<string, string> m_settings;

        private S3CommonOptions()
        {
            InitializeComponent();
        }

        public S3CommonOptions(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
            : this()
        {
            m_settings = options;

            UseEUBuckets.Checked = ExtractDefaultEUBuckets(m_settings);
            AllowCredentialStorage.Checked = ExtractAllowCredentialStorage(m_settings);

            CredentialList.Items.Clear();
            foreach(KeyValuePair<string, string> k in ExtractAccounts(m_settings))
                CredentialList.Items.Add(new Core.ComboBoxItemPair<string>(k.Key, k.Value));
        }

        private void RemoveAllButton_Click(object sender, EventArgs e)
        {
            CredentialList.Items.Clear();
        }

        private void CredentialList_SelectedIndexChanged(object sender, EventArgs e)
        {
            RemoveSelectedButton.Enabled = CredentialList.SelectedItem as Core.ComboBoxItemPair<string> != null;
        }

        private void RemoveSelectedButton_Click(object sender, EventArgs e)
        {
            if (CredentialList.SelectedItem as Core.ComboBoxItemPair<string> != null)
                CredentialList.Items.RemoveAt(CredentialList.SelectedIndex);
        }

        private void AllowCredentialStorage_CheckedChanged(object sender, EventArgs e)
        {
            CredentialGroup.Enabled = AllowCredentialStorage.Checked;
        }

        #region API implementation

        public bool Save(bool validate)
        {
            if (!validate)
            {
                m_settings[ALLOW_SAVED_CREDENTIALS] = AllowCredentialStorage.Checked.ToString();
                m_settings[DEFAULT_EU_BUCKET] = UseEUBuckets.Checked.ToString();

                Dictionary<string, string> tmp = new Dictionary<string, string>();
                foreach (Core.ComboBoxItemPair<string> item in CredentialList.Items)
                    tmp[item.ToString()] = item.Value;

                EncodeAccounts(tmp, m_settings);
            }

            return true;
        }

        public static string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(SAVED_CREDENTIALS))
                applicationSettings[SAVED_CREDENTIALS] = guiOptions[SAVED_CREDENTIALS];

            if (guiOptions.ContainsKey(DEFAULT_EU_BUCKET))
                applicationSettings[DEFAULT_EU_BUCKET] = guiOptions.ContainsKey(DEFAULT_EU_BUCKET) ? guiOptions[DEFAULT_EU_BUCKET] : "false";

            if (guiOptions.ContainsKey(ALLOW_SAVED_CREDENTIALS))
                applicationSettings[ALLOW_SAVED_CREDENTIALS] = guiOptions.ContainsKey(ALLOW_SAVED_CREDENTIALS) ? guiOptions[ALLOW_SAVED_CREDENTIALS] : "true";

            return null;
        }

        /// <summary>
        /// Encodes the account information from <paramref name="accounts"/> into the settings found in <paramref name="options"/>
        /// </summary>
        /// <param name="accounts">A list of usernames and passwords saved for the S3 account</param>
        /// <param name="options">The dictionary into which the settings should be encoded</param>
        public static void EncodeAccounts(Dictionary<string, string> accounts, IDictionary<string, string> options)
        {
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            System.Xml.XmlNode root = doc.AppendChild(doc.CreateElement(XML_ROOT));
            foreach (KeyValuePair<string, string> item in accounts)
            {
                System.Xml.XmlNode acc = root.AppendChild(doc.CreateElement(XML_ACCOUNT));
                acc.AppendChild(doc.CreateElement(XML_ACCOUNT_NAME)).InnerText = item.Key;
                acc.AppendChild(doc.CreateElement(XML_ACCOUNT_CODE)).InnerText = item.Value;
            }

            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                doc.Save(ms);
                options[SAVED_CREDENTIALS] = Convert.ToBase64String(ms.ToArray());
            }
        }

        /// <summary>
        /// Extracts a list of usernames and passwords from the <paramref name="options"/>.
        /// </summary>
        /// <param name="options">The previously save options</param>
        /// <returns>A list of usernames and passwords</returns>
        public static Dictionary<string, string> ExtractAccounts(IDictionary<string, string> options)
        {
            string keyv;
            Dictionary<string, string> accs = new Dictionary<string, string>();

            if (options.TryGetValue(SAVED_CREDENTIALS, out keyv) && !string.IsNullOrEmpty(keyv) && keyv.Trim().Length != 0)
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream(Convert.FromBase64String(keyv)))
                {
                    System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                    doc.Load(ms);
                    foreach (System.Xml.XmlNode n in doc.SelectNodes(XML_ROOT + "/" + XML_ACCOUNT))
                        if (n[XML_ACCOUNT_NAME] != null && n[XML_ACCOUNT_CODE] != null)
                            accs[n[XML_ACCOUNT_NAME].InnerText] = n[XML_ACCOUNT_CODE].InnerText;
                }

            return accs;
        }

        /// <summary>
        /// Extracts a boolean from <paramref name="options"/> that indicates if the user allows saving S3 account information
        /// </summary>
        /// <param name="options">The list of options</param>
        /// <returns>True if the user allows saving account information, false otherwise</returns>
        public static bool ExtractAllowCredentialStorage(IDictionary<string, string> options)
        {
            string keyv;

            options.TryGetValue(ALLOW_SAVED_CREDENTIALS, out keyv);
            return Core.Utility.ParseBool(keyv, true);
        }

        /// <summary>
        /// Extracts a boolean from <paramref name="options"/> that indicates if the user wants the EU buckets option on when creating a new backup
        /// </summary>
        /// <param name="options">The list of options</param>
        /// <returns>True if the user wants the EU buckets option on when creating a new backup, false otherwise</returns>
        public static bool ExtractDefaultEUBuckets(IDictionary<string, string> options)
        {
            string keyv;

            options.TryGetValue(DEFAULT_EU_BUCKET, out keyv);
            return Core.Utility.ParseBool(keyv, false);
        }

        #endregion
    }
}
