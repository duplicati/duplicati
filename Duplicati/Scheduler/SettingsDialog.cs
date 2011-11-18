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
    /// Edit settings
    /// </summary>
    public partial class SettingsDialog : Form
    {
        /// <summary>
        /// Edited settings
        /// </summary>
        public Duplicati.Scheduler.Data.SchedulerDataSet.SettingsDataTable Settings { get; private set; }
        /// <summary>
        /// XML prior to edit
        /// </summary>
        private string OriginalXML;
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
            this.Settings = aSettings;
            OriginalXML = XmlFromTable(aSettings);
            this.checkBox1.Checked = this.Settings.UseGlobalPassword;
            this.passwordControl1.CheckMod = this.Settings.Values.CheckMod;
            this.passwordControl1.Checksum = this.Settings.Values.Checksum;
            this.numericUpDown1.Value = (decimal)this.Settings.Values.LogFileAgeDays;
            if (this.Settings.Values.IsShowBubblesNull()) this.Settings.Values.ShowBubbles = true;
            this.BubbleCheckBox.Checked = this.Settings.Values.ShowBubbles;
        }
        private string XmlFromTable(Duplicati.Scheduler.Data.SchedulerDataSet.SettingsDataTable aSettings)
        {
            System.IO.MemoryStream ms = new System.IO.MemoryStream();
            aSettings.WriteXml(ms);
            return System.Text.ASCIIEncoding.ASCII.GetString(ms.ToArray());
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
            this.Settings.Values.CheckSrc = this.checkBox1.Checked;
            this.Settings.Values.CheckMod = this.passwordControl1.CheckMod;
            this.Settings.Values.Checksum = this.passwordControl1.Checksum;
            this.Settings.Values.LogFileAgeDays = (int)this.numericUpDown1.Value;
            this.Settings.Values.ShowBubbles = this.BubbleCheckBox.Checked;
            if (OriginalXML != XmlFromTable(this.Settings))
            {
                this.Settings.Values.LastMod = DateTime.Now;
                ((Duplicati.Scheduler.Data.SchedulerDataSet)this.Settings.DataSet).Save();
            }
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
