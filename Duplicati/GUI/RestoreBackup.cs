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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;
using Duplicati.Library.Utility;

namespace Duplicati.GUI
{
    public partial class RestoreBackup : Form
    {
        private Schedule m_schedule;

        public RestoreBackup()
        {
            InitializeComponent();
            backupItems.listView.SelectedIndexChanged += new EventHandler(listView_SelectedIndexChanged);

#if DEBUG
            this.Text += " (DEBUG)";
#endif
        }

        void listView_SelectedIndexChanged(object sender, EventArgs e)
        {
            TargetFolder_TextChanged(null, null);
        }

        public void Setup(Schedule schedule)
        {
            m_schedule = schedule;
            backupItems.Setup(schedule);
        }

        private void TargetFolder_TextChanged(object sender, EventArgs e)
        {
            OKBtn.Enabled = backupItems.SelectedItem.Ticks > 0;
        }

        private void SelectTargetFolder_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                TargetFolder.Text = folderBrowserDialog.SelectedPath;
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            DateTime dt = new DateTime();
            dt = backupItems.SelectedItem;

            if (System.IO.Directory.Exists(TargetFolder.Text))
            {
                if (System.IO.Directory.GetFileSystemEntries(TargetFolder.Text).Length > 0)
                    if (MessageBox.Show(this, "The selected folder is not empty.\r\nDo you want to restore there anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                        return;
            }
            else
                if (MessageBox.Show(this, "The selected folder does not exist.\r\nDo you want to restore there anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
                    return;

            Program.WorkThread.AddTask(new RestoreTask(m_schedule, TargetFolder.Text, dt));
            Program.DisplayHelper.ShowStatus();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}