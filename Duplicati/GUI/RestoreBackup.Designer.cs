namespace Duplicati.GUI
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
            this.backupItems = new Duplicati.GUI.HelperControls.BackupItems();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // TargetFolder
            // 
            resources.ApplyResources(this.TargetFolder, "TargetFolder");
            this.errorProvider.SetIconAlignment(this.TargetFolder, ((System.Windows.Forms.ErrorIconAlignment)(resources.GetObject("TargetFolder.IconAlignment"))));
            this.TargetFolder.Name = "TargetFolder";
            this.TargetFolder.TextChanged += new System.EventHandler(this.TargetFolder_TextChanged);
            // 
            // SelectTargetFolder
            // 
            resources.ApplyResources(this.SelectTargetFolder, "SelectTargetFolder");
            this.SelectTargetFolder.Name = "SelectTargetFolder";
            this.SelectTargetFolder.UseVisualStyleBackColor = true;
            this.SelectTargetFolder.Click += new System.EventHandler(this.SelectTargetFolder_Click);
            // 
            // OKBtn
            // 
            resources.ApplyResources(this.OKBtn, "OKBtn");
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            resources.ApplyResources(this.CancelBtn, "CancelBtn");
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // folderBrowserDialog
            // 
            resources.ApplyResources(this.folderBrowserDialog, "folderBrowserDialog");
            // 
            // backupItems
            // 
            resources.ApplyResources(this.backupItems, "backupItems");
            this.backupItems.Name = "backupItems";
            // 
            // RestoreBackup
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.SelectTargetFolder);
            this.Controls.Add(this.TargetFolder);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.backupItems);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "RestoreBackup";
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private Duplicati.GUI.HelperControls.BackupItems backupItems;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox TargetFolder;
        private System.Windows.Forms.Button SelectTargetFolder;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private System.Windows.Forms.ErrorProvider errorProvider;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    }
}