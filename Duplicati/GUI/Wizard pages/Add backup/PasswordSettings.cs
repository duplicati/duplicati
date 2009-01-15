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

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class PasswordSettings : UserControl, IWizardControl, Interfaces.ITaskBased
    {
        private bool m_warnedNoPassword = false;
        private bool m_showAsInitial = false;
        private Task m_task;

        public PasswordSettings()
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
            get { return "Select password for the backup"; }
        }

        string IWizardControl.HelpText
        {
            get { return "On this page you can select options that protect your backups from being read or altered."; }
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
            if (m_showAsInitial)
            {
                EnablePassword.Checked = true;
                Password.Text = "";
                EnableSigning.Checked = true;
                Signkey.Text = "";
                m_showAsInitial = false;
            }
            else
            {
                if (m_task != null)
                {
                    EnablePassword.Checked = !string.IsNullOrEmpty(m_task.Encryptionkey);
                    Password.Text = m_task.Encryptionkey;
                    EnableSigning.Checked = !string.IsNullOrEmpty(m_task.Signaturekey);
                    Signkey.Text = m_task.Signaturekey;
                }
            }
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            if (EnablePassword.Checked && Password.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "You must enter a password, remove the check mark next to the box to disable encryption of the backups.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cancel = true;
                return;
            }

            if (EnableSigning.Checked && Signkey.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "You must enter a signature key, remove the check mark next to the box to disable signing of the backups.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cancel = true;
                return;
            }

            if (EnableSigning.Checked)
            {
                bool valid = true;

                if (Signkey.Text.Length != 8)
                    valid = false;
                else
                {
                    List<char> l = new List<char>(KeyGenerator.HEX_CHARS);
                    for (int i = 0; i < Signkey.Text.Length; i++)
                        if (!l.Contains(Signkey.Text[i]))
                        {
                            valid = false;
                            break;
                        }
                }

                if (!valid)
                {
                    MessageBox.Show(this, "The signature key must be excatly eight characters, and only contain the letters A through F, and the numbers 0 to 9.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    cancel = true;
                    return;
                }
            }

            if (!m_warnedNoPassword && !EnablePassword.Checked)
            {
                if (MessageBox.Show(this, "If the backup is stored on machine or device that is not under your direct control,\nit is possible that others may view the files you have stored in the backups.\nIt is highly recomended that you enable encryption.\nDo you want to continue without encryption?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    cancel = true;
                    return;
                }
            }

            if (EnablePassword.Checked)
                m_task.Encryptionkey = Password.Text;
            else
                m_task.Encryptionkey = null;

            if (EnableSigning.Checked)
                m_task.Signaturekey = Signkey.Text;
            else
                m_task.Signaturekey = null;
        }

        #endregion

        private void GenerateSignKey_Click(object sender, EventArgs e)
        {
            Signkey.Text = KeyGenerator.GenerateSignKey();
        }

        private void GeneratePassword_Click(object sender, EventArgs e)
        {
            Password.Text = KeyGenerator.GenerateKey((int)PassphraseLength.Value, (int)PassphraseLength.Value);
        }

        private void EnablePassword_CheckedChanged(object sender, EventArgs e)
        {
            Password.Enabled = PasswordHelptext.Enabled = PasswordGeneratorSettings.Enabled = EnablePassword.Checked;
            m_warnedNoPassword = false;
        }

        private void EnableSigning_CheckedChanged(object sender, EventArgs e)
        {
            Signkey.Enabled = GenerateSignKey.Enabled = SignHelptext.Enabled = EnableSigning.Checked;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_warnedNoPassword = false;
        }

        #region IScheduleBased Members

        public void Setup(Duplicati.Datamodel.Task task)
        {
            m_task = task;
            if (m_task != null)
                m_showAsInitial = !m_task.ExistsInDb;
        }

        #endregion
    }
}
