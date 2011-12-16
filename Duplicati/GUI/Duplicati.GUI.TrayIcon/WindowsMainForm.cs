using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Duplicati.Server.Serialization;

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

        void Connection_StatusUpdated(ISerializableStatus status)
        {
            if (this.InvokeRequired)
                this.Invoke(new HttpServerConnection.StatusUpdate(Connection_StatusUpdated), status);
            else
            {
                switch(status.SuggestedStatusIcon)
                {
                    case SuggestedStatusIcon.Active:
                        TrayIcon.Icon =  Properties.Resources.TrayWorking;
                        break;
                    case SuggestedStatusIcon.ActivePaused:
                        TrayIcon.Icon =  Properties.Resources.TrayWorkingPause;
                        break;
                    case SuggestedStatusIcon.ReadyError:
                        TrayIcon.Icon =  Properties.Resources.TrayNormalError;
                        break;
                    case SuggestedStatusIcon.ReadyWarning:
                        TrayIcon.Icon =  Properties.Resources.TrayNormalWarning;
                        break;
                    case SuggestedStatusIcon.Paused:
                        TrayIcon.Icon =  Properties.Resources.TrayNormalPause;
                        break;
                    case SuggestedStatusIcon.Ready:
                    default:    
                        TrayIcon.Icon = Properties.Resources.TrayNormal;
                        break;
                    
                }

                if (status.ProgramState == LiveControlState.Running)
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
