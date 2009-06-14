#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Backends.SSH
{
    public partial class SSHOptions : WizardControl
    {
        private bool m_warnedPath = false;
        private bool m_hasTested = false;
        private bool m_warnedNoSFTP = false;

        private SSHSettings m_wrapper;

        public SSHOptions()
            : base("Backup storage options", "On this page you can select where to store the backup data.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(SSHOptions_PageEnter);
            base.PageLeave += new PageChangeHandler(SSHOptions_PageLeave);
        }

        void SSHOptions_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["SSH:HasWarned"] = m_hasTested;
            m_settings["SSH:WarnedPath"] = m_warnedPath;
            m_settings["SSH:WarnedNoSFTP"] = m_warnedNoSFTP;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!ValidateForm())
            {
                args.Cancel = true;
                return;
            }

            if (!m_hasTested)
                if (MessageBox.Show(this, "Do you want to test the connection?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    TestConnection_Click(null, null);
                    if (!m_hasTested)
                        return;
                }

            m_settings["SSH:HasWarned"] = m_hasTested;
            m_settings["SSH:WarnedPath"] = m_warnedPath;
            m_settings["SSH:WarnedNoSFTP"] = m_warnedNoSFTP;

            m_wrapper.Passwordless = !UsePassword.Checked;
            m_wrapper.Password = m_wrapper.Passwordless ? "" : Password.Text;
            m_wrapper.Port = (int)Port.Value;
            m_wrapper.Server = Servername.Text;
            m_wrapper.Path = Path.Text;
            m_wrapper.Username = Username.Text;

            if (new WizardSettingsWrapper(m_settings).PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                args.NextPage = new Add_backup.GeneratedFilenameOptions();
            else
                args.NextPage = new Add_backup.AdvancedOptions();
        }

        void SSHOptions_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings).SSHSettings;

            if (!m_valuesAutoLoaded)
            {
                Servername.Text = m_wrapper.Server;
                Path.Text = m_wrapper.Path;
                Username.Text = m_wrapper.Username;
                UsePassword.Checked = !m_wrapper.Passwordless;
                Password.Text = m_wrapper.Password;
                Port.Value = m_wrapper.Port;
            }

            if (m_settings.ContainsKey("SSH:HasTested"))
                m_hasTested = (bool)m_settings["SSH:HasTested"];

            if (m_settings.ContainsKey("SSH:WarnedPath"))
                m_warnedPath = (bool)m_settings["SSH:WarnedPath"];

            if (System.Environment.OSVersion.Platform == PlatformID.Unix || System.Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                //sftp is likely present on linux/mac
                m_warnedNoSFTP = true;
            }
            else
            {
                if (m_settings.ContainsKey("SSH:WarnedNoSFTP"))
                    m_warnedNoSFTP = (bool)m_settings["SSH:WarnedNoSFTP"];
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
                    System.Data.LightDatamodel.IDataFetcherCached con = new System.Data.LightDatamodel.DataFetcherNested(Program.DataConnection);
                    Datamodel.Backends.SSH ssh = new Duplicati.Datamodel.Backends.SSH(con.Add<Task>());

                    ssh.Folder = Path.Text;
                    ssh.Host = Servername.Text;
                    ssh.Password = UsePassword.Checked ? Password.Text : "";
                    ssh.Passwordless = !UsePassword.Checked;
                    ssh.Port = (int)Port.Value;
                    ssh.Username = Username.Text;

                    string target = ssh.GetDestinationPath();

                    Dictionary<string, string> options = new Dictionary<string, string>();
                    ssh.GetOptions(options);

                    //Make sure that "testing" always produce a log
                    options.Add("debug-to-console", "");

                    string[] files = Duplicati.Library.Main.Interface.List(target, options);

                    MessageBox.Show(this, "Connection succeeded!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                }
                this.Cursor = c;
            }
        }

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter the name of the server", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (Username.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter a username", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Username.Focus(); }
                catch { }

                return false;
            }

            if (Password.Text.Trim().Length <= 0 && UsePassword.Checked)
            {
                MessageBox.Show(this, "You must enter a password", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Password.Focus(); }
                catch { }

                return false;
            }

            if (!m_warnedPath && Path.Text.Trim().Length == 0)
            {
                if (MessageBox.Show(this, "You have not entered a path. This will store all backups in the default directory. Is this what you want?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return false;
                }
                m_warnedPath = true;
            }

            if (!m_warnedNoSFTP)
            {
                ApplicationSettings appset = new ApplicationSettings(Program.DataConnection);
                System.IO.FileInfo fi = null;
                try { fi = new System.IO.FileInfo(System.Environment.ExpandEnvironmentVariables(appset.SFtpPath)); }
                catch { }

                if (fi == null || !fi.Exists)
                {
                    if (MessageBox.Show(this, "Duplicati was unable to verify the existence of the sftp program.\nscp may work regardless, if it is located in the system search path.\n\nDo you want to continue anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
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
    }
}
