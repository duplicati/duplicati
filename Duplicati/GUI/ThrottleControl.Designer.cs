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
            this.OKBtn.Location = new System.Drawing.Point(96, 128);
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.Size = new System.Drawing.Size(75, 23);
            this.OKBtn.TabIndex = 4;
            this.OKBtn.Text = "OK";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBtn.Location = new System.Drawing.Point(184, 128);
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.Size = new System.Drawing.Size(75, 23);
            this.CancelBtn.TabIndex = 5;
            this.CancelBtn.Text = "Cancel";
            this.CancelBtn.UseVisualStyleBackColor = true;
            this.CancelBtn.Click += new System.EventHandler(this.CancelBtn_Click);
            // 
            // ThreadPriorityPicker
            // 
            this.ThreadPriorityPicker.Location = new System.Drawing.Point(16, 16);
            this.ThreadPriorityPicker.Name = "ThreadPriorityPicker";
            this.ThreadPriorityPicker.Size = new System.Drawing.Size(322, 23);
            this.ThreadPriorityPicker.TabIndex = 6;
            // 
            // BandwidthLimit
            // 
            this.BandwidthLimit.DownloadLimit = null;
            this.BandwidthLimit.DownloadLimitInBytes = ((long)(0));
            this.BandwidthLimit.Location = new System.Drawing.Point(16, 48);
            this.BandwidthLimit.Name = "BandwidthLimit";
            this.BandwidthLimit.Size = new System.Drawing.Size(321, 54);
            this.BandwidthLimit.TabIndex = 7;
            this.BandwidthLimit.UploadLimit = null;
            this.BandwidthLimit.UploadLimitInBytes = ((long)(0));
            // 
            // ThrottleControl
            // 
            this.AcceptButton = this.OKBtn;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.ClientSize = new System.Drawing.Size(352, 166);
            this.Controls.Add(this.BandwidthLimit);
            this.Controls.Add(this.ThreadPriorityPicker);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ThrottleControl";
            this.Text = "Throttle control";
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