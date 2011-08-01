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
    /// Edit settings
    /// </summary>
    public partial class SettingsDialog : Form
    {
        /// <summary>
        /// Edited settings
        /// </summary>
        public Duplicati.Scheduler.Data.SchedulerDataSet.SettingsDataTable Settings { get; private set; }
        /// <summary>
        /// Edit Settings
        /// </summary>
        /// <remarks>
        /// Here a global password may be set.  If set, User may use it for backups, or not as User is want.
        /// The bubbles thing is not used now.
        /// </remarks>
        /// <param name="aSettings">Settings to edit</param>
        public SettingsDialog(Duplicati.Scheduler.Data.SchedulerDataSet.SettingsDataTable aSettings)
        {
            InitializeComponent();
            Settings = aSettings;
            this.checkBox1.Checked = Settings.UseGlobalPassword;
            this.passwordControl1.CheckMod = Settings.Values.CheckMod;
            this.passwordControl1.Checksum = Settings.Values.Checksum;
            this.numericUpDown1.Value = (decimal)Settings.Values.LogFileAgeDays;
            if (Settings.Values.IsShowBubblesNull()) Settings.Values.ShowBubbles = true;
            this.BubbleCheckBox.Checked = Settings.Values.ShowBubbles;
        }
        /// <summary>
        /// Let user know what zero means
        /// </summary>
        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            this.RemoveLabel.Text = this.numericUpDown1.Value == 0 ? "Keep all log entries." : "Remove log entries older than";
        }
        /// <summary>
        /// Pressed OK, update settings and close
        /// </summary>
        private void OKButton_Click(object sender, EventArgs e)
        {
            Settings.Values.CheckSrc = this.checkBox1.Checked;
            Settings.Values.CheckMod = this.passwordControl1.CheckMod;
            Settings.Values.Checksum = this.passwordControl1.Checksum;
            Settings.Values.LogFileAgeDays = (int)this.numericUpDown1.Value;
            Settings.Values.ShowBubbles = this.BubbleCheckBox.Checked;
            ((Duplicati.Scheduler.Data.SchedulerDataSet)Settings.DataSet).Save();
            this.DialogResult = DialogResult.OK;
            Close();
        }
        /// <summary>
        /// Pressed CANCEL - close
        /// </summary>
        private void CanButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            this.passwordControl1.Enabled = this.checkBox1.Checked;
        }
    }
}
