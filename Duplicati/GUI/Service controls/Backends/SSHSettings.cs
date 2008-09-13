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
using Duplicati.Datamodel.Backends;

namespace Duplicati.GUI.Service_controls
{
    public partial class SSHSettings : UserControl
    {
        private SSH m_ssh;
        private bool m_isUpdating = false;

        public SSHSettings()
        {
            InitializeComponent();
        }

        public void Setup(SSH ssh)
        {
            try
            {
                m_isUpdating = true;
                m_ssh = ssh;

                Passwordless.Checked = m_ssh.Passwordless;
                Username.Text = m_ssh.Username;
                Password.Text = m_ssh.Password;
                Host.Text = m_ssh.Host;
                Folder.Text = m_ssh.Folder;
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void Passwordless_CheckedChanged(object sender, EventArgs e)
        {
            PasswordPanel.Enabled = !Passwordless.Checked;

            if (m_isUpdating || m_ssh == null)
                return;

            m_ssh.Passwordless = Passwordless.Checked;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_ssh == null)
                return;

            m_ssh.Username = Username.Text;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_ssh == null)
                return;

            m_ssh.Password = Password.Text;
        }

        private void Host_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_ssh == null)
                return;

            m_ssh.Host = Host.Text;
        }

        private void Folder_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_ssh == null)
                return;

            m_ssh.Folder = Folder.Text;
        }
    }
}
