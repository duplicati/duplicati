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
