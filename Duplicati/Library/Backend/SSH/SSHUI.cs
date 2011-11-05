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
    public partial class SSHUI : UserControl
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";
        private const string DEBUG_ENABLED = "Debug enabled";
        private const string USE_UNMANAGED_SSH = "Use Unmanaged SSH";
        private const string SSH_KEYFILE = "Keyfile";

        private const string HAS_WARNED_PATH = "UI: Has warned path";
        private const string HAS_TESTED = "UI: Has tested";
        private const string HAS_WARNED_NO_SFTP = "UI: Has warned SFTP";
        private const string INITIALPASSWORD = "UI: Temp password";

        private bool m_warnedPath = false;
        private bool m_hasTested = false;
        private bool m_warnedNoSFTP = false;

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        private IDictionary<string, string> m_options;
        private IDictionary<string, string> m_applicationSettings;

        public SSHUI(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
            : this()
        {
            m_applicationSettings = applicationSettings;
            m_options = options;
        }

        private SSHUI()
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
            m_options[HAS_WARNED_NO_SFTP] = m_warnedNoSFTP.ToString();

            m_options[PASWORDLESS]= (!UsePassword.Checked).ToString();
            m_options[PASSWORD] = UsePassword.Checked ? Password.Text : "";
            m_options[PORT] = ((int)Port.Value).ToString();
            m_options[HOST] = Servername.Text;
            m_options[FOLDER] = Path.Text;
            m_options[USERNAME] = Username.Text;
            m_options[DEBUG_ENABLED] = GenerateDebugOutput.Checked.ToString();
            m_options[USE_UNMANAGED_SSH] = UseUnmanagedSSH.Checked.ToString();
            m_options[SSH_KEYFILE] = Keyfile.Text;
            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;
            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);
        }

        void SSHUI_PageLoad(object sender, EventArgs args)
        {
            bool passwordless;
            bool debug;
            bool useUnmanaged;
            int port;

            if (!m_options.ContainsKey(PASWORDLESS) || !bool.TryParse(m_options[PASWORDLESS], out passwordless))
                passwordless = false;
            if (!m_options.ContainsKey(PORT) || !int.TryParse(m_options[PORT], out port) || port < 0)
                port = 22;
            if (!m_options.ContainsKey(DEBUG_ENABLED) || !bool.TryParse(m_options[DEBUG_ENABLED], out debug))
                debug = false;

            if (!m_options.ContainsKey(USE_UNMANAGED_SSH) || !bool.TryParse(m_options[USE_UNMANAGED_SSH], out useUnmanaged))
            {
                useUnmanaged = false;
                if (m_applicationSettings.ContainsKey(SSHCommonOptions.DEFAULT_MANAGED))
                    useUnmanaged = !Utility.Utility.ParseBool(m_applicationSettings[SSHCommonOptions.DEFAULT_MANAGED], true);
            }

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
            UseUnmanagedSSH.Checked = useUnmanaged;

            if (!m_options.ContainsKey(HAS_TESTED) || !bool.TryParse(m_options[HAS_TESTED], out m_hasTested))
                m_hasTested = false;
            if (!m_options.ContainsKey(HAS_WARNED_PATH) || !bool.TryParse(m_options[HAS_WARNED_PATH], out m_warnedPath))
                m_warnedPath = false;

            if (Library.Utility.Utility.IsClientLinux)
            {
                //sftp is likely present on linux/mac
                m_warnedNoSFTP = true;
            }
            else
            {
                if (!m_options.ContainsKey(HAS_WARNED_NO_SFTP) || !bool.TryParse(m_options[HAS_WARNED_NO_SFTP], out m_warnedNoSFTP))
                    m_warnedNoSFTP = false;
            }

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
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

                    bool existingBackup = false;
                    using (SSH ssh = new SSH(destination, options))
                    {
                        foreach (Interface.IFileEntry n in ssh.List())
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
                    switch (MessageBox.Show(this, Strings.SSHUI.CreateMissingFolderQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
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

            if (!Library.Utility.Utility.IsValidHostname(Servername.Text))
            {
                MessageBox.Show(this, string.Format(Library.Interface.CommonStrings.InvalidServernameError, Servername.Text), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                if (!UseUnmanagedSSH.Checked && Keyfile.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, Interface.CommonStrings.EmptyPasswordError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { Password.Focus(); }
                    catch { }

                    return false;
                }
            }

            if (!UsePassword.Checked && !UseUnmanagedSSH.Checked && Keyfile.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, Strings.SSHUI.PasswordRequiredManagedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            if (!m_warnedNoSFTP && UseUnmanagedSSH.Checked)
            {
                string sftpPath = "";
                if (m_applicationSettings.ContainsKey(SSHCommonOptions.SFTP_PATH))
                    sftpPath = m_applicationSettings[SSHCommonOptions.SFTP_PATH];

                System.IO.FileInfo fi = null;
                try { fi = new System.IO.FileInfo(System.Environment.ExpandEnvironmentVariables(sftpPath)); }
                catch { }

                if (fi == null || !fi.Exists)
                {
                    if (MessageBox.Show(this, Strings.SSHUI.MissingSCPWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return false;
                    }

                    m_warnedNoSFTP = true;
                }
            }

            if (!UseUnmanagedSSH.Checked && !string.IsNullOrEmpty(Keyfile.Text))
            {
                try
                {
                    SSH.ValidateKeyFile(Keyfile.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                    try { Keyfile.Focus(); }
                    catch { }

                    return false;
                }
            }

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
            get { return Strings.SSHUI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.SSHUI.PageDescription; }
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

            if (applicationSettings.ContainsKey(SSHCommonOptions.SFTP_PATH))
                commandlineOptions[SSH.SFTP_PATH_OPTION] = applicationSettings[SSHCommonOptions.SFTP_PATH];

            bool debug;
            if (!guiOptions.ContainsKey(DEBUG_ENABLED) || !bool.TryParse(guiOptions[DEBUG_ENABLED], out debug))
                debug = false;

            if (debug)
                commandlineOptions["debug-to-console"] = "";

            bool useUnmanaged = guiOptions.ContainsKey(USE_UNMANAGED_SSH) ? Utility.Utility.ParseBool(guiOptions[USE_UNMANAGED_SSH], false) : false;

            if (useUnmanaged)
            {
                commandlineOptions[SSH.USE_UNMANAGED_OPTION] = "";
                commandlineOptions["disable-streaming-transfers"] = "";
            }
            else
            {
                string keyfile;
                guiOptions.TryGetValue(SSH_KEYFILE, out keyfile);

                if ((keyfile ?? "").Trim().Length > 0)
                    commandlineOptions[SSH.SSH_KEYFILE_OPTION] = guiOptions[SSH_KEYFILE];
            }

            if (!guiOptions.ContainsKey(HOST))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, HOST));

            return "ssh://" + guiOptions[HOST] + ":" + port.ToString() + "/" + (guiOptions.ContainsKey(FOLDER) ? guiOptions[FOLDER] : "");
        }

        private void UseUnmanagedSSH_CheckedChanged(object sender, EventArgs e)
        {
            Keyfilelabel.Enabled =
            Keyfile.Enabled =
            BrowseForKeyFileButton.Enabled =
                !UseUnmanagedSSH.Checked;
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

                    SSH ssh = new SSH(destination, options);
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
                    SSH.ValidateKeyFile(OpenSSHKeyFileDialog.FileName);
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
