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

namespace Duplicati.Wizard_pages.Add_backup
{
    public partial class SelectName : UserControl, IWizardControl, Interfaces.IScheduleBased
    {
        private Schedule m_schedule;

        public SelectName()
        {
            InitializeComponent();
            BackupFolder.treeView.HideSelection = false;
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return "Enter a name for the backup"; }
        }

        string IWizardControl.HelpText
        {
            get { return "On this page you can enter a name for the backup, so you can find and modify it later"; }
        }

        Image IWizardControl.Image
        {
            get { return null; }
        }

        bool IWizardControl.FullSize
        {
            get { return false; }
        }

        void IWizardControl.Enter(IWizardForm owner)
        {
            BackupName.Text = m_schedule.Name;
            BackupFolder.SelectedFolder = (string.IsNullOrEmpty(m_schedule.Path) ? "" : m_schedule.Path + BackupFolder.treeView.PathSeparator) + m_schedule.Name;
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            if (BackupName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter a name for the backup", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cancel = true;
                return;
            }
            m_schedule.Name = BackupName.Text;
            if (BackupFolder.treeView.SelectedNode != null)
                m_schedule.Path = BackupFolder.SelectedFolder;
            else
                m_schedule.Path = "";
        }

        #endregion

        #region IScheduleBased Members

        public void Setup(Schedule schedule)
        {
            m_schedule = schedule;
            BackupFolder.Setup(schedule.DataParent, true, true);
        }

        #endregion

        private void button1_Click(object sender, EventArgs e)
        {
            BackupFolder.SelectedFolder = null;
            BackupFolder.AddFolder(null);
        }
    }
}
