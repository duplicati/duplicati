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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.HelperControls
{
    /// <summary>
    /// A helper class to encapsulate a single folder entry UI
    /// </summary>
    public partial class FolderPathEntry : UserControl
    {
        /// <summary>
        /// An internal reference to the folder browser
        /// </summary>
        private FolderBrowserDialog m_browseDialog = null;

        /// <summary>
        /// Constructs a new FolderPathEntry
        /// </summary>
        public FolderPathEntry()
        {
            InitializeComponent();

            FolderPath.TextChanged += new EventHandler(FolderPath_TextChanged);
            DeleteButton.Click += new EventHandler(DeleteButton_Click);
            BrowseFolderButton.Click += new EventHandler(BrowseFolderButton_Click);
            base.GotFocus += new EventHandler(FolderPathEntry_GotFocus);
        }

        /// <summary>
        /// An event that handles the control getting focus
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">An unused event argument</param>
        void FolderPathEntry_GotFocus(object sender, EventArgs e)
        {
            try { FolderPath.Focus(); }
            catch { }
        }


        /// <summary>
        /// An event handler for the click of the BrowseFolderButton
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">An unused event argument</param>
        private void BrowseFolderButton_Click(object sender, EventArgs e)
        {
            if (m_browseDialog != null)
                if (m_browseDialog.ShowDialog(this) == DialogResult.OK)
                {
                    FolderPath.Text = m_browseDialog.SelectedPath;
                    FolderPath_Leave(this, null);
                }
        }

        /// <summary>
        /// An event hander for the click of the DeleteButton
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">An unused event argument</param>
        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (DeleteButton_Clicked != null)
                DeleteButton_Clicked(this, null);
        }

        /// <summary>
        /// An event handler for changed text in the FolderPath textbox
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">An unused event argument</param>
        private void FolderPath_TextChanged(object sender, EventArgs e)
        {
            FolderStateImage.Image = FolderPath.Text.Trim().Length == 0 ? Properties.Resources.AddedFolder : Properties.Resources.FolderOpen;
            if (SelectedPathChanged != null)
                SelectedPathChanged(this, null);
        }

        /// <summary>
        /// An event that is raised when the current folder changes
        /// </summary>
        public event EventHandler SelectedPathChanged;
        /// <summary>
        /// An event that is raised when the delete button is clicked
        /// </summary>
        public event EventHandler DeleteButton_Clicked;
        /// <summary>
        /// An event that is raised when the current folder looses focus
        /// </summary>
        public event EventHandler SelectedPathLeave;

        /// <summary>
        /// Gets the current path
        /// </summary>
        public string SelectedPath
        {
            get { return FolderPath.Text; }
            set { FolderPath.Text = value; }
        }

        /// <summary>
        /// Gets or sets the dialog used for folder browsing
        /// </summary>
        public FolderBrowserDialog FolderBrowserDialog
        {
            get { return m_browseDialog; }
            set { m_browseDialog = value; }
        }

        /// <summary>
        /// Gets or sets the size of the folder
        /// </summary>
        public string FolderSize
        {
            get { return SizeLabel.Text; }
            set { SizeLabel.Text = value; }
        }

        /// <summary>
        /// An event handler for leaving the FolderPath textbox
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">An unused event argument</param>
        private void FolderPath_Leave(object sender, EventArgs e)
        {
            if (SelectedPathLeave != null)
                SelectedPathLeave(this, null);
        }

        /// <summary>
        /// An event handler for turning the Enter keypress into a Tab keypress
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">The keypress information argument</param>
        private void FolderPath_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.Parent.SelectNextControl(this, true, false, true, false);
                e.Handled = true;
            }
        }
    }
}
