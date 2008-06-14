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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati
{
    public partial class ServiceStatus : Form
    {
        public ServiceStatus()
        {
            InitializeComponent();
        }

        private void ServiceStatus_Load(object sender, EventArgs e)
        {
            this.FormClosed += new FormClosedEventHandler(ServiceStatus_FormClosed);
            Program.WorkThread.StartingWork += new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork += new EventHandler(WorkThread_CompletedWork);
            Program.WorkThread.AddedWork += new EventHandler(WorkThread_AddedWork);
            Program.Scheduler.NewSchedule += new EventHandler(Scheduler_NewSchedule);

            Schedule c = Program.WorkThread.CurrentTask;
            if (c == null)
                WorkThread_CompletedWork(null, null);
            else
                WorkThread_StartingWork(null, null);

            WorkThread_AddedWork(null, null);
            Scheduler_NewSchedule(null, null);
            BuildRecent();
            PlaceAtBottom();
        }

        void Scheduler_NewSchedule(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(Scheduler_NewSchedule), sender, e);
            else
            {
                scheduledBackups.Items.Clear();
                lock(Program.MainLock)
                    foreach (Schedule s in Program.Scheduler.Schedule)
                        scheduledBackups.Items.Add(s.When.ToString("g") + " " + s.Name);
            }
        }

        void ServiceStatus_FormClosed(object sender, FormClosedEventArgs e)
        {
            Program.WorkThread.StartingWork -= new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork -= new EventHandler(WorkThread_CompletedWork);
            Program.WorkThread.AddedWork -= new EventHandler(WorkThread_AddedWork);
            Program.Scheduler.NewSchedule -= new EventHandler(Scheduler_NewSchedule);
        }

        void WorkThread_CompletedWork(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_CompletedWork), sender, e);
            else
            {
                CurrentStatus.Text = "Waiting for next backup";
                statusImage.Image = Program.NormalImage;
                BuildRecent();
            }
        }

        void WorkThread_StartingWork(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(WorkThread_StartingWork), sender, e);
            else
            {
                Schedule c = Program.WorkThread.CurrentTask;
                CurrentStatus.Text = c == null ? "Waiting for next backup" : "Running " + c.Name;
                statusImage.Image = Program.WorkingImage;
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
                foreach (Schedule s in Program.WorkThread.CurrentTasks)
                    pendingBackups.Items.Add(s.Name == null ? "" : s.Name);

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
                ListViewItem lvi = new ListViewItem(new string[] { l.EndTime.ToString("g", System.Globalization.CultureInfo.CurrentUICulture), l.OwnerTask.Schedule.Name, l.TransferSizeString });

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
            dlg.LogText.Text = l.LogBlob.StringData;

            dlg.ShowDialog(this);
        }

    }
}