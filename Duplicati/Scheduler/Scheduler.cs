#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
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
using System.Data;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;

// This is a x86 exe - If you are using Express, see this:
// http://social.msdn.microsoft.com/Forums/en-US/Vsexpressvcs/thread/4650481d-b385-43f3-89c7-c07546a7f7cd
//
namespace Duplicati.Scheduler
{
    /// <summary>
    /// The mother form, shows a Summary and a tool menu
    /// </summary>
    public partial class Scheduler : Form
    {
        /// <summary>
        /// The mother form, shows a Summary and a tool menu
        /// </summary>
        public Scheduler()
        {
            InitializeComponent();
            this.Text = "Schedule (" + Utility.User.UserName + ")";
            // Load the database
            this.SchedulerDataSet.Load();
            // Advertise for pipe connections
            Pipe.Server(OperationProgress);
            // Put up the navigation buttons
            EnableNav();
#if DEBUG
            this.toolStripButton2.Visible = true;
#endif
        }
        private System.IO.FileSystemWatcher itsLogWatcher;
        // This little guy just looks for new log files and turns the "Log" button green if one arrives
        private void InitializeLogWatcher()
        {
            itsLogWatcher = new System.IO.FileSystemWatcher(Utility.Tools.LogFileDirectory(Program.Package), "*.txt") { NotifyFilter = System.IO.NotifyFilters.Attributes };
            itsLogWatcher.Changed += new System.IO.FileSystemEventHandler(
                delegate(object sender, System.IO.FileSystemEventArgs e)
                {
                    this.BeginInvoke((Action)delegate() { this.LogToolStripButton.ForeColor = System.Drawing.Color.Green; });
                });
            itsLogWatcher.EnableRaisingEvents = true;
        }
        /// <summary>
        /// Adds a new job to the task scheduler
        /// </summary>
        /// <param name="aRow">Job to add</param>
        /// <param name="aTrigger">Trigger to add</param>
        /// <returns>True if all went well</returns>
        private bool NewTask(Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow aRow, Microsoft.Win32.TaskScheduler.Trigger aTrigger)
        {
            // DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG DEBUG
            if (this.toolStripButton2.Checked && aTrigger is Microsoft.Win32.TaskScheduler.TimeTrigger)
            {
                ((Microsoft.Win32.TaskScheduler.TimeTrigger)aTrigger).Repetition.Interval = TimeSpan.FromMinutes(5);
                ((Microsoft.Win32.TaskScheduler.TimeTrigger)aTrigger).Repetition.Duration = TimeSpan.FromDays(1);
            }
            // Used for password
            System.Security.SecureString ss = null;
            bool Result = false;
            do
            {
                DialogResult dr = DialogResult.None;
                // Get the user credentials from the user
                Result = Utility.Tools.NoException((Action)delegate()
                {
                    dr = TaskScheduler.InvokeCredentialDialog(this, aRow.TaskName,
                        "Enter user account information for running this backup:", Utility.User.UserName, out ss);
                });
                // If there was an error or user pressed Cancel, bail.
                if (!Result || dr != DialogResult.OK) return false;
                // Send the Trigger to the task scheduler
                Result = Utility.Tools.NoException((Action)delegate()
                {
                    TaskScheduler.CreateOrUpdateTask(aRow.TaskName,
                        System.IO.Path.Combine(Application.StartupPath, "Duplicati.Scheduler.RunBackup.exe"),
                        "Duplicati backup task", Utility.User.UserName, ss, aTrigger,
                        "\"" + aRow.Name + "\" \"" + Duplicati.Scheduler.Data.SchedulerDataSet.DefaultPath() + "\"");
                });
                // Assume any error is a security thing and make user enter password again.
                if (!Result && MessageBox.Show("Logon information is not valid for " + Utility.User.UserName + ", [OK] to try again?",
                    "Wrong", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.Cancel) return false;
            } while (!Result);
            return Result; // gonna be true
        }
        /// <summary>
        /// Pressed ADD button, add a new job
        /// </summary>
        private void AddToolStripButton_Click(object sender, EventArgs e)
        {
            bool OK = false;
            // Get the name, must be unique
            SchedulerNameDialog snd = new SchedulerNameDialog();
            do
            {
                if (snd.ShowDialog() == DialogResult.Cancel) return;
                OK = this.SchedulerDataSet.Jobs.FindByName(snd.BackupName) == null;
                if (!OK) MessageBox.Show("Backup name " + snd.BackupName + " is already used.");
            } while (!OK);
            this.Cursor = Cursors.WaitCursor;
            // Fire up the editor
            JobDialog sd = new JobDialog(this.SchedulerDataSet.Jobs.NewJobsRow(snd.BackupName));
            if (sd.ShowDialog() == DialogResult.OK && NewTask(sd.Row, sd.Trigger))
            {
                // Add this to the database
                this.SchedulerDataSet.Jobs.AddJobsRow(sd.Row);
                Save(sd.Row);
                this.jobSummary1.Enabled = sd.Row.Enabled;
            }
            this.Cursor = Cursors.Default;
        }
        /// <summary>
        /// Reflects the current JobsBindingSource row as a JobsRow
        /// </summary>
        private Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow CurrentRow
        {
            get
            {
                if (this.JobsBindingSource.Current == null) return null;
                return (Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow)((DataRowView)this.JobsBindingSource.Current).Row;
            }
        }
        /// <summary>
        /// Pressed the EDIT button
        /// </summary>
        private void EditToolStripButton_Click(object sender, EventArgs e)
        {
            if (CurrentRow == null) return;
            this.Cursor = Cursors.WaitCursor;
            // Fire up the editor
            JobDialog sd = new JobDialog(CurrentRow);
            // Get the trigger
            sd.Trigger = TaskScheduler.GetTrigger(CurrentRow.TaskName);
            // User said OK and the Task Scheduler took it OK, so save it
            if (sd.ShowDialog() == DialogResult.OK && NewTask(sd.Row, sd.Trigger))
            {
                // Save the edited row
                Save(sd.Row);
                this.jobSummary1.Enabled = sd.Row.Enabled;
            }
            // Updates the screen
            JobsBindingSource_CurrentChanged(null, null);
            this.Cursor = Cursors.Default;  // Should be in a 'finnaly'
        }
        /// <summary>
        /// Save the database
        /// </summary>
        /// <param name="aRow">Altered row</param>
        private void Save(Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow aRow)
        {
            if (aRow == null) return; //
            aRow.LastMod = DateTime.Now;
            // Update the drive table
            System.Collections.Generic.Dictionary<string, string> DriveMap = new System.Collections.Generic.Dictionary<string, string>();
            foreach (System.IO.DriveInfo di in System.IO.DriveInfo.GetDrives())
                if ((di.IsReady && di.DriveType == System.IO.DriveType.Network))
                    DriveMap.Add(di.Name[0].ToString(), Utility.Tools.DriveToUNC(di.Name[0]));
            aRow.SetDriveMaps(DriveMap);

            Exception Ex = this.SchedulerDataSet.Save();
            if (Ex != null)
                MessageBox.Show("Can not save schedule: " + Ex.Message);
        }
        /// <summary>
        /// Pressed the LOG button - show the history
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LogToolStripButton_Click(object sender, EventArgs e)
        {
            // If button was green, ungreen it
            this.LogToolStripButton.ForeColor = this.FilesToolStripButton.ForeColor;
            if (CurrentRow == null) return;
            LogView lv = new LogView(CurrentRow.Name);
            lv.ShowDialog();
        }
        /// <summary>
        /// Pressed the RESTORE button - List the backups
        /// </summary>
        private void FilesToolStripButton_Click(object sender, EventArgs e)
        {
            if (CurrentRow == null) return;
            BackupListDialog fd = new BackupListDialog(CurrentRow.Name, CurrentRow.Destination, CurrentRow.Options);
            fd.ShowDialog();
        }
        /// <summary>
        /// Pressed the select dropdown, populate the drop-downs
        /// </summary>
        private void SelectToolStripDropDownButton_DropDownOpening(object sender, EventArgs e)
        {
            // Make a drop down entry for every job in the database
            this.SelectToolStripDropDownButton.DropDownItems.Clear();
            this.SelectToolStripDropDownButton.DropDownItems.AddRange(
                (from Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow qR in this.SchedulerDataSet.Jobs.Select()
                 select new ToolStripMenuItem(qR.Name)).ToArray());
        }
        /// <summary>
        /// Clicked a SELECT drop-down - Go to that record
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectToolStripDropDownButton_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            // This is how one moves a bindingsource record...
            int ix = this.JobsBindingSource.Find("Name", e.ClickedItem.Text);
            if (ix >= 0) this.JobsBindingSource.Position = ix;
        }
        /// <summary>
        /// Pressed a NEXT or PREV
        /// </summary>
        private void PositionButtons_Click(object sender, EventArgs e)
        {
            if (((ToolStripButton)sender).Text == "next") this.JobsBindingSource.MoveNext();
            else this.JobsBindingSource.MovePrevious();
            EnableNav();
        }
        /// <summary>
        /// Make sure the correct NEXT, PREV, SELECT buttons are lit depending on the binding
        /// </summary>
        private void EnableNav()
        {
            this.SelectToolStripDropDownButton.Enabled = this.JobsBindingSource.Count > 1;
            this.NextToolStripButton.Enabled = this.JobsBindingSource.Position < this.JobsBindingSource.Count-1;
            this.PrevToolStripButton.Enabled = this.JobsBindingSource.Position > 0;
            this.EditToolStripButton.Enabled = 
                this.DelToolStripButton.Enabled =
                this.FilesToolStripButton.Enabled =
                this.LogToolStripButton.Enabled =
                this.JobsBindingSource.Count > 0;
            this.SelectToolStripDropDownButton.Text = "Select (" + this.JobsBindingSource.Count.ToString() + ")";
        }
        /// <summary>
        /// Selected a new record, adjust everything
        /// </summary>
        private void JobsBindingSource_CurrentChanged(object sender, EventArgs e)
        {
            if (CurrentRow == null)
            {
                this.jobSummary1.Clear();
            }
            else
            {
                this.jobSummary1.SetSummary(CurrentRow, TaskScheduler.Describe(CurrentRow.TaskName));
                this.jobSummary1.Enabled = TaskScheduler.Enabled(CurrentRow.TaskName);
            }
            EnableNav();
            if (CurrentRow == null) this.JobsBindingSource.MoveFirst(); // Will reenter if there are any more records
        }
        /// <summary>
        /// Pressed the DELETE button
        /// </summary>
        private void DelToolStripButton_Click(object sender, EventArgs e)
        {
            if (CurrentRow == null || MessageBox.Show("Do you want to delete this job?", "DELETE", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return;
            CurrentRow.Delete();
            Save(null);
            // Updates the screen
            JobsBindingSource_CurrentChanged(null, null);
        }
        /// <summary>
        /// Update the progress bar
        /// </summary>
        private void SetProgress(int aProgress, string aMessage)
        {
            Debug.WriteLine(aMessage + ":" + aProgress.ToString());
            try
            {
                if (aMessage.EndsWith("Completed")) aProgress = 100;
                if (aProgress < 0)
                {
                    this.toolStripProgressBar1.Style = ProgressBarStyle.Marquee;
                }
                else
                {
                    this.toolStripProgressBar1.Style = ProgressBarStyle.Continuous;
                    //if (aProgress > this.toolStripProgressBar1.Maximum) this.toolStripProgressBar1.Maximum = aProgress + 20;
                    this.toolStripProgressBar1.Value = Math.Min(this.toolStripProgressBar1.Maximum, aProgress);
                }
                if (!string.IsNullOrEmpty(aMessage))
                {
                    this.toolStripStatusLabel1.Text = aMessage;
                    this.toolStripStatusLabel1.ToolTipText = aMessage;
                }
            }
            catch (Exception Ex)
            {
                Debug.WriteLine(Ex);
            }
        }
        /// <summary>
        /// Called from the pipe thread to indicate progress from the exec
        /// </summary>
        /// <param name="aMessage">A message from the exec</param>
        private void OperationProgress(string aMessage)
        {
            if (!this.IsHandleCreated || string.IsNullOrEmpty(aMessage)) return;
            // Decode the message
            Duplicati.Scheduler.RunBackup.Pipe.ProgressArguments pa = new Duplicati.Scheduler.RunBackup.Pipe.ProgressArguments(aMessage);
            // Update the screen, be sure to use the forms thread
            if (pa.OK) this.BeginInvoke((Action)delegate()
                { SetProgress(pa.Progress, pa.Job + ":" + pa.Operation + ":" + pa.Message); });
        }
        /// <summary>
        /// Pressed the SETTINGS button, edit settings
        /// </summary>
        private void SettingsToolStripButton_Click(object sender, EventArgs e)
        {
            // Deals with its own save, cancel, etc.
            new SettingsDialog(this.SchedulerDataSet.Settings).ShowDialog();
        }

        private void helpToolStripButton_Click(object sender, EventArgs e)
        {

        }
        /// <summary>
        /// The settings drop down is coming down, load the items
        /// </summary>
        private void SettingsToolStripDropDownButton_DropDownOpening(object sender, EventArgs e)
        {
            // If the drop downs are empty, make them
            if (this.monitorSettingsToolStripMenuItem.Enabled && this.monitorSettingsToolStripMenuItem.DropDownItems.Count == 0)
            {
                // Look for plug in's
                string[] Plugins = System.IO.Directory.GetFiles(Application.StartupPath, "Duplicati.Scheduler.Monitor.*.dll");
                // No plugins?  Disable the settings
                if (Plugins.Length == 0)
                {
                    this.monitorSettingsToolStripMenuItem.Enabled = false;
                    this.monitorSettingsToolStripMenuItem.Visible = false;
                }
                else
                {
                    // Load a drop down for each plugin
                    foreach (string Plug in Plugins)
                    {
                        // Load the assembly [ Shouldn't there be an Unload? ]
                        System.Reflection.Assembly Ass = System.Reflection.Assembly.LoadFile(Plug);
                        if (Ass != null)
                        {
                            // Get the update type
                            Type ClassType = Ass.GetTypes().Where(qR => qR.Name == "Plugin").First();
                            Duplicati.Scheduler.Data.IMonitorPlugin Monitor = Activator.CreateInstance(ClassType, null) 
                                as Duplicati.Scheduler.Data.IMonitorPlugin;
                            bool Disabled = this.SchedulerDataSet.Settings.DisabledMonitors.Contains(Monitor.Name);
                            ToolStripMenuItem tsi = new ToolStripMenuItem(Monitor.Name) 
                            { 
                                Tag = Monitor, 
                                Checked = !Disabled, 
                                ForeColor = Disabled ? System.Drawing.Color.Gray : this.ForeColor 
                            };
                            tsi.MouseUp += new MouseEventHandler(tsi_MouseUp);
                            this.monitorSettingsToolStripMenuItem.DropDownItems.Add(tsi);
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Use the mouse up event to determine if user is pressing the check mark area or the drop down
        /// </summary>
        private void tsi_MouseUp(object sender, MouseEventArgs e)
        {
            // Only interested in left clicks
            if (e.Button != MouseButtons.Left) return;
            ToolStripMenuItem tsi = (ToolStripMenuItem)sender;
            // The monitor is in the tag
            Duplicati.Scheduler.Data.IMonitorPlugin Mon = (Duplicati.Scheduler.Data.IMonitorPlugin)tsi.Tag;
            // X < 28, user clicked check mark area
            if (e.X < 28 || !tsi.Checked)
            {
                // Toggle checked
                tsi.Checked = !tsi.Checked;
                // Enable/Disable the monitor
                this.SchedulerDataSet.Settings.EnableMonitor(Mon.Name, tsi.Checked);
                this.SchedulerDataSet.Save();
            }
            // Clicked the sweet spot, show the configuration menu from the monitor
            else if (tsi.Checked)
                Mon.Configure();
            // Turn gray if not selected
            tsi.ForeColor = tsi.Checked ? this.ForeColor : System.Drawing.Color.Gray;
        }
    }
}
