#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
namespace Duplicati.Service_controls
{
    partial class ScheduleSettings
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
            this.components = new System.ComponentModel.Container();
            this.label1 = new System.Windows.Forms.Label();
            this.OffsetDate = new System.Windows.Forms.DateTimePicker();
            this.OffsetTime = new System.Windows.Forms.DateTimePicker();
            this.RepeatCheck = new System.Windows.Forms.CheckBox();
            this.RepeatInterval = new System.Windows.Forms.TextBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.CleanupDuration = new System.Windows.Forms.TextBox();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.label2 = new System.Windows.Forms.Label();
            this.WeekdayPicker = new System.Windows.Forms.CheckedListBox();
            this.TimingGroup = new System.Windows.Forms.GroupBox();
            this.CleanGroup = new System.Windows.Forms.GroupBox();
            this.CleanupFullCount = new System.Windows.Forms.NumericUpDown();
            this.KeepIntervalCheckbox = new System.Windows.Forms.CheckBox();
            this.KeepNFullCheckbox = new System.Windows.Forms.CheckBox();
            this.FullGroup = new System.Windows.Forms.GroupBox();
            this.ForceFullBackupTimespan = new System.Windows.Forms.TextBox();
            this.ForceFullBackup = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.TimingGroup.SuspendLayout();
            this.CleanGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.CleanupFullCount)).BeginInit();
            this.FullGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(35, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Offset";
            // 
            // OffsetDate
            // 
            this.OffsetDate.Location = new System.Drawing.Point(64, 24);
            this.OffsetDate.Name = "OffsetDate";
            this.OffsetDate.Size = new System.Drawing.Size(128, 20);
            this.OffsetDate.TabIndex = 1;
            this.OffsetDate.ValueChanged += new System.EventHandler(this.OffsetDate_ValueChanged);
            // 
            // OffsetTime
            // 
            this.OffsetTime.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            this.OffsetTime.Location = new System.Drawing.Point(192, 24);
            this.OffsetTime.Name = "OffsetTime";
            this.OffsetTime.ShowUpDown = true;
            this.OffsetTime.Size = new System.Drawing.Size(80, 20);
            this.OffsetTime.TabIndex = 2;
            this.OffsetTime.ValueChanged += new System.EventHandler(this.OffsetTime_ValueChanged);
            // 
            // RepeatCheck
            // 
            this.RepeatCheck.AutoSize = true;
            this.RepeatCheck.Location = new System.Drawing.Point(8, 48);
            this.RepeatCheck.Name = "RepeatCheck";
            this.RepeatCheck.Size = new System.Drawing.Size(91, 17);
            this.RepeatCheck.TabIndex = 3;
            this.RepeatCheck.Text = "Repeat each:";
            this.RepeatCheck.UseVisualStyleBackColor = true;
            // 
            // RepeatInterval
            // 
            this.errorProvider.SetIconAlignment(this.RepeatInterval, System.Windows.Forms.ErrorIconAlignment.MiddleLeft);
            this.RepeatInterval.Location = new System.Drawing.Point(120, 48);
            this.RepeatInterval.Name = "RepeatInterval";
            this.RepeatInterval.Size = new System.Drawing.Size(152, 20);
            this.RepeatInterval.TabIndex = 4;
            this.toolTip.SetToolTip(this.RepeatInterval, "Enter the duration as a string, ea.: 1Y2M = 1 year, 2 months.  Valid identifiers " +
                    "are: s, m, h, D, M, Y.");
            this.RepeatInterval.TextChanged += new System.EventHandler(this.RepeatInterval_TextChanged);
            // 
            // CleanupDuration
            // 
            this.errorProvider.SetIconAlignment(this.CleanupDuration, System.Windows.Forms.ErrorIconAlignment.MiddleLeft);
            this.CleanupDuration.Location = new System.Drawing.Point(120, 64);
            this.CleanupDuration.Name = "CleanupDuration";
            this.CleanupDuration.Size = new System.Drawing.Size(152, 20);
            this.CleanupDuration.TabIndex = 5;
            this.toolTip.SetToolTip(this.CleanupDuration, "Enter the duration as a string, ea.: 1Y2M = 1 year, 2 months.  Valid identifiers " +
                    "are: s, m, h, D, M, Y.");
            this.CleanupDuration.TextChanged += new System.EventHandler(this.CleanupDuration_TextChanged);
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 72);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(88, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Allow running on:";
            // 
            // WeekdayPicker
            // 
            this.WeekdayPicker.FormattingEnabled = true;
            this.WeekdayPicker.Items.AddRange(new object[] {
            "All days",
            "Weekdays",
            "Weekends",
            "Sunday",
            "Monday",
            "Tuesday",
            "Wednesday",
            "Thursday",
            "Friday",
            "Saturday"});
            this.WeekdayPicker.Location = new System.Drawing.Point(8, 88);
            this.WeekdayPicker.Name = "WeekdayPicker";
            this.WeekdayPicker.Size = new System.Drawing.Size(264, 154);
            this.WeekdayPicker.TabIndex = 6;
            this.WeekdayPicker.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this.WeekdayPicker_ItemCheck);
            // 
            // TimingGroup
            // 
            this.TimingGroup.Controls.Add(this.WeekdayPicker);
            this.TimingGroup.Controls.Add(this.label2);
            this.TimingGroup.Controls.Add(this.RepeatInterval);
            this.TimingGroup.Controls.Add(this.RepeatCheck);
            this.TimingGroup.Controls.Add(this.OffsetTime);
            this.TimingGroup.Controls.Add(this.OffsetDate);
            this.TimingGroup.Controls.Add(this.label1);
            this.TimingGroup.Location = new System.Drawing.Point(0, 0);
            this.TimingGroup.Name = "TimingGroup";
            this.TimingGroup.Size = new System.Drawing.Size(280, 256);
            this.TimingGroup.TabIndex = 7;
            this.TimingGroup.TabStop = false;
            this.TimingGroup.Text = "Time settings";
            // 
            // CleanGroup
            // 
            this.CleanGroup.Controls.Add(this.CleanupDuration);
            this.CleanGroup.Controls.Add(this.CleanupFullCount);
            this.CleanGroup.Controls.Add(this.KeepIntervalCheckbox);
            this.CleanGroup.Controls.Add(this.KeepNFullCheckbox);
            this.CleanGroup.Location = new System.Drawing.Point(0, 264);
            this.CleanGroup.Name = "CleanGroup";
            this.CleanGroup.Size = new System.Drawing.Size(280, 96);
            this.CleanGroup.TabIndex = 8;
            this.CleanGroup.TabStop = false;
            this.CleanGroup.Text = "Clean up settings";
            // 
            // CleanupFullCount
            // 
            this.CleanupFullCount.Location = new System.Drawing.Point(192, 16);
            this.CleanupFullCount.Name = "CleanupFullCount";
            this.CleanupFullCount.Size = new System.Drawing.Size(80, 20);
            this.CleanupFullCount.TabIndex = 2;
            this.CleanupFullCount.ValueChanged += new System.EventHandler(this.CleanupFullCount_ValueChanged);
            // 
            // KeepIntervalCheckbox
            // 
            this.KeepIntervalCheckbox.AutoSize = true;
            this.KeepIntervalCheckbox.Location = new System.Drawing.Point(16, 40);
            this.KeepIntervalCheckbox.Name = "KeepIntervalCheckbox";
            this.KeepIntervalCheckbox.Size = new System.Drawing.Size(163, 17);
            this.KeepIntervalCheckbox.TabIndex = 1;
            this.KeepIntervalCheckbox.Text = "Keep no backups older than:";
            this.KeepIntervalCheckbox.UseVisualStyleBackColor = true;
            // 
            // KeepNFullCheckbox
            // 
            this.KeepNFullCheckbox.AutoSize = true;
            this.KeepNFullCheckbox.Location = new System.Drawing.Point(16, 16);
            this.KeepNFullCheckbox.Name = "KeepNFullCheckbox";
            this.KeepNFullCheckbox.Size = new System.Drawing.Size(169, 17);
            this.KeepNFullCheckbox.TabIndex = 0;
            this.KeepNFullCheckbox.Text = "Keep no more full copies than:";
            this.KeepNFullCheckbox.UseVisualStyleBackColor = true;
            // 
            // FullGroup
            // 
            this.FullGroup.Controls.Add(this.ForceFullBackupTimespan);
            this.FullGroup.Controls.Add(this.ForceFullBackup);
            this.FullGroup.Location = new System.Drawing.Point(0, 368);
            this.FullGroup.Name = "FullGroup";
            this.FullGroup.Size = new System.Drawing.Size(280, 80);
            this.FullGroup.TabIndex = 9;
            this.FullGroup.TabStop = false;
            this.FullGroup.Text = "Incremental";
            // 
            // ForceFullBackupTimespan
            // 
            this.errorProvider.SetIconAlignment(this.ForceFullBackupTimespan, System.Windows.Forms.ErrorIconAlignment.MiddleLeft);
            this.ForceFullBackupTimespan.Location = new System.Drawing.Point(120, 48);
            this.ForceFullBackupTimespan.Name = "ForceFullBackupTimespan";
            this.ForceFullBackupTimespan.Size = new System.Drawing.Size(152, 20);
            this.ForceFullBackupTimespan.TabIndex = 5;
            this.toolTip.SetToolTip(this.ForceFullBackupTimespan, "Enter the duration as a string, ea.: 1Y2M = 1 year, 2 months.  Valid identifiers " +
                    "are: s, m, h, D, M, Y.");
            this.ForceFullBackupTimespan.TextChanged += new System.EventHandler(this.ForceFullBackupTimespan_TextChanged);
            // 
            // ForceFullBackup
            // 
            this.ForceFullBackup.AutoSize = true;
            this.ForceFullBackup.Location = new System.Drawing.Point(16, 24);
            this.ForceFullBackup.Name = "ForceFullBackup";
            this.ForceFullBackup.Size = new System.Drawing.Size(144, 17);
            this.ForceFullBackup.TabIndex = 1;
            this.ForceFullBackup.Text = "Force a full backup after:";
            this.ForceFullBackup.UseVisualStyleBackColor = true;
            // 
            // ScheduleSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.FullGroup);
            this.Controls.Add(this.CleanGroup);
            this.Controls.Add(this.TimingGroup);
            this.Name = "ScheduleSettings";
            this.Size = new System.Drawing.Size(283, 452);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.TimingGroup.ResumeLayout(false);
            this.TimingGroup.PerformLayout();
            this.CleanGroup.ResumeLayout(false);
            this.CleanGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.CleanupFullCount)).EndInit();
            this.FullGroup.ResumeLayout(false);
            this.FullGroup.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.DateTimePicker OffsetDate;
        private System.Windows.Forms.DateTimePicker OffsetTime;
        private System.Windows.Forms.CheckBox RepeatCheck;
        private System.Windows.Forms.TextBox RepeatInterval;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.ErrorProvider errorProvider;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox CleanGroup;
        private System.Windows.Forms.TextBox CleanupDuration;
        private System.Windows.Forms.NumericUpDown CleanupFullCount;
        private System.Windows.Forms.CheckBox KeepIntervalCheckbox;
        private System.Windows.Forms.CheckBox KeepNFullCheckbox;
        private System.Windows.Forms.GroupBox TimingGroup;
        private System.Windows.Forms.CheckedListBox WeekdayPicker;
        private System.Windows.Forms.GroupBox FullGroup;
        private System.Windows.Forms.TextBox ForceFullBackupTimespan;
        private System.Windows.Forms.CheckBox ForceFullBackup;
    }
}
