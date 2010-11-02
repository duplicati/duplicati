namespace Duplicati.GUI.Wizard_pages.Restore
{
    partial class TargetFolder
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TargetFolder));
            this.BrowseFolder = new System.Windows.Forms.Button();
            this.TargetPath = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.PartialSettings = new System.Windows.Forms.GroupBox();
            this.backupFileList = new Duplicati.GUI.HelperControls.BackupFileList();
            this.PartialRestore = new System.Windows.Forms.CheckBox();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.PartialSettings.SuspendLayout();
            this.SuspendLayout();
            // 
            // BrowseFolder
            // 
            resources.ApplyResources(this.BrowseFolder, "BrowseFolder");
            this.BrowseFolder.Name = "BrowseFolder";
            this.BrowseFolder.UseVisualStyleBackColor = true;
            this.BrowseFolder.Click += new System.EventHandler(this.BrowseFolder_Click);
            // 
            // TargetPath
            // 
            this.TargetPath.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.Suggest;
            this.TargetPath.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.FileSystemDirectories;
            resources.ApplyResources(this.TargetPath, "TargetPath");
            this.TargetPath.Name = "TargetPath";
            this.TargetPath.TextChanged += new System.EventHandler(this.TargetPath_TextChanged);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // PartialSettings
            // 
            this.PartialSettings.Controls.Add(this.backupFileList);
            resources.ApplyResources(this.PartialSettings, "PartialSettings");
            this.PartialSettings.Name = "PartialSettings";
            this.PartialSettings.TabStop = false;
            // 
            // backupFileList
            // 
            this.backupFileList.CheckedFiles = ((System.Collections.Generic.List<string>)(resources.GetObject("backupFileList.CheckedFiles")));
            this.backupFileList.DefaultTarget = null;
            resources.ApplyResources(this.backupFileList, "backupFileList");
            this.backupFileList.Name = "backupFileList";
            this.backupFileList.FileListLoaded += new System.EventHandler(this.backupFileList_FileListLoaded);
            // 
            // PartialRestore
            // 
            resources.ApplyResources(this.PartialRestore, "PartialRestore");
            this.PartialRestore.Name = "PartialRestore";
            this.PartialRestore.UseVisualStyleBackColor = true;
            this.PartialRestore.CheckedChanged += new System.EventHandler(this.PartialRestore_CheckedChanged);
            // 
            // folderBrowserDialog
            // 
            resources.ApplyResources(this.folderBrowserDialog, "folderBrowserDialog");
            // 
            // TargetFolder
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.PartialRestore);
            this.Controls.Add(this.PartialSettings);
            this.Controls.Add(this.BrowseFolder);
            this.Controls.Add(this.TargetPath);
            this.Controls.Add(this.label2);
            this.Name = "TargetFolder";
            this.Load += new System.EventHandler(this.TargetFolder_Load);
            this.PartialSettings.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button BrowseFolder;
        private System.Windows.Forms.TextBox TargetPath;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox PartialSettings;
        private System.Windows.Forms.CheckBox PartialRestore;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
        private Duplicati.GUI.HelperControls.BackupFileList backupFileList;
    }
}
