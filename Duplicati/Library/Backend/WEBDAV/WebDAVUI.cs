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

        private const string USE_SSL = "Use SSL";
        private const string ACCEPT_ANY_CERTIFICATE = "Accept Any Server Certificate";
        private const string ACCEPT_SPECIFIC_CERTIFICATE = "Accept Specific Server Certificate";

        private const string HAS_WARNED_PASSWORD = "UI: Has warned password";
        private const string HAS_WARNED_USERNAME = "UI: Has warned username";
        private const string HAS_WARNED_PATH = "UI: Has warned path";
        private const string HAS_WARNED_LEADING_SLASH = "UI: Has warned leading slash";
        private const string HAS_WARNED_BACKSLASH = "UI: Has warned backslash";
        private const string HAS_TESTED = "UI: Has tested";
        private const string INITIALPASSWORD = "UI: Temp password";

        private bool m_warnedPassword = false;
        private bool m_warnedUsername = false;
        private bool m_hasTested;
        private bool m_warnedPath;
        private bool m_warnedLeadingSlash;
        private bool m_warnedBackslash;

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        private static System.Text.RegularExpressions.Regex HashRegEx = new System.Text.RegularExpressions.Regex("[^0-9a-fA-F]");

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
                switch (MessageBox.Show(this, Interface.CommonStrings.ConfirmTestConnectionQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
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

            m_options.Remove(INITIALPASSWORD);

            return true;
        }

        void WebDAVUI_PageLoad(object sender, EventArgs args)
        {
            if (m_options.ContainsKey(HOST))
                Servername.Text = m_options[HOST];
            if (m_options.ContainsKey(FOLDER))
                Path.Text = m_options[FOLDER];
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            if (m_options.ContainsKey(PASSWORD))
                Password.Text = m_options[PASSWORD];

            if (!m_options.ContainsKey(INITIALPASSWORD))
                m_options[INITIALPASSWORD] = m_options.ContainsKey(PASSWORD) ? m_options[PASSWORD] : "";
            Password.AskToEnterNewPassword = !string.IsNullOrEmpty(m_options[INITIALPASSWORD]);
            Password.InitialPassword = m_options[INITIALPASSWORD];

            int port;
            if (!m_options.ContainsKey(PORT) || !int.TryParse(m_options[PORT], out port))
                port = 80;

            bool integratedAuth;
            if (!m_options.ContainsKey(INTEGRATED_AUTHENTICATION) || !bool.TryParse(m_options[INTEGRATED_AUTHENTICATION], out integratedAuth))
                integratedAuth = false;

            bool forceDigest;
            if (!m_options.ContainsKey(FORCE_DIGEST_AUTHENTICATION) || !bool.TryParse(m_options[FORCE_DIGEST_AUTHENTICATION], out forceDigest))
                forceDigest = false;

            bool useSSL;
            if (!m_options.ContainsKey(USE_SSL) || !bool.TryParse(m_options[USE_SSL], out useSSL))
                useSSL = false;

            bool acceptAnyCertificate;
            if (!m_options.ContainsKey(ACCEPT_ANY_CERTIFICATE) || !bool.TryParse(m_options[ACCEPT_ANY_CERTIFICATE], out acceptAnyCertificate))
                acceptAnyCertificate = false;

            UseSSL.Checked = useSSL;
            AcceptAnyHash.Checked = acceptAnyCertificate;
            
            if (m_options.ContainsKey(ACCEPT_SPECIFIC_CERTIFICATE))
                SpecifiedHash.Text = m_options[ACCEPT_SPECIFIC_CERTIFICATE];
            AcceptSpecifiedHash.Checked = !string.IsNullOrEmpty(SpecifiedHash.Text);

            Port.Value = port;
            UseIntegratedAuth.Checked = integratedAuth;
            DigestAuth.Checked = forceDigest;

            if (!m_options.ContainsKey(HAS_TESTED) || !bool.TryParse(m_options[HAS_TESTED], out m_hasTested))
                m_hasTested = false;

            if (!m_options.ContainsKey(HAS_WARNED_PATH) || !bool.TryParse(m_options[HAS_WARNED_PATH], out m_warnedPath))
                m_warnedPath = false;

            if (!m_options.ContainsKey(HAS_WARNED_LEADING_SLASH) || !bool.TryParse(m_options[HAS_WARNED_LEADING_SLASH], out m_warnedLeadingSlash))
                m_warnedLeadingSlash = false;

            if (!m_options.ContainsKey(HAS_WARNED_BACKSLASH) || !bool.TryParse(m_options[HAS_WARNED_BACKSLASH], out m_warnedBackslash))
                m_warnedBackslash = false;

            if (!m_options.ContainsKey(HAS_WARNED_USERNAME) || !bool.TryParse(m_options[HAS_WARNED_USERNAME], out m_warnedUsername))
                m_warnedUsername = false;

            if (!m_options.ContainsKey(HAS_WARNED_PASSWORD) || !bool.TryParse(m_options[HAS_WARNED_PASSWORD], out m_warnedPassword))
                m_warnedPassword = false;

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
        }

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Interface.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (!Library.Utility.Utility.IsValidHostname(Servername.Text))
            {
                MessageBox.Show(this, string.Format(Library.Interface.CommonStrings.InvalidServernameError, Servername.Text), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (Path.Text.Trim().Length <= 0 && !m_warnedPath)
            {
                if (MessageBox.Show(this, Strings.WebDAVUI.EmptyFolderPathWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                {
                    try { Path.Focus(); }
                    catch { }

                    return false;
                }

                m_warnedPath = true;
            }


            if (Path.Text.Contains("\\") && !m_warnedBackslash)
            {
                DialogResult res = MessageBox.Show(this, Strings.WebDAVUI.BackslashWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (res == DialogResult.Yes)
                {
                    Path.Text = Path.Text.Replace('\\', '/');
                }
                else if (res == DialogResult.Cancel)
                {
                    return false;
                }
                else
                {
                    m_warnedBackslash = true;
                }
            }

            if (Path.Text.Trim().StartsWith("/") && !m_warnedLeadingSlash)
            {
                DialogResult res = MessageBox.Show(this, Strings.WebDAVUI.LeadingSlashWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (res == DialogResult.Yes)
                {
                    string s = Path.Text.Trim();
                    while(s.StartsWith("/"))
                        s = s.Substring(1);
                    Path.Text = s;
                }
                else if (res == DialogResult.Cancel)
                {
                    return false;
                }
                else
                {
                    m_warnedLeadingSlash = true;
                }
            }

            if (!UseIntegratedAuth.Checked)
            {
                if (Username.Text.Trim().Length <= 0 && !m_warnedUsername)
                {
                    if (MessageBox.Show(this, Interface.CommonStrings.EmptyUsernameWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                    {
                        try { Username.Focus(); }
                        catch { }

                        return false;
                    }

                    m_warnedUsername = true;
                }

                if (Password.Text.Trim().Length <= 0 && !m_warnedPassword)
                {
                    if (MessageBox.Show(this, Interface.CommonStrings.EmptyPasswordWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                    {
                        try { Password.Focus(); }
                        catch { }

                        return false;
                    }
                    m_warnedPassword = true;
                }
            }

            if (UseSSL.Checked && AcceptSpecifiedHash.Checked)
            {
                if (SpecifiedHash.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, Strings.WebDAVUI.EmptyHashError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { SpecifiedHash.Focus(); }
                    catch { }
                    return false;
                }

                if (SpecifiedHash.Text.Length % 2 > 0 || HashRegEx.Match(SpecifiedHash.Text).Success)
                {
                    MessageBox.Show(this, Strings.WebDAVUI.InvalidHashError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { SpecifiedHash.Focus(); }
                    catch { }
                    return false;
                }
            }

            return true;
        }

        private void Save()
        {
            string initialPwd;
            bool hasInitial = m_options.TryGetValue(INITIALPASSWORD, out initialPwd);

            m_options.Clear();
            m_options[HAS_TESTED] = m_hasTested.ToString();
            m_options[HAS_WARNED_PATH] = m_warnedPath.ToString();
            m_options[HAS_WARNED_LEADING_SLASH] = m_warnedLeadingSlash.ToString();
            m_options[HAS_WARNED_BACKSLASH] = m_warnedBackslash.ToString();
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
            
            m_options[USE_SSL] = UseSSL.Checked.ToString();
            m_options[ACCEPT_ANY_CERTIFICATE] = AcceptAnyHash.Checked.ToString();
            if (AcceptSpecifiedHash.Checked)
                m_options[ACCEPT_SPECIFIC_CERTIFICATE] = SpecifiedHash.Text;

            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;
            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                bool retry = true;
                while (retry == true)
                {
                    retry = false;
                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;

                        Save();
                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string destination = GetConfiguration(m_options, options);

                        bool existingBackup = false;
                        using (Duplicati.Library.Modules.Builtin.HttpOptions httpconf = new Duplicati.Library.Modules.Builtin.HttpOptions())
                        {
                            httpconf.Configure(options);
                            using(WEBDAV webDAV = new WEBDAV(destination, options))
                                foreach (Interface.IFileEntry n in webDAV.List())
                                    if (n.Name.StartsWith("duplicati-"))
                                    {
                                        existingBackup = true;
                                        break;
                                    }
                        }

                        bool isUiAdd = string.IsNullOrEmpty(m_uiAction) || string.Equals(m_uiAction, "add", StringComparison.InvariantCultureIgnoreCase);
                        if (existingBackup && isUiAdd)
                        {
                            if (MessageBox.Show(this, string.Format(Interface.CommonStrings.ExistingBackupDetectedQuestion), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                                return;
                        }
                        else
                        {
                            MessageBox.Show(this, Interface.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }

                        m_hasTested = true;
                    }
                    catch (Interface.FolderMissingException)
                    {
                        switch (MessageBox.Show(this, Strings.WebDAVUI.CreateMissingFolderQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                        {
                            case DialogResult.Yes:
                                CreateFolderButton.PerformClick();
                                TestConnection.PerformClick();
                                return;
                            default:
                                return;
                        }
                    }
                    catch (Utility.SslCertificateValidator.InvalidCertificateException cex)
                    {
                        if (string.IsNullOrEmpty(cex.Certificate))
                            MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, cex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        else
                        {
                            if (MessageBox.Show(this, string.Format(Strings.WebDAVUI.ApproveCertificateHashQuestion, cex.SslError), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                            {
                                retry = true;
                                AcceptSpecifiedHash.Checked = true;
                                SpecifiedHash.Text = cex.Certificate;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        this.Cursor = c;
                    }
                }
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
            m_warnedLeadingSlash = false;
            m_warnedBackslash = false;
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
                bool retry = true;
                while (retry == true)
                {
                    retry = false;
                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;
                        Save();

                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string destination = GetConfiguration(m_options, options);

                        using (Duplicati.Library.Modules.Builtin.HttpOptions httpconf = new Duplicati.Library.Modules.Builtin.HttpOptions())
                        {
                            httpconf.Configure(options);
                            WEBDAV webDAV = new WEBDAV(destination, options);
                            webDAV.CreateFolder();
                        }
                        
                        MessageBox.Show(this, Interface.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        m_hasTested = true;
                    }
                    catch (Utility.SslCertificateValidator.InvalidCertificateException cex)
                    {
                        if (string.IsNullOrEmpty(cex.Certificate))
                            MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, cex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        else
                        {
                            if (MessageBox.Show(this, string.Format(Strings.WebDAVUI.ApproveCertificateHashQuestion, cex.SslError), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                            {
                                retry = true;
                                AcceptSpecifiedHash.Checked = true;
                                SpecifiedHash.Text = cex.Certificate;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        this.Cursor = c;
                    }
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

            bool useSSL;
            if (!guiOptions.ContainsKey(USE_SSL) || !bool.TryParse(guiOptions[USE_SSL], out useSSL))
                useSSL = false;

            bool acceptAnyCertificate;
            if (!guiOptions.ContainsKey(ACCEPT_ANY_CERTIFICATE) || !bool.TryParse(guiOptions[ACCEPT_ANY_CERTIFICATE], out acceptAnyCertificate))
                acceptAnyCertificate = false;

            if (useSSL)
                commandlineOptions["use-ssl"] = "";
            if (acceptAnyCertificate)
                commandlineOptions["accept-any-ssl-certificate"] = "";
            if (guiOptions.ContainsKey(ACCEPT_SPECIFIC_CERTIFICATE))
                commandlineOptions["accept-specified-ssl-hash"] = guiOptions[ACCEPT_SPECIFIC_CERTIFICATE];

            if (!guiOptions.ContainsKey(HOST))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, HOST));

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

        private void UseSSL_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            SSLGroup.Enabled = UseSSL.Checked;
            if (Port.Value == 80 || Port.Value == 443)
                Port.Value = UseSSL.Checked ? 443 : 80;
        }

        private void AcceptSpecifiedHash_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            SpecifiedHash.Enabled = AcceptSpecifiedHash.Checked;
        }

        private void SpecifiedHash_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void AcceptAnyHash_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

    }
}
