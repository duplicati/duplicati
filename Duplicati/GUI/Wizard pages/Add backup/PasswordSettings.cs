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
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class PasswordSettings : WizardControl
    {
        private bool m_warnedNoPassword = false;
        private bool m_warnedNoGPG = false;
        private bool m_warnedChanged = false;
        private bool m_settingsChanged = false;
        private WizardSettingsWrapper m_wrapper;

        public PasswordSettings()
            : base(Strings.PasswordSettings.PageTitle, Strings.PasswordSettings.PageDescription) 
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(PasswordSettings_PageEnter);
            base.PageLeave += new PageChangeHandler(PasswordSettings_PageLeave);
        }

        void PasswordSettings_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["Password:WarnedNoPassword"] = m_warnedNoPassword;
            m_settings["Password:WarnedNoGPG"] = m_warnedNoGPG;
            m_settings["Password:WarnedChanged"] = m_warnedChanged;
            m_settings["Password:SettingsChanged"] = m_settingsChanged;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (EnablePassword.Checked && Password.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, Strings.PasswordSettings.EmptyPasswordError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (!m_warnedNoPassword && !EnablePassword.Checked && m_wrapper.PrimayAction != WizardSettingsWrapper.MainAction.RestoreSetup && m_wrapper.PrimayAction != WizardSettingsWrapper.MainAction.Restore)
            {
                if (MessageBox.Show(this, Strings.PasswordSettings.NoPasswordWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }

                m_warnedNoPassword = true;
            }

            if (!m_warnedNoGPG && UseGPGEncryption.Checked)
            {
                ApplicationSettings apps = new ApplicationSettings(Program.DataConnection);
                System.IO.FileInfo fi = null;
                try { fi = new System.IO.FileInfo(System.Environment.ExpandEnvironmentVariables(apps.GPGPath)); }
                catch { }

                if (fi == null || !fi.Exists)
                {
                    if (MessageBox.Show(this, Strings.PasswordSettings.GPGNotFoundWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        args.Cancel = true;
                        return;
                    }

                    m_warnedNoGPG = true;
                }
            }

            if (!m_warnedChanged && m_settingsChanged && m_wrapper.ScheduleID > 0)
            {
                if (MessageBox.Show(this, Strings.PasswordSettings.PasswordChangedWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
            }

            m_settings["Password:SettingsChanged"] = m_settingsChanged;
            m_settings["Password:WarnedNoPassword"] = m_warnedNoPassword;
            m_settings["Password:WarnedNoGPG"] = m_warnedNoGPG;
            m_settings["Password:WarnedChanged"] = m_warnedChanged;
            m_wrapper.BackupPassword = EnablePassword.Checked ? Password.Text : "";
            m_wrapper.GPGEncryption = UseGPGEncryption.Checked;
            m_wrapper.UseEncryptionAsDefault = UseSettingsAsDefault.Checked;

            args.NextPage = new SelectBackend();
        }

        void PasswordSettings_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (!m_valuesAutoLoaded)
            {
                EnablePassword.Checked = !string.IsNullOrEmpty(m_wrapper.BackupPassword) || (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore);
                Password.Text = m_wrapper.BackupPassword;
                UseGPGEncryption.Checked = m_wrapper.GPGEncryption;
                m_settingsChanged = false;

                if (Program.DataConnection.GetObjects<Schedule>().Length == 0)
                    UseSettingsAsDefault.Checked = true;
            }

            if (m_settings.ContainsKey("Password:WarnedNoPassword"))
                m_warnedNoPassword = (bool)m_settings["Password:WarnedNoPassword"];
            if (m_settings.ContainsKey("Password:WarnedNoGPG"))
                m_warnedNoPassword = (bool)m_settings["Password:WarnedNoGPG"];
            if (m_settings.ContainsKey("Password:WarnedChanged"))
                m_warnedChanged = (bool)m_settings["Password:WarnedChanged"];
            if (m_settings.ContainsKey("Password:SettingsChanged"))
                m_settingsChanged = (bool)m_settings["Password:SettingsChanged"];

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore)
            {
                PasswordHelptext.Visible =
                PasswordGeneratorSettings.Visible = 
                UseSettingsAsDefault.Visible = false;

                EnablePassword.Text = Strings.PasswordSettings.EnablePasswordRestoreText;
                EncryptionMethod.Top = PasswordHelptext.Top;
            }
        }

        private void GeneratePassword_Click(object sender, EventArgs e)
        {
            Password.Text = Duplicati.Library.Core.KeyGenerator.GenerateKey((int)PassphraseLength.Value, (int)PassphraseLength.Value);
        }

        private void EnablePassword_CheckedChanged(object sender, EventArgs e)
        {
            Password.Enabled = EncryptionMethod.Enabled = PasswordGeneratorSettings.Enabled = EnablePassword.Checked;
            m_warnedNoPassword = false;
            m_settingsChanged = true;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_settingsChanged = true;
            m_warnedNoPassword = false;
        }

        public override string HelpText
        {
            get
            {
                if (m_wrapper.PrimayAction != WizardSettingsWrapper.MainAction.RestoreSetup && m_wrapper.PrimayAction != WizardSettingsWrapper.MainAction.Restore)
                    return base.HelpText;
                else
                    return Strings.PasswordSettings.PageDescriptionRestore;
            }
        }

        private void UseAESEncryption_CheckedChanged(object sender, EventArgs e)
        {
            m_settingsChanged = true;
        }

        private void UseGPGEncryption_CheckedChanged(object sender, EventArgs e)
        {
            m_settingsChanged = true;
            m_warnedNoGPG = false;
        }

    }
}
