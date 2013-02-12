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
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// Get a new job name
    /// </summary>
    public partial class SchedulerNameDialog : Form
    {
        private string itsBackupName;
        /// <summary>
        /// The selected name
        /// </summary>
        public string BackupName
        {
            get { return itsBackupName; }
            set { itsBackupName = this.textBox1.Text = value; }
        }
        /// <summary>
        /// Get a new job name
        /// </summary>
        public SchedulerNameDialog()
        {
            InitializeComponent();
            List<char> Adder = new List<char>(System.IO.Path.GetInvalidFileNameChars());
            this.textBox1.TextChanged += new EventHandler(textBox1_TextChanged);
        }
        // Stuff I don't like, some of them are probably OK; but, I don't care, I don't like um.
        private string BadList = "+=!@#$%^&*[]{}:\\';\"" + new string(System.IO.Path.GetInvalidFileNameChars());
        private volatile bool Ignore = false;       // Stops re-entry
        /// <summary>
        /// User entered text, make sure it's not on the naughty list
        /// </summary>
        void  textBox1_TextChanged(object sender, EventArgs e)
        {
            int Index = this.textBox1.Text.Length - 1;
            if (!Ignore && !string.IsNullOrEmpty(this.textBox1.Text) && BadList.Contains(this.textBox1.Text[Index]))
            {
                Ignore = true;
                this.textBox1.Text = this.textBox1.Text.Substring(0, Index);
                this.textBox1.SelectionStart = this.textBox1.Text.Length;
                System.Media.SystemSounds.Beep.Play();
            }
            Ignore = false;
        }
        /// <summary>
        /// Pressed OK, store result and close
        /// </summary>
        private void OKButton_Click(object sender, EventArgs e)
        {
            itsBackupName = this.textBox1.Text;
            this.DialogResult = DialogResult.OK;
            Close();
        }
        /// <summary>
        /// Pressed CANCEL - just close
        /// </summary>
        private void CanButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
