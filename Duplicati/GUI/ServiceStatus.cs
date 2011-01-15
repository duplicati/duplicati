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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;
using Duplicati.Library.Utility;

namespace Duplicati.GUI
{
    public partial class ServiceStatus : Form
    {
        public ServiceStatus()
        {
            InitializeComponent();

            imageList.Images.Clear();
            imageList.Images.Add(DuplicatiOutputParser.OKStatus, Properties.Resources.OKStatusIcon);
            imageList.Images.Add(DuplicatiOutputParser.WarningStatus, Properties.Resources.WarningStatusIcon);
            imageList.Images.Add(DuplicatiOutputParser.ErrorStatus, Properties.Resources.ErrorStatusIcon);
            imageList.Images.Add(DuplicatiOutputParser.PartialStatus, Properties.Resources.PartialStatusIcon);

            //We have the display open in the designer, so we set the right size here
            this.Size = this.MinimumSize;
#if DEBUG
            this.Text += " (DEBUG)";
#endif
        }

        private void ServiceStatus_Load(object sender, EventArgs e)
        {
            this.FormClosed += new FormClosedEventHandler(ServiceStatus_FormClosed);
            Program.WorkThread.StartingWork += new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork += new EventHandler(WorkThread_CompletedWork);
            Program.WorkThread.WorkQueueChanged += new EventHandler(WorkThread_WorkQueueChanged);
            Program.Scheduler.NewSchedule += new EventHandler(Scheduler_NewSchedule);
            Program.Runner.DuplicatiProgress += new DuplicatiRunner.DuplicatiRunnerProgress(Runner_DuplicatiProgress);
            Program.LiveControl.StateChanged += new EventHandler(LiveControl_StateChanged);
            Program.DataConnection.AfterDataConnection += new System.Data.LightDatamodel.DataConnectionEventHandler(DataConnection_AfterDataConnection);

            LiveControl_StateChanged(null, null);

            WorkThread_WorkQueueChanged(null, null);
            Scheduler_NewSchedule(null, null);
            BuildRecent();
            PlaceAtBottom();

            if (Program.WorkThread.CurrentTask != null)
                Program.Runner.ReinvokeLastProgressEvent();
        }

        private delegate void EmptyDelegate();
        private void DataConnection_AfterDataConnection(object sender, System.Data.LightDatamodel.DataActions action)
        {
            if (action != System.Data.LightDatamodel.DataActions.Fetch)
                this.BeginInvoke(new EmptyDelegate(BuildRecent));
        }

        void LiveControl_StateChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new EventHandler(LiveControl_StateChanged), sender, e);
                return;
            }

            stopBackupToolStripMenuItem.Enabled = Program.WorkThread.Active;

            if (Program.LiveControl.State == LiveControls.LiveControlState.Paused)
            {
                pauseBackupToolStripMenuItem.Checked = true;
                pauseBackupToolStripMenuItem.Text = Strings.Common.MenuResume;
                CurrentStatus.Text = Strings.ServiceStatus.StatusPaused;
                statusImage.Image = Properties.Resources.Status_pause;
            }
            else
            {
                pauseBackupToolStripMenuItem.Checked = false;
                pauseBackupToolStripMenuItem.Text = Strings.Common.MenuPause;
                if (Program.WorkThread.CurrentTask == null)
                    WorkThread_CompletedWork(null, null);
                else
                    WorkThread_StartingWork(null, null);
            }
        }

        void Runner_DuplicatiProgress(Duplicati.Library.Main.DuplicatiOperation operation, DuplicatiRunner.RunnerState state, string message, string submessage, int progress, int subprogress)
        {
            if (this.InvokeRequired)
                this.Invoke(new DuplicatiRunner.DuplicatiRunnerProgress(Runner_DuplicatiProgress), operation, state, message, submessage, progress, subprogress);
            else
            {
                WorkProgressbar.Visible = ProgressMessage.Visible = state != DuplicatiRunner.RunnerState.Stopped;
                WorkProgressbar.Style = progress < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
                WorkProgressbar.Value = Math.Max(Math.Min(WorkProgressbar.Maximum, progress), WorkProgressbar.Minimum);
                ProgressMessage.Text = message;
                toolTip1.SetToolTip(SubProgressBar, submessage);

                SubProgressBar.Value = Math.Max(Math.Min(SubProgressBar.Maximum, subprogress), SubProgressBar.Minimum);
                if (!SubProgressBar.Visible && subprogress >= 0)
                    ProgressMessage_TextChanged(null, null);
                SubProgressBar.Visible = subprogress >= 0;

                toolTip1.SetToolTip(ProgressMessage, ProgressMessage.Text);
                toolTip1.SetToolTip(WorkProgressbar, ProgressMessage.Text);
            }
        }

        void Scheduler_NewSchedule(object sender, EventArgs e)
        {
            try
            {
                if (this.InvokeRequired)
                    this.Invoke(new EventHandler(Scheduler_NewSchedule), sender, e);
                else
                {
                    scheduledBackups.Items.Clear();
                    lock (Program.MainLock)
                        foreach (Schedule s in Program.Scheduler.Schedule)
                            scheduledBackups.Items.Add(s.When.ToString("g") + " " + s.Name);
                }
            }
            catch 
            {
                //TODO: This happens occasionally when the user closes the dialog because, the form is disposed before the event is detached
                //I've only seen it happen once
            }
        }

        void ServiceStatus_FormClosed(object sender, FormClosedEventArgs e)
        {
            Program.DataConnection.AfterDataConnection -= new System.Data.LightDatamodel.DataConnectionEventHandler(DataConnection_AfterDataConnection);
            Program.WorkThread.StartingWork -= new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork -= new EventHandler(WorkThread_CompletedWork);
            Program.WorkThread.WorkQueueChanged -= new EventHandler(WorkThread_WorkQueueChanged);
            Program.Scheduler.NewSchedule -= new EventHandler(Scheduler_NewSchedule);
            Program.Runner.DuplicatiProgress -= new DuplicatiRunner.DuplicatiRunnerProgress(Runner_DuplicatiProgress);
            Program.LiveControl.StateChanged -= new EventHandler(LiveControl_StateChanged);
        }

        void WorkThread_CompletedWork(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_CompletedWork), sender, e);
            else
            {
                if (Program.LiveControl.State != LiveControls.LiveControlState.Paused)
                {
                    CurrentStatus.Text = Strings.ServiceStatus.StatusWaiting;
                    statusImage.Image = Properties.Resources.Status_OK;
                    BuildRecent();
                }

                stopBackupToolStripMenuItem.Enabled = false;
            }
        }

        void WorkThread_StartingWork(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_StartingWork), sender, e);
            else
            {
                Schedule c = Program.WorkThread.CurrentTask.Schedule;
                string prefix = Program.WorkThread.CurrentTask.TaskType == DuplicityTaskType.Restore ? Strings.ServiceStatus.StatusRestore : Strings.ServiceStatus.StatusBackup;

                CurrentStatus.Text = c == null ? Strings.ServiceStatus.StatusWaiting : string.Format(prefix, c.Name);
                statusImage.Image = Properties.Resources.Status_Working;
                WorkThread_WorkQueueChanged(sender, e);
                stopBackupToolStripMenuItem.Enabled = true;
            }
        }

        void WorkThread_WorkQueueChanged(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_WorkQueueChanged), sender, e);
            else
            {
                try
                {
                    pendingBackups.BeginUpdate();
                    pendingBackups.Items.Clear();
                    //No locking here, the list is protected by the thread raising the event
                    foreach (IDuplicityTask t in Program.WorkThread.CurrentTasks)
                        pendingBackups.Items.Add(Program.LocalizeTaskType(t.TaskType) + ": " + t.Schedule.Name == null ? "" : t.Schedule.Name);
                }
                finally
                {
                    pendingBackups.EndUpdate();
                }
            }

        }

        private void ShowAdvanced_Click(object sender, EventArgs e)
        {
            int height = simplePanel.Height + (this.Padding.All + simplePanel.Margin.All) * 5;
            bool reposition = (this.Top == (Screen.PrimaryScreen.WorkingArea.Height - this.Height) && this.Left == (Screen.PrimaryScreen.WorkingArea.Width - this.Width));

            if (advancedPanel.Visible)
            {
                advancedPanel.Visible = false;
                ShowAdvanced.Text = Strings.ServiceStatus.SwitchToAdvanced;
            }
            else
            {
                advancedPanel.Visible = true;
                ShowAdvanced.Text = Strings.ServiceStatus.SwitchToSimple;
            }


            this.Height = (advancedPanel.Visible ? (advancedPanel.Height - (this.Padding.All + simplePanel.Margin.All)) : 0) + height;

            if (reposition)
                PlaceAtBottom();
        }

        private void PlaceAtBottom()
        {
            this.Top = Screen.PrimaryScreen.WorkingArea.Height - this.Height;
            this.Left = Screen.PrimaryScreen.WorkingArea.Width - this.Width;
        }

        private void BuildRecent()
        {
            Log[] logs;
            lock(Program.MainLock)
                logs = Program.DataConnection.GetObjects<Log>("EndTime > ? AND SubAction LIKE ? ORDER BY EndTime DESC", Timeparser.ParseTimeInterval(new Datamodel.ApplicationSettings(Program.DataConnection).RecentBackupDuration, DateTime.Now, true), "Primary");

            try
            {
                recentBackups.BeginUpdate();
                recentBackups.Items.Clear();
                foreach (Log l in logs)
                {
                    ListViewItem lvi = new ListViewItem(new string[] { l.EndTime.ToString("g", System.Globalization.CultureInfo.CurrentUICulture), l.OwnerTask == null || l.OwnerTask.Schedule == null ? "" : l.OwnerTask.Schedule.Name, l.Transfersize > 0 ? l.TransferSizeString : "" });

                    lvi.Tag = l;
                    lvi.ImageIndex = imageList.Images.ContainsKey(l.ParsedStatus) ? imageList.Images.IndexOfKey(l.ParsedStatus) : imageList.Images.IndexOfKey("Warning");
                    recentBackups.Items.Add(lvi);

                    if (!string.IsNullOrEmpty(l.ParsedMessage))
                    {
                        lvi.ToolTipText = l.ParsedMessage;
                    }
                    else
                    {
                        switch (l.ParsedStatus)
                        {
                            case DuplicatiOutputParser.OKStatus:
                                lvi.ToolTipText = Strings.ServiceStatus.BackupStatusOK;
                                break;
                            case DuplicatiOutputParser.ErrorStatus:
                                lvi.ToolTipText = Strings.ServiceStatus.BackupStatusError;
                                break;
                            case DuplicatiOutputParser.WarningStatus:
                                lvi.ToolTipText = Strings.ServiceStatus.BackupStatusWarning;
                                break;
                            case DuplicatiOutputParser.PartialStatus:
                                lvi.ToolTipText = Strings.ServiceStatus.BackupStatusPartial;
                                break;
                        }
                    }
                }
            }
            finally
            {
                recentBackups.EndUpdate();
            }
        }

        private void recentBackups_DoubleClick(object sender, EventArgs e)
        {
            if (recentBackups.SelectedItems.Count != 1)
                return;

            Log l = recentBackups.SelectedItems[0].Tag as Log;
            if (l == null)
                return;

            LogViewer dlg = new LogViewer();
            if (!string.IsNullOrEmpty(l.ParsedMessage))
                dlg.LogText.Text = l.ParsedMessage + Environment.NewLine + Environment.NewLine;
            else
                dlg.LogText.Text = "";

            //TODO: Figure out if LDM still fails here
            dlg.LogText.Text += l.Blob.StringData;

            dlg.ShowDialog(this);
        }

        private void ServiceStatus_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                e.Handled = true;
            }
        }

        private void ProgressMessage_TextChanged(object sender, EventArgs e)
        {
            int gap = ProgressMessage.Left - statusImage.Right;
            SubProgressBar.Width = Math.Max(0, (simplePanel.Width - ProgressMessage.Right) - (gap * 2));
            SubProgressBar.Left = simplePanel.Width - SubProgressBar.Width;
        }

        private void recentBackups_SelectedIndexChanged(object sender, EventArgs e)
        {
            viewFilesToolStripMenuItem.Enabled = viewLogToolStripMenuItem.Enabled = recentBackups.SelectedItems.Count == 1;
        }

        private void viewFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (recentBackups.SelectedItems.Count != 1)
                return;

            Log l = recentBackups.SelectedItems[0].Tag as Log;
            if (l == null)
                return;
            Schedule s = l.OwnerTask.Schedule;
            DateTime time = l.EndTime; //Not the exact time to use, but close enough unless there were multiple backups running at the same time

            ListBackupFiles dlg = new ListBackupFiles();
            dlg.ShowList(this, s, time);
        }

        private void viewLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            recentBackups_DoubleClick(sender, e);
        }

        private void pauseBackupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.LiveControl.State == LiveControls.LiveControlState.Running)
                Program.LiveControl.Pause();
            else
                Program.LiveControl.Resume();
        }

        private void stopBackupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Program.Runner.Stop(CloseReason.UserClosing);
        }

        private void statusImage_Click(object sender, EventArgs e)
        {
            backupTasks.Show(Cursor.Position.X, Cursor.Position.Y);
        }

        private void statusImage_DoubleClick(object sender, EventArgs e)
        {
            pauseBackupToolStripMenuItem_Click(sender, e);
        }

        private void PauseDurationMenu_Click(object sender, EventArgs e)
        {
            Program.LiveControl.Pause((string)((ToolStripItem)sender).Tag);
        }

        private void ServiceStatus_Resize(object sender, EventArgs e)
        {
            ProgressMessage.MaximumSize = new Size(WorkProgressbar.Width, ProgressMessage.MaximumSize.Height);
        }
     }
}