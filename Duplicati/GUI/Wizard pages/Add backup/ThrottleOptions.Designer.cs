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
            this.BackupLimitEnabled = new System.Windows.Forms.CheckBox();
            this.AsyncEnabled = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.VolumeSize = new Duplicati.GUI.HelperControls.SizeSelector();
            this.BackupLimit = new Duplicati.GUI.HelperControls.SizeSelector();
            this.Bandwidth = new Duplicati.GUI.HelperControls.BandwidthLimit();
            this.ThreadPriorityPicker = new Duplicati.GUI.HelperControls.ThreadPriorityPicker();
            this.SuspendLayout();
            // 
            // BackupLimitEnabled
            // 
            resources.ApplyResources(this.BackupLimitEnabled, "BackupLimitEnabled");
            this.BackupLimitEnabled.Name = "BackupLimitEnabled";
            this.BackupLimitEnabled.UseVisualStyleBackColor = true;
            this.BackupLimitEnabled.CheckedChanged += new System.EventHandler(this.BackupLimitEnabled_CheckedChanged);
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
            // VolumeSize
            // 
            this.VolumeSize.CurrentSize = "0b";
            this.VolumeSize.CurrentSizeInBytes = ((long)(0));
            resources.ApplyResources(this.VolumeSize, "VolumeSize");
            this.VolumeSize.Name = "VolumeSize";
            // 
            // BackupLimit
            // 
            this.BackupLimit.CurrentSize = "0b";
            this.BackupLimit.CurrentSizeInBytes = ((long)(0));
            resources.ApplyResources(this.BackupLimit, "BackupLimit");
            this.BackupLimit.Name = "BackupLimit";
            // 
            // Bandwidth
            // 
            this.Bandwidth.DownloadLimit = null;
            this.Bandwidth.DownloadLimitInBytes = ((long)(0));
            resources.ApplyResources(this.Bandwidth, "Bandwidth");
            this.Bandwidth.Name = "Bandwidth";
            this.Bandwidth.UploadLimit = null;
            this.Bandwidth.UploadLimitInBytes = ((long)(0));
            // 
            // ThreadPriorityPicker
            // 
            resources.ApplyResources(this.ThreadPriorityPicker, "ThreadPriorityPicker");
            this.ThreadPriorityPicker.Name = "ThreadPriorityPicker";
            // 
            // ThrottleOptions
            // 
            this.Controls.Add(this.ThreadPriorityPicker);
            this.Controls.Add(this.Bandwidth);
            this.Controls.Add(this.VolumeSize);
            this.Controls.Add(this.BackupLimit);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.AsyncEnabled);
            this.Controls.Add(this.BackupLimitEnabled);
            this.Name = "ThrottleOptions";
            resources.ApplyResources(this, "$this");
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox BackupLimitEnabled;
        private System.Windows.Forms.CheckBox AsyncEnabled;
        private System.Windows.Forms.Label label1;
        private Duplicati.GUI.HelperControls.SizeSelector BackupLimit;
        private Duplicati.GUI.HelperControls.SizeSelector VolumeSize;
        private Duplicati.GUI.HelperControls.BandwidthLimit Bandwidth;
        private Duplicati.GUI.HelperControls.ThreadPriorityPicker ThreadPriorityPicker;
    }
}
