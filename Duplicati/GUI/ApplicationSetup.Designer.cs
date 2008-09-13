namespace Duplicati
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
            this.RecentDuration = new System.Windows.Forms.TextBox();
            this.PGPPath = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.BrowseNcFTP = new System.Windows.Forms.Button();
            this.NcFTPPath = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.BrowseDuplicity = new System.Windows.Forms.Button();
            this.BrowsePython = new System.Windows.Forms.Button();
            this.BrowsePGP = new System.Windows.Forms.Button();
            this.DuplicityPath = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.PythonPath = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.PGPBrowser = new System.Windows.Forms.FolderBrowserDialog();
            this.PythonFileDialog = new System.Windows.Forms.OpenFileDialog();
            this.DuplicityFileDialog = new System.Windows.Forms.OpenFileDialog();
            this.NcFTPBrowser = new System.Windows.Forms.FolderBrowserDialog();
            this.BrowsePutty = new System.Windows.Forms.Button();
            this.PuttyPath = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.PuttyBrowser = new System.Windows.Forms.FolderBrowserDialog();
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
            // RecentDuration
            // 
            this.RecentDuration.Location = new System.Drawing.Point(232, 24);
            this.RecentDuration.Name = "RecentDuration";
            this.RecentDuration.Size = new System.Drawing.Size(168, 20);
            this.RecentDuration.TabIndex = 1;
            this.RecentDuration.TextChanged += new System.EventHandler(this.RecentDuration_TextChanged);
            // 
            // PGPPath
            // 
            this.PGPPath.Location = new System.Drawing.Point(128, 24);
            this.PGPPath.Name = "PGPPath";
            this.PGPPath.Size = new System.Drawing.Size(248, 20);
            this.PGPPath.TabIndex = 3;
            this.PGPPath.TextChanged += new System.EventHandler(this.PGPPath_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(58, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "PGP folder";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.RecentDuration);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(8, 8);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(408, 56);
            this.groupBox1.TabIndex = 4;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "User interface settings";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.BrowsePutty);
            this.groupBox2.Controls.Add(this.PuttyPath);
            this.groupBox2.Controls.Add(this.label6);
            this.groupBox2.Controls.Add(this.BrowseNcFTP);
            this.groupBox2.Controls.Add(this.NcFTPPath);
            this.groupBox2.Controls.Add(this.label5);
            this.groupBox2.Controls.Add(this.BrowseDuplicity);
            this.groupBox2.Controls.Add(this.BrowsePython);
            this.groupBox2.Controls.Add(this.BrowsePGP);
            this.groupBox2.Controls.Add(this.DuplicityPath);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.PythonPath);
            this.groupBox2.Controls.Add(this.label3);
            this.groupBox2.Controls.Add(this.PGPPath);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Location = new System.Drawing.Point(8, 72);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(408, 160);
            this.groupBox2.TabIndex = 5;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Path settings (VERY ADVANCED)";
            // 
            // BrowseNcFTP
            // 
            this.BrowseNcFTP.Location = new System.Drawing.Point(376, 96);
            this.BrowseNcFTP.Name = "BrowseNcFTP";
            this.BrowseNcFTP.Size = new System.Drawing.Size(24, 20);
            this.BrowseNcFTP.TabIndex = 13;
            this.BrowseNcFTP.Text = "...";
            this.BrowseNcFTP.UseVisualStyleBackColor = true;
            this.BrowseNcFTP.Click += new System.EventHandler(this.BrowseNcFTP_Click);
            // 
            // NcFTPPath
            // 
            this.NcFTPPath.Location = new System.Drawing.Point(128, 96);
            this.NcFTPPath.Name = "NcFTPPath";
            this.NcFTPPath.Size = new System.Drawing.Size(248, 20);
            this.NcFTPPath.TabIndex = 12;
            this.NcFTPPath.TextChanged += new System.EventHandler(this.NcFTPPath_TextChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(8, 96);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(70, 13);
            this.label5.TabIndex = 11;
            this.label5.Text = "NcFTP folder";
            // 
            // BrowseDuplicity
            // 
            this.BrowseDuplicity.Location = new System.Drawing.Point(376, 72);
            this.BrowseDuplicity.Name = "BrowseDuplicity";
            this.BrowseDuplicity.Size = new System.Drawing.Size(24, 20);
            this.BrowseDuplicity.TabIndex = 10;
            this.BrowseDuplicity.Text = "...";
            this.BrowseDuplicity.UseVisualStyleBackColor = true;
            this.BrowseDuplicity.Click += new System.EventHandler(this.BrowseDuplicity_Click);
            // 
            // BrowsePython
            // 
            this.BrowsePython.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.BrowsePython.Location = new System.Drawing.Point(376, 48);
            this.BrowsePython.Name = "BrowsePython";
            this.BrowsePython.Size = new System.Drawing.Size(24, 20);
            this.BrowsePython.TabIndex = 9;
            this.BrowsePython.Text = "...";
            this.BrowsePython.UseVisualStyleBackColor = true;
            this.BrowsePython.Click += new System.EventHandler(this.BrowsePython_Click);
            // 
            // BrowsePGP
            // 
            this.BrowsePGP.Location = new System.Drawing.Point(376, 24);
            this.BrowsePGP.Name = "BrowsePGP";
            this.BrowsePGP.Size = new System.Drawing.Size(24, 20);
            this.BrowsePGP.TabIndex = 8;
            this.BrowsePGP.Text = "...";
            this.BrowsePGP.UseVisualStyleBackColor = true;
            this.BrowsePGP.Click += new System.EventHandler(this.BrowsePGP_Click);
            // 
            // DuplicityPath
            // 
            this.DuplicityPath.Location = new System.Drawing.Point(128, 72);
            this.DuplicityPath.Name = "DuplicityPath";
            this.DuplicityPath.Size = new System.Drawing.Size(248, 20);
            this.DuplicityPath.TabIndex = 7;
            this.DuplicityPath.TextChanged += new System.EventHandler(this.DuplicityPath_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(8, 72);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(91, 13);
            this.label4.TabIndex = 6;
            this.label4.Text = "Duplicity script file";
            // 
            // PythonPath
            // 
            this.PythonPath.Location = new System.Drawing.Point(128, 48);
            this.PythonPath.Name = "PythonPath";
            this.PythonPath.Size = new System.Drawing.Size(248, 20);
            this.PythonPath.TabIndex = 5;
            this.PythonPath.TextChanged += new System.EventHandler(this.PythonPath_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 48);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(101, 13);
            this.label3.TabIndex = 4;
            this.label3.Text = "Python executeable";
            // 
            // OKBtn
            // 
            this.OKBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.OKBtn.Location = new System.Drawing.Point(120, 251);
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
            this.CancelBtn.Location = new System.Drawing.Point(216, 251);
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.Size = new System.Drawing.Size(72, 24);
            this.CancelBtn.TabIndex = 7;
            this.CancelBtn.Text = "Cancel";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // PGPBrowser
            // 
            this.PGPBrowser.Description = "Select the folder where the PGP files are located";
            this.PGPBrowser.ShowNewFolderButton = false;
            // 
            // PythonFileDialog
            // 
            this.PythonFileDialog.DefaultExt = "exe";
            this.PythonFileDialog.FileName = "Python.exe";
            this.PythonFileDialog.Filter = "Executeables (*.exe)|*.exe|All files (*.*)|*.*";
            this.PythonFileDialog.SupportMultiDottedExtensions = true;
            this.PythonFileDialog.Title = "Select the Python executeable to use";
            // 
            // DuplicityFileDialog
            // 
            this.DuplicityFileDialog.DefaultExt = "py";
            this.DuplicityFileDialog.FileName = "Duplicity.py";
            this.DuplicityFileDialog.Filter = "Python files (*.py)|*.py|All files (*.*)|*.*";
            this.DuplicityFileDialog.SupportMultiDottedExtensions = true;
            this.DuplicityFileDialog.Title = "Select the main Duplicity script file";
            // 
            // NcFTPBrowser
            // 
            this.NcFTPBrowser.Description = "Select the folder where the NcFTP files are located";
            this.NcFTPBrowser.ShowNewFolderButton = false;
            // 
            // BrowsePutty
            // 
            this.BrowsePutty.Location = new System.Drawing.Point(376, 120);
            this.BrowsePutty.Name = "BrowsePutty";
            this.BrowsePutty.Size = new System.Drawing.Size(24, 20);
            this.BrowsePutty.TabIndex = 16;
            this.BrowsePutty.Text = "...";
            this.BrowsePutty.UseVisualStyleBackColor = true;
            this.BrowsePutty.Click += new System.EventHandler(this.BrowsePutty_Click);
            // 
            // PuttyPath
            // 
            this.PuttyPath.Location = new System.Drawing.Point(128, 120);
            this.PuttyPath.Name = "PuttyPath";
            this.PuttyPath.Size = new System.Drawing.Size(248, 20);
            this.PuttyPath.TabIndex = 15;
            this.PuttyPath.TextChanged += new System.EventHandler(this.PuttyPath_TextChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(8, 120);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(60, 13);
            this.label6.TabIndex = 14;
            this.label6.Text = "Putty folder";
            // 
            // PuttyBrowser
            // 
            this.PuttyBrowser.Description = "Select the folder where the putty files are located";
            this.PuttyBrowser.ShowNewFolderButton = false;
            // 
            // ApplicationSetup
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.ClientSize = new System.Drawing.Size(423, 286);
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
        private System.Windows.Forms.TextBox RecentDuration;
        private System.Windows.Forms.TextBox PGPPath;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button BrowseDuplicity;
        private System.Windows.Forms.Button BrowsePython;
        private System.Windows.Forms.Button BrowsePGP;
        private System.Windows.Forms.TextBox DuplicityPath;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox PythonPath;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private System.Windows.Forms.FolderBrowserDialog PGPBrowser;
        private System.Windows.Forms.OpenFileDialog PythonFileDialog;
        private System.Windows.Forms.OpenFileDialog DuplicityFileDialog;
        private System.Windows.Forms.Button BrowseNcFTP;
        private System.Windows.Forms.TextBox NcFTPPath;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.FolderBrowserDialog NcFTPBrowser;
        private System.Windows.Forms.Button BrowsePutty;
        private System.Windows.Forms.TextBox PuttyPath;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.FolderBrowserDialog PuttyBrowser;
    }
}