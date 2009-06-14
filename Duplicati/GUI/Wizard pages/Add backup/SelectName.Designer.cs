namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class SelectName
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectName));
            this.BackupFolder = new Duplicati.GUI.HelperControls.BackupTreeView();
            this.label1 = new System.Windows.Forms.Label();
            this.BackupName = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // BackupFolder
            // 
            resources.ApplyResources(this.BackupFolder, "BackupFolder");
            this.BackupFolder.Name = "BackupFolder";
            this.BackupFolder.SelectedBackup = null;
            this.BackupFolder.SelectedFolder = "";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // BackupName
            // 
            resources.ApplyResources(this.BackupName, "BackupName");
            this.BackupName.Name = "BackupName";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // SelectName
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.BackupName);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.BackupFolder);
            this.Name = "SelectName";
            this.Load += new System.EventHandler(this.SelectName_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Duplicati.GUI.HelperControls.BackupTreeView BackupFolder;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox BackupName;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button button1;
    }
}
