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
            this.BackupList = new Duplicati.GUI.HelperControls.BackupTreeView();
            this.topLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // BackupList
            // 
            resources.ApplyResources(this.BackupList, "BackupList");
            this.BackupList.Name = "BackupList";
            this.BackupList.SelectedBackup = null;
            this.BackupList.SelectedFolder = null;
            this.BackupList.TreeDoubleClicked += new System.EventHandler(this.BackupList_TreeDoubleClicked);
            // 
            // topLabel
            // 
            resources.ApplyResources(this.topLabel, "topLabel");
            this.topLabel.Name = "topLabel";
            // 
            // SelectBackup
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.topLabel);
            this.Controls.Add(this.BackupList);
            this.Name = "SelectBackup";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Duplicati.GUI.HelperControls.BackupTreeView BackupList;
        private System.Windows.Forms.Label topLabel;
    }
}
