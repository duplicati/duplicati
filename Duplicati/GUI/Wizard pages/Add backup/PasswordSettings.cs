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
        private bool m_warnedNoGPG = false;
        private WizardSettingsWrapper m_wrapper;

        public PasswordSettings()
            : base("Select password for the backup", "On this page you can select options that protect your backups from being read or altered.") 
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(PasswordSettings_PageEnter);
            base.PageLeave += new PageChangeHandler(PasswordSettings_PageLeave);
        }

        void PasswordSettings_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["Password:WarnedNoPassword"] = m_warnedNoPassword;
            m_settings["Password:WarnedNoGPG"] = m_warnedNoGPG;

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

            if (!m_warnedNoGPG && UseGPG.Checked)
            {
                ApplicationSettings apps = new ApplicationSettings(Program.DataConnection);
                System.IO.FileInfo fi = null;
                try { fi = new System.IO.FileInfo(System.Environment.ExpandEnvironmentVariables(apps.GPGPath)); }
                catch { }

                if (fi == null || !fi.Exists)
                {
                    if (MessageBox.Show(this, "Duplicati was unable to verify the existence of GNU Privacy Guard.\nGPG may work regardless, if it is located in the system search path.\nIf the encryption fails, no files will be backed up\nDo you want to continue anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        args.Cancel = true;
                        return;
                    }

                    m_warnedNoGPG = true;
                }
            }

            m_settings["Password:WarnedNoPassword"] = m_warnedNoPassword;
            m_settings["Password:WarnedNoGPG"] = m_warnedNoGPG;
            m_wrapper.BackupPassword = EnablePassword.Checked ? Password.Text : "";
            m_wrapper.GPGEncryption = UseGPG.Checked;

            args.NextPage = new SelectBackend();
        }

        void PasswordSettings_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (!m_valuesAutoLoaded)
            {
                EnablePassword.Checked = !string.IsNullOrEmpty(m_wrapper.BackupPassword) || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add;
                Password.Text = m_wrapper.BackupPassword;
                UseGPG.Checked = m_wrapper.GPGEncryption;
            }

            if (m_settings.ContainsKey("Password:WarnedNoPassword"))
                m_warnedNoPassword = (bool)m_settings["Password:WarnedNoPassword"];
            if (m_settings.ContainsKey("Password:WarnedNoGPG"))
                m_warnedNoPassword = (bool)m_settings["Password:WarnedNoGPG"];
        }

        private void GeneratePassword_Click(object sender, EventArgs e)
        {
            Password.Text = Duplicati.Library.Core.KeyGenerator.GenerateKey((int)PassphraseLength.Value, (int)PassphraseLength.Value);
        }

        private void EnablePassword_CheckedChanged(object sender, EventArgs e)
        {
            Password.Enabled = UseGPG.Enabled = PasswordGeneratorSettings.Enabled = EnablePassword.Checked;
            m_warnedNoPassword = false;
            
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_warnedNoPassword = false;
        }

        private void UseGPG_CheckedChanged(object sender, EventArgs e)
        {
            m_warnedNoGPG = false;
        }

    }
}
