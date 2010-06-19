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
using Duplicati.Library.Core;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class PasswordSettings : WizardControl
    {
        private bool m_warnedNoPassword = false;
        private bool m_warnedChanged = false;
        private bool m_settingsChanged = false;
        private WizardSettingsWrapper m_wrapper;
        private Library.Interface.IEncryption m_encryptionModule;

        public PasswordSettings()
            : base(Strings.PasswordSettings.PageTitle, Strings.PasswordSettings.PageDescription) 
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(PasswordSettings_PageEnter);
            base.PageLeave += new PageChangeHandler(PasswordSettings_PageLeave);
            base.PageDisplay += new PageChangeHandler(PasswordSettings_PageDisplay);

            try
            {
                EncryptionModule.Items.Clear();

                foreach (Library.Interface.IEncryption e in Library.DynamicLoader.EncryptionLoader.Modules)
                    EncryptionModule.Items.Add(new ComboBoxItemPair<Library.Interface.IEncryption>(e.DisplayName, e));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.PasswordSettings.EncryptionModuleLoadError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void PasswordSettings_PageDisplay(object sender, PageChangedArgs args)
        {
            try { Password.Focus(); }
            catch { }
        }

        void PasswordSettings_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["Password:WarnedNoPassword"] = m_warnedNoPassword;
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

            if (!m_warnedNoPassword && !EnablePassword.Checked && m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
            {
                if (MessageBox.Show(this, Strings.PasswordSettings.NoPasswordWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }

                m_warnedNoPassword = true;
            }

            if (EnablePassword.Checked)
            {
                if (m_encryptionModule == null)
                {
                    MessageBox.Show(this, Strings.PasswordSettings.NoEncryptionMethodSelected, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                if (m_encryptionModule is Library.Interface.IGUIMiniControl)
                {
                    if (!(m_encryptionModule as Library.Interface.IGUIMiniControl).Validate(EncryptionControlContainer.Controls[0]))
                    {
                        args.Cancel = true;
                        return;
                    }
                }
            }

            if (!m_warnedChanged && m_settingsChanged && m_wrapper.ScheduleID > 0)
            {
                if (MessageBox.Show(this, Strings.PasswordSettings.PasswordChangedWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }

                m_warnedChanged = true;
            }

            m_settings["Password:SettingsChanged"] = m_settingsChanged;
            m_settings["Password:WarnedNoPassword"] = m_warnedNoPassword;
            m_settings["Password:WarnedChanged"] = m_warnedChanged;
            m_wrapper.BackupPassword = EnablePassword.Checked ? Password.Text : "";

            m_wrapper.EncryptionModule = m_encryptionModule == null ? null : m_encryptionModule.FilenameExtension;
            m_wrapper.UseEncryptionAsDefault = UseSettingsAsDefault.Checked;

            args.NextPage = new SelectBackend();

            if (m_encryptionModule != null)
            {
                if (m_encryptionModule is Library.Interface.IEncryptionGUI)
                {
                    if (!(m_encryptionModule is Library.Interface.IGUIMiniControl))
                        args.NextPage = new GUIContainer(args.NextPage, m_encryptionModule as Library.Interface.IEncryptionGUI);
                }
                else
                {
                    if (m_encryptionModule.SupportedCommands != null && m_encryptionModule.SupportedCommands.Count > 0)
                        args.NextPage = new GridContainer(args.NextPage, m_encryptionModule.SupportedCommands, m_wrapper.EncryptionSettings, "encryption:" + m_encryptionModule.FilenameExtension, m_encryptionModule.DisplayName, m_encryptionModule.Description);
                }
            }
        }

        void PasswordSettings_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (!m_valuesAutoLoaded)
            {
                EnablePassword.Checked = !string.IsNullOrEmpty(m_wrapper.BackupPassword) || (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore);
                Password.Text = m_wrapper.BackupPassword;

                for (int i = 0; i < EncryptionModule.Items.Count; i++)
                    if (((ComboBoxItemPair<Library.Interface.IEncryption>)EncryptionModule.Items[i]).Value.FilenameExtension == m_wrapper.EncryptionModule)
                    {
                        EncryptionModule.SelectedIndex = i;
                        break;
                    }

                m_settingsChanged = false;

                if (Program.DataConnection.GetObjects<Schedule>().Length == 0)
                    UseSettingsAsDefault.Checked = true;
            }

            if (m_settings.ContainsKey("Password:WarnedNoPassword"))
                m_warnedNoPassword = (bool)m_settings["Password:WarnedNoPassword"];
            if (m_settings.ContainsKey("Password:WarnedChanged"))
                m_warnedChanged = (bool)m_settings["Password:WarnedChanged"];
            if (m_settings.ContainsKey("Password:SettingsChanged"))
                m_settingsChanged = (bool)m_settings["Password:SettingsChanged"];

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore)
            {
                PasswordHelptext.Visible =
                GeneratePasswordButton.Visible =
                UseSettingsAsDefault.Visible = false;

                EnablePassword.Text = Strings.PasswordSettings.EnablePasswordRestoreText;
                EncryptionModule.Top = PasswordHelptext.Top;


            }
        }

        private void EnablePassword_CheckedChanged(object sender, EventArgs e)
        {
            Password.Enabled = EncryptionModule.Enabled = GeneratePasswordButton.Enabled = EncryptionControlContainer.Enabled = EnablePassword.Checked;
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

        private void EncryptionModule_SelectedIndexChanged(object sender, EventArgs e)
        {
            m_settingsChanged = true;

            EncryptionControlContainer.Controls.Clear();
            if (EncryptionModule.SelectedItem as ComboBoxItemPair<Library.Interface.IEncryption> != null)
            {
                m_encryptionModule = (EncryptionModule.SelectedItem as ComboBoxItemPair<Library.Interface.IEncryption>).Value;
                if (m_encryptionModule is Library.Interface.IEncryptionGUI && m_encryptionModule is Library.Interface.IGUIMiniControl)
                {
                    Control c = (m_encryptionModule as Library.Interface.IEncryptionGUI).GetControl(m_wrapper.ApplicationSettings, m_wrapper.EncryptionSettings);
                    c.Dock = DockStyle.Fill;
                    EncryptionControlContainer.Controls.Add(c);
                }
            }
            else
                m_encryptionModule = null;
        }
    }
}
