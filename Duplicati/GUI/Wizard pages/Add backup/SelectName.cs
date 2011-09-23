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

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class SelectName : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public SelectName()
            : base(Strings.SelectName.PageTitle, Strings.SelectName.PageDescription)
        {
            InitializeComponent();
            BackupFolder.treeView.HideSelection = false;

            base.PageEnter += new PageChangeHandler(SelectName_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectName_PageLeave);
            base.PageDisplay += new PageChangeHandler(SelectName_PageDisplay);
        }

        void SelectName_PageDisplay(object sender, PageChangedArgs args)
        {
            try { BackupName.Focus(); }
            catch { }
        }

        void SelectName_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (BackupName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.SelectName.NoNameEnteredError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            try
            {
                Schedule[] tmp = Program.DataConnection.GetObjects<Schedule>("Name LIKE ? AND Path LIKE ?", BackupName.Text, BackupFolder.SelectedFolder);
                if ((tmp.Length == 1 && tmp[0].ID != m_wrapper.ScheduleID) || tmp.Length > 1)
                {
                    MessageBox.Show(this, Strings.SelectName.DuplicateNameEnteredError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.SelectName.InvalidNameEnteredError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            m_wrapper.ScheduleName = BackupName.Text;
            m_wrapper.SchedulePath = BackupFolder.SelectedFolder;

            args.NextPage = new SelectFiles();
        }

        void SelectName_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            BackupFolder.Setup(Program.DataConnection, true, true);

            if (!m_valuesAutoLoaded)
            {
                BackupName.Text = m_wrapper.ScheduleName;
                BackupFolder.SelectedFolder = m_wrapper.SchedulePath;
            }

            try { BackupName.Focus(); }
            catch { }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            BackupFolder.SelectedFolder = null;
            //BackupFolder.Focus();
            BackupFolder.AddFolder(null).BeginEdit();
        }

        private void SelectName_Load(object sender, EventArgs e)
        {
            try { BackupName.Focus(); }
            catch { }
        }

    }
}
