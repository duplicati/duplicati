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

namespace Duplicati.GUI.Wizard_pages
{
    public partial class MainPage : WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public MainPage()
            : base("Welcome to the Duplicati Wizard", "Please select the action you want to perform below")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(MainPage_PageEnter);
            base.PageLeave += new PageChangeHandler(MainPage_PageLeave);
        }

        void MainPage_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            UpdateButtonState();

            this.Controls.Remove(ShowAdvanced);
            m_owner.ButtonPanel.Controls.Add(ShowAdvanced);
            ShowAdvanced.Top = m_owner.CancelButton.Top;
            ShowAdvanced.Left = m_owner.ButtonPanel.Width - m_owner.CancelButton.Right;
            ShowAdvanced.Visible = true;
            args.TreatAsLast = false;
        }

        void MainPage_PageLeave(object sender, PageChangedArgs args)
        {
            if (CreateNew.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Add;
            else if (Edit.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Edit;
            else if (Restore.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Restore;
            else if (Backup.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.RunNow;
            else if (Remove.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Remove;
            else
            {
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Unknown;
                args.NextPage = null;
                args.Cancel = true;
                return;
            }

            m_owner.ButtonPanel.Controls.Remove(ShowAdvanced);
            this.Controls.Add(ShowAdvanced);
            ShowAdvanced.Visible = false;

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
            {
                args.NextPage = new Add_backup.SelectName();
                SetupDefaults();
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Add;
            }
            else
                args.NextPage = new SelectBackup();
        }

        private void UpdateButtonState()
        {
            if (m_owner != null)
                m_owner.NextButton.Enabled = CreateNew.Checked | Edit.Checked | Restore.Checked | Backup.Checked | Remove.Checked;
        }

        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            UpdateButtonState();
        }

        private void ShowAdvanced_Click(object sender, EventArgs e)
        {
            Program.ShowSetup();
            m_owner.Dialog.DialogResult = DialogResult.Cancel;
            m_owner.Dialog.Close();
        }

        /// <summary>
        /// The purpose of this function is to set the default
        /// settings on the new backup.
        /// </summary>
        private void SetupDefaults()
        {
            m_settings.Clear();

            //TODO: These settings should be read from a file, 
            //so they are customizable by the end user
            m_wrapper.FullBackupInterval = "1M";
            m_wrapper.MaxFullBackups = 4;
            m_wrapper.BackupExpireInterval = "";
            m_wrapper.RepeatInterval = "1D";
            m_wrapper.VolumeSize = "5MB";

            //Run each day at 13:00 (1 pm)
            m_wrapper.BackupTimeOffset = DateTime.Now.Date.AddHours(13);
        }

    }
}
