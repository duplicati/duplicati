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

        private const string HAS_WARNED_PATH = "UI: Has warned path";
        private const string HAS_TESTED = "UI: Has tested";
        private const string HAS_WARNED_NO_SFTP = "UI: Has warned SFTP";


        //The name of the setting that contains the SFTP path
        private const string APPSET_SFTP_PATH = "SFTP Path";

        private bool m_warnedPath = false;
        private bool m_hasTested = false;
        private bool m_warnedNoSFTP = false;

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

        private void Save()
        {
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
                useUnmanaged = false;

            if (m_options.ContainsKey(HOST))
                Servername.Text = m_options[HOST];
            if (m_options.ContainsKey(FOLDER))
                Path.Text = m_options[FOLDER];
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            UsePassword.Checked = !passwordless;
            if (m_options.ContainsKey(PASSWORD))
                Password.Text = m_options[PASSWORD];
            Port.Value = port;
            GenerateDebugOutput.Checked = debug;
            UseUnmanagedSSH.Checked = useUnmanaged;

            if (!m_options.ContainsKey(HAS_TESTED) || !bool.TryParse(m_options[HAS_TESTED], out m_hasTested))
                m_hasTested = false;
            if (!m_options.ContainsKey(HAS_WARNED_PATH) || !bool.TryParse(m_options[HAS_WARNED_PATH], out m_warnedPath))
                m_warnedPath = false;

            if (Library.Core.Utility.IsClientLinux)
            {
                //sftp is likely present on linux/mac
                m_warnedNoSFTP = true;
            }
            else
            {
                if (!m_options.ContainsKey(HAS_WARNED_NO_SFTP) || !bool.TryParse(m_options[HAS_WARNED_NO_SFTP], out m_warnedNoSFTP))
                    m_warnedNoSFTP = false;
            }
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

                    SSH ssh = new SSH(destination, options);
                    ssh.List();

                    MessageBox.Show(this, Backend.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Backend.FolderMissingException)
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
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show(this, Backend.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (Username.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Backend.CommonStrings.EmptyUsernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Username.Focus(); }
                catch { }

                return false;
            }

            if (Password.Text.Trim().Length <= 0 && UsePassword.Checked)
            {
                MessageBox.Show(this, Backend.CommonStrings.EmptyPasswordError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Password.Focus(); }
                catch { }

                return false;
            }

            if (!UsePassword.Checked && !UseUnmanagedSSH.Checked)
            {
                MessageBox.Show(this, Strings.SSHUI.PasswordRequiredError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UsePassword.Checked = true;
                try { Password.Focus(); }
                catch { }

                return false;

            }

            if (!m_warnedPath && Path.Text.Trim().Length == 0)
            {
                if (MessageBox.Show(this, Backend.CommonStrings.DefaultDirectoryWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return false;
                }
                m_warnedPath = true;
            }

            if (!m_warnedNoSFTP && UseUnmanagedSSH.Checked)
            {
                string sftpPath = "";
                if (m_applicationSettings.ContainsKey(APPSET_SFTP_PATH))
                    sftpPath = m_applicationSettings[APPSET_SFTP_PATH];

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

            if (port != 22)
                commandlineOptions["ssh-options"] += "-P " + port.ToString();

            if (applicationSettings.ContainsKey(APPSET_SFTP_PATH))
                commandlineOptions["sftp-command"] = applicationSettings[APPSET_SFTP_PATH];

            bool debug;
            if (!guiOptions.ContainsKey(DEBUG_ENABLED) || !bool.TryParse(guiOptions[DEBUG_ENABLED], out debug))
                debug = false;

            if (debug)
                commandlineOptions["debug-to-console"] = "";

            bool useUnmanaged;
            if (!guiOptions.ContainsKey(USE_UNMANAGED_SSH) || !bool.TryParse(guiOptions[USE_UNMANAGED_SSH], out useUnmanaged))
                useUnmanaged = false;

            if (useUnmanaged)
                commandlineOptions["use-sftp-application"] = "";

            if (!guiOptions.ContainsKey(HOST))
                throw new Exception(string.Format(Backend.CommonStrings.ConfigurationIsMissingItemError, HOST));

            return "ssh://" + guiOptions[HOST] + "/" + (guiOptions.ContainsKey(FOLDER) ? guiOptions[FOLDER] : "");
        }

        private void UseUnmanagedSSH_CheckedChanged(object sender, EventArgs e)
        {

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

                    MessageBox.Show(this, Backend.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    this.Cursor = c;
                }
            }
        }
    }
}
