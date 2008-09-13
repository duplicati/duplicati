namespace Duplicati.Wizard_pages
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
            this.BackupList = new Duplicati.HelperControls.BackupTreeView();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // BackupList
            // 
            this.BackupList.Location = new System.Drawing.Point(32, 40);
            this.BackupList.Name = "BackupList";
            this.BackupList.SelectedFolder = "";
            this.BackupList.Size = new System.Drawing.Size(432, 168);
            this.BackupList.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(32, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(121, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Select backup to modify";
            // 
            // SelectBackup
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.BackupList);
            this.Name = "SelectBackup";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Duplicati.HelperControls.BackupTreeView BackupList;
        private System.Windows.Forms.Label label1;
    }
}
