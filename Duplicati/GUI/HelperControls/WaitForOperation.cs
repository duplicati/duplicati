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

        public WaitForOperation()
        {
            InitializeComponent();
        }

        public void Setup(DoWorkEventHandler callback, string title)
        {
            backgroundWorker1.DoWork += callback;
            this.Text = title;
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
    }
}