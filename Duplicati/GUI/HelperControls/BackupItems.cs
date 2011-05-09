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
        private object m_lock = new object();
        private DuplicatiRunner m_runner = null;

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
            WaitPanel.Dock = DockStyle.None;
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            progressBar.Visible = true;
            listView.Visible = false;

            backgroundWorker.RunWorkerAsync(schedule);
        }

        public DateTime SelectedItem 
        { 
            get 
            { 
                return listView.SelectedItems.Count != 1 ? new DateTime() : ((Library.Main.ManifestEntry)listView.SelectedItems[0].Tag).Time; 
            } 
        }

        private void viewFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListBackupFiles dlg = new ListBackupFiles();
            dlg.ShowList(this, m_schedule, this.SelectedItem);
        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                lock (m_lock)
                    m_runner = new DuplicatiRunner();
                e.Result = m_runner.ListBackupEntries(m_schedule);
                if (m_runner.IsAborted)
                    e.Cancel = true;
            }
            finally
            {
                lock (m_lock)
                    m_runner = null;
            }
        }

        public void Abort()
        {
            try
            {
                lock (m_lock)
                    if (m_runner != null)
                        m_runner.Terminate();
            }
            catch
            {
            }
        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            WaitPanel.Visible = false;

            if (e.Error != null || e.Cancelled || e.Result == null)
            {
                Exception ex = e.Error;
                if (ex == null && e.Cancelled)
                    ex = new Exception(Strings.BackupItems.OperationCancelled);
                else if (ex == null && e.Result == null)
                    ex = new Exception(Strings.BackupItems.NoDataReceived);

                progressBar.Visible = false;
                WaitPanel.Visible = true;
                WaitPanel.Dock = DockStyle.Fill;
                statusLabel.Text = string.Format(Strings.BackupItems.ErrorStatusDisplay, ex.Message);
                statusLabel.Visible = true;
                statusLabel.TextAlign = ContentAlignment.TopLeft;
                WaitPanel.Visible = true;

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

                    foreach (Library.Main.ManifestEntry ef in (List<Library.Main.ManifestEntry>)e.Result)
                    {
                        long fullSize = Math.Max(ef.Fileentry.Size, 0);
                        foreach (KeyValuePair<Library.Main.SignatureEntry, Library.Main.ContentEntry> v in ef.Volumes)
                            fullSize += Math.Max(v.Key.Fileentry.Size, 0) + Math.Max(v.Value.Fileentry.Size, 0);

                        ListViewItem n = new ListViewItem(ef.Time.ToLongDateString() + " " + ef.Time.ToLongTimeString(), 0);
                        n.Tag = ef;

                        if (fullSize <= 0)
                            n.ToolTipText = Strings.BackupItems.TooltipFullBackup;
                        else
                            n.ToolTipText = string.Format(Strings.BackupItems.TooltipFullBackupWithSize, Library.Utility.Utility.FormatSizeString(fullSize));
                        
                        listView.Items.Add(n);

                        foreach (Library.Main.ManifestEntry i in ef.Incrementals)
                        {
                            long incSize = Math.Max(i.Fileentry.Size, 0);
                            foreach (KeyValuePair<Library.Main.SignatureEntry, Library.Main.ContentEntry> v in i.Volumes)
                                incSize += Math.Max(v.Key.Fileentry.Size, 0) + Math.Max(v.Value.Fileentry.Size, 0);

                            ListViewItem nn = new ListViewItem(i.Time.ToLongDateString() + " " + i.Time.ToLongTimeString(), 1);
                            nn.Tag = i;

                            fullSize += incSize;

                            if (fullSize <= 0)
                                nn.ToolTipText = Strings.BackupItems.TooltipIncrementalBackup;
                            else
                                nn.ToolTipText = string.Format(Strings.BackupItems.TooltipIncrementalBackupWithSize, Library.Utility.Utility.FormatSizeString(incSize), Library.Utility.Utility.FormatSizeString(fullSize));
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

        private void listView_DoubleClick(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1 && ItemDoubleClicked != null)
                ItemDoubleClicked(sender, e);

        }

    }
}
