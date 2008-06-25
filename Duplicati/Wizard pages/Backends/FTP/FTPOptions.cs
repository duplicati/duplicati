using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;

namespace Duplicati.Wizard_pages.Backends.FTP
{
    public partial class FTPOptions : UserControl, IWizardControl
    {
        private bool m_warnedPassword;
        private bool m_hasTested;
        private bool m_warnedPath;

        public FTPOptions()
        {
            InitializeComponent();
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return "Backup storage options"; }
        }

        string IWizardControl.HelpText
        {
            get { return "On this page you can select where to store the backup data."; }
        }

        Image IWizardControl.Image
        {
            get { return null; }
        }

        bool IWizardControl.FullSize
        {
            get { return false; }
        }

        void IWizardControl.Enter(IWizardForm owner)
        {
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            if (!ValidateForm())
            {
                cancel = true;
                return;
            }

            if (!m_hasTested)
                if (MessageBox.Show(this, "Do you want to test the connection?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    TestConnection_Click(null, null);
                    if (!m_hasTested)
                    {
                        cancel = true;
                        return;
                    }
                }

            if (!m_warnedPath)
            {
                if (MessageBox.Show(this, "You have not entered a path. This will store all backups in the default directory. Is this what you want?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    cancel = true;
                    return;
                }
                m_warnedPath = true;
            }
        }

        #endregion

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter the name of the server", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (Username.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter a username", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Username.Focus(); }
                catch { }

                return false;
            }

            if (Password.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter a password", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Password.Focus(); }
                catch { }

                return false;
            }

            return true;
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                try
                {
                    System.Net.WebClient wc = new System.Net.WebClient();
                    wc.Credentials = new System.Net.NetworkCredential(Username.Text, Password.Text);
                    string hostname = "ftp://" + Servername.Text + (Port.Value != 21 ? ":21/" : "/") + Path;
                    wc.DownloadData(hostname);

                    MessageBox.Show(this, "Connection succeeded!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                }
            }
        }

        private void Port_ValueChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPath = false;
        }

        private void Servername_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }
    }
}
