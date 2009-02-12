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
using System.Data.LightDatamodel;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages
{
    public partial class SelectBackup : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public SelectBackup()
            : base ("", "")
        {
            InitializeComponent();
            BackupList.treeView.HideSelection = false;

            base.PageLeave += new PageChangeHandler(SelectBackup_PageLeave);
            base.PageEnter += new PageChangeHandler(SelectBackup_PageEnter);
        }

        void SelectBackup_PageEnter(object sender, PageChangedArgs args)
        {
            BackupList.Setup(Program.DataConnection, true, false);
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (m_wrapper.ScheduleID > 0)
                BackupList.SelectedBackup = Program.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID);

            topLabel.Text = this.Title;
        }

        void SelectBackup_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (BackupList.SelectedBackup == null)
            {
                MessageBox.Show(this, "You must select a backup before you can continue", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            m_wrapper.ReflectSchedule(BackupList.SelectedBackup);
            switch (m_wrapper.PrimayAction)
            {
                case WizardSettingsWrapper.MainAction.Edit:
                    args.NextPage = new Add_backup.SelectName();
                    break;
                case WizardSettingsWrapper.MainAction.Remove:
                    args.NextPage = new Delete_backup.DeleteFinished();
                    break;
                case WizardSettingsWrapper.MainAction.Restore:
                    args.NextPage = new Restore.SelectBackup();
                    break;
                case WizardSettingsWrapper.MainAction.RunNow:
                    args.NextPage = new RunNow.RunNowFinished();
                    break;
                default:
                    args.NextPage = null;
                    args.Cancel = true;
                    return;
            }

        }

        public override string Title
        {
            get
            {
                m_wrapper = new WizardSettingsWrapper(m_settings);
                switch (m_wrapper.PrimayAction)
                {
                    case WizardSettingsWrapper.MainAction.RunNow:
                        return "Select the backup to run now";
                    case WizardSettingsWrapper.MainAction.Remove:
                        return "Select the backup to remove";
                    case WizardSettingsWrapper.MainAction.Edit:
                        return "Select the backup to modify";
                    case WizardSettingsWrapper.MainAction.Restore:
                        return "Select the backup to restore";
                    default:
                        return "Unknown action";
                }

            }
        }

        public override string HelpText
        {
            get
            {
                m_wrapper = new WizardSettingsWrapper(m_settings);
                switch (m_wrapper.PrimayAction)
                {
                    case WizardSettingsWrapper.MainAction.RunNow:
                        return "In the list below, select the backup you want to run immediately";
                    case WizardSettingsWrapper.MainAction.Remove:
                        return "In the list below, select the backup you want to delete";
                    case WizardSettingsWrapper.MainAction.Edit:
                        return "In the list below, select the backup you want to modify";
                    case WizardSettingsWrapper.MainAction.Restore:
                        return "In the list below, select the backup you want to restore";
                    default:
                        return "Unknown action";
                }
            }
        }

        private void BackupList_TreeDoubleClicked(object sender, EventArgs e)
        {
            m_owner.NextButton.PerformClick();
        }
    }
}
