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
        private const string DEFAULT_RRS = "S3: Default Use RRS";
        private const string DEFAULT_BUCKET_REGION = "S3: Default Bucket Region";
        private const string DEFAULT_SERVERNAME = "S3: Default Servername";

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

            string region = ExtractDefaultBucketRegion(m_settings);
            string server = ExtractDefaultServername(m_settings);
            AllowCredentialStorage.Checked = ExtractAllowCredentialStorage(m_settings);
            UseRRS.Checked = ExtractDefaultRRS(m_settings);

            CredentialList.Items.Clear();
            foreach(KeyValuePair<string, string> k in ExtractAccounts(m_settings))
                CredentialList.Items.Add(new Utility.ComboBoxItemPair<string>(k.Key, k.Value));

            DefaultServername.Items.Clear();
            DefaultBucketRegion.Items.Clear();
            foreach (KeyValuePair<string, string> s in S3.KNOWN_S3_LOCATIONS)
                DefaultBucketRegion.Items.Add(new Utility.ComboBoxItemPair<string>(string.Format("{0} ({1})", s.Key, string.IsNullOrEmpty(s.Value) ? "-none-" : s.Value), s.Value));

            foreach (KeyValuePair<string, string> s in S3.KNOWN_S3_PROVIDERS)
                DefaultServername.Items.Add(new Utility.ComboBoxItemPair<string>(string.Format("{0} ({1})", s.Key, s.Value), s.Value));

            DefaultServername.Text = server;
            DefaultBucketRegion.Text = region;
        }

        private void RemoveAllButton_Click(object sender, EventArgs e)
        {
            CredentialList.Items.Clear();
        }

        private void CredentialList_SelectedIndexChanged(object sender, EventArgs e)
        {
            RemoveSelectedButton.Enabled = CredentialList.SelectedItem as Utility.ComboBoxItemPair<string> != null;
        }

        private void RemoveSelectedButton_Click(object sender, EventArgs e)
        {
            if (CredentialList.SelectedItem as Utility.ComboBoxItemPair<string> != null)
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
                if (DefaultServername.SelectedItem as Utility.ComboBoxItemPair<string> == null)
                    m_settings[DEFAULT_SERVERNAME] = DefaultServername.Text;
                else
                    m_settings[DEFAULT_SERVERNAME] = (DefaultServername.SelectedItem as Utility.ComboBoxItemPair<string>).Value;

                if (DefaultBucketRegion.SelectedItem as Utility.ComboBoxItemPair<string> == null)
                    m_settings[DEFAULT_BUCKET_REGION] = DefaultBucketRegion.Text;
                else
                    m_settings[DEFAULT_BUCKET_REGION] = (DefaultBucketRegion.SelectedItem as Utility.ComboBoxItemPair<string>).Value;

                m_settings[DEFAULT_RRS] = UseRRS.Checked.ToString();

                Dictionary<string, string> tmp = new Dictionary<string, string>();
                foreach (Utility.ComboBoxItemPair<string> item in CredentialList.Items)
                    tmp[item.ToString()] = item.Value;

                EncodeAccounts(tmp, m_settings);
            }

            return true;
        }

        public static string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(SAVED_CREDENTIALS))
                applicationSettings[SAVED_CREDENTIALS] = guiOptions[SAVED_CREDENTIALS];

            if (guiOptions.ContainsKey(DEFAULT_BUCKET_REGION))
                applicationSettings[DEFAULT_BUCKET_REGION] = guiOptions[DEFAULT_BUCKET_REGION];
            else if (guiOptions.ContainsKey(DEFAULT_EU_BUCKET))
                applicationSettings[DEFAULT_EU_BUCKET] = guiOptions[DEFAULT_EU_BUCKET];

            if (guiOptions.ContainsKey(DEFAULT_SERVERNAME))
                applicationSettings[DEFAULT_SERVERNAME] = guiOptions[DEFAULT_SERVERNAME];

            if (guiOptions.ContainsKey(DEFAULT_EU_BUCKET))
                applicationSettings[DEFAULT_EU_BUCKET] = guiOptions[DEFAULT_EU_BUCKET];

            if (guiOptions.ContainsKey(DEFAULT_RRS))
                applicationSettings[DEFAULT_RRS] = guiOptions[DEFAULT_RRS];

            if (guiOptions.ContainsKey(ALLOW_SAVED_CREDENTIALS))
                applicationSettings[ALLOW_SAVED_CREDENTIALS] = guiOptions[ALLOW_SAVED_CREDENTIALS];

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
            return Utility.Utility.ParseBool(keyv, true);
        }

        /// <summary>
        /// Extracts a boolean from <paramref name="options"/> that indicates if the user wants the EU buckets option on when creating a new backup.
        /// DEPRECATED
        /// </summary>
        /// <param name="options">The list of options</param>
        /// <returns>True if the user wants the EU buckets option on when creating a new backup, false otherwise</returns>
        private static bool Old_ExtractDefaultEUBuckets(IDictionary<string, string> options)
        {
            string keyv;

            options.TryGetValue(DEFAULT_EU_BUCKET, out keyv);
            return Utility.Utility.ParseBool(keyv, false);
        }

        /// <summary>
        /// Extracts a string from <paramref name="options"/> that indicates the default region to use for creating buckets.
        /// </summary>
        /// <param name="options">The list of options</param>
        /// <returns>Null if the default bucket location should be used, otherwise the bucket location identifier</returns>
        public static string ExtractDefaultBucketRegion(IDictionary<string, string> options)
        {
            string keyv;
            if (options.TryGetValue(DEFAULT_BUCKET_REGION, out keyv))
                return keyv;
            else
                return Old_ExtractDefaultEUBuckets(options) ? S3.S3_EU_REGION_NAME : null;
        }

        /// <summary>
        /// Extracts a string from <paramref name="options"/> that indicates the default server to use.
        /// </summary>
        /// <param name="options">The list of options</param>
        /// <returns>Null if the default server should be used, otherwise the server name identifier</returns>
        public static string ExtractDefaultServername(IDictionary<string, string> options)
        {
            string keyv;
            options.TryGetValue(DEFAULT_SERVERNAME, out keyv);
            if (string.IsNullOrEmpty(keyv))
                return null;
            else
                return keyv;
        }

        /// <summary>
        /// Extracts a boolean from <paramref name="options"/> that indicates if the user wants the RRS option on when creating a new backup
        /// </summary>
        /// <param name="options">The list of options</param>
        /// <returns>True if the user wants the RRS option on when creating a new backup, false otherwise</returns>
        public static bool ExtractDefaultRRS(IDictionary<string, string> options)
        {
            string keyv;

            options.TryGetValue(DEFAULT_RRS, out keyv);
            return Utility.Utility.ParseBool(keyv, false);
        }

        #endregion

        private delegate void SetComboTextDelegate(ComboBox el, string text);
        private void SetComboText(ComboBox el, string text)
        {
            el.Text = text;
        }

        private void DefaultServername_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (DefaultServername.SelectedItem as Utility.ComboBoxItemPair<string> != null)
                BeginInvoke(new SetComboTextDelegate(SetComboText), DefaultServername, (DefaultServername.SelectedItem as Utility.ComboBoxItemPair<string>).Value);
        }

        private void DefaultBucketRegion_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (DefaultBucketRegion.SelectedItem as Utility.ComboBoxItemPair<string> != null)
                BeginInvoke(new SetComboTextDelegate(SetComboText), DefaultBucketRegion, (DefaultBucketRegion.SelectedItem as Utility.ComboBoxItemPair<string>).Value);
        }
    }
}
