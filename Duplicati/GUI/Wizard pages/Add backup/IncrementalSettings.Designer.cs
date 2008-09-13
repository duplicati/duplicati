namespace Duplicati.Wizard_pages.Add_backup
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
            this.FullBackups = new System.Windows.Forms.CheckBox();
            this.FullSettings = new System.Windows.Forms.GroupBox();
            this.label3 = new System.Windows.Forms.Label();
            this.FullDuration = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.EnableFullBackupClean = new System.Windows.Forms.CheckBox();
            this.CleanFullBackupCount = new System.Windows.Forms.NumericUpDown();
            this.CleanFullBackupHelptext = new System.Windows.Forms.Label();
            this.EnableCleanupDuration = new System.Windows.Forms.CheckBox();
            this.CleanupDurationHelptext = new System.Windows.Forms.Label();
            this.CleanupDuration = new System.Windows.Forms.TextBox();
            this.FullSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.CleanFullBackupCount)).BeginInit();
            this.SuspendLayout();
            // 
            // FullBackups
            // 
            this.FullBackups.AutoSize = true;
            this.FullBackups.Checked = true;
            this.FullBackups.CheckState = System.Windows.Forms.CheckState.Checked;
            this.FullBackups.Location = new System.Drawing.Point(32, 8);
            this.FullBackups.Name = "FullBackups";
            this.FullBackups.Size = new System.Drawing.Size(161, 17);
            this.FullBackups.TabIndex = 3;
            this.FullBackups.Text = "Perfom full backups regularly";
            this.FullBackups.UseVisualStyleBackColor = true;
            this.FullBackups.CheckedChanged += new System.EventHandler(this.FullBackups_CheckedChanged);
            // 
            // FullSettings
            // 
            this.FullSettings.Controls.Add(this.label3);
            this.FullSettings.Controls.Add(this.FullDuration);
            this.FullSettings.Controls.Add(this.label2);
            this.FullSettings.Location = new System.Drawing.Point(24, 8);
            this.FullSettings.Name = "FullSettings";
            this.FullSettings.Size = new System.Drawing.Size(448, 88);
            this.FullSettings.TabIndex = 2;
            this.FullSettings.TabStop = false;
            // 
            // label3
            // 
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(16, 48);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(416, 32);
            this.label3.TabIndex = 2;
            this.label3.Text = "Time is entered as 2D for 2 days, and 1M3D for one month, three days. Valid types" +
                " are s,m,h,D,W,M and Y.";
            // 
            // FullDuration
            // 
            this.FullDuration.Location = new System.Drawing.Point(168, 24);
            this.FullDuration.Name = "FullDuration";
            this.FullDuration.Size = new System.Drawing.Size(264, 20);
            this.FullDuration.TabIndex = 1;
            this.FullDuration.Text = "1M";
            this.FullDuration.TextChanged += new System.EventHandler(this.FullDuration_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(134, 13);
            this.label2.TabIndex = 0;
            this.label2.Text = "Perform a full backup each";
            // 
            // EnableFullBackupClean
            // 
            this.EnableFullBackupClean.AutoSize = true;
            this.EnableFullBackupClean.Checked = true;
            this.EnableFullBackupClean.CheckState = System.Windows.Forms.CheckState.Checked;
            this.EnableFullBackupClean.Location = new System.Drawing.Point(32, 112);
            this.EnableFullBackupClean.Name = "EnableFullBackupClean";
            this.EnableFullBackupClean.Size = new System.Drawing.Size(239, 17);
            this.EnableFullBackupClean.TabIndex = 4;
            this.EnableFullBackupClean.Text = "Never keep more than this many full backups";
            this.EnableFullBackupClean.UseVisualStyleBackColor = true;
            this.EnableFullBackupClean.CheckedChanged += new System.EventHandler(this.EnableFullBackupClean_CheckedChanged);
            // 
            // CleanFullBackupCount
            // 
            this.CleanFullBackupCount.Location = new System.Drawing.Point(288, 112);
            this.CleanFullBackupCount.Name = "CleanFullBackupCount";
            this.CleanFullBackupCount.Size = new System.Drawing.Size(56, 20);
            this.CleanFullBackupCount.TabIndex = 5;
            this.CleanFullBackupCount.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.CleanFullBackupCount.ValueChanged += new System.EventHandler(this.CleanFullBackupCount_ValueChanged);
            // 
            // CleanFullBackupHelptext
            // 
            this.CleanFullBackupHelptext.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CleanFullBackupHelptext.Location = new System.Drawing.Point(32, 136);
            this.CleanFullBackupHelptext.Name = "CleanFullBackupHelptext";
            this.CleanFullBackupHelptext.Size = new System.Drawing.Size(440, 16);
            this.CleanFullBackupHelptext.TabIndex = 3;
            this.CleanFullBackupHelptext.Text = "To prevent the backups from growing indefinetly, old backups should be deleted re" +
                "gularly";
            // 
            // EnableCleanupDuration
            // 
            this.EnableCleanupDuration.AutoSize = true;
            this.EnableCleanupDuration.Location = new System.Drawing.Point(32, 168);
            this.EnableCleanupDuration.Name = "EnableCleanupDuration";
            this.EnableCleanupDuration.Size = new System.Drawing.Size(195, 17);
            this.EnableCleanupDuration.TabIndex = 6;
            this.EnableCleanupDuration.Text = "Never keep backups older than this";
            this.EnableCleanupDuration.UseVisualStyleBackColor = true;
            this.EnableCleanupDuration.CheckedChanged += new System.EventHandler(this.EnableCleanupDuration_CheckedChanged);
            // 
            // CleanupDurationHelptext
            // 
            this.CleanupDurationHelptext.Enabled = false;
            this.CleanupDurationHelptext.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CleanupDurationHelptext.Location = new System.Drawing.Point(32, 192);
            this.CleanupDurationHelptext.Name = "CleanupDurationHelptext";
            this.CleanupDurationHelptext.Size = new System.Drawing.Size(432, 32);
            this.CleanupDurationHelptext.TabIndex = 8;
            this.CleanupDurationHelptext.Text = "Time is entered as 2D for 2 days, and 1M3D for one month, three days. Valid types" +
                " are s,m,h,D,W,M and Y.";
            // 
            // CleanupDuration
            // 
            this.CleanupDuration.Enabled = false;
            this.CleanupDuration.Location = new System.Drawing.Point(232, 168);
            this.CleanupDuration.Name = "CleanupDuration";
            this.CleanupDuration.Size = new System.Drawing.Size(232, 20);
            this.CleanupDuration.TabIndex = 7;
            this.CleanupDuration.Text = "6M";
            this.CleanupDuration.TextChanged += new System.EventHandler(this.CleanupDuration_TextChanged);
            // 
            // IncrementalSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.CleanupDurationHelptext);
            this.Controls.Add(this.CleanupDuration);
            this.Controls.Add(this.EnableCleanupDuration);
            this.Controls.Add(this.CleanFullBackupHelptext);
            this.Controls.Add(this.CleanFullBackupCount);
            this.Controls.Add(this.EnableFullBackupClean);
            this.Controls.Add(this.FullBackups);
            this.Controls.Add(this.FullSettings);
            this.Name = "IncrementalSettings";
            this.Size = new System.Drawing.Size(506, 242);
            this.FullSettings.ResumeLayout(false);
            this.FullSettings.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.CleanFullBackupCount)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox FullBackups;
        private System.Windows.Forms.GroupBox FullSettings;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox FullDuration;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox EnableFullBackupClean;
        private System.Windows.Forms.NumericUpDown CleanFullBackupCount;
        private System.Windows.Forms.Label CleanFullBackupHelptext;
        private System.Windows.Forms.CheckBox EnableCleanupDuration;
        private System.Windows.Forms.Label CleanupDurationHelptext;
        private System.Windows.Forms.TextBox CleanupDuration;
    }
}
