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
            this.BackupFolder = new Duplicati.GUI.HelperControls.BackupTreeView();
            this.label1 = new System.Windows.Forms.Label();
            this.BackupName = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // BackupFolder
            // 
            this.BackupFolder.Location = new System.Drawing.Point(24, 80);
            this.BackupFolder.Name = "BackupFolder";
            this.BackupFolder.SelectedBackup = null;
            this.BackupFolder.SelectedFolder = "";
            this.BackupFolder.Size = new System.Drawing.Size(440, 120);
            this.BackupFolder.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(73, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Backup name";
            // 
            // BackupName
            // 
            this.BackupName.Location = new System.Drawing.Point(120, 16);
            this.BackupName.Name = "BackupName";
            this.BackupName.Size = new System.Drawing.Size(344, 20);
            this.BackupName.TabIndex = 2;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(24, 64);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(187, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Place the backup in a group (optional)";
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(344, 200);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(120, 24);
            this.button1.TabIndex = 4;
            this.button1.Text = "Add a group";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // SelectName
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.BackupName);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.BackupFolder);
            this.Name = "SelectName";
            this.Size = new System.Drawing.Size(506, 242);
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
