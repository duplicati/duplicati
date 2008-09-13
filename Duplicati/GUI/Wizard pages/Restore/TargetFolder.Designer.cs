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
            this.BrowseFolder = new System.Windows.Forms.Button();
            this.TargetPath = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.PartialSettings = new System.Windows.Forms.GroupBox();
            this.PartialRestore = new System.Windows.Forms.CheckBox();
            this.PartialSelection = new System.Windows.Forms.TreeView();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.PartialSettings.SuspendLayout();
            this.SuspendLayout();
            // 
            // BrowseFolder
            // 
            this.BrowseFolder.Location = new System.Drawing.Point(464, 16);
            this.BrowseFolder.Name = "BrowseFolder";
            this.BrowseFolder.Size = new System.Drawing.Size(24, 20);
            this.BrowseFolder.TabIndex = 7;
            this.BrowseFolder.Text = "...";
            this.BrowseFolder.UseVisualStyleBackColor = true;
            this.BrowseFolder.Click += new System.EventHandler(this.BrowseFolder_Click);
            // 
            // TargetPath
            // 
            this.TargetPath.Location = new System.Drawing.Point(128, 16);
            this.TargetPath.Name = "TargetPath";
            this.TargetPath.Size = new System.Drawing.Size(336, 20);
            this.TargetPath.TabIndex = 6;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 16);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(104, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Restore to this folder";
            // 
            // PartialSettings
            // 
            this.PartialSettings.Controls.Add(this.PartialSelection);
            this.PartialSettings.Enabled = false;
            this.PartialSettings.Location = new System.Drawing.Point(16, 64);
            this.PartialSettings.Name = "PartialSettings";
            this.PartialSettings.Size = new System.Drawing.Size(472, 144);
            this.PartialSettings.TabIndex = 8;
            this.PartialSettings.TabStop = false;
            // 
            // PartialRestore
            // 
            this.PartialRestore.AutoSize = true;
            this.PartialRestore.Enabled = false;
            this.PartialRestore.Location = new System.Drawing.Point(24, 64);
            this.PartialRestore.Name = "PartialRestore";
            this.PartialRestore.Size = new System.Drawing.Size(204, 17);
            this.PartialRestore.TabIndex = 9;
            this.PartialRestore.Text = "Restore only the items selected below";
            this.PartialRestore.UseVisualStyleBackColor = true;
            // 
            // PartialSelection
            // 
            this.PartialSelection.CheckBoxes = true;
            this.PartialSelection.Location = new System.Drawing.Point(16, 24);
            this.PartialSelection.Name = "PartialSelection";
            this.PartialSelection.Size = new System.Drawing.Size(440, 104);
            this.PartialSelection.TabIndex = 0;
            // 
            // folderBrowserDialog
            // 
            this.folderBrowserDialog.Description = "Select the folder where the backup will be restored";
            // 
            // TargetFolder
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.PartialRestore);
            this.Controls.Add(this.PartialSettings);
            this.Controls.Add(this.BrowseFolder);
            this.Controls.Add(this.TargetPath);
            this.Controls.Add(this.label2);
            this.Name = "TargetFolder";
            this.Size = new System.Drawing.Size(506, 242);
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
        private System.Windows.Forms.TreeView PartialSelection;
        private System.Windows.Forms.CheckBox PartialRestore;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    }
}
