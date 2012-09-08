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

namespace Duplicati.GUI
{
    /// <summary>
    /// This class handles the Duplicati TrayIcon
    /// </summary>
    public partial class MainForm : Form
    {
        private static readonly int BALLOON_SHOW_TIME = (int)TimeSpan.FromSeconds(60).TotalMilliseconds;
        private ServiceStatus StatusDialog;
        private WizardHandler WizardDialog;
        private Datamodel.ApplicationSettings m_settings;

        private delegate void EmptyDelegate();
		
		private string[] m_initialArguments;

        private bool m_hasAttemptedBackupTermination = false;

        private DuplicatiRunner.RunnerResult m_currentIconState;
        private Icon m_currentIcon;
        private string m_currentTooltip;
        private TrayIconProxy m_trayIcon;

        /// <summary>
        /// A recursion protection helper flag
        /// </summary>
        private bool m_inQuit = false;

        /// <summary>
        /// Internal helper class that can filter all access to the trayicon if the program runs without a trayicon
        /// </summary>
        private class TrayIconProxy
        {
            private readonly NotifyIcon m_owner = null;

            private Icon m_icon;
            private bool m_visible;
            private string m_text;

            public TrayIconProxy(NotifyIcon owner)
            {
                if (!Program.TraylessMode)
                    m_owner = owner;
            }

            public Icon Icon
            {
                get { return m_icon; }
                set 
                {
                    m_icon = value;
                    if (m_owner != null)
                        m_owner.Icon = value; 
                }
            }

            public bool Visible
            {
                get { return m_visible; }
                set
                {
                    m_visible = value;
                    if (m_owner != null)
                        m_owner.Visible = value;
                }
            }

            public string Text
            {
                get { return m_text; }
                set
                {
                    m_text = value;
                    if (m_owner != null)
                        m_owner.Text = value;
                }
            }

            public void ShowBalloonTip(int timeout, string tipTitle, string tipText, ToolTipIcon tipIcon)
            {
                if (m_owner != null)
                    m_owner.ShowBalloonTip(timeout, tipTitle, tipText, tipIcon);
            }
        }

        public string[] InitialArguments
        {
            get { return m_initialArguments; }
            set { m_initialArguments = value; }
        }

        public MainForm()
        {
            InitializeComponent();

            m_trayIcon = new TrayIconProxy(TrayIcon);
            m_currentIcon = Properties.Resources.TrayNormal;
            m_currentTooltip = Strings.MainForm.TrayStatusReady;

            Program.LiveControl.StateChanged += new EventHandler(LiveControl_StateChanged);
            Program.WorkThread.StartingWork += new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork += new EventHandler(WorkThread_CompletedWork);
            Program.SingleInstance.SecondInstanceDetected += new SingleInstance.SecondInstanceDelegate(SingleInstance_SecondInstanceDetected);

            m_settings = new Duplicati.Datamodel.ApplicationSettings(Program.DataConnection);
            Program.DataConnection.AfterDataChange += new System.Data.LightDatamodel.DataChangeEventHandler(DataConnection_AfterDataChange);

            Program.Runner.ProgressEvent += new DuplicatiRunner.ProgressEventDelegate(Runner_DuplicatiProgress);
            Program.Runner.ResultEvent += new DuplicatiRunner.ResultEventDelegate(Runner_ResultEvent);
#if DEBUG
            this.Text += " (DEBUG)";
#endif
        }

        public void SetCurrentIcon(DuplicatiRunner.RunnerResult icon, string message)
        {
            if (icon == DuplicatiRunner.RunnerResult.Error)
            {
                m_currentIcon = Properties.Resources.TrayNormalError;
                m_currentIconState = icon;
                m_currentTooltip = message;

            }
            else if ((icon == DuplicatiRunner.RunnerResult.Warning || icon == DuplicatiRunner.RunnerResult.Partial) && m_currentIconState != DuplicatiRunner.RunnerResult.Error)
            {
                m_currentIcon = Properties.Resources.TrayNormalWarning;
                m_currentIconState = icon;
                m_currentTooltip = message;
            }
            
            UpdateTrayIcon();
        }

        public void ResetCurrentIcon()
        {
            m_currentIcon = Properties.Resources.TrayNormal;
            m_currentTooltip = Strings.MainForm.TrayStatusReady;
            m_currentIconState = DuplicatiRunner.RunnerResult.OK;
            UpdateTrayIcon();
        }

        private void UpdateTrayIcon()
        {
            //TODO: Hard to maintain consistent state because the number of cases here can change
            if (Program.LiveControl.State == LiveControls.LiveControlState.Running && !Program.WorkThread.Active)
            {
                m_trayIcon.Icon = m_currentIcon;
                SetTrayIconText(m_currentTooltip);
            }
        }

        void Runner_ResultEvent(DuplicatiRunner.RunnerResult result, string parsedMessage, string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new DuplicatiRunner.ResultEventDelegate(Runner_ResultEvent), result, parsedMessage, message);
                return;
            }

            string name = "";

            //Dirty read of the instance variable
            try { name = Program.WorkThread.CurrentTask.Schedule.Name; }
            catch { }

            if (result == DuplicatiRunner.RunnerResult.Error)
                SetCurrentIcon(result, String.Format(Strings.MainForm.BalloonTip_Error, name, parsedMessage));
            else if (result == DuplicatiRunner.RunnerResult.Partial || result == DuplicatiRunner.RunnerResult.Warning)
                SetCurrentIcon(result, String.Format(Strings.MainForm.BalloonTip_Warning, name));
                

            if (result == DuplicatiRunner.RunnerResult.OK || m_settings.BallonNotificationLevel == Duplicati.Datamodel.ApplicationSettings.NotificationLevel.Off)
                return;

            if (result == DuplicatiRunner.RunnerResult.Error)
                m_trayIcon.ShowBalloonTip(BALLOON_SHOW_TIME, Application.ProductName, String.Format(Strings.MainForm.BalloonTip_Error, name, parsedMessage), ToolTipIcon.Error);

            else if (result == DuplicatiRunner.RunnerResult.Warning || result == DuplicatiRunner.RunnerResult.Partial)
                m_trayIcon.ShowBalloonTip(BALLOON_SHOW_TIME, Application.ProductName, String.Format(Strings.MainForm.BalloonTip_Warning, name), ToolTipIcon.Warning);
        }

        void DataConnection_AfterDataChange(object sender, string propertyname, object oldvalue, object newvalue)
        {
            if (sender as Datamodel.ApplicationSetting == null)
                return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new System.Data.LightDatamodel.DataChangeEventHandler(DataConnection_AfterDataChange), sender, propertyname, oldvalue, newvalue);
                return;
            }

            m_settings = new Duplicati.Datamodel.ApplicationSettings(Program.DataConnection);
        }

        void Runner_DuplicatiProgress(Duplicati.Library.Main.DuplicatiOperation operation, DuplicatiRunner.RunnerState state, string message, string submessage, int progress, int subprogress)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new DuplicatiRunner.ProgressEventDelegate(Runner_DuplicatiProgress), operation, state, message, submessage, progress, subprogress);
                return;
            }

            string name = "";

            //Dirty read of the instance variable
            try { name = Program.WorkThread.CurrentTask.Schedule.Name; }
            catch { }

            Datamodel.ApplicationSettings.NotificationLevel level;
            try { level = m_settings.BallonNotificationLevel; }
            catch
            {
                m_settings = new Datamodel.ApplicationSettings(Program.DataConnection);
                try { level = m_settings.BallonNotificationLevel; }
                catch
                {
                    //TODO: Should find the cause for this, but at least we do not crash the process
                    return;
                }
            }


            if (state == DuplicatiRunner.RunnerState.Started && (level == Duplicati.Datamodel.ApplicationSettings.NotificationLevel.StartAndStop || level == Duplicati.Datamodel.ApplicationSettings.NotificationLevel.Start || level == Duplicati.Datamodel.ApplicationSettings.NotificationLevel.Continous))
            {
                //Show start balloon
                m_trayIcon.ShowBalloonTip(BALLOON_SHOW_TIME, Application.ProductName, String.Format(Strings.MainForm.BalloonTip_Started, name), ToolTipIcon.Info);
            }
            else if (state == DuplicatiRunner.RunnerState.Stopped && (level == Duplicati.Datamodel.ApplicationSettings.NotificationLevel.StartAndStop || level == Duplicati.Datamodel.ApplicationSettings.NotificationLevel.Continous))
            {
                //Show stop balloon
                m_trayIcon.ShowBalloonTip(BALLOON_SHOW_TIME, Application.ProductName, String.Format(Strings.MainForm.BalloonTip_Stopped, name), ToolTipIcon.Info);
            }
            else if (state == DuplicatiRunner.RunnerState.Running && level == Duplicati.Datamodel.ApplicationSettings.NotificationLevel.Continous)
            {
                //Show update balloon
                m_trayIcon.ShowBalloonTip(BALLOON_SHOW_TIME, Application.ProductName, String.Format(Strings.MainForm.BalloonTip_Running, message), ToolTipIcon.Info);

            }
        }

        void LiveControl_StateChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new EventHandler(LiveControl_StateChanged), sender, e);
                return;
            }

            switch (Program.LiveControl.State)
            {
                case LiveControls.LiveControlState.Paused:
                    pauseToolStripMenuItem.Text = Strings.Common.MenuResume;
                    pauseToolStripMenuItem.Checked = true;

                    m_trayIcon.Icon = Program.WorkThread.Active ? Properties.Resources.TrayWorkingPause : Properties.Resources.TrayNormalPause;
                    SetTrayIconText(Strings.MainForm.TrayStatusPause);
                    break;
                case LiveControls.LiveControlState.Running:
                    //Restore the icon and tooltip
                    if (Program.WorkThread.Active)
                        WorkThread_StartingWork(Program.WorkThread, null);
                    else
                        WorkThread_CompletedWork(Program.WorkThread, null);

                    pauseToolStripMenuItem.Checked = false;
                    pauseToolStripMenuItem.Text = Strings.Common.MenuPause;
                    break;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            m_trayIcon.Icon = m_currentIcon;
            SetTrayIconText(m_currentTooltip);

            LiveControl_StateChanged(Program.LiveControl, null);
            m_trayIcon.Visible = true;

            long count = 0;
            lock (Program.MainLock)
                count = Program.DataConnection.GetObjects<Datamodel.Schedule>().Length;

            if (count == 0)
                ShowWizard();
            else if (InitialArguments != null)
                HandleCommandlineArguments(InitialArguments);

			if (Library.Utility.Utility.IsMono)
				MonoSupport.BeginInvoke (this, new EmptyDelegate(HideWindow));
			else
            	BeginInvoke(new EmptyDelegate(HideWindow));
            if (Program.TraylessMode)
                ShowStatus();
        }

        private void HideWindow()
        {
            this.Visible = false;
        }

        private void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            TrayIcon_MouseClick(sender, e);
        }
		
		private static void AbortOwnThread(object thread)
		{
			System.Threading.Thread.Sleep(5000);
			if (Program.IsRunningMainLoop) 
			{
				Program.IsRunningMainLoop = false;	
				((System.Threading.Thread)thread).Abort();
			}
		}

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (Program.DataConnection.GetObjects<Datamodel.Schedule>().Length == 0)
                    ShowWizard();
                else
                    ToggleStatus();
            }
        }

        private void DelayDurationMenu_Click(object sender, EventArgs e)
        {
            Program.LiveControl.Pause((string)((ToolStripItem)sender).Tag);
        }

        private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.LiveControl.State == LiveControls.LiveControlState.Running)
                Program.LiveControl.Pause();
            else
                Program.LiveControl.Resume();
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Program.Runner.Stop(CloseReason.UserClosing);
        }

        private void SetTrayIconText(string text)
        {
            //Strange 64 character limit on linux: http://code.google.com/p/duplicati/issues/detail?id=298
            try { m_trayIcon.Text = text; }
            catch
            {
                if (text.Length >= 64)
                    m_trayIcon.Text = text.Substring(0, 60) + "...";
            }
        }

        private void WorkThread_StartingWork(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new EventHandler(WorkThread_StartingWork), sender, e);
                return;
            }

            m_trayIcon.Icon = Properties.Resources.TrayWorking;
            string tmp = string.Format(Strings.MainForm.TrayStatusRunning, Program.WorkThread.CurrentTask == null ? "" : Program.WorkThread.CurrentTask.Schedule.Name);
            SetTrayIconText(tmp);
            stopToolStripMenuItem.Enabled = true;
        }

        private void WorkThread_CompletedWork(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new EventHandler(WorkThread_CompletedWork), sender, e);
                return;
            }

            if (Program.LiveControl.State != LiveControls.LiveControlState.Paused)
            {
                m_trayIcon.Icon = m_currentIcon;
                SetTrayIconText(m_currentTooltip);
            }

            stopToolStripMenuItem.Enabled = false;
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Quit();
        }

        public bool IsInQuit
        {
            get { return m_inQuit; }
        }

        public bool Quit()
        {
            //Under Mono we somehow keep invoking this function recursively, eventually causing a stack-overflow exception
            if (m_inQuit)
                return false;

            try
            {
                m_inQuit = true;
                if (Program.WorkThread.Active && MessageBox.Show(Strings.MainForm.ExitWhileBackupIsRunningQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                    return false;

                Program.LiveControl.Pause();
                Program.Runner.Stop(CloseReason.ApplicationExitCall);

                m_trayIcon.Visible = false;
                if (StatusDialog != null && StatusDialog.Visible)
                    StatusDialog.Close();
                if (WizardDialog != null && WizardDialog.Visible)
                    WizardDialog.Close();

                EnsureBackupIsTerminated(CloseReason.ApplicationExitCall);

                Application.Exit();

                return true;
            }
            finally
            {
                m_inQuit = false;
            }
        }

        private void optionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private void statusToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowStatus();
        }

        private void wizardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowWizard();
        }

        private void throttleOptionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new ThrottleControl().ShowDialog(this);
        }


        private void ChangeStatusVisibility(bool toggle)
        {
            if (InvokeRequired)
            {
                Invoke(new EmptyDelegate(ShowStatus));
                return;
            }

            if (StatusDialog == null || !StatusDialog.Visible)
                StatusDialog = new ServiceStatus();

            if (toggle && StatusDialog.Visible)
                StatusDialog.Close();
            else
            {
                StatusDialog.Show();
                StatusDialog.Activate();
            }
        }
        public void ToggleStatus()
        {
            ChangeStatusVisibility(true);
        }
        public void ShowStatus()
        {
            ChangeStatusVisibility(false);
        }

        public void ShowWizard()
        {
            if (InvokeRequired)
            {
                Invoke(new EmptyDelegate(ShowWizard));
                return;
            }

            if (WizardDialog == null || !WizardDialog.Visible)
                WizardDialog = new WizardHandler();

            WizardDialog.Show();
        }

        public void ShowSettings()
        {
            if (InvokeRequired)
            {
                Invoke(new EmptyDelegate(ShowSettings));
                return;
            }

            lock (Program.MainLock)
            {
                ApplicationSetup dlg = new ApplicationSetup();
                dlg.ShowDialog(this);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            m_trayIcon.Visible = false;
            Program.LiveControl.StateChanged -= new EventHandler(LiveControl_StateChanged);
            Program.WorkThread.StartingWork -= new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork -= new EventHandler(WorkThread_CompletedWork);
            Program.SingleInstance.SecondInstanceDetected -= new SingleInstance.SecondInstanceDelegate(SingleInstance_SecondInstanceDetected);

            EnsureBackupIsTerminated(e.CloseReason == CloseReason.UserClosing ? CloseReason.ApplicationExitCall : e.CloseReason);
			
			//The form occasionally hangs on Mono, so we force kill it after 5 secs if it did not quit itself
			if (Library.Utility.Utility.IsMono)
				new System.Threading.Thread(AbortOwnThread).Start (System.Threading.Thread.CurrentThread);
		}

        private void SingleInstance_SecondInstanceDetected(string[] commandlineargs)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new SingleInstance.SecondInstanceDelegate(SingleInstance_SecondInstanceDetected), new object[] { commandlineargs });
                return;
            }

            if (HandleCommandlineArguments(commandlineargs))
                return;

            //TODO: This actually blocks the app thread, and thus may pile up remote invocations
            if (Program.TraylessMode)
                try { StatusDialog.Focus(); }
                catch { }
            else
                ShowWizard();
        }

        private bool HandleCommandlineArguments(string[] _args)
        {
			bool anyUiShown = false;
			
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = CommandLine.CommandLineParser.ExtractOptions(args);

            //Backwards compatible options
            if (args.Count == 2 && args[0].ToLower().Trim() == "run-backup")
                options["run-backup"] = args[1].Trim();
            
            //Backwards compatible options
            if (args.Count == 1 && args[0] == "show-status")
                options["show-status"] = "";

            //Backwards compatible options
            if (args.Count == 2 && args[0].ToLower().Trim() == "run-backup-group")
                options["run-backup-group"] = args[1].Trim();

            //If pause is requested, pause before parsing --run-backup
            if (options.ContainsKey("pause"))
            {
                string period = options["pause"];
                if (string.IsNullOrEmpty(period))
                    Program.LiveControl.Pause();
                else
                {
                    try
                    {
                        Program.LiveControl.Pause(period);
                    }
                    catch (Exception ex)
                    {
                        Program.LiveControl.Pause();
                        MessageBox.Show(this, string.Format(Strings.MainForm.PauseOperationFailed, ex), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
				
				anyUiShown = true;
            }

            if (options.ContainsKey("run-backup"))
            {
				string backupname = options["run-backup"];
                if (string.IsNullOrEmpty(backupname))
                {
                    if (WizardDialog == null || !WizardDialog.Visible)
                    {
                        WizardDialog = new WizardHandler(new System.Windows.Forms.Wizard.IWizardControl[] { new Wizard_pages.SelectBackup(Duplicati.GUI.Wizard_pages.WizardSettingsWrapper.MainAction.RunNow) });
                        WizardDialog.Show();
						anyUiShown = true;
                    }
                }
                else
                {
					anyUiShown = true;
					
                    Datamodel.Schedule[] schedules = Program.DataConnection.GetObjects<Datamodel.Schedule>("Name LIKE ?", backupname.Trim());
                    if (schedules == null || schedules.Length == 0)
                    {
                        MessageBox.Show(string.Format(Strings.MainForm.NamedBackupNotFound, backupname), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else if (schedules.Length > 1)
                    {
                        MessageBox.Show(string.Format(Strings.MainForm.MultipleNamedBackupsFound, backupname, schedules.Length), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
					else
					{
	                    if (Library.Utility.Utility.ParseBoolOption(options, "full"))
	                        Program.WorkThread.AddTask(new FullBackupTask(schedules[0]));
	                    else
	                        Program.WorkThread.AddTask(new IncrementalBackupTask(schedules[0]));
					}
                }
            }

            if (options.ContainsKey("run-backup-group"))
            {
                string groupname = options["run-backup-group"];
                if (string.IsNullOrEmpty(groupname))
                {
                    if (WizardDialog == null || !WizardDialog.Visible)
                    {
                        WizardDialog = new WizardHandler(new System.Windows.Forms.Wizard.IWizardControl[] { new Wizard_pages.SelectBackup(Duplicati.GUI.Wizard_pages.WizardSettingsWrapper.MainAction.RunNow) });
                        WizardDialog.Show();
						anyUiShown = true;
                    }
                }
                else
                {
                    Datamodel.Schedule[] schedules = Program.DataConnection.GetObjects<Datamodel.Schedule>("Path LIKE ?", groupname.Trim());
                    if (schedules == null || schedules.Length == 0)
                    {
                        MessageBox.Show(string.Format(Strings.MainForm.NamedBackupGroupNotFound, groupname), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    } 
					else 
					{
	                    bool full = Library.Utility.Utility.ParseBoolOption(options, "full");
	
	                    foreach(Datamodel.Schedule s in schedules)
	                        if (full)
	                            Program.WorkThread.AddTask(new FullBackupTask(s));
	                        else
	                            Program.WorkThread.AddTask(new IncrementalBackupTask(s));
					}
					
					anyUiShown = true;
                }
            }

            if (Library.Utility.Utility.ParseBoolOption(options, "show-status"))
			{
                ShowStatus();
				anyUiShown = true;
			}

			if (Library.Utility.Utility.ParseBoolOption(options, "show-wizard"))
			{
                ShowWizard();
				anyUiShown = true;
			}

            //Resume if requested
            if (Library.Utility.Utility.ParseBoolOption(options, "resume")) 
			{
                Program.LiveControl.Resume();
				anyUiShown = true;
			}
			
            return anyUiShown;
        }

        private void EnsureBackupIsTerminated(CloseReason reason)
        {
            //Ensure that this function can only be called once, 
            // as this prevents multiple termination questions presented to the user
            if (m_hasAttemptedBackupTermination)
                return;
            m_hasAttemptedBackupTermination = true;

            if (Program.Runner != null && Program.WorkThread != null && Program.WorkThread.Active)
            {
                //Make sure no new items can enter the queue
                if (Program.Scheduler != null)
                    Program.Scheduler.Terminate(true);

                //We want no new items to enter the queue
                Program.WorkThread.Terminate(false);

                Program.Runner.Pause();
                if (!Program.Runner.IsStopRequested)
                    Program.Runner.Stop(reason);

                //Wait 15 seconds to see if the stop works
                for (int i = 0; i < 15; i++)
                {
                    Program.WorkThread.Join(1000);
                    Application.DoEvents();
                    if (!Program.WorkThread.Active)
                        break;
                }

                while (Program.WorkThread.Active)
                {
                    //Ask the user if we should abort
                    if (MessageBox.Show(Strings.MainForm.TerminateForExitQuestion, Application.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                    {
                        //Abort the thread
                        Program.Runner.Terminate(reason);
                        
                        //Give last 5 second chance to write the remaining data
                        int i = 5;
                        while (i > 0 && !Program.WorkThread.Join(1000))
                        {
                            //The Join call blocks the main thread, so let pending events through
                            Application.DoEvents();
                            i--;
                        }
                        break;
                    }

                    //Wait 18 * 10 seconds = 3 minutes before asking again
                    for (int i = 0; i < 18; i++)
                    {
                        Program.WorkThread.Join(1000 * 10);
                        Application.DoEvents();

                        if (!Program.WorkThread.Active)
                            break;
                    }
                }
            }
        }

        private void TrayIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            ShowStatus();
        }

        private void TrayMenu_LocationChanged(object sender, EventArgs e)
        {
            // This is a workaround by Jenlehma for not displaying the context menu on the right screen:
            // http://code.google.com/p/duplicati/issues/detail?id=600

            // We assume that only Windows is broken here
            if (!Duplicati.Library.Utility.Utility.IsClientLinux)
            {
                // First we get the top/left position of the TrayMenu and the current mouse position
                Point TrayMenuTopLeft = new Point(TrayMenu.Left, TrayMenu.Top);
                Point mousePos = Cursor.Position;

                // Now get the screen objects of both coordinates (mouse / traymenu)
                Screen ScrContextMenu = Screen.FromPoint(TrayMenuTopLeft);
                Screen ScrCursor = Screen.FromPoint(mousePos);

                // if the mouse and the tray menu is on different screens ...
                if (ScrContextMenu.DeviceName != ScrCursor.DeviceName)
                {
                    // if the mouse sits exactly on the top or the bottom of the menu, the menu might have an offset on the x-axis
                    if (TrayMenuTopLeft.Y + TrayMenu.Height == mousePos.Y || TrayMenuTopLeft.Y == mousePos.Y)
                    {
                        if (TrayMenuTopLeft.X < mousePos.X)
                            TrayMenu.Left = mousePos.X;
                        else if (TrayMenuTopLeft.X > mousePos.X)
                            TrayMenu.Left = mousePos.X - TrayMenu.Width;
                    }
                    else
                    {
                        // if the y-axis is off .. just move that.
                        TrayMenu.Top = mousePos.Y;
                    }
                }
            }
        }
    }
}
