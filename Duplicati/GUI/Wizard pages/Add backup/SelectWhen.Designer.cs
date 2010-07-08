namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class SelectWhen
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectWhen));
            this.label2 = new System.Windows.Forms.Label();
            this.OffsetTime = new System.Windows.Forms.DateTimePicker();
            this.OffsetDate = new System.Windows.Forms.DateTimePicker();
            this.label1 = new System.Windows.Forms.Label();
            this.ScheduleGroup = new System.Windows.Forms.GroupBox();
            this.AllowedDaysPanel = new System.Windows.Forms.Panel();
            this.Day7Label = new System.Windows.Forms.Label();
            this.Day6Label = new System.Windows.Forms.Label();
            this.Day5Label = new System.Windows.Forms.Label();
            this.Day4Label = new System.Windows.Forms.Label();
            this.Day3Label = new System.Windows.Forms.Label();
            this.Sunday = new System.Windows.Forms.CheckBox();
            this.Saturday = new System.Windows.Forms.CheckBox();
            this.Friday = new System.Windows.Forms.CheckBox();
            this.Wednesday = new System.Windows.Forms.CheckBox();
            this.Thursday = new System.Windows.Forms.CheckBox();
            this.Day2Label = new System.Windows.Forms.Label();
            this.Day1Label = new System.Windows.Forms.Label();
            this.Tuesday = new System.Windows.Forms.CheckBox();
            this.Monday = new System.Windows.Forms.CheckBox();
            this.label3 = new System.Windows.Forms.Label();
            this.NoScheduleRadio = new System.Windows.Forms.RadioButton();
            this.ScheduleRadio = new System.Windows.Forms.RadioButton();
            this.FullStrategyPanel = new System.Windows.Forms.GroupBox();
            this.AlwaysFullRadio = new System.Windows.Forms.RadioButton();
            this.NeverFullRadio = new System.Windows.Forms.RadioButton();
            this.IncrementalPeriodRadio = new System.Windows.Forms.RadioButton();
            this.FullDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.RepeatInterval = new Duplicati.GUI.HelperControls.DurationEditor();
            this.ScheduleGroup.SuspendLayout();
            this.AllowedDaysPanel.SuspendLayout();
            this.FullStrategyPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // OffsetTime
            // 
            this.OffsetTime.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            resources.ApplyResources(this.OffsetTime, "OffsetTime");
            this.OffsetTime.Name = "OffsetTime";
            this.OffsetTime.ShowUpDown = true;
            // 
            // OffsetDate
            // 
            resources.ApplyResources(this.OffsetDate, "OffsetDate");
            this.OffsetDate.Name = "OffsetDate";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // ScheduleGroup
            // 
            this.ScheduleGroup.Controls.Add(this.AllowedDaysPanel);
            this.ScheduleGroup.Controls.Add(this.label3);
            this.ScheduleGroup.Controls.Add(this.RepeatInterval);
            this.ScheduleGroup.Controls.Add(this.label2);
            this.ScheduleGroup.Controls.Add(this.OffsetTime);
            this.ScheduleGroup.Controls.Add(this.OffsetDate);
            this.ScheduleGroup.Controls.Add(this.label1);
            resources.ApplyResources(this.ScheduleGroup, "ScheduleGroup");
            this.ScheduleGroup.Name = "ScheduleGroup";
            this.ScheduleGroup.TabStop = false;
            // 
            // AllowedDaysPanel
            // 
            this.AllowedDaysPanel.Controls.Add(this.Day7Label);
            this.AllowedDaysPanel.Controls.Add(this.Day6Label);
            this.AllowedDaysPanel.Controls.Add(this.Day5Label);
            this.AllowedDaysPanel.Controls.Add(this.Day4Label);
            this.AllowedDaysPanel.Controls.Add(this.Day3Label);
            this.AllowedDaysPanel.Controls.Add(this.Sunday);
            this.AllowedDaysPanel.Controls.Add(this.Saturday);
            this.AllowedDaysPanel.Controls.Add(this.Friday);
            this.AllowedDaysPanel.Controls.Add(this.Wednesday);
            this.AllowedDaysPanel.Controls.Add(this.Thursday);
            this.AllowedDaysPanel.Controls.Add(this.Day2Label);
            this.AllowedDaysPanel.Controls.Add(this.Day1Label);
            this.AllowedDaysPanel.Controls.Add(this.Tuesday);
            this.AllowedDaysPanel.Controls.Add(this.Monday);
            resources.ApplyResources(this.AllowedDaysPanel, "AllowedDaysPanel");
            this.AllowedDaysPanel.Name = "AllowedDaysPanel";
            // 
            // Day7Label
            // 
            resources.ApplyResources(this.Day7Label, "Day7Label");
            this.Day7Label.Name = "Day7Label";
            // 
            // Day6Label
            // 
            resources.ApplyResources(this.Day6Label, "Day6Label");
            this.Day6Label.Name = "Day6Label";
            // 
            // Day5Label
            // 
            resources.ApplyResources(this.Day5Label, "Day5Label");
            this.Day5Label.Name = "Day5Label";
            // 
            // Day4Label
            // 
            resources.ApplyResources(this.Day4Label, "Day4Label");
            this.Day4Label.Name = "Day4Label";
            // 
            // Day3Label
            // 
            resources.ApplyResources(this.Day3Label, "Day3Label");
            this.Day3Label.Name = "Day3Label";
            // 
            // Sunday
            // 
            resources.ApplyResources(this.Sunday, "Sunday");
            this.Sunday.Checked = true;
            this.Sunday.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Sunday.Name = "Sunday";
            this.Sunday.UseVisualStyleBackColor = true;
            this.Sunday.CheckedChanged += new System.EventHandler(this.AllowedDay_CheckedChanged);
            // 
            // Saturday
            // 
            resources.ApplyResources(this.Saturday, "Saturday");
            this.Saturday.Checked = true;
            this.Saturday.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Saturday.Name = "Saturday";
            this.Saturday.UseVisualStyleBackColor = true;
            this.Saturday.CheckedChanged += new System.EventHandler(this.AllowedDay_CheckedChanged);
            // 
            // Friday
            // 
            resources.ApplyResources(this.Friday, "Friday");
            this.Friday.Checked = true;
            this.Friday.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Friday.Name = "Friday";
            this.Friday.UseVisualStyleBackColor = true;
            this.Friday.CheckedChanged += new System.EventHandler(this.AllowedDay_CheckedChanged);
            // 
            // Wednesday
            // 
            resources.ApplyResources(this.Wednesday, "Wednesday");
            this.Wednesday.Checked = true;
            this.Wednesday.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Wednesday.Name = "Wednesday";
            this.Wednesday.UseVisualStyleBackColor = true;
            this.Wednesday.CheckedChanged += new System.EventHandler(this.AllowedDay_CheckedChanged);
            // 
            // Thursday
            // 
            resources.ApplyResources(this.Thursday, "Thursday");
            this.Thursday.Checked = true;
            this.Thursday.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Thursday.Name = "Thursday";
            this.Thursday.UseVisualStyleBackColor = true;
            this.Thursday.CheckedChanged += new System.EventHandler(this.AllowedDay_CheckedChanged);
            // 
            // Day2Label
            // 
            resources.ApplyResources(this.Day2Label, "Day2Label");
            this.Day2Label.Name = "Day2Label";
            // 
            // Day1Label
            // 
            resources.ApplyResources(this.Day1Label, "Day1Label");
            this.Day1Label.Name = "Day1Label";
            // 
            // Tuesday
            // 
            resources.ApplyResources(this.Tuesday, "Tuesday");
            this.Tuesday.Checked = true;
            this.Tuesday.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Tuesday.Name = "Tuesday";
            this.Tuesday.UseVisualStyleBackColor = true;
            this.Tuesday.CheckedChanged += new System.EventHandler(this.AllowedDay_CheckedChanged);
            // 
            // Monday
            // 
            resources.ApplyResources(this.Monday, "Monday");
            this.Monday.Checked = true;
            this.Monday.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Monday.Name = "Monday";
            this.Monday.UseVisualStyleBackColor = true;
            this.Monday.CheckedChanged += new System.EventHandler(this.AllowedDay_CheckedChanged);
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // NoScheduleRadio
            // 
            resources.ApplyResources(this.NoScheduleRadio, "NoScheduleRadio");
            this.NoScheduleRadio.Name = "NoScheduleRadio";
            this.NoScheduleRadio.UseVisualStyleBackColor = true;
            this.NoScheduleRadio.CheckedChanged += new System.EventHandler(this.NoScheduleRadio_CheckedChanged);
            // 
            // ScheduleRadio
            // 
            resources.ApplyResources(this.ScheduleRadio, "ScheduleRadio");
            this.ScheduleRadio.Checked = true;
            this.ScheduleRadio.Name = "ScheduleRadio";
            this.ScheduleRadio.TabStop = true;
            this.ScheduleRadio.UseVisualStyleBackColor = true;
            this.ScheduleRadio.CheckedChanged += new System.EventHandler(this.ScheduleRadio_CheckedChanged);
            // 
            // FullStrategyPanel
            // 
            this.FullStrategyPanel.Controls.Add(this.AlwaysFullRadio);
            this.FullStrategyPanel.Controls.Add(this.NeverFullRadio);
            this.FullStrategyPanel.Controls.Add(this.IncrementalPeriodRadio);
            this.FullStrategyPanel.Controls.Add(this.FullDuration);
            resources.ApplyResources(this.FullStrategyPanel, "FullStrategyPanel");
            this.FullStrategyPanel.Name = "FullStrategyPanel";
            this.FullStrategyPanel.TabStop = false;
            // 
            // AlwaysFullRadio
            // 
            resources.ApplyResources(this.AlwaysFullRadio, "AlwaysFullRadio");
            this.AlwaysFullRadio.Name = "AlwaysFullRadio";
            this.AlwaysFullRadio.TabStop = true;
            this.AlwaysFullRadio.UseVisualStyleBackColor = true;
            this.AlwaysFullRadio.CheckedChanged += new System.EventHandler(this.AlwaysFullRadio_CheckedChanged);
            // 
            // NeverFullRadio
            // 
            resources.ApplyResources(this.NeverFullRadio, "NeverFullRadio");
            this.NeverFullRadio.Name = "NeverFullRadio";
            this.NeverFullRadio.TabStop = true;
            this.NeverFullRadio.UseVisualStyleBackColor = true;
            this.NeverFullRadio.CheckedChanged += new System.EventHandler(this.NeverFullRadio_CheckedChanged);
            // 
            // IncrementalPeriodRadio
            // 
            resources.ApplyResources(this.IncrementalPeriodRadio, "IncrementalPeriodRadio");
            this.IncrementalPeriodRadio.Name = "IncrementalPeriodRadio";
            this.IncrementalPeriodRadio.TabStop = true;
            this.IncrementalPeriodRadio.UseVisualStyleBackColor = true;
            this.IncrementalPeriodRadio.CheckedChanged += new System.EventHandler(this.IncrementalPeriodRadio_CheckedChanged);
            // 
            // FullDuration
            // 
            resources.ApplyResources(this.FullDuration, "FullDuration");
            this.FullDuration.Name = "FullDuration";
            this.FullDuration.Value = "";
            // 
            // RepeatInterval
            // 
            resources.ApplyResources(this.RepeatInterval, "RepeatInterval");
            this.RepeatInterval.Name = "RepeatInterval";
            this.RepeatInterval.Value = "";
            this.RepeatInterval.ValueChanged += new System.EventHandler(this.RepeatInterval_ValueChanged);
            // 
            // SelectWhen
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.FullStrategyPanel);
            this.Controls.Add(this.ScheduleRadio);
            this.Controls.Add(this.NoScheduleRadio);
            this.Controls.Add(this.ScheduleGroup);
            this.Name = "SelectWhen";
            this.ScheduleGroup.ResumeLayout(false);
            this.ScheduleGroup.PerformLayout();
            this.AllowedDaysPanel.ResumeLayout(false);
            this.AllowedDaysPanel.PerformLayout();
            this.FullStrategyPanel.ResumeLayout(false);
            this.FullStrategyPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.DateTimePicker OffsetTime;
        private System.Windows.Forms.DateTimePicker OffsetDate;
        private System.Windows.Forms.Label label1;
        private Duplicati.GUI.HelperControls.DurationEditor RepeatInterval;
        private System.Windows.Forms.GroupBox ScheduleGroup;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox Monday;
        private System.Windows.Forms.Panel AllowedDaysPanel;
        private System.Windows.Forms.Label Day7Label;
        private System.Windows.Forms.Label Day6Label;
        private System.Windows.Forms.Label Day5Label;
        private System.Windows.Forms.Label Day4Label;
        private System.Windows.Forms.Label Day3Label;
        private System.Windows.Forms.CheckBox Sunday;
        private System.Windows.Forms.CheckBox Saturday;
        private System.Windows.Forms.CheckBox Friday;
        private System.Windows.Forms.CheckBox Wednesday;
        private System.Windows.Forms.CheckBox Thursday;
        private System.Windows.Forms.Label Day2Label;
        private System.Windows.Forms.Label Day1Label;
        private System.Windows.Forms.CheckBox Tuesday;
        private Duplicati.GUI.HelperControls.DurationEditor FullDuration;
        private System.Windows.Forms.RadioButton NoScheduleRadio;
        private System.Windows.Forms.RadioButton ScheduleRadio;
        private System.Windows.Forms.GroupBox FullStrategyPanel;
        private System.Windows.Forms.RadioButton AlwaysFullRadio;
        private System.Windows.Forms.RadioButton NeverFullRadio;
        private System.Windows.Forms.RadioButton IncrementalPeriodRadio;
    }
}
