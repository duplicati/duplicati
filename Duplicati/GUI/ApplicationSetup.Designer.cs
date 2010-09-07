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
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.BrowseTempPath = new System.Windows.Forms.FolderBrowserDialog();
            this.PasswordDefaultsGroup = new System.Windows.Forms.GroupBox();
            this.PasswordPanel = new System.Windows.Forms.Panel();
            this.EncryptionModule = new System.Windows.Forms.ComboBox();
            this.EncryptionMethod = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.CommonPassword = new System.Windows.Forms.TextBox();
            this.UseCommonPassword = new System.Windows.Forms.CheckBox();
            this.BrowseSignatureCachePath = new System.Windows.Forms.FolderBrowserDialog();
            this.CacheSizeCalculator = new System.ComponentModel.BackgroundWorker();
            this.TabContainer = new System.Windows.Forms.TabControl();
            this.BasicTab = new System.Windows.Forms.TabPage();
            this.AdvancedTab = new System.Windows.Forms.TabPage();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.LicenseTab = new System.Windows.Forms.TabPage();
            this.LicenseSections = new System.Windows.Forms.ListBox();
            this.label2 = new System.Windows.Forms.Label();
            this.LicenseText = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.LicenseLink = new System.Windows.Forms.LinkLabel();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.StartupDelayDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.RecentDuration = new Duplicati.GUI.HelperControls.DurationEditor();
            this.Bandwidth = new Duplicati.GUI.HelperControls.BandwidthLimit();
            this.ThreadPriorityPicker = new Duplicati.GUI.HelperControls.ThreadPriorityPicker();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.PasswordDefaultsGroup.SuspendLayout();
            this.PasswordPanel.SuspendLayout();
            this.TabContainer.SuspendLayout();
            this.BasicTab.SuspendLayout();
            this.AdvancedTab.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.panel1.SuspendLayout();
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
            // 
            // HideDonateButton
            // 
            resources.ApplyResources(this.HideDonateButton, "HideDonateButton");
            this.HideDonateButton.Name = "HideDonateButton";
            this.toolTip.SetToolTip(this.HideDonateButton, resources.GetString("HideDonateButton.ToolTip"));
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
            this.toolTip.SetToolTip(this.CacheSizeLabel, resources.GetString("CacheSizeLabel.ToolTip"));
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
            this.toolTip.SetToolTip(this.label4, resources.GetString("label4.ToolTip"));
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
            // PasswordDefaultsGroup
            // 
            this.PasswordDefaultsGroup.Controls.Add(this.PasswordPanel);
            this.PasswordDefaultsGroup.Controls.Add(this.UseCommonPassword);
            resources.ApplyResources(this.PasswordDefaultsGroup, "PasswordDefaultsGroup");
            this.PasswordDefaultsGroup.Name = "PasswordDefaultsGroup";
            this.PasswordDefaultsGroup.TabStop = false;
            // 
            // PasswordPanel
            // 
            this.PasswordPanel.Controls.Add(this.EncryptionModule);
            this.PasswordPanel.Controls.Add(this.EncryptionMethod);
            this.PasswordPanel.Controls.Add(this.label5);
            this.PasswordPanel.Controls.Add(this.CommonPassword);
            resources.ApplyResources(this.PasswordPanel, "PasswordPanel");
            this.PasswordPanel.Name = "PasswordPanel";
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
            // EncryptionMethod
            // 
            resources.ApplyResources(this.EncryptionMethod, "EncryptionMethod");
            this.EncryptionMethod.Name = "EncryptionMethod";
            this.toolTip.SetToolTip(this.EncryptionMethod, resources.GetString("EncryptionMethod.ToolTip"));
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            this.toolTip.SetToolTip(this.label5, resources.GetString("label5.ToolTip"));
            // 
            // CommonPassword
            // 
            resources.ApplyResources(this.CommonPassword, "CommonPassword");
            this.CommonPassword.Name = "CommonPassword";
            this.toolTip.SetToolTip(this.CommonPassword, resources.GetString("CommonPassword.ToolTip"));
            this.CommonPassword.UseSystemPasswordChar = true;
            this.CommonPassword.TextChanged += new System.EventHandler(this.CommonPassword_TextChanged);
            // 
            // UseCommonPassword
            // 
            resources.ApplyResources(this.UseCommonPassword, "UseCommonPassword");
            this.UseCommonPassword.Name = "UseCommonPassword";
            this.toolTip.SetToolTip(this.UseCommonPassword, resources.GetString("UseCommonPassword.ToolTip"));
            this.UseCommonPassword.UseVisualStyleBackColor = true;
            this.UseCommonPassword.CheckedChanged += new System.EventHandler(this.UseCommonPassword_CheckedChanged);
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
            // panel1
            // 
            this.panel1.Controls.Add(this.OKBtn);
            this.panel1.Controls.Add(this.CancelBtn);
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
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
            // LicenseSections
            // 
            this.LicenseSections.FormattingEnabled = true;
            resources.ApplyResources(this.LicenseSections, "LicenseSections");
            this.LicenseSections.Name = "LicenseSections";
            this.toolTip.SetToolTip(this.LicenseSections, resources.GetString("LicenseSections.ToolTip"));
            this.LicenseSections.SelectedIndexChanged += new System.EventHandler(this.LicenseSections_SelectedIndexChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // LicenseText
            // 
            resources.ApplyResources(this.LicenseText, "LicenseText");
            this.LicenseText.Name = "LicenseText";
            this.LicenseText.ReadOnly = true;
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // LicenseLink
            // 
            resources.ApplyResources(this.LicenseLink, "LicenseLink");
            this.LicenseLink.Name = "LicenseLink";
            this.LicenseLink.TabStop = true;
            this.toolTip.SetToolTip(this.LicenseLink, resources.GetString("LicenseLink.ToolTip"));
            this.LicenseLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.LicenseLink_LinkClicked);
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
            this.Controls.Add(this.TabContainer);
            this.Controls.Add(this.panel1);
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
            this.PasswordDefaultsGroup.ResumeLayout(false);
            this.PasswordDefaultsGroup.PerformLayout();
            this.PasswordPanel.ResumeLayout(false);
            this.PasswordPanel.PerformLayout();
            this.TabContainer.ResumeLayout(false);
            this.BasicTab.ResumeLayout(false);
            this.AdvancedTab.ResumeLayout(false);
            this.groupBox4.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.LicenseTab.ResumeLayout(false);
            this.LicenseTab.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private Duplicati.GUI.HelperControls.DurationEditor RecentDuration;
        private System.Windows.Forms.Button TempPathBrowse;
        private System.Windows.Forms.TextBox TempPath;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.FolderBrowserDialog BrowseTempPath;
        private System.Windows.Forms.GroupBox PasswordDefaultsGroup;
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
        private System.Windows.Forms.Panel panel1;
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
    }
}