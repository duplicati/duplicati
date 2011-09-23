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
    /// Allow user to enter filters
    /// </summary>
    public partial class FilterDialog : Form
    {
        private string[] itsFilter;
        /// <summary>
        /// The entered filters, null terminated
        /// </summary>
        public string[] Filter 
        { 
            get { return itsFilter; } 
            set 
            { 
                this.richTextBox1.Lines = value; 
            } 
        }
        /// <summary>
        /// Enter filters - I need to do a better one of these "some day".
        /// </summary>
        public FilterDialog()
        {
            InitializeComponent();
        }
        /// <summary>
        /// User pressed OK, pack up the filters and close
        /// </summary>
        private void OKButton_Click(object sender, EventArgs e)
        {
            itsFilter = this.richTextBox1.Lines;
            this.DialogResult = DialogResult.OK;
            Close();
        }
        private FilterHelp itsHelp = null;
        /// <summary>
        /// User pressed help, show a modified version of Duplicati's filter help
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void helpToolStripButton_Click(object sender, EventArgs e)
        {
            if (itsHelp == null)
            { 
                itsHelp = new FilterHelp();
                itsHelp.FormClosing += new FormClosingEventHandler(itsHelp_FormClosing);
            }
            itsHelp.Show();
        }
        /// <summary>
        /// Don't close help, just hide it
        /// </summary>
        void itsHelp_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = e.CloseReason == CloseReason.UserClosing;
            itsHelp.Hide();
        }
        /// <summary>
        /// Allows reading a file
        /// </summary>
        private void OpenoolStripButton_Click(object sender, EventArgs e)
        {
            if (this.openFileDialog1.ShowDialog() == DialogResult.Cancel) return;
            try
            {
                this.richTextBox1.Text += System.IO.File.ReadAllText(this.openFileDialog1.FileName);
            }
            catch (Exception Ex)
            {
                MessageBox.Show("Can not open " + this.openFileDialog1.FileName + "._: " + Ex.Message);
            }
        }
        /// <summary>
        /// Allows saving a file
        /// </summary>
        private void SaveToolStripButton_Click(object sender, EventArgs e)
        {
            if (this.saveFileDialog1.ShowDialog() == DialogResult.Cancel) return;
            try
            {
                System.IO.File.WriteAllText(this.saveFileDialog1.FileName, this.richTextBox1.Text);
            }
            catch (Exception Ex)
            {
                MessageBox.Show("Can not save " + this.saveFileDialog1.FileName + "._: " + Ex.Message);
            }
        }
        /// <summary>
        /// User pressed Cancel, just close
        /// </summary>
        private void CanButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
        string itsCleared = string.Empty;
        /// <summary>
        /// Removes the text to a buffer
        /// </summary>
        private void clearAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(this.richTextBox1.Text)) return;
            itsCleared = this.richTextBox1.Text;
            this.richTextBox1.Text = string.Empty;
            unclearToolStripMenuItem.Enabled = true;
        }
        /// <summary>
        /// Unremoves the text from a buffer
        /// </summary>
        private void unclearToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.richTextBox1.Text = itsCleared;
            itsCleared = string.Empty;
            this.unclearToolStripMenuItem.Enabled = false;
        }
    }
}
