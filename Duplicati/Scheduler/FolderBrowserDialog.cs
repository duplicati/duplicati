using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// Allow user to select folders to backup
    /// </summary>
    public partial class FolderBrowserDialog : Form
    {
        public string Prompt { get { return this.PromptLabel.Text; } set { this.PromptLabel.Text = value; } }
        private string[] itsSelectedFolders;
        /// <summary>
        /// Folders selected
        /// </summary>
        public string[] SelectedFolders
        {
            get { return itsSelectedFolders; }
            set { this.folderTreeControl1.SetSelectedFolders(value); itsSelectedFolders = value; }
        }
        /// <summary>
        /// Allow user to select folders to backup
        /// </summary>
        public FolderBrowserDialog()
        {
            InitializeComponent();
            this.folderTreeControl1.ForeColor = this.ForeColor;
            this.folderTreeControl1.BackColor = this.BackColor;
        }
        /// <summary>
        /// OK button, get selection and close
        /// </summary>
        private void OKButton_Click(object sender, EventArgs e)
        {
            itsSelectedFolders = this.folderTreeControl1.SelectedFolders;
            this.DialogResult = DialogResult.OK;
            Close();
        }
        /// <summary>
        /// Cancel button, just close
        /// </summary>
        private void CanButton_Click(object sender, EventArgs e)
        {
            itsSelectedFolders = new string[0];
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
