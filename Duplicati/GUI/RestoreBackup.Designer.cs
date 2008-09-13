namespace Duplicati
{
    partial class RestoreBackup
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RestoreBackup));
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.TargetFolder = new System.Windows.Forms.TextBox();
            this.SelectTargetFolder = new System.Windows.Forms.Button();
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.backupItems = new Duplicati.HelperControls.BackupItems();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 8);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(91, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Backup to restore";
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 139);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(60, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Destination";
            // 
            // TargetFolder
            // 
            this.TargetFolder.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.errorProvider.SetIconAlignment(this.TargetFolder, System.Windows.Forms.ErrorIconAlignment.MiddleLeft);
            this.TargetFolder.Location = new System.Drawing.Point(88, 139);
            this.TargetFolder.Name = "TargetFolder";
            this.TargetFolder.Size = new System.Drawing.Size(305, 20);
            this.TargetFolder.TabIndex = 3;
            this.TargetFolder.TextChanged += new System.EventHandler(this.TargetFolder_TextChanged);
            // 
            // SelectTargetFolder
            // 
            this.SelectTargetFolder.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.SelectTargetFolder.Location = new System.Drawing.Point(393, 139);
            this.SelectTargetFolder.Name = "SelectTargetFolder";
            this.SelectTargetFolder.Size = new System.Drawing.Size(24, 20);
            this.SelectTargetFolder.TabIndex = 4;
            this.SelectTargetFolder.Text = "...";
            this.SelectTargetFolder.UseVisualStyleBackColor = true;
            this.SelectTargetFolder.Click += new System.EventHandler(this.SelectTargetFolder_Click);
            // 
            // OKBtn
            // 
            this.OKBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.OKBtn.Enabled = false;
            this.OKBtn.Location = new System.Drawing.Point(132, 170);
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.Size = new System.Drawing.Size(80, 24);
            this.OKBtn.TabIndex = 5;
            this.OKBtn.Text = "OK";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            this.CancelBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBtn.Location = new System.Drawing.Point(220, 170);
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.Size = new System.Drawing.Size(80, 24);
            this.CancelBtn.TabIndex = 6;
            this.CancelBtn.Text = "Cancel";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // folderBrowserDialog
            // 
            this.folderBrowserDialog.Description = "Select the folder where the data will be restored to";
            // 
            // backupItems
            // 
            this.backupItems.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.backupItems.Location = new System.Drawing.Point(8, 24);
            this.backupItems.Name = "backupItems";
            this.backupItems.Size = new System.Drawing.Size(417, 107);
            this.backupItems.TabIndex = 1;
            // 
            // RestoreBackup
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.ClientSize = new System.Drawing.Size(435, 207);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.SelectTargetFolder);
            this.Controls.Add(this.TargetFolder);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.backupItems);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "RestoreBackup";
            this.Text = "RestoreBackup";
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private Duplicati.HelperControls.BackupItems backupItems;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox TargetFolder;
        private System.Windows.Forms.Button SelectTargetFolder;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private System.Windows.Forms.ErrorProvider errorProvider;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    }
}