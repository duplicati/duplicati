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
using Duplicati.Datamodel;
using Duplicati.Library.Utility;

namespace Duplicati.GUI.Wizard_pages.Restore
{
    public partial class SelectBackupVersion : WizardControl
    {
        WizardSettingsWrapper m_wrapper;
        DateTime m_selectedDate = new DateTime();

        public SelectBackupVersion()
            : base(Strings.SelectBackupVersion.PageTitle, Strings.SelectBackupVersion.PageDescription)
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(SelectBackupVersion_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectBackupVersion_PageLeave);
        }

        void SelectBackupVersion_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
            {
                BackupList.Abort();
                return;
            }

            if (BackupList.SelectedItem.Ticks == 0)
            {
                MessageBox.Show(this, Strings.SelectBackupVersion.NoBackupSelectedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            m_selectedDate = new DateTime();
            m_selectedDate = BackupList.SelectedItem;

            m_wrapper.RestoreTime = m_selectedDate;
            args.NextPage = new TargetFolder();
        }

        void SelectBackupVersion_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            m_wrapper.UpdateSchedule(m_wrapper.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID));
            BackupList.Setup(m_wrapper.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID));
        }

        private void BackupList_ItemDoubleClicked(object sender, EventArgs e)
        {
            m_owner.NextButton.PerformClick();
        }
    }
}
