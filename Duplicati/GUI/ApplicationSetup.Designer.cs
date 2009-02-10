namespace Duplicati.GUI
{
    partial class ApplicationSetup
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ApplicationSetup));
            this.label1 = new System.Windows.Forms.Label();
            this.GPGPath = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.RecentDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.BrowseSCP = new System.Windows.Forms.Button();
            this.SCPPath = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.BrowseSFTP = new System.Windows.Forms.Button();
            this.SFTPPath = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.BrowsePGP = new System.Windows.Forms.Button();
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.BrowseGPGDialog = new System.Windows.Forms.OpenFileDialog();
            this.BrowseSFTPDialog = new System.Windows.Forms.OpenFileDialog();
            this.BrowseSCPDialog = new System.Windows.Forms.OpenFileDialog();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(188, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Time a log is shown in recent backups";
            // 
            // GPGPath
            // 
            this.GPGPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.GPGPath.Location = new System.Drawing.Point(128, 24);
            this.GPGPath.Name = "GPGPath";
            this.GPGPath.Size = new System.Drawing.Size(272, 20);
            this.GPGPath.TabIndex = 3;
            this.GPGPath.TextChanged += new System.EventHandler(this.GPGPath_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(54, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "GPG path";
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.Controls.Add(this.RecentDuration);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(8, 8);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(437, 56);
            this.groupBox1.TabIndex = 4;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "User interface settings";
            // 
            // RecentDuration
            // 
            this.RecentDuration.Location = new System.Drawing.Point(208, 24);
            this.RecentDuration.Name = "RecentDuration";
            this.RecentDuration.Size = new System.Drawing.Size(221, 21);
            this.RecentDuration.TabIndex = 1;
            this.RecentDuration.Value = "";
            this.RecentDuration.ValueChanged += new System.EventHandler(this.RecentDuration_ValueChanged);
            // 
            // groupBox2
            // 
            this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox2.Controls.Add(this.BrowseSCP);
            this.groupBox2.Controls.Add(this.SCPPath);
            this.groupBox2.Controls.Add(this.label3);
            this.groupBox2.Controls.Add(this.BrowseSFTP);
            this.groupBox2.Controls.Add(this.SFTPPath);
            this.groupBox2.Controls.Add(this.label6);
            this.groupBox2.Controls.Add(this.BrowsePGP);
            this.groupBox2.Controls.Add(this.GPGPath);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Location = new System.Drawing.Point(8, 72);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(437, 104);
            this.groupBox2.TabIndex = 5;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Path settings (Advanced)";
            // 
            // BrowseSCP
            // 
            this.BrowseSCP.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BrowseSCP.Location = new System.Drawing.Point(400, 72);
            this.BrowseSCP.Name = "BrowseSCP";
            this.BrowseSCP.Size = new System.Drawing.Size(24, 20);
            this.BrowseSCP.TabIndex = 19;
            this.BrowseSCP.Text = "...";
            this.BrowseSCP.UseVisualStyleBackColor = true;
            this.BrowseSCP.Click += new System.EventHandler(this.BrowseSCP_Click);
            // 
            // SCPPath
            // 
            this.SCPPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.SCPPath.Location = new System.Drawing.Point(128, 72);
            this.SCPPath.Name = "SCPPath";
            this.SCPPath.Size = new System.Drawing.Size(272, 20);
            this.SCPPath.TabIndex = 18;
            this.SCPPath.TextChanged += new System.EventHandler(this.SCPPath_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 72);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(48, 13);
            this.label3.TabIndex = 17;
            this.label3.Text = "scp path";
            // 
            // BrowseSFTP
            // 
            this.BrowseSFTP.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BrowseSFTP.Location = new System.Drawing.Point(400, 48);
            this.BrowseSFTP.Name = "BrowseSFTP";
            this.BrowseSFTP.Size = new System.Drawing.Size(24, 20);
            this.BrowseSFTP.TabIndex = 16;
            this.BrowseSFTP.Text = "...";
            this.BrowseSFTP.UseVisualStyleBackColor = true;
            this.BrowseSFTP.Click += new System.EventHandler(this.BrowseSFTP_Click);
            // 
            // SFTPPath
            // 
            this.SFTPPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.SFTPPath.Location = new System.Drawing.Point(128, 48);
            this.SFTPPath.Name = "SFTPPath";
            this.SFTPPath.Size = new System.Drawing.Size(272, 20);
            this.SFTPPath.TabIndex = 15;
            this.SFTPPath.TextChanged += new System.EventHandler(this.SFTPPath_TextChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(8, 48);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(48, 13);
            this.label6.TabIndex = 14;
            this.label6.Text = "sftp path";
            // 
            // BrowsePGP
            // 
            this.BrowsePGP.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BrowsePGP.Location = new System.Drawing.Point(400, 24);
            this.BrowsePGP.Name = "BrowsePGP";
            this.BrowsePGP.Size = new System.Drawing.Size(24, 20);
            this.BrowsePGP.TabIndex = 8;
            this.BrowsePGP.Text = "...";
            this.BrowsePGP.UseVisualStyleBackColor = true;
            this.BrowsePGP.Click += new System.EventHandler(this.BrowseGPG_Click);
            // 
            // OKBtn
            // 
            this.OKBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.OKBtn.Location = new System.Drawing.Point(135, 186);
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.Size = new System.Drawing.Size(80, 24);
            this.OKBtn.TabIndex = 6;
            this.OKBtn.Text = "OK";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            this.CancelBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBtn.Location = new System.Drawing.Point(231, 186);
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.Size = new System.Drawing.Size(72, 24);
            this.CancelBtn.TabIndex = 7;
            this.CancelBtn.Text = "Cancel";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // BrowseGPGDialog
            // 
            this.BrowseGPGDialog.AddExtension = false;
            this.BrowseGPGDialog.FileName = "gpg.exe";
            this.BrowseGPGDialog.Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*";
            this.BrowseGPGDialog.Title = "Select the GNU Privacy Guard executable";
            // 
            // BrowseSFTPDialog
            // 
            this.BrowseSFTPDialog.AddExtension = false;
            this.BrowseSFTPDialog.FileName = "psftp.exe";
            this.BrowseSFTPDialog.Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*";
            this.BrowseSFTPDialog.Title = "Select the SFTP executable";
            // 
            // BrowseSCPDialog
            // 
            this.BrowseSCPDialog.AddExtension = false;
            this.BrowseSCPDialog.FileName = "pscp.exe";
            this.BrowseSCPDialog.Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*";
            this.BrowseSCPDialog.Title = "Select the SCP executable";
            // 
            // ApplicationSetup
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.ClientSize = new System.Drawing.Size(453, 224);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ApplicationSetup";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Duplicati setup";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox GPGPath;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button BrowsePGP;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private System.Windows.Forms.Button BrowseSFTP;
        private System.Windows.Forms.TextBox SFTPPath;
        private System.Windows.Forms.Label label6;
        private Duplicati.GUI.HelperControls.DurationEditor RecentDuration;
        private System.Windows.Forms.Button BrowseSCP;
        private System.Windows.Forms.TextBox SCPPath;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.OpenFileDialog BrowseGPGDialog;
        private System.Windows.Forms.OpenFileDialog BrowseSFTPDialog;
        private System.Windows.Forms.OpenFileDialog BrowseSCPDialog;
    }
}