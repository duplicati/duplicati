namespace Duplicati.GUI.HelperControls
{
    partial class BandwidthLimit
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BandwidthLimit));
            this.DownloadLimitCheckbox = new System.Windows.Forms.CheckBox();
            this.UploadLimitCheckbox = new System.Windows.Forms.CheckBox();
            this.UploadLimitPicker = new Duplicati.GUI.HelperControls.SizeSelector();
            this.DownloadLimitPicker = new Duplicati.GUI.HelperControls.SizeSelector();
            this.SuspendLayout();
            // 
            // DownloadLimitCheckbox
            // 
            resources.ApplyResources(this.DownloadLimitCheckbox, "DownloadLimitCheckbox");
            this.DownloadLimitCheckbox.Name = "DownloadLimitCheckbox";
            this.DownloadLimitCheckbox.UseVisualStyleBackColor = true;
            this.DownloadLimitCheckbox.CheckedChanged += new System.EventHandler(this.DownloadLimitCheckbox_CheckedChanged);
            // 
            // UploadLimitCheckbox
            // 
            resources.ApplyResources(this.UploadLimitCheckbox, "UploadLimitCheckbox");
            this.UploadLimitCheckbox.Name = "UploadLimitCheckbox";
            this.UploadLimitCheckbox.UseVisualStyleBackColor = true;
            this.UploadLimitCheckbox.CheckedChanged += new System.EventHandler(this.UploadLimitCheckbox_CheckedChanged);
            // 
            // UploadLimitPicker
            // 
            resources.ApplyResources(this.UploadLimitPicker, "UploadLimitPicker");
            this.UploadLimitPicker.CurrentSize = "0b";
            this.UploadLimitPicker.CurrentSizeInBytes = ((long)(0));
            this.UploadLimitPicker.Name = "UploadLimitPicker";
            this.UploadLimitPicker.UseSpeedNotation = true;
            this.UploadLimitPicker.CurrentSizeChanged += new System.EventHandler(this.UploadLimitPicker_CurrentSizeChanged);
            // 
            // DownloadLimitPicker
            // 
            resources.ApplyResources(this.DownloadLimitPicker, "DownloadLimitPicker");
            this.DownloadLimitPicker.CurrentSize = "0b";
            this.DownloadLimitPicker.CurrentSizeInBytes = ((long)(0));
            this.DownloadLimitPicker.Name = "DownloadLimitPicker";
            this.DownloadLimitPicker.UseSpeedNotation = true;
            this.DownloadLimitPicker.CurrentSizeChanged += new System.EventHandler(this.DownloadLimitPicker_CurrentSizeChanged);
            // 
            // BandwidthLimit
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.UploadLimitPicker);
            this.Controls.Add(this.DownloadLimitPicker);
            this.Controls.Add(this.DownloadLimitCheckbox);
            this.Controls.Add(this.UploadLimitCheckbox);
            this.Name = "BandwidthLimit";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private SizeSelector UploadLimitPicker;
        private SizeSelector DownloadLimitPicker;
        private System.Windows.Forms.CheckBox DownloadLimitCheckbox;
        private System.Windows.Forms.CheckBox UploadLimitCheckbox;
    }
}
