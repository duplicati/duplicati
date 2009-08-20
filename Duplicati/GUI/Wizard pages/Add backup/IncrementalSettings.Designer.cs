namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class IncrementalSettings
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(IncrementalSettings));
            this.FullBackups = new System.Windows.Forms.CheckBox();
            this.FullSettings = new System.Windows.Forms.GroupBox();
            this.FullDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.label2 = new System.Windows.Forms.Label();
            this.EnableFullBackupClean = new System.Windows.Forms.CheckBox();
            this.CleanFullBackupCount = new System.Windows.Forms.NumericUpDown();
            this.CleanFullBackupHelptext = new System.Windows.Forms.Label();
            this.EnableCleanupDuration = new System.Windows.Forms.CheckBox();
            this.CleanupDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.IgnoreTimestamps = new System.Windows.Forms.CheckBox();
            this.FullSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.CleanFullBackupCount)).BeginInit();
            this.SuspendLayout();
            // 
            // FullBackups
            // 
            resources.ApplyResources(this.FullBackups, "FullBackups");
            this.FullBackups.Checked = true;
            this.FullBackups.CheckState = System.Windows.Forms.CheckState.Checked;
            this.FullBackups.Name = "FullBackups";
            this.FullBackups.UseVisualStyleBackColor = true;
            this.FullBackups.CheckedChanged += new System.EventHandler(this.FullBackups_CheckedChanged);
            // 
            // FullSettings
            // 
            this.FullSettings.Controls.Add(this.FullDuration);
            this.FullSettings.Controls.Add(this.label2);
            resources.ApplyResources(this.FullSettings, "FullSettings");
            this.FullSettings.Name = "FullSettings";
            this.FullSettings.TabStop = false;
            // 
            // FullDuration
            // 
            resources.ApplyResources(this.FullDuration, "FullDuration");
            this.FullDuration.Name = "FullDuration";
            this.FullDuration.Value = "";
            this.FullDuration.ValueChanged += new System.EventHandler(this.FullDuration_ValueChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // EnableFullBackupClean
            // 
            resources.ApplyResources(this.EnableFullBackupClean, "EnableFullBackupClean");
            this.EnableFullBackupClean.Checked = true;
            this.EnableFullBackupClean.CheckState = System.Windows.Forms.CheckState.Checked;
            this.EnableFullBackupClean.Name = "EnableFullBackupClean";
            this.EnableFullBackupClean.UseVisualStyleBackColor = true;
            this.EnableFullBackupClean.CheckedChanged += new System.EventHandler(this.EnableFullBackupClean_CheckedChanged);
            // 
            // CleanFullBackupCount
            // 
            resources.ApplyResources(this.CleanFullBackupCount, "CleanFullBackupCount");
            this.CleanFullBackupCount.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.CleanFullBackupCount.Name = "CleanFullBackupCount";
            this.CleanFullBackupCount.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.CleanFullBackupCount.ValueChanged += new System.EventHandler(this.CleanFullBackupCount_ValueChanged);
            // 
            // CleanFullBackupHelptext
            // 
            resources.ApplyResources(this.CleanFullBackupHelptext, "CleanFullBackupHelptext");
            this.CleanFullBackupHelptext.Name = "CleanFullBackupHelptext";
            // 
            // EnableCleanupDuration
            // 
            resources.ApplyResources(this.EnableCleanupDuration, "EnableCleanupDuration");
            this.EnableCleanupDuration.Name = "EnableCleanupDuration";
            this.EnableCleanupDuration.UseVisualStyleBackColor = true;
            this.EnableCleanupDuration.CheckedChanged += new System.EventHandler(this.EnableCleanupDuration_CheckedChanged);
            // 
            // CleanupDuration
            // 
            resources.ApplyResources(this.CleanupDuration, "CleanupDuration");
            this.CleanupDuration.Name = "CleanupDuration";
            this.CleanupDuration.Value = "";
            this.CleanupDuration.ValueChanged += new System.EventHandler(this.CleanupDuration_ValueChanged);
            // 
            // IgnoreTimestamps
            // 
            resources.ApplyResources(this.IgnoreTimestamps, "IgnoreTimestamps");
            this.IgnoreTimestamps.Name = "IgnoreTimestamps";
            this.IgnoreTimestamps.UseVisualStyleBackColor = true;
            // 
            // IncrementalSettings
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.IgnoreTimestamps);
            this.Controls.Add(this.CleanupDuration);
            this.Controls.Add(this.EnableCleanupDuration);
            this.Controls.Add(this.CleanFullBackupHelptext);
            this.Controls.Add(this.CleanFullBackupCount);
            this.Controls.Add(this.EnableFullBackupClean);
            this.Controls.Add(this.FullBackups);
            this.Controls.Add(this.FullSettings);
            this.Name = "IncrementalSettings";
            this.Load += new System.EventHandler(this.IncrementalSettings_Load);
            this.FullSettings.ResumeLayout(false);
            this.FullSettings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.CleanFullBackupCount)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox FullBackups;
        private System.Windows.Forms.GroupBox FullSettings;
        private System.Windows.Forms.Label label2;
        private Duplicati.GUI.HelperControls.DurationEditor FullDuration;
        private System.Windows.Forms.CheckBox EnableFullBackupClean;
        private System.Windows.Forms.NumericUpDown CleanFullBackupCount;
        private System.Windows.Forms.Label CleanFullBackupHelptext;
        private System.Windows.Forms.CheckBox EnableCleanupDuration;
        private Duplicati.GUI.HelperControls.DurationEditor CleanupDuration;
        private System.Windows.Forms.CheckBox IgnoreTimestamps;
    }
}
