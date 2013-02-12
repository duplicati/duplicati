namespace Duplicati.GUI.Wizard_pages
{
    partial class FirstLaunch
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FirstLaunch));
            this.CreateNew = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.RestoreSetup = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.SimpleHelpHeader = new System.Windows.Forms.Label();
            this.SetupNewBackupHelp = new System.Windows.Forms.Label();
            this.RestoreSettingsHelp = new System.Windows.Forms.Label();
            this.RestoreFilesHelp = new System.Windows.Forms.Label();
            this.RestoreFiles = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.SuspendLayout();
            // 
            // CreateNew
            // 
            resources.ApplyResources(this.CreateNew, "CreateNew");
            this.CreateNew.Name = "CreateNew";
            this.CreateNew.TabStop = true;
            this.CreateNew.UseVisualStyleBackColor = true;
            this.CreateNew.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.CreateNew.CheckedChanged += new System.EventHandler(this.CreateNew_CheckedChanged);
            // 
            // RestoreSetup
            // 
            resources.ApplyResources(this.RestoreSetup, "RestoreSetup");
            this.RestoreSetup.Name = "RestoreSetup";
            this.RestoreSetup.TabStop = true;
            this.RestoreSetup.UseVisualStyleBackColor = true;
            this.RestoreSetup.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.RestoreSetup.CheckedChanged += new System.EventHandler(this.RestoreSetup_CheckedChanged);
            // 
            // SimpleHelpHeader
            // 
            resources.ApplyResources(this.SimpleHelpHeader, "SimpleHelpHeader");
            this.SimpleHelpHeader.Name = "SimpleHelpHeader";
            // 
            // SetupNewBackupHelp
            // 
            resources.ApplyResources(this.SetupNewBackupHelp, "SetupNewBackupHelp");
            this.SetupNewBackupHelp.Name = "SetupNewBackupHelp";
            // 
            // RestoreSettingsHelp
            // 
            resources.ApplyResources(this.RestoreSettingsHelp, "RestoreSettingsHelp");
            this.RestoreSettingsHelp.Name = "RestoreSettingsHelp";
            // 
            // RestoreFilesHelp
            // 
            resources.ApplyResources(this.RestoreFilesHelp, "RestoreFilesHelp");
            this.RestoreFilesHelp.Name = "RestoreFilesHelp";
            // 
            // RestoreFiles
            // 
            resources.ApplyResources(this.RestoreFiles, "RestoreFiles");
            this.RestoreFiles.Name = "RestoreFiles";
            this.RestoreFiles.TabStop = true;
            this.RestoreFiles.UseVisualStyleBackColor = true;
            this.RestoreFiles.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.RestoreFiles.CheckedChanged += new System.EventHandler(this.RestoreFiles_CheckedChanged);
            // 
            // FirstLaunch
            // 
            this.Controls.Add(this.RestoreFilesHelp);
            this.Controls.Add(this.RestoreFiles);
            this.Controls.Add(this.RestoreSettingsHelp);
            this.Controls.Add(this.SetupNewBackupHelp);
            this.Controls.Add(this.SimpleHelpHeader);
            this.Controls.Add(this.RestoreSetup);
            this.Controls.Add(this.CreateNew);
            this.Name = "FirstLaunch";
            resources.ApplyResources(this, "$this");
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Wizard.DoubleClickRadioButton CreateNew;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton RestoreSetup;
        private System.Windows.Forms.Label SimpleHelpHeader;
        private System.Windows.Forms.Label SetupNewBackupHelp;
        private System.Windows.Forms.Label RestoreSettingsHelp;
        private System.Windows.Forms.Label RestoreFilesHelp;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton RestoreFiles;

    }
}
