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
    /// Shows a summary page
    /// </summary>
    public partial class JobSummary : UserControl
    {
        /// <summary>
        /// Shows a summary page with error indicators
        /// </summary>
        public JobSummary()
        {
            InitializeComponent();
        }
        /// <summary>
        /// Enabling this only makes it mad
        /// </summary>
        public new bool Enabled
        {
            get { return !this.EnableLabel.Visible; }
            // Show the disabled label (says 'not enabled' in red) and grey the Description
            set { this.EnableLabel.Visible = !value; this.DescriptionTextBox.Enabled = value; }
        }
        /// <summary>
        /// Set the values
        /// </summary>
        /// <param name="aRow">The job row</param>
        /// <param name="aDetails">Text description of the job trigger</param>
        public void SetSummary(Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow aRow, string aDetails)
        {
            this.nameTextBox.Text = aRow.Name;
            this.DescriptionTextBox.Text = aDetails;
            this.sourceTextBox.Text = aRow.Source;
            this.destinationTextBox.Text = aRow.Destination;
            this.LastModLabel.Text = (aRow.IsLastModNull() || aRow.LastMod == DateTime.MinValue) ? "Never edited" : "Last edited: " + aRow.LastMod.ToString("ddd, dd MMMM yyyy hh:mm tt");
            if (aRow.FullOnly || (aRow.FullRepeatDays == 0 && aRow.FullAfterN == 0)) this.fullRepeatStrTextBox.Text = "Always do full backups";
            else this.fullRepeatStrTextBox.Text = aRow.FullRepeatDays > 0 ? aRow.FullRepeatDays + " days " : aRow.FullAfterN.ToString()+" incrementals";
            this.MaxFullTextBox.Text = aRow.MaxFulls > 0 ? "the last "+aRow.MaxFulls.ToString("N0") : "all" ;
            this.MaxAgeTextBox.Text = aRow.MaxAgeDays > 0 ? aRow.MaxAgeDays.ToString("N0") + " days" : "<no limit>" ;
            this.PassRichTextBox.Text = new string[] { "not password protected.", "protected by local password.", "protected by global password." }[aRow.GetCheckSrc()];
        }
        public void Clear()
        {
            this.nameTextBox.Text =
            this.DescriptionTextBox.Text =
            this.sourceTextBox.Text =
            this.destinationTextBox.Text =
            this.fullRepeatStrTextBox.Text =
            this.MaxFullTextBox.Text =
            this.MaxAgeTextBox.Text =
            this.PassRichTextBox.Text =
                string.Empty;
            Enabled = false;
        }
        /// <summary>
        /// Sets the errors
        /// </summary>
        /// <returns>true if no errors</returns>
        public bool ValidateSettings()
        {
            bool SrcOK = !string.IsNullOrEmpty(this.sourceTextBox.Text);
            bool DstOK = !string.IsNullOrEmpty(this.destinationTextBox.Text) && !this.destinationTextBox.Text.EndsWith("://");
            this.DestErrorProvider.SetError(this.destinationTextBox, DstOK ? string.Empty : "A destination for the backup must be supplied.  Use the Destination tab.");
            this.SourceErrorProvider.SetError(this.sourceTextBox, SrcOK ? string.Empty : "A source set of folders to backup must be supplied.  Use the Source tab.");
            return SrcOK && DstOK;
        }
        /// <summary>
        /// Pressed double-clicked Source - show a list box
        /// </summary>
        private void sourceTextBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // This is to give User a better look at source
            this.listBox1.Items.Clear();
            this.listBox1.Items.Add("<Click to dismiss>");
            this.listBox1.Items.AddRange(this.sourceTextBox.Text.Split(System.IO.Path.PathSeparator));
            this.panel1.Visible = true;
            this.panel1.BringToFront();
        }
        /// <summary>
        /// Pressed the list box, hide it
        /// </summary>
        private void listBox1_Click(object sender, EventArgs e)
        {
            this.panel1.Visible = false;
        }
    }
}
