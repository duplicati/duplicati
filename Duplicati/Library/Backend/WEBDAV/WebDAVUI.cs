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
    public partial class WebDAVUI : UserControl
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";
        private const string INTEGRATED_AUTHENTICATION = "Integrated Authentication";
        private const string FORCE_DIGEST_AUTHENTICATION = "Force Digest Authentication";

        private const string HAS_WARNED_PASSWORD = "UI: Has warned password";
        private const string HAS_WARNED_USERNAME = "UI: Has warned username";
        private const string HAS_WARNED_PATH = "UI: Has warned path";
        private const string HAS_TESTED = "UI: Has tested";

        private bool m_warnedPassword = false;
        private bool m_warnedUsername = false;
        private bool m_hasTested;
        private bool m_warnedPath;

        private IDictionary<string, string> m_options;

        public WebDAVUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
        }

        private WebDAVUI()
        {
            InitializeComponent();
        }

        internal bool Save(bool validate)
        {
            Save();

            if (!validate)
                return true;

            if (!ValidateForm())
                return false;

            if (!m_hasTested)
                switch (MessageBox.Show(this, Backend.CommonStrings.ConfirmTestConnectionQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        TestConnection_Click(null, null);
                        if (!m_hasTested)
                            return false;
                        break;
                    case DialogResult.No:
                        break;
                    default: //Cancel
                        return false;
                }

            Save();
            return true;
        }

        void WebDAVUI_PageLoad(object sender, EventArgs args)
        {
            if (!m_options.ContainsKey(HAS_TESTED) || !bool.TryParse(m_options[HAS_TESTED], out m_hasTested))
                m_hasTested = false;

            if (!m_options.ContainsKey(HAS_WARNED_PATH) || !bool.TryParse(m_options[HAS_WARNED_PATH], out m_warnedPath))
                m_warnedPath = false;

            if (!m_options.ContainsKey(HAS_WARNED_USERNAME) || !bool.TryParse(m_options[HAS_WARNED_USERNAME], out m_warnedUsername))
                m_warnedUsername = false;

            if (!m_options.ContainsKey(HAS_WARNED_PASSWORD) || !bool.TryParse(m_options[HAS_WARNED_PASSWORD], out m_warnedPassword))
                m_warnedPassword = false;

            if (m_options.ContainsKey(HOST))
                Servername.Text = m_options[HOST];
            if (m_options.ContainsKey(FOLDER))
                Path.Text = m_options[FOLDER];
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            if (m_options.ContainsKey(PASSWORD))
                Password.Text = m_options[PASSWORD];
            
            int port;
            if (!m_options.ContainsKey(PORT) || !int.TryParse(m_options[PORT], out port))
                port = 80;

            bool integratedAuth;
            if (!m_options.ContainsKey(INTEGRATED_AUTHENTICATION) || !bool.TryParse(m_options[INTEGRATED_AUTHENTICATION], out integratedAuth))
                integratedAuth = false;

            bool forceDigest;
            if (!m_options.ContainsKey(FORCE_DIGEST_AUTHENTICATION) || !bool.TryParse(m_options[FORCE_DIGEST_AUTHENTICATION], out forceDigest))
                forceDigest = false;

            Port.Value = port;
            UseIntegratedAuth.Checked = integratedAuth;
            DigestAuth.Checked = forceDigest;
        }

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Backend.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (!UseIntegratedAuth.Checked)
            {
                if (Username.Text.Trim().Length <= 0 && !m_warnedUsername)
                {
                    if (MessageBox.Show(this, Backend.CommonStrings.EmptyUsernameWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                    {
                        try { Username.Focus(); }
                        catch { }

                        return false;
                    }

                    m_warnedUsername = true;
                }

                if (Password.Text.Trim().Length <= 0 && !m_warnedPassword)
                {
                    if (MessageBox.Show(this, Backend.CommonStrings.EmptyPasswordWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                    {
                        try { Password.Focus(); }
                        catch { }

                        return false;
                    }
                    m_warnedPassword = true;
                }
            }

            return true;
        }

        private void Save()
        {
            m_options.Clear();
            m_options[HAS_TESTED] = m_hasTested.ToString();
            m_options[HAS_WARNED_PATH] = m_warnedPath.ToString();
            m_options[HAS_WARNED_USERNAME] = m_warnedUsername.ToString();
            m_options[HAS_WARNED_PASSWORD] = m_warnedPassword.ToString();

            m_options[HOST] = Servername.Text;
            m_options[FOLDER] = Path.Text;
            m_options[INTEGRATED_AUTHENTICATION] = UseIntegratedAuth.Checked.ToString();
            if (UseIntegratedAuth.Checked)
            {
                m_options[USERNAME] = "";
                m_options[PASSWORD] = "";
            }
            else
            {
                m_options[USERNAME] = Username.Text;
                m_options[PASSWORD] = Password.Text;
            }
            m_options[PORT] = ((int)Port.Value).ToString();
            m_options[FORCE_DIGEST_AUTHENTICATION] = DigestAuth.Checked.ToString();
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;

                    Save();
                    Dictionary<string, string> options = new Dictionary<string, string>();
                    string destination = GetConfiguration(m_options, options);

                    WEBDAV webDAV = new WEBDAV(destination, options);
                    webDAV.List();

                    MessageBox.Show(this, Backend.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                this.Cursor = c;
            }
        }

        private void Port_ValueChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Servername_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPath = false;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedUsername = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPassword = false;
        }

        private void UseIntegratedAuth_CheckedChanged(object sender, EventArgs e)
        {
            PasswordSettings.Enabled = !UseIntegratedAuth.Checked;
            m_hasTested = false;
        }

        private void CreateFolderButton_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                try
                {
                    string url = "http://" + Servername.Text + ":" + Port.Value.ToString() + "/" + Path.Text;
                    System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url);
                    if (UseIntegratedAuth.Checked)
                        req.UseDefaultCredentials = true;
                    else if (DigestAuth.Checked)
                    {
                        System.Net.CredentialCache cred = new System.Net.CredentialCache();
                        cred.Add(new Uri(url), "Digest", new System.Net.NetworkCredential(Username.Text, Password.Text));
                        req.Credentials = cred;
                    }
                    else
                        req.Credentials = new System.Net.NetworkCredential(Username.Text, Password.Text);

                    req.Method = System.Net.WebRequestMethods.Http.MkCol;
                    req.KeepAlive = false;
                    using (req.GetResponse())
                    { }

                    MessageBox.Show(this, Backend.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DigestAuth_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        internal static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["ftp-username"] = guiOptions[USERNAME];

            if (guiOptions.ContainsKey(PASSWORD) && !string.IsNullOrEmpty(guiOptions[PASSWORD]))
                commandlineOptions["ftp-password"] = guiOptions[PASSWORD];

            int port;
            if (!guiOptions.ContainsKey(PORT) || !int.TryParse(guiOptions[PORT], out port))
                port = 80;

            bool integratedAuth;
            if (!guiOptions.ContainsKey(INTEGRATED_AUTHENTICATION) || !bool.TryParse(guiOptions[INTEGRATED_AUTHENTICATION], out integratedAuth))
                integratedAuth = false;

            bool forceDigest;
            if (!guiOptions.ContainsKey(FORCE_DIGEST_AUTHENTICATION) || !bool.TryParse(guiOptions[FORCE_DIGEST_AUTHENTICATION], out forceDigest))
                forceDigest = false;

            if (integratedAuth)
                commandlineOptions["integrated-authentication"] = "";
            if (forceDigest)
                commandlineOptions["force-digest-authentication"] = "";

            if (!guiOptions.ContainsKey(HOST))
                throw new Exception(string.Format(Backend.CommonStrings.ConfigurationIsMissingItemError, HOST));

            return "webdav://" + guiOptions[HOST] + ":" + port.ToString() + "/" + (guiOptions.ContainsKey(FOLDER) ? guiOptions[FOLDER] : "");
        }

        public static string PageTitle
        {
            get { return Strings.WebDAVUI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.WebDAVUI.PageDescription; }
        }

    }
}
