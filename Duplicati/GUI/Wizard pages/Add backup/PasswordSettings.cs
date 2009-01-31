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
    public partial class PasswordSettings : WizardControl
    {
        private bool m_warnedNoPassword = false;
        private Task m_task;

        public PasswordSettings()
            : base("Select password for the backup", "On this page you can select options that protect your backups from being read or altered.") 
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(PasswordSettings_PageEnter);
            base.PageLeave += new PageChangeHandler(PasswordSettings_PageLeave);
        }

        void PasswordSettings_PageLeave(object sender, PageChangedArgs args)
        {
            SaveSettings();

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (EnablePassword.Checked && Password.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "You must enter a password, remove the check mark next to the box to disable encryption of the backups.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (!m_warnedNoPassword && !EnablePassword.Checked)
            {
                if (MessageBox.Show(this, "If the backup is stored on machine or device that is not under your direct control,\nit is possible that others may view the files you have stored in the backups.\nIt is highly recomended that you enable encryption.\nDo you want to continue without encryption?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }

                m_warnedNoPassword = true;
            }

            SaveSettings();

            args.NextPage = null;
        }

        private void SaveSettings()
        {
            if (EnablePassword.Checked)
                m_task.Encryptionkey = Password.Text;
            else
                m_task.Encryptionkey = null;

            m_task.Signaturekey = null;
            m_settings["Password:WarnedNoPassword"] = m_warnedNoPassword;
            m_settings["Password:PasswordChecked"] = EnablePassword.Checked;
        }

        void PasswordSettings_PageEnter(object sender, PageChangedArgs args)
        {
            m_task = ((Schedule)m_settings["Schedule"]).Tasks[0];

            if (m_settings.ContainsKey("Password:PasswordChecked"))
            {
                EnablePassword.Checked = (bool)m_settings["Password:PasswordChecked"];
                Password.Text = m_task.Encryptionkey;
            }
            else
            {
                EnablePassword.Checked = true;
                Password.Text = "";
            }

            if (m_settings.ContainsKey("Password:WarnedNoPassword"))
                m_warnedNoPassword = (bool)m_settings["Password:WarnedNoPassword"];
        }

        private void GeneratePassword_Click(object sender, EventArgs e)
        {
            Password.Text = Duplicati.Library.Core.KeyGenerator.GenerateKey((int)PassphraseLength.Value, (int)PassphraseLength.Value);
        }

        private void EnablePassword_CheckedChanged(object sender, EventArgs e)
        {
            Password.Enabled = PasswordHelptext.Enabled = PasswordGeneratorSettings.Enabled = EnablePassword.Checked;
            m_warnedNoPassword = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_warnedNoPassword = false;
        }

    }
}
