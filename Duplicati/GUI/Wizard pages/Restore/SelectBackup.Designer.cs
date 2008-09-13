namespace Duplicati.Wizard_pages.Restore
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
            this.BackupList = new Duplicati.HelperControls.BackupItems();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // BackupList
            // 
            this.BackupList.Location = new System.Drawing.Point(16, 40);
            this.BackupList.Name = "BackupList";
            this.BackupList.Size = new System.Drawing.Size(480, 184);
            this.BackupList.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(16, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(187, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Select the backup you want to restore";
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

        private Duplicati.HelperControls.BackupItems BackupList;
        private System.Windows.Forms.Label label1;
    }
}
