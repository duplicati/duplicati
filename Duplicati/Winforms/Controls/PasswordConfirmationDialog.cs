using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Winforms.Controls
{
    public partial class PasswordConfirmationDialog : Form
    {
        private string m_previousPassword;

        public PasswordConfirmationDialog(string previousPassword)
        {
            InitializeComponent();

            m_previousPassword = previousPassword;
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            if (m_previousPassword != Password.Text)
            {
                MessageBox.Show(this, Strings.PasswordConfirmationDialog.PasswordDoNotMatchError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            else
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }
}
