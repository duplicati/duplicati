namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class ThrottleOptions
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.UploadLimitNumber = new System.Windows.Forms.NumericUpDown();
            this.DownloadLimitNumber = new System.Windows.Forms.NumericUpDown();
            this.UploadLimitSuffix = new System.Windows.Forms.ComboBox();
            this.DownloadLimitSuffix = new System.Windows.Forms.ComboBox();
            this.UploadLimitEnabled = new System.Windows.Forms.CheckBox();
            this.DownloadLimitEnabled = new System.Windows.Forms.CheckBox();
            this.BackupLimitEnabled = new System.Windows.Forms.CheckBox();
            this.BackupLimitSuffix = new System.Windows.Forms.ComboBox();
            this.BackupLimitNumber = new System.Windows.Forms.NumericUpDown();
            this.VolumeSizeLimitSuffix = new System.Windows.Forms.ComboBox();
            this.VolumeSizeLimitNumber = new System.Windows.Forms.NumericUpDown();
            this.AsyncEnabled = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.ThreadPriorityEnabled = new System.Windows.Forms.CheckBox();
            this.ThreadPriority = new System.Windows.Forms.ComboBox();
            ((System.ComponentModel.ISupportInitialize)(this.UploadLimitNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.DownloadLimitNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.BackupLimitNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.VolumeSizeLimitNumber)).BeginInit();
            this.SuspendLayout();
            // 
            // UploadLimitNumber
            // 
            this.UploadLimitNumber.Enabled = false;
            this.UploadLimitNumber.Location = new System.Drawing.Point(184, 16);
            this.UploadLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.UploadLimitNumber.Name = "UploadLimitNumber";
            this.UploadLimitNumber.Size = new System.Drawing.Size(88, 20);
            this.UploadLimitNumber.TabIndex = 3;
            this.UploadLimitNumber.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // DownloadLimitNumber
            // 
            this.DownloadLimitNumber.Enabled = false;
            this.DownloadLimitNumber.Location = new System.Drawing.Point(184, 48);
            this.DownloadLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.DownloadLimitNumber.Name = "DownloadLimitNumber";
            this.DownloadLimitNumber.Size = new System.Drawing.Size(88, 20);
            this.DownloadLimitNumber.TabIndex = 4;
            this.DownloadLimitNumber.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // UploadLimitSuffix
            // 
            this.UploadLimitSuffix.Enabled = false;
            this.UploadLimitSuffix.FormattingEnabled = true;
            this.UploadLimitSuffix.Items.AddRange(new object[] {
            "b/s",
            "kb/s",
            "mb/s",
            "gb/s"});
            this.UploadLimitSuffix.Location = new System.Drawing.Point(280, 16);
            this.UploadLimitSuffix.Name = "UploadLimitSuffix";
            this.UploadLimitSuffix.Size = new System.Drawing.Size(72, 21);
            this.UploadLimitSuffix.TabIndex = 5;
            // 
            // DownloadLimitSuffix
            // 
            this.DownloadLimitSuffix.Enabled = false;
            this.DownloadLimitSuffix.FormattingEnabled = true;
            this.DownloadLimitSuffix.Items.AddRange(new object[] {
            "b/s",
            "kb/s",
            "mb/s",
            "gb/s"});
            this.DownloadLimitSuffix.Location = new System.Drawing.Point(280, 48);
            this.DownloadLimitSuffix.Name = "DownloadLimitSuffix";
            this.DownloadLimitSuffix.Size = new System.Drawing.Size(72, 21);
            this.DownloadLimitSuffix.TabIndex = 6;
            // 
            // UploadLimitEnabled
            // 
            this.UploadLimitEnabled.AutoSize = true;
            this.UploadLimitEnabled.Location = new System.Drawing.Point(32, 16);
            this.UploadLimitEnabled.Name = "UploadLimitEnabled";
            this.UploadLimitEnabled.Size = new System.Drawing.Size(125, 17);
            this.UploadLimitEnabled.TabIndex = 7;
            this.UploadLimitEnabled.Text = "Upload limit (backup)";
            this.UploadLimitEnabled.UseVisualStyleBackColor = true;
            this.UploadLimitEnabled.CheckedChanged += new System.EventHandler(this.UploadLimitEnabled_CheckedChanged);
            // 
            // DownloadLimitEnabled
            // 
            this.DownloadLimitEnabled.AutoSize = true;
            this.DownloadLimitEnabled.Location = new System.Drawing.Point(32, 48);
            this.DownloadLimitEnabled.Name = "DownloadLimitEnabled";
            this.DownloadLimitEnabled.Size = new System.Drawing.Size(135, 17);
            this.DownloadLimitEnabled.TabIndex = 8;
            this.DownloadLimitEnabled.Text = "Download limit (restore)";
            this.DownloadLimitEnabled.UseVisualStyleBackColor = true;
            this.DownloadLimitEnabled.CheckedChanged += new System.EventHandler(this.DownloadLimitEnabled_CheckedChanged);
            // 
            // BackupLimitEnabled
            // 
            this.BackupLimitEnabled.AutoSize = true;
            this.BackupLimitEnabled.Location = new System.Drawing.Point(32, 96);
            this.BackupLimitEnabled.Name = "BackupLimitEnabled";
            this.BackupLimitEnabled.Size = new System.Drawing.Size(134, 17);
            this.BackupLimitEnabled.TabIndex = 9;
            this.BackupLimitEnabled.Text = "Each backup size limit ";
            this.BackupLimitEnabled.UseVisualStyleBackColor = true;
            this.BackupLimitEnabled.CheckedChanged += new System.EventHandler(this.BackupLimitEnabled_CheckedChanged);
            // 
            // BackupLimitSuffix
            // 
            this.BackupLimitSuffix.Enabled = false;
            this.BackupLimitSuffix.FormattingEnabled = true;
            this.BackupLimitSuffix.Items.AddRange(new object[] {
            "b",
            "kb",
            "mb",
            "gb"});
            this.BackupLimitSuffix.Location = new System.Drawing.Point(281, 95);
            this.BackupLimitSuffix.Name = "BackupLimitSuffix";
            this.BackupLimitSuffix.Size = new System.Drawing.Size(72, 21);
            this.BackupLimitSuffix.TabIndex = 11;
            // 
            // BackupLimitNumber
            // 
            this.BackupLimitNumber.Enabled = false;
            this.BackupLimitNumber.Location = new System.Drawing.Point(185, 95);
            this.BackupLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.BackupLimitNumber.Name = "BackupLimitNumber";
            this.BackupLimitNumber.Size = new System.Drawing.Size(88, 20);
            this.BackupLimitNumber.TabIndex = 10;
            this.BackupLimitNumber.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // VolumeSizeLimitSuffix
            // 
            this.VolumeSizeLimitSuffix.FormattingEnabled = true;
            this.VolumeSizeLimitSuffix.Items.AddRange(new object[] {
            "b",
            "kb",
            "mb",
            "gb"});
            this.VolumeSizeLimitSuffix.Location = new System.Drawing.Point(280, 128);
            this.VolumeSizeLimitSuffix.Name = "VolumeSizeLimitSuffix";
            this.VolumeSizeLimitSuffix.Size = new System.Drawing.Size(72, 21);
            this.VolumeSizeLimitSuffix.TabIndex = 14;
            // 
            // VolumeSizeLimitNumber
            // 
            this.VolumeSizeLimitNumber.Location = new System.Drawing.Point(184, 128);
            this.VolumeSizeLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.VolumeSizeLimitNumber.Name = "VolumeSizeLimitNumber";
            this.VolumeSizeLimitNumber.Size = new System.Drawing.Size(88, 20);
            this.VolumeSizeLimitNumber.TabIndex = 13;
            this.VolumeSizeLimitNumber.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.VolumeSizeLimitNumber.ValueChanged += new System.EventHandler(this.VolumeSizeLimitNumber_ValueChanged);
            // 
            // AsyncEnabled
            // 
            this.AsyncEnabled.AutoSize = true;
            this.AsyncEnabled.Location = new System.Drawing.Point(32, 168);
            this.AsyncEnabled.Name = "AsyncEnabled";
            this.AsyncEnabled.Size = new System.Drawing.Size(124, 17);
            this.AsyncEnabled.TabIndex = 15;
            this.AsyncEnabled.Text = "Upload asyncronosly";
            this.AsyncEnabled.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(48, 128);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(103, 13);
            this.label1.TabIndex = 16;
            this.label1.Text = "Size of each volume";
            // 
            // ThreadPriorityEnabled
            // 
            this.ThreadPriorityEnabled.AutoSize = true;
            this.ThreadPriorityEnabled.Location = new System.Drawing.Point(32, 200);
            this.ThreadPriorityEnabled.Name = "ThreadPriorityEnabled";
            this.ThreadPriorityEnabled.Size = new System.Drawing.Size(108, 17);
            this.ThreadPriorityEnabled.TabIndex = 17;
            this.ThreadPriorityEnabled.Text = "Set thread priority";
            this.ThreadPriorityEnabled.UseVisualStyleBackColor = true;
            this.ThreadPriorityEnabled.CheckedChanged += new System.EventHandler(this.ThreadPriorityEnabled_CheckedChanged);
            // 
            // ThreadPriority
            // 
            this.ThreadPriority.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ThreadPriority.Enabled = false;
            this.ThreadPriority.FormattingEnabled = true;
            this.ThreadPriority.Items.AddRange(new object[] {
            "High (Not recommended)",
            "Above normal",
            "Normal",
            "Below normal",
            "Low"});
            this.ThreadPriority.Location = new System.Drawing.Point(184, 200);
            this.ThreadPriority.Name = "ThreadPriority";
            this.ThreadPriority.Size = new System.Drawing.Size(168, 21);
            this.ThreadPriority.TabIndex = 18;
            // 
            // ThrottleOptions
            // 
            this.Controls.Add(this.ThreadPriority);
            this.Controls.Add(this.ThreadPriorityEnabled);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.AsyncEnabled);
            this.Controls.Add(this.VolumeSizeLimitSuffix);
            this.Controls.Add(this.VolumeSizeLimitNumber);
            this.Controls.Add(this.BackupLimitSuffix);
            this.Controls.Add(this.BackupLimitNumber);
            this.Controls.Add(this.BackupLimitEnabled);
            this.Controls.Add(this.DownloadLimitEnabled);
            this.Controls.Add(this.UploadLimitEnabled);
            this.Controls.Add(this.DownloadLimitSuffix);
            this.Controls.Add(this.UploadLimitSuffix);
            this.Controls.Add(this.DownloadLimitNumber);
            this.Controls.Add(this.UploadLimitNumber);
            this.Name = "ThrottleOptions";
            this.Size = new System.Drawing.Size(506, 242);
            ((System.ComponentModel.ISupportInitialize)(this.UploadLimitNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.DownloadLimitNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.BackupLimitNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.VolumeSizeLimitNumber)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.NumericUpDown UploadLimitNumber;
        private System.Windows.Forms.NumericUpDown DownloadLimitNumber;
        private System.Windows.Forms.ComboBox UploadLimitSuffix;
        private System.Windows.Forms.ComboBox DownloadLimitSuffix;
        private System.Windows.Forms.CheckBox UploadLimitEnabled;
        private System.Windows.Forms.CheckBox DownloadLimitEnabled;
        private System.Windows.Forms.CheckBox BackupLimitEnabled;
        private System.Windows.Forms.ComboBox BackupLimitSuffix;
        private System.Windows.Forms.NumericUpDown BackupLimitNumber;
        private System.Windows.Forms.ComboBox VolumeSizeLimitSuffix;
        private System.Windows.Forms.NumericUpDown VolumeSizeLimitNumber;
        private System.Windows.Forms.CheckBox AsyncEnabled;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox ThreadPriorityEnabled;
        private System.Windows.Forms.ComboBox ThreadPriority;
    }
}
