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
using System.Threading;
using Duplicati.Datamodel;

namespace Duplicati.GUI.HelperControls
{
    public partial class BackupItems : UserControl
    {
        public event EventHandler ListLoaded;
        public event EventHandler LoadError;
        public event EventHandler ItemDoubleClicked;
        private Datamodel.Schedule m_schedule;

        public BackupItems()
        {
            InitializeComponent();

            imageList.Images.Add("full", Properties.Resources.FullBackup);
            imageList.Images.Add("partial", Properties.Resources.PartialBackup);
        }

        public void Setup(Schedule schedule)
        {
            m_schedule = schedule;
            WaitPanel.Visible = true;
            statusLabel.Visible = false;
            listView.Visible = false;

            backgroundWorker.RunWorkerAsync(schedule);
        }

        public DateTime SelectedItem 
        { 
            get 
            { 
                return listView.SelectedItems.Count != 1 ? new DateTime() : ((Library.Main.BackupEntry)listView.SelectedItems[0].Tag).Time; 
            } 
        }

        private void viewFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListBackupFiles dlg = new ListBackupFiles();
            dlg.ShowList(this, m_schedule, this.SelectedItem);
        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            DuplicatiRunner r = new DuplicatiRunner();
            e.Result = r.ListBackupEntries(m_schedule);
        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WaitPanel.Visible = false;

            if (e.Error != null || e.Cancelled || e.Result == null)
            {
                Exception ex = e.Error;
                if (e.Cancelled)
                    ex = new Exception(Strings.BackupItems.OperationCancelled);
                else if (e.Result == null)
                    ex = new Exception(Strings.BackupItems.NoDataRecieved);

                progressBar.Visible = false;
                statusLabel.Text = string.Format(Strings.BackupItems.ErrorStatusDisplay, ex.Message);
                if (LoadError != null)
                    LoadError(this, null);
            }
            else
            {
                try
                {
                    listView.Visible = true;
                    listView.BeginUpdate();
                    listView.Items.Clear();

                    foreach (Library.Main.BackupEntry ef in (List<Library.Main.BackupEntry>)e.Result)
                    {
                        ListViewItem n = new ListViewItem(ef.Time.ToLongDateString() + " " + ef.Time.ToLongTimeString(), 0);
                        n.Tag = ef;
                        n.ToolTipText = Strings.BackupItems.TooltipFullBackup;
                        listView.Items.Add(n);

                        foreach (Library.Main.BackupEntry i in ef.Incrementals)
                        {
                            ListViewItem nn = new ListViewItem(i.Time.ToLongDateString() + " " + i.Time.ToLongTimeString(), 1);
                            nn.Tag = i;
                            nn.ToolTipText = Strings.BackupItems.TooltipPartialBackup;
                            listView.Items.Add(nn);

                        }
                    }
                }
                finally
                {
                    listView.EndUpdate();
                }

                if (ListLoaded != null)
                    ListLoaded(this, null);

            }

        }

    }
}
