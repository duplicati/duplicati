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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ThrottleOptions));
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
            resources.ApplyResources(this.UploadLimitNumber, "UploadLimitNumber");
            this.UploadLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.UploadLimitNumber.Name = "UploadLimitNumber";
            // 
            // DownloadLimitNumber
            // 
            resources.ApplyResources(this.DownloadLimitNumber, "DownloadLimitNumber");
            this.DownloadLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.DownloadLimitNumber.Name = "DownloadLimitNumber";
            // 
            // UploadLimitSuffix
            // 
            this.UploadLimitSuffix.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            resources.ApplyResources(this.UploadLimitSuffix, "UploadLimitSuffix");
            this.UploadLimitSuffix.FormattingEnabled = true;
            this.UploadLimitSuffix.Items.AddRange(new object[] {
            resources.GetString("UploadLimitSuffix.Items"),
            resources.GetString("UploadLimitSuffix.Items1"),
            resources.GetString("UploadLimitSuffix.Items2"),
            resources.GetString("UploadLimitSuffix.Items3")});
            this.UploadLimitSuffix.Name = "UploadLimitSuffix";
            // 
            // DownloadLimitSuffix
            // 
            this.DownloadLimitSuffix.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            resources.ApplyResources(this.DownloadLimitSuffix, "DownloadLimitSuffix");
            this.DownloadLimitSuffix.FormattingEnabled = true;
            this.DownloadLimitSuffix.Items.AddRange(new object[] {
            resources.GetString("DownloadLimitSuffix.Items"),
            resources.GetString("DownloadLimitSuffix.Items1"),
            resources.GetString("DownloadLimitSuffix.Items2"),
            resources.GetString("DownloadLimitSuffix.Items3")});
            this.DownloadLimitSuffix.Name = "DownloadLimitSuffix";
            // 
            // UploadLimitEnabled
            // 
            resources.ApplyResources(this.UploadLimitEnabled, "UploadLimitEnabled");
            this.UploadLimitEnabled.Name = "UploadLimitEnabled";
            this.UploadLimitEnabled.UseVisualStyleBackColor = true;
            this.UploadLimitEnabled.CheckedChanged += new System.EventHandler(this.UploadLimitEnabled_CheckedChanged);
            // 
            // DownloadLimitEnabled
            // 
            resources.ApplyResources(this.DownloadLimitEnabled, "DownloadLimitEnabled");
            this.DownloadLimitEnabled.Name = "DownloadLimitEnabled";
            this.DownloadLimitEnabled.UseVisualStyleBackColor = true;
            this.DownloadLimitEnabled.CheckedChanged += new System.EventHandler(this.DownloadLimitEnabled_CheckedChanged);
            // 
            // BackupLimitEnabled
            // 
            resources.ApplyResources(this.BackupLimitEnabled, "BackupLimitEnabled");
            this.BackupLimitEnabled.Name = "BackupLimitEnabled";
            this.BackupLimitEnabled.UseVisualStyleBackColor = true;
            this.BackupLimitEnabled.CheckedChanged += new System.EventHandler(this.BackupLimitEnabled_CheckedChanged);
            // 
            // BackupLimitSuffix
            // 
            this.BackupLimitSuffix.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            resources.ApplyResources(this.BackupLimitSuffix, "BackupLimitSuffix");
            this.BackupLimitSuffix.FormattingEnabled = true;
            this.BackupLimitSuffix.Items.AddRange(new object[] {
            resources.GetString("BackupLimitSuffix.Items"),
            resources.GetString("BackupLimitSuffix.Items1"),
            resources.GetString("BackupLimitSuffix.Items2"),
            resources.GetString("BackupLimitSuffix.Items3")});
            this.BackupLimitSuffix.Name = "BackupLimitSuffix";
            // 
            // BackupLimitNumber
            // 
            resources.ApplyResources(this.BackupLimitNumber, "BackupLimitNumber");
            this.BackupLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.BackupLimitNumber.Name = "BackupLimitNumber";
            // 
            // VolumeSizeLimitSuffix
            // 
            this.VolumeSizeLimitSuffix.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.VolumeSizeLimitSuffix.FormattingEnabled = true;
            this.VolumeSizeLimitSuffix.Items.AddRange(new object[] {
            resources.GetString("VolumeSizeLimitSuffix.Items"),
            resources.GetString("VolumeSizeLimitSuffix.Items1"),
            resources.GetString("VolumeSizeLimitSuffix.Items2"),
            resources.GetString("VolumeSizeLimitSuffix.Items3")});
            resources.ApplyResources(this.VolumeSizeLimitSuffix, "VolumeSizeLimitSuffix");
            this.VolumeSizeLimitSuffix.Name = "VolumeSizeLimitSuffix";
            // 
            // VolumeSizeLimitNumber
            // 
            resources.ApplyResources(this.VolumeSizeLimitNumber, "VolumeSizeLimitNumber");
            this.VolumeSizeLimitNumber.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.VolumeSizeLimitNumber.Name = "VolumeSizeLimitNumber";
            // 
            // AsyncEnabled
            // 
            resources.ApplyResources(this.AsyncEnabled, "AsyncEnabled");
            this.AsyncEnabled.Name = "AsyncEnabled";
            this.AsyncEnabled.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // ThreadPriorityEnabled
            // 
            resources.ApplyResources(this.ThreadPriorityEnabled, "ThreadPriorityEnabled");
            this.ThreadPriorityEnabled.Name = "ThreadPriorityEnabled";
            this.ThreadPriorityEnabled.UseVisualStyleBackColor = true;
            this.ThreadPriorityEnabled.CheckedChanged += new System.EventHandler(this.ThreadPriorityEnabled_CheckedChanged);
            // 
            // ThreadPriority
            // 
            this.ThreadPriority.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            resources.ApplyResources(this.ThreadPriority, "ThreadPriority");
            this.ThreadPriority.FormattingEnabled = true;
            this.ThreadPriority.Items.AddRange(new object[] {
            resources.GetString("ThreadPriority.Items"),
            resources.GetString("ThreadPriority.Items1"),
            resources.GetString("ThreadPriority.Items2"),
            resources.GetString("ThreadPriority.Items3"),
            resources.GetString("ThreadPriority.Items4")});
            this.ThreadPriority.Name = "ThreadPriority";
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
            resources.ApplyResources(this, "$this");
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
