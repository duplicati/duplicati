#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
        private WizardSettingsWrapper.MainAction? m_action = null;
        private bool m_skipFirstEvent = false;

        public SelectBackup(WizardSettingsWrapper.MainAction action)
            : this()
        {
            m_action = action;
        }

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

            if (m_action != null)
                m_wrapper.PrimayAction = m_action.Value;

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RunNow && m_wrapper.DataConnection == null)
                m_wrapper.DataConnection = Program.DataConnection;

            if (m_wrapper.ScheduleID > 0)
                BackupList.SelectedBackup = (m_wrapper.DataConnection ?? Program.DataConnection).GetObjectById<Schedule>(m_wrapper.ScheduleID);

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore)
            {
                topLabel.Visible = false;
                RestoreOptions.Visible = true;
            }
            else
            {
                topLabel.Visible = true;
                RestoreOptions.Visible = false;
                ShowAdvancedPanel.Visible = false;
                topLabel.Text = this.Title;
            }
            
            if (m_valuesAutoLoaded)
            {

                m_skipFirstEvent = true;
            }

            args.TreatAsLast = false;
        }

        void SelectBackup_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if ((RestoreExisting.Checked && BackupList.SelectedBackup == null) || (!RestoreExisting.Checked && !DirectRestore.Checked))
            {
                MessageBox.Show(this, Strings.SelectBackup.NoActionSelected, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (DirectRestore.Checked)
            {
                m_wrapper.SetupDefaults();
                m_wrapper.DataConnection = new System.Data.LightDatamodel.DataFetcherNested(Program.DataConnection);

                Schedule s = new Schedule();
                Task t = new Task();

                m_wrapper.DataConnection.Add(s);
                m_wrapper.DataConnection.Add(t);

                s.Task = t;

                m_wrapper.ScheduleID = s.ID;
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Restore;
            }
            else
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
                    m_wrapper.ShowAdvancedRestoreOptions = ShowAdvanced.Checked;
                    if (DirectRestore.Checked)
                        args.NextPage = new Add_backup.PasswordSettings();
                    else
                        args.NextPage = m_wrapper.ShowAdvancedRestoreOptions ? (IWizardControl)new Add_backup.SettingOverrides() : (IWizardControl)new Restore.SelectBackupVersion();
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
                        return Strings.SelectBackup.PageTitleRunNow;
                    case WizardSettingsWrapper.MainAction.Remove:
                        return Strings.SelectBackup.PageTitleRemove;
                    case WizardSettingsWrapper.MainAction.Edit:
                        return Strings.SelectBackup.PageTitleEdit;
                    case WizardSettingsWrapper.MainAction.Restore:
                        return Strings.SelectBackup.PageTitleRestore;
                    default:
                        return Strings.SelectBackup.PageTitleUnknown;
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
                        return Strings.SelectBackup.PageHelptextRunNow;
                    case WizardSettingsWrapper.MainAction.Remove:
                        return Strings.SelectBackup.PageHelptextRemove;
                    case WizardSettingsWrapper.MainAction.Edit:
                        return Strings.SelectBackup.PageHelptextEdit;
                    case WizardSettingsWrapper.MainAction.Restore:
                        return Strings.SelectBackup.PageHelptextRestore;
                    default:
                        return Strings.SelectBackup.PageTitleRunNow;
                }
            }
        }

        private void BackupList_TreeDoubleClicked(object sender, EventArgs e)
        {
            RestoreExisting.Checked = true;
            m_owner.NextButton.PerformClick();
        }

        private void BackupList_SelectedBackupChanged(object sender, EventArgs e)
        {
            if (m_skipFirstEvent)
                m_skipFirstEvent = false;
            else
                RestoreExisting.Checked = true;
        }

        private void DirectRestore_DoubleClick(object sender, EventArgs e)
        {
            m_owner.NextButton.PerformClick();
        }
    }
}
