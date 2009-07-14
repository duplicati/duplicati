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
            this.CacheSizeLabel = new System.Windows.Forms.Label();
            this.ClearCacheButton = new System.Windows.Forms.Button();
            this.SignatureCacheEnabled = new System.Windows.Forms.CheckBox();
            this.SignatureCachePathBrowse = new System.Windows.Forms.Button();
            this.SignatureCachePath = new System.Windows.Forms.TextBox();
            this.TempPathBrowse = new System.Windows.Forms.Button();
            this.TempPath = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.BrowseSFTP = new System.Windows.Forms.Button();
            this.SFTPPath = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.BrowsePGP = new System.Windows.Forms.Button();
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.BrowseGPGDialog = new System.Windows.Forms.OpenFileDialog();
            this.BrowseSFTPDialog = new System.Windows.Forms.OpenFileDialog();
            this.BrowseTempPath = new System.Windows.Forms.FolderBrowserDialog();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.PasswordPanel = new System.Windows.Forms.Panel();
            this.EncryptionMethod = new System.Windows.Forms.GroupBox();
            this.UseGPGEncryption = new System.Windows.Forms.RadioButton();
            this.UseAESEncryption = new System.Windows.Forms.RadioButton();
            this.label5 = new System.Windows.Forms.Label();
            this.CommonPassword = new System.Windows.Forms.TextBox();
            this.UseCommonPassword = new System.Windows.Forms.CheckBox();
            this.BrowseSignatureCachePath = new System.Windows.Forms.FolderBrowserDialog();
            this.CacheSizeCalculator = new System.ComponentModel.BackgroundWorker();
            this.label3 = new System.Windows.Forms.Label();
            this.LanguageSelection = new System.Windows.Forms.ComboBox();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.PasswordPanel.SuspendLayout();
            this.EncryptionMethod.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // GPGPath
            // 
            resources.ApplyResources(this.GPGPath, "GPGPath");
            this.GPGPath.Name = "GPGPath";
            this.GPGPath.TextChanged += new System.EventHandler(this.GPGPath_TextChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // groupBox1
            // 
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Controls.Add(this.LanguageSelection);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.RecentDuration);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            // 
            // RecentDuration
            // 
            resources.ApplyResources(this.RecentDuration, "RecentDuration");
            this.RecentDuration.Name = "RecentDuration";
            this.RecentDuration.Value = "";
            this.RecentDuration.ValueChanged += new System.EventHandler(this.RecentDuration_ValueChanged);
            // 
            // groupBox2
            // 
            resources.ApplyResources(this.groupBox2, "groupBox2");
            this.groupBox2.Controls.Add(this.CacheSizeLabel);
            this.groupBox2.Controls.Add(this.ClearCacheButton);
            this.groupBox2.Controls.Add(this.SignatureCacheEnabled);
            this.groupBox2.Controls.Add(this.SignatureCachePathBrowse);
            this.groupBox2.Controls.Add(this.SignatureCachePath);
            this.groupBox2.Controls.Add(this.TempPathBrowse);
            this.groupBox2.Controls.Add(this.TempPath);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.BrowseSFTP);
            this.groupBox2.Controls.Add(this.SFTPPath);
            this.groupBox2.Controls.Add(this.label6);
            this.groupBox2.Controls.Add(this.BrowsePGP);
            this.groupBox2.Controls.Add(this.GPGPath);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.TabStop = false;
            // 
            // CacheSizeLabel
            // 
            resources.ApplyResources(this.CacheSizeLabel, "CacheSizeLabel");
            this.CacheSizeLabel.Name = "CacheSizeLabel";
            // 
            // ClearCacheButton
            // 
            resources.ApplyResources(this.ClearCacheButton, "ClearCacheButton");
            this.ClearCacheButton.Name = "ClearCacheButton";
            this.ClearCacheButton.UseVisualStyleBackColor = true;
            this.ClearCacheButton.Click += new System.EventHandler(this.ClearCacheButton_Click);
            // 
            // SignatureCacheEnabled
            // 
            resources.ApplyResources(this.SignatureCacheEnabled, "SignatureCacheEnabled");
            this.SignatureCacheEnabled.Name = "SignatureCacheEnabled";
            this.SignatureCacheEnabled.UseVisualStyleBackColor = true;
            this.SignatureCacheEnabled.CheckedChanged += new System.EventHandler(this.SignatureCacheEnabled_CheckedChanged);
            // 
            // SignatureCachePathBrowse
            // 
            resources.ApplyResources(this.SignatureCachePathBrowse, "SignatureCachePathBrowse");
            this.SignatureCachePathBrowse.Name = "SignatureCachePathBrowse";
            this.SignatureCachePathBrowse.UseVisualStyleBackColor = true;
            this.SignatureCachePathBrowse.Click += new System.EventHandler(this.SignatureCachePathBrowse_Click);
            // 
            // SignatureCachePath
            // 
            resources.ApplyResources(this.SignatureCachePath, "SignatureCachePath");
            this.SignatureCachePath.Name = "SignatureCachePath";
            this.SignatureCachePath.TextChanged += new System.EventHandler(this.SignatureCachePath_TextChanged);
            // 
            // TempPathBrowse
            // 
            resources.ApplyResources(this.TempPathBrowse, "TempPathBrowse");
            this.TempPathBrowse.Name = "TempPathBrowse";
            this.TempPathBrowse.UseVisualStyleBackColor = true;
            this.TempPathBrowse.Click += new System.EventHandler(this.TempPathBrowse_Click);
            // 
            // TempPath
            // 
            resources.ApplyResources(this.TempPath, "TempPath");
            this.TempPath.Name = "TempPath";
            this.TempPath.TextChanged += new System.EventHandler(this.TempPath_TextChanged);
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // BrowseSFTP
            // 
            resources.ApplyResources(this.BrowseSFTP, "BrowseSFTP");
            this.BrowseSFTP.Name = "BrowseSFTP";
            this.BrowseSFTP.UseVisualStyleBackColor = true;
            this.BrowseSFTP.Click += new System.EventHandler(this.BrowseSFTP_Click);
            // 
            // SFTPPath
            // 
            resources.ApplyResources(this.SFTPPath, "SFTPPath");
            this.SFTPPath.Name = "SFTPPath";
            this.SFTPPath.TextChanged += new System.EventHandler(this.SFTPPath_TextChanged);
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // BrowsePGP
            // 
            resources.ApplyResources(this.BrowsePGP, "BrowsePGP");
            this.BrowsePGP.Name = "BrowsePGP";
            this.BrowsePGP.UseVisualStyleBackColor = true;
            this.BrowsePGP.Click += new System.EventHandler(this.BrowseGPG_Click);
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
            resources.ApplyResources(this.CancelBtn, "CancelBtn");
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // BrowseGPGDialog
            // 
            this.BrowseGPGDialog.AddExtension = false;
            this.BrowseGPGDialog.FileName = "gpg.exe";
            resources.ApplyResources(this.BrowseGPGDialog, "BrowseGPGDialog");
            // 
            // BrowseSFTPDialog
            // 
            this.BrowseSFTPDialog.AddExtension = false;
            this.BrowseSFTPDialog.FileName = "psftp.exe";
            resources.ApplyResources(this.BrowseSFTPDialog, "BrowseSFTPDialog");
            // 
            // groupBox3
            // 
            resources.ApplyResources(this.groupBox3, "groupBox3");
            this.groupBox3.Controls.Add(this.PasswordPanel);
            this.groupBox3.Controls.Add(this.UseCommonPassword);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.TabStop = false;
            // 
            // PasswordPanel
            // 
            this.PasswordPanel.Controls.Add(this.EncryptionMethod);
            this.PasswordPanel.Controls.Add(this.label5);
            this.PasswordPanel.Controls.Add(this.CommonPassword);
            resources.ApplyResources(this.PasswordPanel, "PasswordPanel");
            this.PasswordPanel.Name = "PasswordPanel";
            // 
            // EncryptionMethod
            // 
            this.EncryptionMethod.Controls.Add(this.UseGPGEncryption);
            this.EncryptionMethod.Controls.Add(this.UseAESEncryption);
            resources.ApplyResources(this.EncryptionMethod, "EncryptionMethod");
            this.EncryptionMethod.Name = "EncryptionMethod";
            this.EncryptionMethod.TabStop = false;
            // 
            // UseGPGEncryption
            // 
            resources.ApplyResources(this.UseGPGEncryption, "UseGPGEncryption");
            this.UseGPGEncryption.Name = "UseGPGEncryption";
            this.UseGPGEncryption.UseVisualStyleBackColor = true;
            this.UseGPGEncryption.CheckedChanged += new System.EventHandler(this.UseGPGEncryption_CheckedChanged);
            // 
            // UseAESEncryption
            // 
            resources.ApplyResources(this.UseAESEncryption, "UseAESEncryption");
            this.UseAESEncryption.Checked = true;
            this.UseAESEncryption.Name = "UseAESEncryption";
            this.UseAESEncryption.TabStop = true;
            this.UseAESEncryption.UseVisualStyleBackColor = true;
            this.UseAESEncryption.CheckedChanged += new System.EventHandler(this.UseAESEncryption_CheckedChanged);
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // CommonPassword
            // 
            resources.ApplyResources(this.CommonPassword, "CommonPassword");
            this.CommonPassword.Name = "CommonPassword";
            this.CommonPassword.UseSystemPasswordChar = true;
            this.CommonPassword.TextChanged += new System.EventHandler(this.CommonPassword_TextChanged);
            // 
            // UseCommonPassword
            // 
            resources.ApplyResources(this.UseCommonPassword, "UseCommonPassword");
            this.UseCommonPassword.Name = "UseCommonPassword";
            this.UseCommonPassword.UseVisualStyleBackColor = true;
            this.UseCommonPassword.CheckedChanged += new System.EventHandler(this.UseCommonPassword_CheckedChanged);
            // 
            // CacheSizeCalculator
            // 
            this.CacheSizeCalculator.WorkerSupportsCancellation = true;
            this.CacheSizeCalculator.DoWork += new System.ComponentModel.DoWorkEventHandler(this.CacheSizeCalculator_DoWork);
            this.CacheSizeCalculator.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.CacheSizeCalculator_RunWorkerCompleted);
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // LanguageSelection
            // 
            resources.ApplyResources(this.LanguageSelection, "LanguageSelection");
            this.LanguageSelection.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.LanguageSelection.FormattingEnabled = true;
            this.LanguageSelection.Name = "LanguageSelection";
            this.LanguageSelection.SelectedIndexChanged += new System.EventHandler(this.LanguageSelection_SelectedIndexChanged);
            // 
            // ApplicationSetup
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ApplicationSetup";
            this.Load += new System.EventHandler(this.ApplicationSetup_Load);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ApplicationSetup_FormClosing);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.PasswordPanel.ResumeLayout(false);
            this.PasswordPanel.PerformLayout();
            this.EncryptionMethod.ResumeLayout(false);
            this.EncryptionMethod.PerformLayout();
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
        private System.Windows.Forms.OpenFileDialog BrowseGPGDialog;
        private System.Windows.Forms.OpenFileDialog BrowseSFTPDialog;
        private System.Windows.Forms.Button TempPathBrowse;
        private System.Windows.Forms.TextBox TempPath;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.FolderBrowserDialog BrowseTempPath;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.TextBox CommonPassword;
        private System.Windows.Forms.CheckBox UseCommonPassword;
        private System.Windows.Forms.Panel PasswordPanel;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.CheckBox SignatureCacheEnabled;
        private System.Windows.Forms.Button SignatureCachePathBrowse;
        private System.Windows.Forms.TextBox SignatureCachePath;
        private System.Windows.Forms.FolderBrowserDialog BrowseSignatureCachePath;
        private System.Windows.Forms.Label CacheSizeLabel;
        private System.Windows.Forms.Button ClearCacheButton;
        private System.ComponentModel.BackgroundWorker CacheSizeCalculator;
        private System.Windows.Forms.GroupBox EncryptionMethod;
        private System.Windows.Forms.RadioButton UseGPGEncryption;
        private System.Windows.Forms.RadioButton UseAESEncryption;
        private System.Windows.Forms.ComboBox LanguageSelection;
        private System.Windows.Forms.Label label3;
    }
}