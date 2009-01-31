#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

        private Duplicati.Datamodel.Backends.SSH m_ssh;

        public SSHOptions()
            : base("Backup storage options", "On this page you can select where to store the backup data.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(SSHOptions_PageEnter);
            base.PageLeave += new PageChangeHandler(SSHOptions_PageLeave);
        }

        void SSHOptions_PageLeave(object sender, PageChangedArgs args)
        {
            SaveSettings();

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

            SaveSettings();

            args.NextPage = new Add_backup.AdvancedOptions();
        }

        void SSHOptions_PageEnter(object sender, PageChangedArgs args)
        {
            m_ssh = new Duplicati.Datamodel.Backends.SSH(((Schedule)m_settings["Schedule"]).Tasks[0]);

            if (!m_valuesAutoLoaded)
            {
                Servername.Text = m_ssh.Host;
                Path.Text = m_ssh.Folder;
                Username.Text = m_ssh.Username;
                UsePassword.Checked = !m_ssh.Passwordless;
                Password.Text = m_ssh.Password;
                Port.Value = m_ssh.Port;
            }

            if (!m_settings.ContainsKey("SSH:HasTested"))
                m_hasTested = (bool)m_settings["SSH:HasTested"];

            if (!m_settings.ContainsKey("SSH:WarnedPath"))
                m_warnedPath = (bool)m_settings["SSH:WarnedPath"];
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                SaveSettings();

                try
                {
                    string target = m_ssh.GetDestinationPath();
                    Dictionary<string, string> options = new Dictionary<string, string>();
                    m_ssh.GetOptions(options);

                    string[] files = Duplicati.Library.Main.Interface.List(target, options);

                    MessageBox.Show(this, "Connection succeeded!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                }
            }
        }

        private void SaveSettings()
        {
            if (UsePassword.Checked)
            {
                m_ssh.Passwordless = false;
                m_ssh.Password = Password.Text;
            }
            else
            {
                m_ssh.Passwordless = true;
                m_ssh.Password = null;
            }

            m_ssh.Port = (int)Port.Value;
            m_ssh.Host = Servername.Text;
            m_ssh.Folder = Path.Text;
            m_ssh.Username = Username.Text;

            m_settings["SSH:HasWarned"] = m_hasTested;
            m_settings["SSH:WarnedPath"] = m_warnedPath;

            m_ssh.SetService();
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

        #region ITaskBased Members

        public void Setup(Task task)
        {
            m_ssh = new Duplicati.Datamodel.Backends.SSH(task);
        }

        #endregion

        private void UsePassword_CheckedChanged(object sender, EventArgs e)
        {
            Password.Enabled = UsePassword.Checked;
            m_hasTested = false;
        }
    }
}
