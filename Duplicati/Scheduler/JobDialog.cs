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
    /// Edit a job
    /// </summary>
    public partial class JobDialog : Form
    {
        private Microsoft.Win32.TaskScheduler.Trigger itsTrigger;
        /// <summary>
        /// Gets/Sets the job's task scheduler trigger
        /// </summary>
        public Microsoft.Win32.TaskScheduler.Trigger Trigger
        {
            get { return itsTrigger; }
            set { this.TaskEditor.SetTrigger(value); }
        }
        /// <summary>
        /// Gets the JobsRow being edited
        /// </summary>
        public Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow Row { get; private set; }
        /// <summary>
        /// Gets the settings
        /// </summary>
        private Duplicati.Scheduler.Data.SchedulerDataSet.SettingsDataTable Settings
        {
            get { return ((Duplicati.Scheduler.Data.SchedulerDataSet)Row.Table.DataSet).Settings; }
        }
        /// <summary>
        /// XML of row prior to any changes
        /// </summary>
        private byte[] OriginalXML;
        /// <summary>
        /// Edit a Job
        /// </summary>
        /// <param name="aRow">Row to edit</param>
        public JobDialog(Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow aRow)
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(Properties.Settings.Default.TreeViewState))
                this.folderSelectControl1.State = Properties.Settings.Default.TreeViewState;
            this.Text = "Edit job " + aRow.Name + "(" + Utility.User.UserName + ")";
            // Dang background colors in tab pages - can't trust them.
            foreach (TabPage P in this.MainTabControl.TabPages)
                P.BackColor = this.BackColor;
            foreach (TabPage P in this.SourceTabControl.TabPages)
                P.BackColor = this.BackColor;
            this.folderSelectControl1.BackColor = this.BackColor;
            // Add radio buttons for backends; put the backend in the tag
            this.BackEndTableLayoutPanel.Controls.AddRange(
                (from Library.Interface.IBackend qB in Duplicati.Library.DynamicLoader.BackendLoader.Backends
                 orderby qB.DisplayName
                 select BackendRadioButton(qB)).ToArray());
            // Put the tooltip text at the bottom of the screen for easy user viewage
            this.ExplainToolStripLabel.Text = this.MainTabControl.SelectedTab.ToolTipText;
            // And use the row
            SetRow(aRow);
        }
        /// <summary>
        /// Make a radio button with the backend attached to tag
        /// </summary>
        /// <param name="aBackend">Backend to attach</param>
        /// <returns>new radio button</returns>
        private RadioButton BackendRadioButton(Library.Interface.IBackend aBackend)
        {
            RadioButton Result = new RadioButton()
            {
                Text = aBackend.DisplayName,
                Tag = aBackend,
            };
            Result.CheckedChanged += new EventHandler(BackendRadioButtonClicked);
            return Result;
        }

        /// <summary>
        /// The backend control was entered, probably user selected a backend
        /// </summary>
        void BackendRadioButtonClicked(object sender, EventArgs e)
        {
            if (!((RadioButton)sender).Checked) return;
            // Find the checked radio button and assign the GUI from the tag
            GuiControl = ((RadioButton)sender).Tag as Library.Interface.IGUIControl;
            // Show the new user interface
            GuiInterface = GuiControl.GetControl(new Dictionary<string, string>(), Row.GuiOptions);
            GuiInterface.SetBounds(0, 0, this.UIFPanel.Width, this.UIFPanel.Height);
            GuiInterface.Visible = true;
            this.UIFPanel.Controls.Clear();
            this.UIFPanel.Controls.Add(GuiInterface);
        }
        // The current backend user control
        private Library.Interface.IGUIControl GuiControl = null;
        // The current backend user control control
        private Control GuiInterface = null;
        /// <summary>
        /// Returns the XML of a jobs row
        /// </summary>
        /// <param name="aRow">Row to convert</param>
        /// <returns>XML text in byte array</returns>
        private static byte[] RowtoXml(Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow aRow)
        {
            byte[] Result = null;
            using (Duplicati.Scheduler.Data.SchedulerDataSet.JobsDataTable cTable = new Data.SchedulerDataSet.JobsDataTable())
            {
                cTable.Rows.Add(aRow.ItemArray);
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    cTable.WriteXml(ms);
                    Result = ms.ToArray();
                }
            }
            return Result;
        }
        /// <summary>
        /// Set up the controls for a job row
        /// </summary>
        /// <param name="aRow">Row to use</param>
        private void SetRow(Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow aRow)
        {
            this.Row = aRow;
            this.OriginalXML = RowtoXml(aRow);
            this.Text = "Job: "+ Row.Name;
            // Set the source tree contents
            if (!Row.IsSourceNull())
                this.folderSelectControl1.SelectedFolders = Row.Source.Split(new char[] { System.IO.Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            if (Row.IsFullRepeatDaysNull()) Row.FullRepeatDays = 10;
            // Full of it.
            this.FullAlwaysRadioButton.Checked = (Row.IsFullOnlyNull() ? false : Row.FullOnly) || (Row.FullAfterN == 0 && Row.FullRepeatDays == 0);
            this.FullDaysNumericUpDown.Value = (decimal)Row.FullRepeatDays;
            this.FullDaysRadioButton.Checked = Row.FullRepeatDays > 0;
            this.FullAfterNNumericUpDown.Value = (decimal)Row.FullAfterN;
            this.FullAfterNRadioButton.Checked = Row.FullAfterN > 0;
            if (!Row.IsFilterNull()) this.FilterList = Row.FilterLines;
            if (!Row.IsMaxFullsNull()) this.MaxFullsNumericUpDown.Value = Row.MaxFulls;
            if (Row.IsMaxAgeDaysNull()) Row.MaxAgeDays = 0;
            this.MaxAgeCheckBox.Checked = Row.MaxAgeDays > 0;
            this.MaxAgeNnumericUpDown.Value = (decimal)Row.MaxAgeDays;
            if (Row.IsMaxFullsNull()) Row.MaxFulls = 4;
            this.MaxFullsCheckBox.Checked = Row.MaxFulls > 0;
            this.MaxFullsNumericUpDown.Value = (decimal)Row.MaxFulls;
            this.PasswordMethodComboBox.SelectedIndex = Row.GetCheckSrc();
            
            itsAdvanced.Map = Row.MapDrives;
            itsAdvanced.AutoDelete = Row.AutoCleanup;
            this.passwordControl1.CheckMod = aRow.CheckMod;
            this.passwordControl1.Checksum = aRow.Checksum;
            if (Settings.Values.IsCheckSrcNull()) Settings.Values.CheckSrc = false;
            if (!Settings.Values.CheckSrc) this.PasswordMethodComboBox.Items.RemoveAt(2); // No global option if not set
            if (!Row.IsDestinationNull())
            {
                foreach (Control C in this.BackEndTableLayoutPanel.Controls)
                    ((RadioButton)C).Checked = Row.Destination.StartsWith(((Duplicati.Library.Interface.IBackend)C.Tag).ProtocolKey);
            }
        }
        /// <summary>
        /// Rips the configuration from the backend control
        /// </summary>
        /// <param name="outOptions">The Options from the control</param>
        /// <param name="outDestination">The backup destination</param>
        /// <returns></returns>
        private bool GetConfiguration(out Dictionary<string, string> outOptions, out Dictionary<string, string> outGuiOptions, out string outDestination)
        {
            // This makes me feel like a kiddie, and out args are evil
            outDestination = null;
            outOptions = null;
            outGuiOptions = null;
            if (GuiControl == null) return false;
            // First, call the 'Leave' thing
            GuiControl.Leave(GuiInterface);
            bool Result = false;
            // Now, go in with reflection and get that m_options thing
            try
            {
                System.Reflection.FieldInfo fld = GuiInterface.GetType().GetField("m_options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (fld != null)
                {
                    outGuiOptions = (Dictionary<string, string>)fld.GetValue(GuiInterface);
                    outOptions = new Dictionary<string,string>();
                    // Now, we can call the GetConfiguration thing and convert the Options to usable ones.
                    outDestination = GuiControl.GetConfiguration(new Dictionary<string, string>(), outGuiOptions, outOptions);
                    Result = true;
                }
            }
            catch (Exception Ex)
            {
                MessageBox.Show(Ex.Message);
            }
            return Result;
        }
        /// <summary>
        /// Returns a new un-attached JobsRow filled with edit results
        /// </summary>
        /// <returns>a new un-attached JobsRow</returns>
        private Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow GetNewRow()
        {
            Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow NewRow = null;
            // The table will die, yet the row lives on...
            using (Duplicati.Scheduler.Data.SchedulerDataSet.JobsDataTable TempTable = new Duplicati.Scheduler.Data.SchedulerDataSet.JobsDataTable())
            {
                NewRow = TempTable.NewJobsRow();
                NewRow.ItemArray = Row.ItemArray;
                GetRow(NewRow);
            }
            return NewRow;
        }
        /// <summary>
        /// Populate a JobsRow with results from the controls
        /// </summary>
        /// <param name="aRow">Row to fill</param>
        private void GetRow(Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow aRow)
        {
            // Now, if User has been in the Destination tab, we may have a new
            // set of destination and options, fetch them.
            if (GuiControl != null && VisitedDestination)
            {
                Dictionary<string, string> Opts = new Dictionary<string, string>();
                Dictionary<string, string> GuiOpts = new Dictionary<string, string>();
                string Dest = string.Empty;
                if (GetConfiguration(out Opts, out GuiOpts, out Dest))
                {
                    aRow.SetOptions(Opts);
                    aRow.SetGuiOptions(GuiOpts);
                    aRow.Destination = Dest;
                }
            }
            // Get stuff from the controls
            aRow.Source = string.Join(System.IO.Path.PathSeparator.ToString(), this.folderSelectControl1.SelectedFolders);
            aRow.FullOnly = this.FullAlwaysRadioButton.Checked;
            aRow.FullAfterN = this.FullAfterNRadioButton.Checked ? (int)this.FullAfterNNumericUpDown.Value : 0;
            aRow.FullRepeatDays = this.FullDaysRadioButton.Checked ? (int)this.FullDaysNumericUpDown.Value : 0;
            aRow.FilterLines = this.FilterList;
            aRow.MaxFulls = this.MaxFullsCheckBox.Checked ? (int)this.MaxFullsNumericUpDown.Value : 0;
            aRow.MaxAgeDays = this.MaxAgeCheckBox.Checked ? (int)this.MaxAgeNnumericUpDown.Value : 0;
            aRow.Enabled = this.TaskEditor.GetTrigger().Enabled;
            aRow.TriggerXML = TaskEditControl.TriggerToXml(this.TaskEditor.GetTrigger());
            if (itsAdvanced.DialogResult == DialogResult.OK)
            {
                aRow.AutoCleanup = itsAdvanced.AutoDelete;
                aRow.MapDrives = itsAdvanced.Map;
            }
            aRow.Checksum = Settings.Values.Checksum;
            aRow.CheckMod = Settings.Values.CheckMod;
            aRow.SetCheckSrc(this.PasswordMethodComboBox.SelectedIndex);
            // Never edited (?)
            if (aRow.IsLastModNull()) aRow.LastMod = DateTime.Now;
        }
        /// <summary>
        /// Updates a summary with the latest
        /// </summary>
        private void GetSummary()
        {
            this.jobSummary1.SetSummary(GetNewRow(), TaskScheduler.Describe(this.TaskEditor.GetTrigger())); // (Row.TaskName));
            this.jobSummary1.Enabled = this.TaskEditor.GetTrigger().Enabled;
            this.SummaryTabPage.ImageIndex = this.jobSummary1.ValidateSettings() ? 5 : 6;
        }
        /// <summary>
        /// Pressed the OK button, update the row and close
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OKButton_Click(object sender, EventArgs e)
        {
            // JobSummary has error checking, do it
            if (!this.jobSummary1.ValidateSettings())
            {
                MessageBox.Show("The errors must be resolved first.");
                return;
            }
            GetRow(Row);    // Populate the row
            // Get the trigger
            itsTrigger = this.TaskEditor.GetTrigger();
            // And report all OK if there was a change
            this.DialogResult = (OriginalXML == RowtoXml(Row)) ? DialogResult.Ignore : DialogResult.OK;
            Close();
        }
        /// <summary>
        /// Pressed CANCEL, just close
        /// </summary>
        private void CanButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
        // Has user been in the Destination Tab
        private bool VisitedDestination = false;
        /// <summary>
        /// Changed a TAB either directly or with NEXT or BACK
        /// </summary>
        private void MainTabControlSelectedIndexChanged(object sender, EventArgs e)
        {
            // Can we go back?
            this.BackButton.Enabled = this.MainTabControl.SelectedIndex > 0;
            // Show user the tooltip
            this.ExplainToolStripLabel.Text = this.MainTabControl.SelectedTab.ToolTipText;
            // Are we on the summary page?
            bool LastPage = (this.MainTabControl.SelectedIndex == this.MainTabControl.TabPages.Count - 1);
            // What's next
            this.NextButton.Text = LastPage ? " FINISHED " : " NEXT > ";
            // Is we in the Destination place?
            VisitedDestination = VisitedDestination || this.MainTabControl.SelectedTab.Text == "Destination";
            // If summary page, populate it with the latest stuff
            if (LastPage) GetSummary();
        }
        /// <summary>
        /// Pressed BACK - go to previous page
        /// </summary>
        private void BackButton_Click(object sender, EventArgs e)
        {
            this.MainTabControl.SelectedIndex -= 1;
        }
        /// <summary>
        /// Pressed NEXT - go to next page or show 'finished'
        /// </summary>
        private void NextButton_Click(object sender, EventArgs e)
        {
            if (this.NextButton.Text == " FINISHED ")
                this.OKButton_Click(sender, e);
            else
                this.MainTabControl.SelectedIndex++;
        }
        /// <summary>
        /// Get/Set the filters
        /// </summary>
        private string[] FilterList
        {
            get { return (from object qO in this.FilterListBox.Items select qO.ToString()).ToArray(); }
            set { this.FilterListBox.Items.Clear(); this.FilterListBox.Items.AddRange(value); }
        }
        /// <summary>
        /// Pressed FILTERS - edit the filters
        /// </summary>
        private void FiltersToolStripButton_Click(object sender, EventArgs e)
        {
            FilterDialog fd = new FilterDialog();
            fd.Filter = this.FilterList;
            if (fd.ShowDialog() == DialogResult.OK) this.FilterList = fd.Filter;
        }
        /// <summary>
        /// One of the checkboxes or radio buttons changes
        /// </summary>
        private void CheckedChanged(object sender, EventArgs e)
        {
            this.MaxFullsNumericUpDown.Enabled = this.MaxFullsCheckBox.Checked;
            this.MaxAgeNnumericUpDown.Enabled = this.MaxAgeCheckBox.Checked;
            this.FullAfterNNumericUpDown.Enabled = this.FullAfterNRadioButton.Checked;
            this.FullDaysNumericUpDown.Enabled = this.FullDaysRadioButton.Checked;
        }

        private AdvancedDialog itsAdvanced = new AdvancedDialog();
        /// <summary>
        /// Pressed the ADVANCED button
        /// </summary>
        private void AdvancedButtonClick(object sender, EventArgs e)
        {
            itsAdvanced.ShowDialog();
        }
        /// <summary>
        /// Changes the Source TAB page
        /// </summary>
        private void SourceTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.SourceTabControl.SelectedTab == this.ListViewTabPage)
            {
                this.SourceListBox.Items.Clear();
                this.SourceListBox.Items.AddRange(this.folderSelectControl1.SelectedFolders);
            }
        }
        /// <summary>
        /// Changes the password method
        /// </summary>
        private void PasswordMethodComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.passwordControl1.Enabled = this.PasswordMethodComboBox.SelectedIndex == 1;
        }

        private void JobDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.TreeViewState = this.folderSelectControl1.State;
        }
    }
}
