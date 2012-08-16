namespace Duplicati.GUI
{
    partial class ThrottleControl
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ThrottleControl));
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.ThreadPriorityPicker = new Duplicati.GUI.HelperControls.ThreadPriorityPicker();
            this.BandwidthLimit = new Duplicati.GUI.HelperControls.BandwidthLimit();
            this.SuspendLayout();
            // 
            // OKBtn
            // 
            resources.ApplyResources(this.OKBtn, "OKBtn");
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.CancelBtn, "CancelBtn");
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.UseVisualStyleBackColor = true;
            this.CancelBtn.Click += new System.EventHandler(this.CancelBtn_Click);
            // 
            // ThreadPriorityPicker
            // 
            resources.ApplyResources(this.ThreadPriorityPicker, "ThreadPriorityPicker");
            this.ThreadPriorityPicker.Name = "ThreadPriorityPicker";
            // 
            // BandwidthLimit
            // 
            this.BandwidthLimit.DownloadLimit = null;
            this.BandwidthLimit.DownloadLimitInBytes = ((long)(0));
            resources.ApplyResources(this.BandwidthLimit, "BandwidthLimit");
            this.BandwidthLimit.Name = "BandwidthLimit";
            this.BandwidthLimit.UploadLimit = null;
            this.BandwidthLimit.UploadLimitInBytes = ((long)(0));
            // 
            // ThrottleControl
            // 
            this.AcceptButton = this.OKBtn;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.Controls.Add(this.BandwidthLimit);
            this.Controls.Add(this.ThreadPriorityPicker);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ThrottleControl";
            this.Load += new System.EventHandler(this.ThrottleControl_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private Duplicati.GUI.HelperControls.ThreadPriorityPicker ThreadPriorityPicker;
        private Duplicati.GUI.HelperControls.BandwidthLimit BandwidthLimit;
    }
}