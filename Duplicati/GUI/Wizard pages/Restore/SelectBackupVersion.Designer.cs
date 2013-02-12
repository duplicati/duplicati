namespace Duplicati.GUI.Wizard_pages.Restore
{
    partial class SelectBackupVersion
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectBackupVersion));
            this.BackupList = new Duplicati.GUI.HelperControls.BackupItems();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // BackupList
            // 
            resources.ApplyResources(this.BackupList, "BackupList");
            this.BackupList.Name = "BackupList";
            this.BackupList.ItemDoubleClicked += new System.EventHandler(this.BackupList_ItemDoubleClicked);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // SelectBackup
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.BackupList);
            this.Name = "SelectBackup";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Duplicati.GUI.HelperControls.BackupItems BackupList;
        private System.Windows.Forms.Label label1;
    }
}
