#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
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
