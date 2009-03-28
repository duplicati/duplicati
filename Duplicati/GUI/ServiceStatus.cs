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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;
using Duplicati.Library.Core;

namespace Duplicati.GUI
{
    public partial class ServiceStatus : Form
    {
        public ServiceStatus()
        {
            InitializeComponent();

            imageList.Images.Clear();
            imageList.Images.Add("OK", Properties.Resources.OKStatusIcon);
            imageList.Images.Add("Warning", Properties.Resources.WarningStatusIcon);
            imageList.Images.Add("Error", Properties.Resources.ErrorStatusIcon);
        }

        private void ServiceStatus_Load(object sender, EventArgs e)
        {
            this.FormClosed += new FormClosedEventHandler(ServiceStatus_FormClosed);
            Program.WorkThread.StartingWork += new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork += new EventHandler(WorkThread_CompletedWork);
            Program.WorkThread.AddedWork += new EventHandler(WorkThread_AddedWork);
            Program.Scheduler.NewSchedule += new EventHandler(Scheduler_NewSchedule);
            Program.Runner.DuplicatiProgress += new DuplicatiRunner.DuplicatiRunnerProgress(Runner_DuplicatiProgress);

            if (Program.WorkThread.CurrentTask == null)
                WorkThread_CompletedWork(null, null);
            else
                WorkThread_StartingWork(null, null);

            WorkThread_AddedWork(null, null);
            Scheduler_NewSchedule(null, null);
            BuildRecent();
            PlaceAtBottom();

            if (Program.WorkThread.CurrentTask != null)
                Program.Runner.ReinvokeLastProgressEvent();
        }

        void Runner_DuplicatiProgress(Duplicati.Library.Main.DuplicatiOperation operation, DuplicatiRunner.RunnerState state, string message, string submessage, int progress, int subprogress)
        {
            if (this.InvokeRequired)
                this.Invoke(new DuplicatiRunner.DuplicatiRunnerProgress(Runner_DuplicatiProgress), operation, state, message, submessage, progress, subprogress);
            else
            {
                //TODO: Test the display!
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
            }
        }

        void ServiceStatus_FormClosed(object sender, FormClosedEventArgs e)
        {
            Program.WorkThread.StartingWork -= new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork -= new EventHandler(WorkThread_CompletedWork);
            Program.WorkThread.AddedWork -= new EventHandler(WorkThread_AddedWork);
            Program.Scheduler.NewSchedule -= new EventHandler(Scheduler_NewSchedule);
            Program.Runner.DuplicatiProgress -= new DuplicatiRunner.DuplicatiRunnerProgress(Runner_DuplicatiProgress);
        }

        void WorkThread_CompletedWork(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_CompletedWork), sender, e);
            else
            {
                CurrentStatus.Text = "Waiting for next backup";
                statusImage.Image = Properties.Resources.Status_OK;
                BuildRecent();
            }
        }

        void WorkThread_StartingWork(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_StartingWork), sender, e);
            else
            {
                Schedule c = Program.WorkThread.CurrentTask.Schedule;
                string prefix = Program.WorkThread.CurrentTask.TaskType == DuplicityTaskType.Restore ? "Restore: " : "Backup: ";

                CurrentStatus.Text = c == null ? "Waiting for next backup" : prefix + c.Name;
                statusImage.Image = Properties.Resources.Status_Working;
                WorkThread_AddedWork(sender, e);
            }
        }

        void WorkThread_AddedWork(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_AddedWork), sender, e);
            else
            {
                pendingBackups.Items.Clear();
                //No locking here, the list is protected by the thread raising the event
                foreach (IDuplicityTask t in Program.WorkThread.CurrentTasks)
                    pendingBackups.Items.Add(t.TaskType.ToString() + ": " + t.Schedule.Name == null ? "" : t.Schedule.Name);

            }

        }

        private void ShowAdvanced_Click(object sender, EventArgs e)
        {
            if (advancedPanel.Visible)
            {
                advancedPanel.Visible = false;
                simplePanel.Top = advancedPanel.Top;
                ShowAdvanced.Text = "Advanced <<<";
            }
            else
            {
                advancedPanel.Visible = true;
                simplePanel.Top = advancedPanel.Bottom;
                ShowAdvanced.Text = "Simple >>>";
            }

            bool reposition = (this.Top == (Screen.PrimaryScreen.WorkingArea.Height - this.Height) && this.Left == (Screen.PrimaryScreen.WorkingArea.Width - this.Width));

            this.Width = advancedPanel.Left * 2 + simplePanel.Width;
            this.Height = advancedPanel.Top * 4 + simplePanel.Height + (advancedPanel.Visible ? advancedPanel.Height : 0);

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
            logs = Program.DataConnection.GetObjects<Log>("EndTime > ? AND SubAction LIKE ? ORDER BY EndTime DESC", Timeparser.ParseTimeInterval(Program.ApplicationSettings.RecentBackupDuration, DateTime.Now, true), "Primary");


            recentBackups.Items.Clear();
            foreach (Log l in logs)
            {
                ListViewItem lvi = new ListViewItem(new string[] { l.EndTime.ToString("g", System.Globalization.CultureInfo.CurrentUICulture), l.OwnerTask == null || l.OwnerTask.Schedule == null ? "" : l.OwnerTask.Schedule.Name, l.TransferSizeString });

                lvi.Tag = l;
                lvi.ImageIndex = imageList.Images.ContainsKey(l.ParsedStatus) ? imageList.Images.IndexOfKey(l.ParsedStatus) : imageList.Images.IndexOfKey("Warning");
                recentBackups.Items.Add(lvi);
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
            //TODO: Figure out why the LDM fails here
            dlg.LogText.Text = l.Blob.StringData;

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

    }
}