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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ApplicationSetup));
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.BalloonNotificationLevel = new System.Windows.Forms.ComboBox();
            this.label8 = new System.Windows.Forms.Label();
            this.HideDonateButton = new System.Windows.Forms.CheckBox();
            this.label7 = new System.Windows.Forms.Label();
            this.LanguageSelection = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.CacheSizeLabel = new System.Windows.Forms.Label();
            this.ClearCacheButton = new System.Windows.Forms.Button();
            this.SignatureCacheEnabled = new System.Windows.Forms.CheckBox();
            this.SignatureCachePathBrowse = new System.Windows.Forms.Button();
            this.SignatureCachePath = new System.Windows.Forms.TextBox();
            this.TempPathBrowse = new System.Windows.Forms.Button();
            this.TempPath = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.BrowseTempPath = new System.Windows.Forms.FolderBrowserDialog();
            this.PasswordDefaultsGroup = new System.Windows.Forms.GroupBox();
            this.EncryptionMethod = new System.Windows.Forms.Label();
            this.EncryptionModule = new System.Windows.Forms.ComboBox();
            this.UseCommonPassword = new System.Windows.Forms.CheckBox();
            this.CommonPassword = new Duplicati.Winforms.Controls.PasswordControl();
            this.BrowseSignatureCachePath = new System.Windows.Forms.FolderBrowserDialog();
            this.CacheSizeCalculator = new System.ComponentModel.BackgroundWorker();
            this.TabContainer = new System.Windows.Forms.TabControl();
            this.BasicTab = new System.Windows.Forms.TabPage();
            this.AdvancedTab = new System.Windows.Forms.TabPage();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.LicenseTab = new System.Windows.Forms.TabPage();
            this.LicenseLink = new System.Windows.Forms.LinkLabel();
            this.label6 = new System.Windows.Forms.Label();
            this.LicenseText = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.LicenseSections = new System.Windows.Forms.ListBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.StartupDelayDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.RecentDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.Bandwidth = new Duplicati.GUI.HelperControls.BandwidthLimit();
            this.ThreadPriorityPicker = new Duplicati.GUI.HelperControls.ThreadPriorityPicker();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.PasswordDefaultsGroup.SuspendLayout();
            this.TabContainer.SuspendLayout();
            this.BasicTab.SuspendLayout();
            this.AdvancedTab.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.LicenseTab.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            this.toolTip.SetToolTip(this.label1, resources.GetString("label1.ToolTip"));
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.BalloonNotificationLevel);
            this.groupBox1.Controls.Add(this.label8);
            this.groupBox1.Controls.Add(this.HideDonateButton);
            this.groupBox1.Controls.Add(this.StartupDelayDuration);
            this.groupBox1.Controls.Add(this.label7);
            this.groupBox1.Controls.Add(this.LanguageSelection);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.RecentDuration);
            this.groupBox1.Controls.Add(this.label1);
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            this.groupBox1.Enter += new System.EventHandler(this.groupBox1_Enter);
            // 
            // BalloonNotificationLevel
            // 
            resources.ApplyResources(this.BalloonNotificationLevel, "BalloonNotificationLevel");
            this.BalloonNotificationLevel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.BalloonNotificationLevel.FormattingEnabled = true;
            this.BalloonNotificationLevel.Name = "BalloonNotificationLevel";
            this.toolTip.SetToolTip(this.BalloonNotificationLevel, resources.GetString("BalloonNotificationLevel.ToolTip"));
            this.BalloonNotificationLevel.SelectedIndexChanged += new System.EventHandler(this.BalloonNotificationLevel_SelectedIndexChanged);
            // 
            // label8
            // 
            resources.ApplyResources(this.label8, "label8");
            this.label8.Name = "label8";
            this.toolTip.SetToolTip(this.label8, resources.GetString("label8.ToolTip"));
            // 
            // HideDonateButton
            // 
            resources.ApplyResources(this.HideDonateButton, "HideDonateButton");
            this.HideDonateButton.Name = "HideDonateButton";
            this.HideDonateButton.UseVisualStyleBackColor = true;
            this.HideDonateButton.CheckedChanged += new System.EventHandler(this.HideDonateButton_CheckedChanged);
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            this.toolTip.SetToolTip(this.label7, resources.GetString("label7.ToolTip"));
            // 
            // LanguageSelection
            // 
            resources.ApplyResources(this.LanguageSelection, "LanguageSelection");
            this.LanguageSelection.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.LanguageSelection.FormattingEnabled = true;
            this.LanguageSelection.Name = "LanguageSelection";
            this.toolTip.SetToolTip(this.LanguageSelection, resources.GetString("LanguageSelection.ToolTip"));
            this.LanguageSelection.SelectedIndexChanged += new System.EventHandler(this.LanguageSelection_SelectedIndexChanged);
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            this.toolTip.SetToolTip(this.label3, resources.GetString("label3.ToolTip"));
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.CacheSizeLabel);
            this.groupBox2.Controls.Add(this.ClearCacheButton);
            this.groupBox2.Controls.Add(this.SignatureCacheEnabled);
            this.groupBox2.Controls.Add(this.SignatureCachePathBrowse);
            this.groupBox2.Controls.Add(this.SignatureCachePath);
            this.groupBox2.Controls.Add(this.TempPathBrowse);
            this.groupBox2.Controls.Add(this.TempPath);
            this.groupBox2.Controls.Add(this.label4);
            resources.ApplyResources(this.groupBox2, "groupBox2");
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
            this.toolTip.SetToolTip(this.ClearCacheButton, resources.GetString("ClearCacheButton.ToolTip"));
            this.ClearCacheButton.UseVisualStyleBackColor = true;
            this.ClearCacheButton.Click += new System.EventHandler(this.ClearCacheButton_Click);
            // 
            // SignatureCacheEnabled
            // 
            resources.ApplyResources(this.SignatureCacheEnabled, "SignatureCacheEnabled");
            this.SignatureCacheEnabled.Name = "SignatureCacheEnabled";
            this.toolTip.SetToolTip(this.SignatureCacheEnabled, resources.GetString("SignatureCacheEnabled.ToolTip"));
            this.SignatureCacheEnabled.UseVisualStyleBackColor = true;
            this.SignatureCacheEnabled.CheckedChanged += new System.EventHandler(this.SignatureCacheEnabled_CheckedChanged);
            // 
            // SignatureCachePathBrowse
            // 
            resources.ApplyResources(this.SignatureCachePathBrowse, "SignatureCachePathBrowse");
            this.SignatureCachePathBrowse.Name = "SignatureCachePathBrowse";
            this.toolTip.SetToolTip(this.SignatureCachePathBrowse, resources.GetString("SignatureCachePathBrowse.ToolTip"));
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
            this.toolTip.SetToolTip(this.TempPathBrowse, resources.GetString("TempPathBrowse.ToolTip"));
            this.TempPathBrowse.UseVisualStyleBackColor = true;
            this.TempPathBrowse.Click += new System.EventHandler(this.TempPathBrowse_Click);
            // 
            // TempPath
            // 
            resources.ApplyResources(this.TempPath, "TempPath");
            this.TempPath.Name = "TempPath";
            this.toolTip.SetToolTip(this.TempPath, resources.GetString("TempPath.ToolTip"));
            this.TempPath.TextChanged += new System.EventHandler(this.TempPath_TextChanged);
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // PasswordDefaultsGroup
            // 
            this.PasswordDefaultsGroup.Controls.Add(this.EncryptionMethod);
            this.PasswordDefaultsGroup.Controls.Add(this.EncryptionModule);
            this.PasswordDefaultsGroup.Controls.Add(this.UseCommonPassword);
            this.PasswordDefaultsGroup.Controls.Add(this.CommonPassword);
            resources.ApplyResources(this.PasswordDefaultsGroup, "PasswordDefaultsGroup");
            this.PasswordDefaultsGroup.Name = "PasswordDefaultsGroup";
            this.PasswordDefaultsGroup.TabStop = false;
            // 
            // EncryptionMethod
            // 
            resources.ApplyResources(this.EncryptionMethod, "EncryptionMethod");
            this.EncryptionMethod.Name = "EncryptionMethod";
            this.toolTip.SetToolTip(this.EncryptionMethod, resources.GetString("EncryptionMethod.ToolTip"));
            // 
            // EncryptionModule
            // 
            this.EncryptionModule.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncryptionModule.FormattingEnabled = true;
            resources.ApplyResources(this.EncryptionModule, "EncryptionModule");
            this.EncryptionModule.Name = "EncryptionModule";
            this.toolTip.SetToolTip(this.EncryptionModule, resources.GetString("EncryptionModule.ToolTip"));
            this.EncryptionModule.SelectedIndexChanged += new System.EventHandler(this.EncryptionModule_SelectedIndexChanged);
            this.EncryptionModule.TextChanged += new System.EventHandler(this.EncryptionModule_SelectedIndexChanged);
            // 
            // UseCommonPassword
            // 
            resources.ApplyResources(this.UseCommonPassword, "UseCommonPassword");
            this.UseCommonPassword.Name = "UseCommonPassword";
            this.toolTip.SetToolTip(this.UseCommonPassword, resources.GetString("UseCommonPassword.ToolTip"));
            this.UseCommonPassword.UseVisualStyleBackColor = true;
            this.UseCommonPassword.CheckedChanged += new System.EventHandler(this.UseCommonPassword_CheckedChanged);
            // 
            // CommonPassword
            // 
            this.CommonPassword.AskToEnterNewPassword = false;
            this.CommonPassword.InitialPassword = null;
            this.CommonPassword.IsPasswordVisible = false;
            resources.ApplyResources(this.CommonPassword, "CommonPassword");
            this.CommonPassword.MaximumSize = new System.Drawing.Size(5000, 20);
            this.CommonPassword.MinimumSize = new System.Drawing.Size(150, 20);
            this.CommonPassword.Name = "CommonPassword";
            this.toolTip.SetToolTip(this.CommonPassword, resources.GetString("CommonPassword.ToolTip"));
            this.CommonPassword.TextChanged += new System.EventHandler(this.CommonPassword_TextChanged);
            // 
            // CacheSizeCalculator
            // 
            this.CacheSizeCalculator.WorkerSupportsCancellation = true;
            this.CacheSizeCalculator.DoWork += new System.ComponentModel.DoWorkEventHandler(this.CacheSizeCalculator_DoWork);
            this.CacheSizeCalculator.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.CacheSizeCalculator_RunWorkerCompleted);
            // 
            // TabContainer
            // 
            this.TabContainer.Controls.Add(this.BasicTab);
            this.TabContainer.Controls.Add(this.AdvancedTab);
            this.TabContainer.Controls.Add(this.LicenseTab);
            resources.ApplyResources(this.TabContainer, "TabContainer");
            this.TabContainer.Name = "TabContainer";
            this.TabContainer.SelectedIndex = 0;
            this.TabContainer.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            // 
            // BasicTab
            // 
            this.BasicTab.Controls.Add(this.PasswordDefaultsGroup);
            this.BasicTab.Controls.Add(this.groupBox1);
            resources.ApplyResources(this.BasicTab, "BasicTab");
            this.BasicTab.Name = "BasicTab";
            this.BasicTab.UseVisualStyleBackColor = true;
            // 
            // AdvancedTab
            // 
            this.AdvancedTab.Controls.Add(this.groupBox2);
            this.AdvancedTab.Controls.Add(this.groupBox4);
            resources.ApplyResources(this.AdvancedTab, "AdvancedTab");
            this.AdvancedTab.Name = "AdvancedTab";
            this.AdvancedTab.UseVisualStyleBackColor = true;
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.Bandwidth);
            this.groupBox4.Controls.Add(this.ThreadPriorityPicker);
            resources.ApplyResources(this.groupBox4, "groupBox4");
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.TabStop = false;
            // 
            // LicenseTab
            // 
            this.LicenseTab.Controls.Add(this.LicenseLink);
            this.LicenseTab.Controls.Add(this.label6);
            this.LicenseTab.Controls.Add(this.LicenseText);
            this.LicenseTab.Controls.Add(this.label2);
            this.LicenseTab.Controls.Add(this.LicenseSections);
            resources.ApplyResources(this.LicenseTab, "LicenseTab");
            this.LicenseTab.Name = "LicenseTab";
            this.LicenseTab.UseVisualStyleBackColor = true;
            // 
            // LicenseLink
            // 
            resources.ApplyResources(this.LicenseLink, "LicenseLink");
            this.LicenseLink.Name = "LicenseLink";
            this.LicenseLink.TabStop = true;
            this.toolTip.SetToolTip(this.LicenseLink, resources.GetString("LicenseLink.ToolTip"));
            this.LicenseLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LicenseLink_LinkClicked);
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // LicenseText
            // 
            this.LicenseText.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(this.LicenseText, "LicenseText");
            this.LicenseText.Name = "LicenseText";
            this.LicenseText.ReadOnly = true;
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // LicenseSections
            // 
            this.LicenseSections.FormattingEnabled = true;
            resources.ApplyResources(this.LicenseSections, "LicenseSections");
            this.LicenseSections.Name = "LicenseSections";
            this.toolTip.SetToolTip(this.LicenseSections, resources.GetString("LicenseSections.ToolTip"));
            this.LicenseSections.SelectedIndexChanged += new System.EventHandler(this.LicenseSections_SelectedIndexChanged);
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
            // StartupDelayDuration
            // 
            resources.ApplyResources(this.StartupDelayDuration, "StartupDelayDuration");
            this.StartupDelayDuration.Name = "StartupDelayDuration";
            this.toolTip.SetToolTip(this.StartupDelayDuration, resources.GetString("StartupDelayDuration.ToolTip"));
            this.StartupDelayDuration.Value = "";
            this.StartupDelayDuration.ValueChanged += new System.EventHandler(this.StartupDelayDuration_ValueChanged);
            // 
            // RecentDuration
            // 
            resources.ApplyResources(this.RecentDuration, "RecentDuration");
            this.RecentDuration.Name = "RecentDuration";
            this.toolTip.SetToolTip(this.RecentDuration, resources.GetString("RecentDuration.ToolTip"));
            this.RecentDuration.Value = "";
            this.RecentDuration.ValueChanged += new System.EventHandler(this.RecentDuration_ValueChanged);
            // 
            // Bandwidth
            // 
            this.Bandwidth.DownloadLimit = null;
            this.Bandwidth.DownloadLimitInBytes = ((long)(0));
            resources.ApplyResources(this.Bandwidth, "Bandwidth");
            this.Bandwidth.Name = "Bandwidth";
            this.toolTip.SetToolTip(this.Bandwidth, resources.GetString("Bandwidth.ToolTip"));
            this.Bandwidth.UploadLimit = null;
            this.Bandwidth.UploadLimitInBytes = ((long)(0));
            this.Bandwidth.DownloadLimitChanged += new System.EventHandler(this.Bandwidth_DownloadLimitChanged);
            this.Bandwidth.UploadLimitChanged += new System.EventHandler(this.Bandwidth_UploadLimitChanged);
            // 
            // ThreadPriorityPicker
            // 
            resources.ApplyResources(this.ThreadPriorityPicker, "ThreadPriorityPicker");
            this.ThreadPriorityPicker.Name = "ThreadPriorityPicker";
            this.toolTip.SetToolTip(this.ThreadPriorityPicker, resources.GetString("ThreadPriorityPicker.ToolTip"));
            this.ThreadPriorityPicker.SelectedPriorityChanged += new System.EventHandler(this.ThreadPriorityPicker_SelectedPriorityChanged);
            // 
            // ApplicationSetup
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.TabContainer);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ApplicationSetup";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ApplicationSetup_FormClosing);
            this.Load += new System.EventHandler(this.ApplicationSetup_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.PasswordDefaultsGroup.ResumeLayout(false);
            this.PasswordDefaultsGroup.PerformLayout();
            this.TabContainer.ResumeLayout(false);
            this.BasicTab.ResumeLayout(false);
            this.AdvancedTab.ResumeLayout(false);
            this.groupBox4.ResumeLayout(false);
            this.LicenseTab.ResumeLayout(false);
            this.LicenseTab.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private Duplicati.GUI.HelperControls.DurationEditor RecentDuration;
        private System.Windows.Forms.Button TempPathBrowse;
        private System.Windows.Forms.TextBox TempPath;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.FolderBrowserDialog BrowseTempPath;
        private System.Windows.Forms.GroupBox PasswordDefaultsGroup;
        private Duplicati.Winforms.Controls.PasswordControl CommonPassword;
        private System.Windows.Forms.CheckBox UseCommonPassword;
        private System.Windows.Forms.CheckBox SignatureCacheEnabled;
        private System.Windows.Forms.Button SignatureCachePathBrowse;
        private System.Windows.Forms.TextBox SignatureCachePath;
        private System.Windows.Forms.FolderBrowserDialog BrowseSignatureCachePath;
        private System.Windows.Forms.Label CacheSizeLabel;
        private System.Windows.Forms.Button ClearCacheButton;
        private System.ComponentModel.BackgroundWorker CacheSizeCalculator;
        private System.Windows.Forms.ComboBox LanguageSelection;
        private System.Windows.Forms.Label label3;
        private Duplicati.GUI.HelperControls.DurationEditor StartupDelayDuration;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TabControl TabContainer;
        private System.Windows.Forms.TabPage BasicTab;
        private System.Windows.Forms.TabPage AdvancedTab;
        private System.Windows.Forms.GroupBox groupBox4;
        private Duplicati.GUI.HelperControls.BandwidthLimit Bandwidth;
        private Duplicati.GUI.HelperControls.ThreadPriorityPicker ThreadPriorityPicker;
        private System.Windows.Forms.Label EncryptionMethod;
        private System.Windows.Forms.ComboBox EncryptionModule;
        private System.Windows.Forms.CheckBox HideDonateButton;
        private System.Windows.Forms.TabPage LicenseTab;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ListBox LicenseSections;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.LinkLabel LicenseLink;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox LicenseText;
        private System.Windows.Forms.ComboBox BalloonNotificationLevel;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
    }
}
