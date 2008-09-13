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

namespace Duplicati.Wizard_pages.Backends.SSH
{
    public partial class SSHOptions : UserControl, IWizardControl, Wizard_pages.Interfaces.ITaskBased
    {
        private bool m_warnedPath = false;
        private bool m_hasTested = false;

        private Duplicati.Datamodel.Backends.SSH m_ssh;

        public SSHOptions()
        {
            InitializeComponent();
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return "Backup storage options"; }
        }

        string IWizardControl.HelpText
        {
            get { return "On this page you can select where to store the backup data."; }
        }

        Image IWizardControl.Image
        {
            get { return null; }
        }

        bool IWizardControl.FullSize
        {
            get { return false; }
        }

        void IWizardControl.Enter(IWizardForm owner)
        {
            bool backset = m_hasTested;
            bool wp = m_warnedPath;

            Servername.Text = m_ssh.Host;
            Path.Text = m_ssh.Folder;
            Username.Text = m_ssh.Username;
            UsePassword.Checked = !m_ssh.Passwordless;
            Password.Text = m_ssh.Password;
            Port.Value = m_ssh.Port;

            m_hasTested = backset;
            m_warnedPath = wp;
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            if (!ValidateForm())
            {
                cancel = true;
                return;
            }

            /*if (!m_hasTested)
                if (MessageBox.Show(this, "Do you want to test the connection?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    TestConnection_Click(null, null);
                    if (!m_hasTested)
                        return;
                }
            */

            if (!m_warnedPath && Path.Text.Trim().Length == 0)
            {
                if (MessageBox.Show(this, "You have not entered a path. This will store all backups in the default directory. Is this what you want?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    cancel = true;
                    return;
                }
                m_warnedPath = true;
            }

            m_ssh.Host = Servername.Text;
            m_ssh.Folder = Path.Text;
            m_ssh.Username = Username.Text;
            
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
            m_ssh.SetService();

        }

        #endregion

        private void TestConnection_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, "Testing is not implemented for SSH yet.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            return true;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_warnedPath = false;
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
