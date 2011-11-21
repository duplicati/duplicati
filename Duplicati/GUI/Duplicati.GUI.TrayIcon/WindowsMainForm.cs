using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.TrayIcon
{
    public partial class WindowsMainForm : Form
    {
        private bool m_stateIsPaused = false;
        private bool m_hasError = false;
        private bool m_hasWarning = false;

        public WindowsMainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            BeginInvoke((Action)(() => { this.Visible = false; }));

            TrayIcon.Visible = true;

            Program.Connection.StatusUpdated += new HttpServerConnection.StatusUpdate(Connection_StatusUpdated);
            Connection_StatusUpdated(Program.Connection.Status);
        }

        void Connection_StatusUpdated(Server.Serialization.ISerializableStatus status)
        {
            if (this.InvokeRequired)
                this.Invoke(new HttpServerConnection.StatusUpdate(Connection_StatusUpdated), status);
            else
            {
                if (status.ActiveScheduleId < 0)
                {
                    if (status.ProgramState == Server.Serialization.LiveControlState.Running)
                    {
                        if (m_hasError)
                            TrayIcon.Icon = Properties.Resources.TrayNormalError;
                        else if (m_hasWarning)
                            TrayIcon.Icon = Properties.Resources.TrayNormalWarning;
                        else
                            TrayIcon.Icon = Properties.Resources.TrayNormal;
                    }
                    else
                        TrayIcon.Icon = Properties.Resources.TrayNormalPause;
                }
                else
                {
                    TrayIcon.Icon =
                       status.ProgramState == Server.Serialization.LiveControlState.Running ?
                       Properties.Resources.TrayWorking : Properties.Resources.TrayWorkingPause;
                }

                if (status.ProgramState == Server.Serialization.LiveControlState.Running)
                {
                    pauseToolStripMenuItem.Image = Properties.Resources.Pause;
                    pauseToolStripMenuItem.Text = Strings.WindowsMainForm.PauseMenuText;
                    m_stateIsPaused = false;
                }
                else
                {
                    pauseToolStripMenuItem.Image = Properties.Resources.Play;
                    pauseToolStripMenuItem.Text = Strings.WindowsMainForm.ResumeMenuText;
                    m_stateIsPaused = true;
                }
            }
        }

        private void statusToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_stateIsPaused)
                Program.Connection.Resume();
            else
                Program.Connection.Pause();
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Program.Connection.StopBackup();
        }

        private void DelayDuration05Menu_Click(object sender, EventArgs e)
        {
            Program.Connection.Pause("5m");
        }

        private void DelayDuration15Menu_Click(object sender, EventArgs e)
        {
            Program.Connection.Pause("15m");
        }

        private void DelayDuration30Menu_Click(object sender, EventArgs e)
        {
            Program.Connection.Pause("30m");
        }

        private void DelayDuration60Menu_Click(object sender, EventArgs e)
        {
            Program.Connection.Pause("1h");
        }
    }
}
