namespace Duplicati.GUI.Wizard_pages
{
    partial class SelectBackup
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectBackup));
            this.topLabel = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.ExpanderPanel = new System.Windows.Forms.Panel();
            this.RestoreOptions = new System.Windows.Forms.Panel();
            this.RestoreExisting = new System.Windows.Forms.RadioButton();
            this.DirectRestore = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.BackupList = new Duplicati.GUI.HelperControls.BackupTreeView();
            this.panel1.SuspendLayout();
            this.ExpanderPanel.SuspendLayout();
            this.RestoreOptions.SuspendLayout();
            this.SuspendLayout();
            // 
            // topLabel
            // 
            resources.ApplyResources(this.topLabel, "topLabel");
            this.topLabel.Name = "topLabel";
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.BackupList);
            this.panel1.Controls.Add(this.topLabel);
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
            // 
            // ExpanderPanel
            // 
            this.ExpanderPanel.Controls.Add(this.panel1);
            this.ExpanderPanel.Controls.Add(this.RestoreOptions);
            resources.ApplyResources(this.ExpanderPanel, "ExpanderPanel");
            this.ExpanderPanel.Name = "ExpanderPanel";
            // 
            // RestoreOptions
            // 
            this.RestoreOptions.Controls.Add(this.DirectRestore);
            this.RestoreOptions.Controls.Add(this.RestoreExisting);
            resources.ApplyResources(this.RestoreOptions, "RestoreOptions");
            this.RestoreOptions.Name = "RestoreOptions";
            // 
            // RestoreExisting
            // 
            resources.ApplyResources(this.RestoreExisting, "RestoreExisting");
            this.RestoreExisting.Name = "RestoreExisting";
            this.RestoreExisting.TabStop = true;
            this.RestoreExisting.UseVisualStyleBackColor = true;
            // 
            // DirectRestore
            // 
            resources.ApplyResources(this.DirectRestore, "DirectRestore");
            this.DirectRestore.Name = "DirectRestore";
            this.DirectRestore.TabStop = true;
            this.DirectRestore.UseVisualStyleBackColor = true;
            this.DirectRestore.DoubleClick += new System.EventHandler(this.DirectRestore_DoubleClick);
            // 
            // BackupList
            // 
            resources.ApplyResources(this.BackupList, "BackupList");
            this.BackupList.Name = "BackupList";
            this.BackupList.SelectedBackup = null;
            this.BackupList.SelectedFolder = "";
            this.BackupList.SelectedBackupChanged += new System.EventHandler(this.BackupList_SelectedBackupChanged);
            this.BackupList.TreeDoubleClicked += new System.EventHandler(this.BackupList_TreeDoubleClicked);
            // 
            // SelectBackup
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ExpanderPanel);
            this.Name = "SelectBackup";
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ExpanderPanel.ResumeLayout(false);
            this.RestoreOptions.ResumeLayout(false);
            this.RestoreOptions.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private Duplicati.GUI.HelperControls.BackupTreeView BackupList;
        private System.Windows.Forms.Label topLabel;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel ExpanderPanel;
        private System.Windows.Forms.Panel RestoreOptions;
        private System.Windows.Forms.RadioButton RestoreExisting;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton DirectRestore;
    }
}
