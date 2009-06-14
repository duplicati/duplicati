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
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.RepeatInterval = new Duplicati.GUI.HelperControls.DurationEditor();
            this.label2 = new System.Windows.Forms.Label();
            this.EnableRepeat = new System.Windows.Forms.CheckBox();
            this.OffsetTime = new System.Windows.Forms.DateTimePicker();
            this.OffsetDate = new System.Windows.Forms.DateTimePicker();
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.RepeatInterval);
            this.groupBox1.Controls.Add(this.label2);
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            // 
            // RepeatInterval
            // 
            resources.ApplyResources(this.RepeatInterval, "RepeatInterval");
            this.RepeatInterval.Name = "RepeatInterval";
            this.RepeatInterval.Value = "";
            this.RepeatInterval.ValueChanged += new System.EventHandler(this.RepeatInterval_ValueChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // EnableRepeat
            // 
            resources.ApplyResources(this.EnableRepeat, "EnableRepeat");
            this.EnableRepeat.Checked = true;
            this.EnableRepeat.CheckState = System.Windows.Forms.CheckState.Checked;
            this.EnableRepeat.Name = "EnableRepeat";
            this.EnableRepeat.UseVisualStyleBackColor = true;
            // 
            // OffsetTime
            // 
            this.OffsetTime.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            resources.ApplyResources(this.OffsetTime, "OffsetTime");
            this.OffsetTime.Name = "OffsetTime";
            this.OffsetTime.ShowUpDown = true;
            this.OffsetTime.ValueChanged += new System.EventHandler(this.OffsetTime_ValueChanged);
            // 
            // OffsetDate
            // 
            resources.ApplyResources(this.OffsetDate, "OffsetDate");
            this.OffsetDate.Name = "OffsetDate";
            this.OffsetDate.ValueChanged += new System.EventHandler(this.OffsetDate_ValueChanged);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // SelectWhen
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.OffsetTime);
            this.Controls.Add(this.EnableRepeat);
            this.Controls.Add(this.OffsetDate);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.groupBox1);
            this.Name = "SelectWhen";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.CheckBox EnableRepeat;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.DateTimePicker OffsetTime;
        private System.Windows.Forms.DateTimePicker OffsetDate;
        private System.Windows.Forms.Label label1;
        private Duplicati.GUI.HelperControls.DurationEditor RepeatInterval;
    }
}
