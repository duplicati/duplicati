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
using Duplicati.Library.Core;

namespace Duplicati.GUI.Wizard_pages.Restore
{
    public partial class SelectBackup : WizardControl
    {
        WizardSettingsWrapper m_wrapper;
        DateTime m_selectedDate = new DateTime();

        public SelectBackup()
            : base("Select the backup to restore", "The list below shows all the avalible backups. Select one to restore")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(SelectBackup_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectBackup_PageLeave);
        }

        void SelectBackup_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (BackupList.SelectedItem == null)
            {
                MessageBox.Show(this, "You must select the backup to restore", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            m_selectedDate = new DateTime();
            try
            {
                m_selectedDate = DateTime.Parse(BackupList.SelectedItem);
            }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, "An error occured while parsing the time: " + ex.Message + "\r\nDo you want to try to restore the most current backup instead?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                m_selectedDate = new DateTime();
            }

            m_wrapper.RestoreTime = m_selectedDate;
            args.NextPage = new TargetFolder();
        }

        void SelectBackup_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            BackupList.Setup(Program.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID));
        }
    }
}
