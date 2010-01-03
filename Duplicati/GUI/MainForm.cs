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
        private ServiceStatus StatusDialog;
        private WizardHandler WizardDialog;

        private delegate void EmptyDelegate();

        public string[] InitialArguments
        {
            get;
            set;
        }

        public MainForm()
        {
            InitializeComponent();

            Program.LiveControl.StateChanged += new EventHandler(LiveControl_StateChanged);
            Program.WorkThread.StartingWork += new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork += new EventHandler(WorkThread_CompletedWork);
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

                    TrayIcon.Icon = Program.WorkThread.Active ? Properties.Resources.TrayWorkingPause : Properties.Resources.TrayNormalPause;
                    TrayIcon.Text = Strings.MainForm.TrayStatusPause;
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
            TrayIcon.Icon = Properties.Resources.TrayNormal;
            TrayIcon.Text = Strings.MainForm.TrayStatusReady;

            LiveControl_StateChanged(Program.LiveControl, null);
            TrayIcon.Visible = true;

            long count = 0;
            lock (Program.MainLock)
                count = Program.DataConnection.GetObjects<Datamodel.Schedule>().Length;

            if (count == 0)
                ShowWizard();
            else if (InitialArguments != null)
                Program.HandleCommandlineArguments(InitialArguments);

            BeginInvoke(new EmptyDelegate(HideWindow));
        }

        private void HideWindow()
        {
            this.Visible = false;
        }

        private void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ShowWizard();
        }

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                ShowWizard();
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
            Program.Runner.Stop();
        }

        private void WorkThread_StartingWork(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new EventHandler(WorkThread_StartingWork), sender, e);
                return;
            }

            TrayIcon.Icon = Properties.Resources.TrayWorking;
            TrayIcon.Text = string.Format(Strings.MainForm.TrayStatusRunning, Program.WorkThread.CurrentTask == null ? "" : Program.WorkThread.CurrentTask.Schedule.Name);
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
                TrayIcon.Icon = Properties.Resources.TrayNormal;
                TrayIcon.Text = Strings.MainForm.TrayStatusReady;
            }

            stopToolStripMenuItem.Enabled = false;
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.WorkThread.Active && MessageBox.Show(Strings.MainForm.ExitWhileBackupIsRunningQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Program.LiveControl.Pause();
            Program.Runner.Stop();
            Application.Exit();
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


        public void ShowStatus()
        {
            if (InvokeRequired)
            {
                Invoke(new EmptyDelegate(ShowStatus));
                return;
            }

            if (StatusDialog == null || !StatusDialog.Visible)
                StatusDialog = new ServiceStatus();

            StatusDialog.Show();
            StatusDialog.Activate();
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
            TrayIcon.Visible = false;
            Program.LiveControl.StateChanged -= new EventHandler(LiveControl_StateChanged);
            Program.WorkThread.StartingWork -= new EventHandler(WorkThread_StartingWork);
            Program.WorkThread.CompletedWork -= new EventHandler(WorkThread_CompletedWork);
        }

    }
}
