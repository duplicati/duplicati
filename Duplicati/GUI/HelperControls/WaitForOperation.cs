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

namespace Duplicati.GUI.HelperControls
{
    public partial class WaitForOperation : Form
    {
        private Exception m_exception;
        private DoWorkEventHandler m_handler;
        private bool m_useAbortCancel;
        private object m_lock = new object();
        private System.Threading.Thread m_workerThread = null;

        public WaitForOperation()
        {
            InitializeComponent();
        }

        public void Setup(DoWorkEventHandler callback, string title, bool useAbortCancel)
        {
            m_handler = callback;
            this.Text = title;
            m_useAbortCancel = useAbortCancel;
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            m_exception = e.Error;
            if (e.Cancelled || e.Error != null)
                this.DialogResult = DialogResult.Cancel;
            else
                this.DialogResult = DialogResult.OK;

            this.Close();
        }

        private void WaitForOperation_Load(object sender, EventArgs e)
        {
            backgroundWorker1.RunWorkerAsync();
        }

        public Exception Error { get { return m_exception; } }

        private delegate void SetTitleDelegate(string title);
        private delegate void SetProgressDelegate(int progress);

        public void SetTitle(string title)
        {
            if (this.InvokeRequired)
                this.Invoke(new SetTitleDelegate(SetTitle), title);
            else
            {
                this.Text = title;
            }
        }

        public void SetProgress(int progress)
        {
            if (this.InvokeRequired)
                this.Invoke(new SetProgressDelegate(SetProgress), progress);
            else
            {
                if (progress < 0)
                {
                    progressBar1.Style = ProgressBarStyle.Marquee;
                }
                else
                {
                    progressBar1.Style = ProgressBarStyle.Continuous;
                    progressBar1.Value = Math.Max(0, Math.Min(progress, 100));
                }
            }
        }

        private void WaitForOperation_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && backgroundWorker1.IsBusy)
            {
                if (!backgroundWorker1.CancellationPending)
                {
                    if (MessageBox.Show(this, Strings.WaitForOperation.ReallyAbortQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) == DialogResult.Yes)
                    {
                        backgroundWorker1.CancelAsync();
                        if (m_useAbortCancel)
                        {
                            lock (m_lock)
                                if (m_workerThread != null)
                                    m_workerThread.Abort();
                        }
                    }
                }

                e.Cancel = true;
                return;
            }
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                lock (m_lock)
                    m_workerThread = System.Threading.Thread.CurrentThread;

                m_handler.Invoke(sender, e);
            }
            catch(System.Threading.ThreadAbortException)
            {
                System.Threading.Thread.ResetAbort();
                e.Cancel = true;
            }
            finally
            {
                lock (m_lock)
                    m_workerThread = null;
            }
        }
    }
}