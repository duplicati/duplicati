namespace FreshKeeper
{
    partial class ClientFileEditor
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
            this.components = new System.ComponentModel.Container();
            this.UpdateApplication = new System.Windows.Forms.ComboBox();
            this.label7 = new System.Windows.Forms.Label();
            this.UpdateArchitecture = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.UpdateDownloadUrls = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.UpdateInterval = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.UpdateBugfixNotification = new System.Windows.Forms.CheckBox();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.UpdateVersion = new System.Windows.Forms.ComboBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.CloseButton = new System.Windows.Forms.Button();
            this.SaveButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // UpdateApplication
            // 
            this.UpdateApplication.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateApplication.FormattingEnabled = true;
            this.UpdateApplication.Location = new System.Drawing.Point(96, 64);
            this.UpdateApplication.Name = "UpdateApplication";
            this.UpdateApplication.Size = new System.Drawing.Size(196, 21);
            this.UpdateApplication.TabIndex = 23;
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(8, 64);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(59, 13);
            this.label7.TabIndex = 22;
            this.label7.Text = "Application";
            // 
            // UpdateArchitecture
            // 
            this.UpdateArchitecture.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateArchitecture.FormattingEnabled = true;
            this.UpdateArchitecture.Items.AddRange(new object[] {
            "Windows",
            "Windows x86",
            "Windows ALPHA",
            "Windows x86 32bit",
            "Windows x86 64bit",
            "Linux",
            "Linux x86",
            "Linux x86 32bit",
            "Linux x86 64bit",
            "Linux ALPHA",
            "Linux MIPS"});
            this.UpdateArchitecture.Location = new System.Drawing.Point(96, 40);
            this.UpdateArchitecture.Name = "UpdateArchitecture";
            this.UpdateArchitecture.Size = new System.Drawing.Size(196, 21);
            this.UpdateArchitecture.TabIndex = 21;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 40);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(64, 13);
            this.label3.TabIndex = 19;
            this.label3.Text = "Architecture";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(42, 13);
            this.label1.TabIndex = 18;
            this.label1.Text = "Version";
            // 
            // UpdateDownloadUrls
            // 
            this.UpdateDownloadUrls.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateDownloadUrls.Location = new System.Drawing.Point(8, 160);
            this.UpdateDownloadUrls.Multiline = true;
            this.UpdateDownloadUrls.Name = "UpdateDownloadUrls";
            this.UpdateDownloadUrls.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.UpdateDownloadUrls.Size = new System.Drawing.Size(284, 72);
            this.UpdateDownloadUrls.TabIndex = 25;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(8, 144);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(90, 13);
            this.label5.TabIndex = 24;
            this.label5.Text = "Update file URL\'s";
            // 
            // UpdateInterval
            // 
            this.UpdateInterval.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateInterval.FormattingEnabled = true;
            this.UpdateInterval.Items.AddRange(new object[] {
            "Always (0)",
            "Each day (1D)",
            "Each week (1W)",
            "Each month (1M)"});
            this.UpdateInterval.Location = new System.Drawing.Point(96, 88);
            this.UpdateInterval.Name = "UpdateInterval";
            this.UpdateInterval.Size = new System.Drawing.Size(196, 21);
            this.UpdateInterval.TabIndex = 27;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 88);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(79, 13);
            this.label2.TabIndex = 26;
            this.label2.Text = "Update interval";
            // 
            // UpdateBugfixNotification
            // 
            this.UpdateBugfixNotification.AutoSize = true;
            this.UpdateBugfixNotification.Location = new System.Drawing.Point(96, 120);
            this.UpdateBugfixNotification.Name = "UpdateBugfixNotification";
            this.UpdateBugfixNotification.Size = new System.Drawing.Size(107, 17);
            this.UpdateBugfixNotification.TabIndex = 28;
            this.UpdateBugfixNotification.Text = "Notify of bugfixes";
            this.UpdateBugfixNotification.UseVisualStyleBackColor = true;
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // UpdateVersion
            // 
            this.UpdateVersion.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateVersion.FormattingEnabled = true;
            this.UpdateVersion.Location = new System.Drawing.Point(96, 16);
            this.UpdateVersion.Name = "UpdateVersion";
            this.UpdateVersion.Size = new System.Drawing.Size(196, 21);
            this.UpdateVersion.TabIndex = 29;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.CloseButton);
            this.panel1.Controls.Add(this.SaveButton);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 248);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(304, 37);
            this.panel1.TabIndex = 30;
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.CloseButton.Location = new System.Drawing.Point(160, 8);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 23);
            this.CloseButton.TabIndex = 1;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            // 
            // SaveButton
            // 
            this.SaveButton.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.SaveButton.Location = new System.Drawing.Point(72, 8);
            this.SaveButton.Name = "SaveButton";
            this.SaveButton.Size = new System.Drawing.Size(75, 23);
            this.SaveButton.TabIndex = 0;
            this.SaveButton.Text = "Save";
            this.SaveButton.UseVisualStyleBackColor = true;
            // 
            // ClientFileEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(304, 285);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.UpdateVersion);
            this.Controls.Add(this.UpdateBugfixNotification);
            this.Controls.Add(this.UpdateInterval);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.UpdateDownloadUrls);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.UpdateApplication);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.UpdateArchitecture);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label1);
            this.Name = "ClientFileEditor";
            this.Text = "Edit client config file";
            this.Load += new System.EventHandler(this.ClientFileEditor_Load);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox UpdateApplication;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.ComboBox UpdateArchitecture;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox UpdateDownloadUrls;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox UpdateInterval;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox UpdateBugfixNotification;
        private System.Windows.Forms.ErrorProvider errorProvider;
        private System.Windows.Forms.ComboBox UpdateVersion;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Button SaveButton;

    }
}