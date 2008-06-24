using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;

namespace Duplicati.Wizard_pages.Backends.SSH
{
    public partial class SSHOptions : UserControl, IWizardControl
    {
        private bool m_warnedPath = false;
        private bool m_hasTested = false;

        public SSHOptions()
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
                cancel = false;

            /*if (!m_hasTested)
                if (MessageBox.Show(this, "Do you want to test the connection?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    TestConnection_Click(null, null);
                    if (!m_hasTested)
                        return;
                }
            */

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

        private void TestConnection_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, "Testing is not implemented for SSH yet.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

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

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_warnedPath = false;
        }

        private void Servername_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Port_ValueChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }
    }
}
