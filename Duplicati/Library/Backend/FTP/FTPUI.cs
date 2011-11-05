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
    public partial class FTPUI : UserControl
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";
        private const string PASSIVE = "Passive";

        private const string USE_SSL = "Use SSL";
        private const string ACCEPT_ANY_CERTIFICATE = "Accept Any Server Certificate";
        private const string ACCEPT_SPECIFIC_CERTIFICATE = "Accept Specific Server Certificate";

        private const string HASTESTED = "UI: HasTested";
        private const string HASWARNEDPATH = "UI: HasWarnedPath";
        private const string HASWARNEDUSERNAME = "UI: HasWarnedUsername";
        private const string HASWARNEDPASSWORD = "UI: HasWarnedPassword";
        private const string INITIALPASSWORD = "UI: Temp password";

        private bool m_warnedPassword = false; 
        private bool m_warnedUsername = false;
        private bool m_hasTested;
        private bool m_warnedPath;

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        private IDictionary<string, string> m_options;

        private static System.Text.RegularExpressions.Regex HashRegEx = new System.Text.RegularExpressions.Regex("[^0-9a-fA-F]");

        private FTPUI()
        {
            InitializeComponent();
        }

        public FTPUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
        }

        internal bool Save(bool validate)
        {
            SaveSettings();

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
                            return false ;
                        break;
                    case DialogResult.No:
                        break;
                    default: //Cancel
                        return false;
                }

            SaveSettings();

            m_options.Remove(INITIALPASSWORD);

            return true;
        }

        void FTPUI_Load(object sender, EventArgs args)
        {
            LoadSettings();
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

            if (UseSSL.Checked && AcceptSpecifiedHash.Checked)
            {
                if (SpecifiedHash.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, Strings.FTPUI.EmptyHashError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { SpecifiedHash.Focus(); }
                    catch { }
                    return false;
                }

                if (SpecifiedHash.Text.Length % 2 > 0 || HashRegEx.Match(SpecifiedHash.Text).Success)
                {
                    MessageBox.Show(this, Strings.FTPUI.InvalidHashError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { SpecifiedHash.Focus(); }
                    catch { }
                    return false;
                }
            }

            return true;
        }

        private void SaveSettings()
        {
            string initialPwd;
            bool hasInitial = m_options.TryGetValue(INITIALPASSWORD, out initialPwd);

            m_options.Clear();
            m_options[HASTESTED] = m_hasTested.ToString();
            m_options[HASWARNEDPATH] = m_warnedPath.ToString();
            m_options[HASWARNEDUSERNAME] = m_warnedUsername.ToString();
            m_options[HASWARNEDPASSWORD] = m_warnedPassword.ToString();
            m_options[HOST] = Servername.Text;
            m_options[FOLDER] = Path.Text;
            m_options[USERNAME] = Username.Text;
            m_options[PASSWORD] = Password.Text;
            m_options[PORT] = ((int)Port.Value).ToString();
            m_options[PASSIVE] = PassiveConnection.Checked.ToString();
            m_options[USE_SSL] = UseSSL.Checked.ToString();
            m_options[ACCEPT_ANY_CERTIFICATE] = AcceptAnyHash.Checked.ToString();
            if (AcceptSpecifiedHash.Checked)
                m_options[ACCEPT_SPECIFIC_CERTIFICATE] = SpecifiedHash.Text;
            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;
            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);
        }

        private void LoadSettings()
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

            bool b;
            if (!m_options.ContainsKey(PASSIVE) || !bool.TryParse(m_options[PASSIVE], out b))
                b = false;

            PassiveConnection.Checked = b;

            int i; ;
            if (!m_options.ContainsKey(PORT) || !int.TryParse(m_options[PORT], out i))
                i = 21;

            Port.Value = i;

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

            //Set internal testing flags
            if (!m_options.ContainsKey(HASTESTED) || !bool.TryParse(m_options[HASTESTED], out m_hasTested))
                m_hasTested = false;
            if (!m_options.ContainsKey(HASWARNEDPATH) || !bool.TryParse(m_options[HASWARNEDPATH], out m_warnedPath))
                m_warnedPath = false;
            if (!m_options.ContainsKey(HASWARNEDUSERNAME) || !bool.TryParse(m_options[HASWARNEDUSERNAME], out m_warnedUsername))
                m_warnedUsername = false;
            if (!m_options.ContainsKey(HASWARNEDPASSWORD) || !bool.TryParse(m_options[HASWARNEDPASSWORD], out m_warnedPassword))
                m_warnedPassword = false;

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                bool retry = true;
                while (retry)
                {
                    retry = false;

                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;
                        SaveSettings();

                        bool existingBackup = false;

                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string hostname = GetConfiguration(m_options, options);
                        using (Duplicati.Library.Modules.Builtin.HttpOptions httpconf = new Duplicati.Library.Modules.Builtin.HttpOptions())
                        {
                            httpconf.Configure(options);
                            using(FTP f = new FTP(hostname, options))
                                foreach (Interface.IFileEntry n in f.List())
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
                        switch (MessageBox.Show(this, Strings.FTPUI.CreateMissingFolderQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
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
                            if (MessageBox.Show(this, string.Format(Strings.FTPUI.ApproveCertificateHashQuestion, cex.SslError), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
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

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPassword = false;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedUsername = false;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPath = false;
        }

        private void Servername_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void PassiveConnection_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void CreateFolderButton_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                bool retry = true;
                while (retry)
                {
                    retry = false;
                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;
                        SaveSettings();

                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string hostname = GetConfiguration(m_options, options);
                        using (Duplicati.Library.Modules.Builtin.HttpOptions httpconf = new Duplicati.Library.Modules.Builtin.HttpOptions())
                        {
                            httpconf.Configure(options);
                            FTP f = new FTP(hostname, options);
                            f.CreateFolder();
                        }

                        MessageBox.Show(this, Interface.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                    }
                    catch (Utility.SslCertificateValidator.InvalidCertificateException cex)
                    {
                        if (string.IsNullOrEmpty(cex.Certificate))
                            MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, cex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        else
                        {
                            if (MessageBox.Show(this, string.Format(Strings.FTPUI.ApproveCertificateHashQuestion, cex.SslError), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
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

        public static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["ftp-username"] = guiOptions[USERNAME];
            if (guiOptions.ContainsKey(PASSWORD) && !string.IsNullOrEmpty(guiOptions[PASSWORD]))
                commandlineOptions["ftp-password"] = guiOptions[PASSWORD];

            bool passive;
            if (!guiOptions.ContainsKey(PASSIVE) || !bool.TryParse(guiOptions[PASSIVE], out passive))
                passive = false;

            if (passive)
                commandlineOptions["ftp-passive"] = "";
            else
                commandlineOptions["ftp-regular"] = "";

            int port;
            if (!guiOptions.ContainsKey(PORT) || !int.TryParse(guiOptions[PORT], out port) || port < 0)
                port = 21;

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
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, FOLDER));

            return "ftp://" + guiOptions[HOST] + ":" + port.ToString() + "/" + (guiOptions.ContainsKey(FOLDER) ? guiOptions[FOLDER] : "");
        }

        public static string PageTitle
        {
            get { return Strings.FTPUI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.FTPUI.PageDescription; }
        }

        private void UseSSL_CheckedChanged(object sender, EventArgs e)
        {
            SSLGroup.Enabled = UseSSL.Checked;
            m_hasTested = false;
        }

        private void AcceptSpecifiedHash_CheckedChanged(object sender, EventArgs e)
        {
            SpecifiedHash.Enabled = AcceptSpecifiedHash.Checked;
            m_hasTested = false;
        }

        private void AcceptAnyHash_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }
    }
}
