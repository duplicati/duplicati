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
    public partial class SSHv2UI : UserControl
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";
        private const string DEBUG_ENABLED = "Debug enabled";
        private const string SSH_KEYFILE = "Keyfile";

        private const string HAS_WARNED_PATH = "UI: Has warned path";
        private const string HAS_TESTED = "UI: Has tested";
        private const string INITIALPASSWORD = "UI: Temp password";

        private bool m_warnedPath = false;
        private bool m_hasTested = false;

        private IDictionary<string, string> m_options;
        private IDictionary<string, string> m_applicationSettings;

        public SSHv2UI(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
            : this()
        {
            m_applicationSettings = applicationSettings;
            m_options = options;
        }

        private SSHv2UI()
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

        private void Save()
        {
            string initialPwd;
            bool hasInitial = m_options.TryGetValue(INITIALPASSWORD, out initialPwd);

            m_options.Clear();
            m_options[HAS_TESTED] = m_hasTested.ToString();
            m_options[HAS_WARNED_PATH] = m_warnedPath.ToString();

            m_options[PASWORDLESS]= (!UsePassword.Checked).ToString();
            m_options[PASSWORD] = UsePassword.Checked ? Password.Text : "";
            m_options[PORT] = ((int)Port.Value).ToString();
            m_options[HOST] = Servername.Text;
            m_options[FOLDER] = Path.Text;
            m_options[USERNAME] = Username.Text;
            m_options[DEBUG_ENABLED] = GenerateDebugOutput.Checked.ToString();
            m_options[SSH_KEYFILE] = Keyfile.Text;
            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;
        }

        void SSHUI_PageLoad(object sender, EventArgs args)
        {
            bool passwordless;
            bool debug;
            int port;

            if (!m_options.ContainsKey(PASWORDLESS) || !bool.TryParse(m_options[PASWORDLESS], out passwordless))
                passwordless = false;
            if (!m_options.ContainsKey(PORT) || !int.TryParse(m_options[PORT], out port) || port < 0)
                port = 22;
            if (!m_options.ContainsKey(DEBUG_ENABLED) || !bool.TryParse(m_options[DEBUG_ENABLED], out debug))
                debug = false;

            if (m_options.ContainsKey(HOST))
                Servername.Text = m_options[HOST];
            if (m_options.ContainsKey(FOLDER))
                Path.Text = m_options[FOLDER];
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            UsePassword.Checked = !passwordless;
            if (m_options.ContainsKey(PASSWORD))
                Password.Text = m_options[PASSWORD];
            if (m_options.ContainsKey(SSH_KEYFILE))
                Keyfile.Text = m_options[SSH_KEYFILE];

            if (!m_options.ContainsKey(INITIALPASSWORD))
                m_options[INITIALPASSWORD] = m_options.ContainsKey(PASSWORD) ? m_options[PASSWORD] : "";
            Password.AskToEnterNewPassword = !string.IsNullOrEmpty(m_options[INITIALPASSWORD]);
            Password.InitialPassword = m_options[INITIALPASSWORD];

            Port.Value = port;
            GenerateDebugOutput.Checked = debug;

            if (!m_options.ContainsKey(HAS_TESTED) || !bool.TryParse(m_options[HAS_TESTED], out m_hasTested))
                m_hasTested = false;
            if (!m_options.ContainsKey(HAS_WARNED_PATH) || !bool.TryParse(m_options[HAS_WARNED_PATH], out m_warnedPath))
                m_warnedPath = false;
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
                    string destination = GetConfiguration(m_applicationSettings, m_options, options);

                    options["debug-to-console"] = "";

                    SSHv2 ssh = new SSHv2(destination, options);
                    ssh.List();

                    MessageBox.Show(this, Interface.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Interface.FolderMissingException)
                {
                    switch (MessageBox.Show(this, Strings.SSHv2UI.CreateMissingFolderQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                    {
                        case DialogResult.Yes:
                            CreateFolderButton.PerformClick();
                            TestConnection.PerformClick();
                            return;
                        default:
                            return;
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

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Interface.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (Username.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Interface.CommonStrings.EmptyUsernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Username.Focus(); }
                catch { }

                return false;
            }

            if (Password.Text.Trim().Length <= 0 && UsePassword.Checked)
            {
                if (Keyfile.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, Interface.CommonStrings.EmptyPasswordError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { Password.Focus(); }
                    catch { }

                    return false;
                }
            }

            if (!UsePassword.Checked && Keyfile.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, Strings.SSHv2UI.PasswordRequiredManagedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UsePassword.Checked = true;
                try { Password.Focus(); }
                catch { }

                return false;
            }

            if (!m_warnedPath && Path.Text.Trim().Length == 0)
            {
                if (MessageBox.Show(this, Interface.CommonStrings.DefaultDirectoryWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return false;
                }
                m_warnedPath = true;
            }

            if (!string.IsNullOrEmpty(Keyfile.Text))
            {
                try
                {
                    SSHv2.ValidateKeyFile(Keyfile.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                    try { Keyfile.Focus(); }
                    catch { }

                    return false;
                }
            }

            if (UsePassword.Checked && !Password.VerifyPasswordIfChanged())
                return false;

            return true;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_warnedPath = false;
            m_hasTested = false;
        }

        private void Servername_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Port_ValueChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void UsePassword_CheckedChanged(object sender, EventArgs e)
        {
            Password.Enabled = UsePassword.Checked;
            m_hasTested = false;
        }

        public static string PageTitle
        {
            get { return Strings.SSHv2UI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.SSHv2UI.PageDescription; }
        }

        public static string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["ftp-username"] = guiOptions[USERNAME];

            bool passwordless;
            if (!guiOptions.ContainsKey(PASWORDLESS) || !bool.TryParse(guiOptions[PASWORDLESS], out passwordless))
                passwordless = false;

            if (!passwordless && guiOptions.ContainsKey(PASSWORD) && !string.IsNullOrEmpty(guiOptions[PASSWORD]))
                commandlineOptions["ftp-password"] = guiOptions[PASSWORD];

            if (!commandlineOptions.ContainsKey("ssh-options"))
                commandlineOptions["ssh-options"] = "";

            int port;
            if (!guiOptions.ContainsKey(PORT) || !int.TryParse(guiOptions[PORT], out port))
                port = 22;

            bool debug;
            if (!guiOptions.ContainsKey(DEBUG_ENABLED) || !bool.TryParse(guiOptions[DEBUG_ENABLED], out debug))
                debug = false;

            if (debug)
                commandlineOptions["debug-to-console"] = "";

            string keyfile;
            guiOptions.TryGetValue(SSH_KEYFILE, out keyfile);

            if ((keyfile ?? "").Trim().Length > 0)
                commandlineOptions[SSHv2.SSH_KEYFILE_OPTION] = guiOptions[SSH_KEYFILE];

            if (!guiOptions.ContainsKey(HOST))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, HOST));

            return "ssh://" + guiOptions[HOST] + ":" + port.ToString() + "/" + (guiOptions.ContainsKey(FOLDER) ? guiOptions[FOLDER] : "");
        }

        private void CreateFolderButton_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    Save();

                    Dictionary<string, string> options = new Dictionary<string, string>();
                    string destination = GetConfiguration(m_applicationSettings, m_options, options);

                    options["debug-to-console"] = "";

                    SSHv2 ssh = new SSHv2(destination, options);
                    ssh.CreateFolder();

                    MessageBox.Show(this, Interface.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
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

        private void BrowseForKeyFileButton_Click(object sender, EventArgs e)
        {
            if (OpenSSHKeyFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    SSHv2.ValidateKeyFile(OpenSSHKeyFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Keyfile.Text = OpenSSHKeyFileDialog.FileName;
            }
        }

        private void Keyfile_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }
    }
}
