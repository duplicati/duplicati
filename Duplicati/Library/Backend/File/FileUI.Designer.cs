namespace Duplicati.Library.Backend
{
    partial class FileUI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FileUI));
            this.UsePath = new System.Windows.Forms.RadioButton();
            this.UseDisk = new System.Windows.Forms.RadioButton();
            this.TargetFolder = new System.Windows.Forms.TextBox();
            this.TargetDrive = new System.Windows.Forms.ComboBox();
            this.BrowseTargetFolder = new System.Windows.Forms.Button();
            this.Credentials = new System.Windows.Forms.GroupBox();
            this.Password = new Duplicati.Winforms.Controls.PasswordControl();
            this.Username = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.UseCredentials = new System.Windows.Forms.CheckBox();
            this.Folder = new System.Windows.Forms.TextBox();
            this.FolderLabel = new System.Windows.Forms.Label();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.Credentials.SuspendLayout();
            this.SuspendLayout();
            // 
            // UsePath
            // 
            resources.ApplyResources(this.UsePath, "UsePath");
            this.UsePath.Checked = true;
            this.UsePath.Name = "UsePath";
            this.UsePath.TabStop = true;
            this.UsePath.UseVisualStyleBackColor = true;
            this.UsePath.CheckedChanged += new System.EventHandler(this.UsePath_CheckedChanged);
            // 
            // UseDisk
            // 
            resources.ApplyResources(this.UseDisk, "UseDisk");
            this.UseDisk.Name = "UseDisk";
            this.UseDisk.UseVisualStyleBackColor = true;
            this.UseDisk.CheckedChanged += new System.EventHandler(this.UseDisk_CheckedChanged);
            // 
            // TargetFolder
            // 
            this.TargetFolder.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.Suggest;
            this.TargetFolder.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.FileSystemDirectories;
            resources.ApplyResources(this.TargetFolder, "TargetFolder");
            this.TargetFolder.Name = "TargetFolder";
            // 
            // TargetDrive
            // 
            this.TargetDrive.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            resources.ApplyResources(this.TargetDrive, "TargetDrive");
            this.TargetDrive.FormattingEnabled = true;
            this.TargetDrive.Name = "TargetDrive";
            // 
            // BrowseTargetFolder
            // 
            resources.ApplyResources(this.BrowseTargetFolder, "BrowseTargetFolder");
            this.BrowseTargetFolder.Name = "BrowseTargetFolder";
            this.BrowseTargetFolder.UseVisualStyleBackColor = true;
            this.BrowseTargetFolder.Click += new System.EventHandler(this.BrowseTargetFolder_Click);
            // 
            // Credentials
            // 
            this.Credentials.Controls.Add(this.Password);
            this.Credentials.Controls.Add(this.Username);
            this.Credentials.Controls.Add(this.label2);
            this.Credentials.Controls.Add(this.label1);
            resources.ApplyResources(this.Credentials, "Credentials");
            this.Credentials.Name = "Credentials";
            this.Credentials.TabStop = false;
            // 
            // Password
            // 
            this.Password.AskToEnterNewPassword = false;
            this.Password.IsPasswordVisible = false;
            resources.ApplyResources(this.Password, "Password");
            this.Password.MaximumSize = new System.Drawing.Size(5000, 20);
            this.Password.MinimumSize = new System.Drawing.Size(150, 20);
            this.Password.Name = "Password";
            // 
            // Username
            // 
            resources.ApplyResources(this.Username, "Username");
            this.Username.Name = "Username";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // UseCredentials
            // 
            resources.ApplyResources(this.UseCredentials, "UseCredentials");
            this.UseCredentials.Name = "UseCredentials";
            this.UseCredentials.UseVisualStyleBackColor = true;
            this.UseCredentials.CheckedChanged += new System.EventHandler(this.UseCredentials_CheckedChanged);
            // 
            // Folder
            // 
            resources.ApplyResources(this.Folder, "Folder");
            this.Folder.Name = "Folder";
            // 
            // FolderLabel
            // 
            resources.ApplyResources(this.FolderLabel, "FolderLabel");
            this.FolderLabel.Name = "FolderLabel";
            // 
            // folderBrowserDialog
            // 
            resources.ApplyResources(this.folderBrowserDialog, "folderBrowserDialog");
            // 
            // FileUI
            // 
            this.Controls.Add(this.FolderLabel);
            this.Controls.Add(this.Folder);
            this.Controls.Add(this.UseCredentials);
            this.Controls.Add(this.Credentials);
            this.Controls.Add(this.BrowseTargetFolder);
            this.Controls.Add(this.TargetDrive);
            this.Controls.Add(this.TargetFolder);
            this.Controls.Add(this.UseDisk);
            this.Controls.Add(this.UsePath);
            this.Name = "FileUI";
            resources.ApplyResources(this, "$this");
            this.Load += new System.EventHandler(this.FileUI_Load);
            this.Credentials.ResumeLayout(false);
            this.Credentials.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton UsePath;
        private System.Windows.Forms.RadioButton UseDisk;
        private System.Windows.Forms.TextBox TargetFolder;
        private System.Windows.Forms.ComboBox TargetDrive;
        private System.Windows.Forms.Button BrowseTargetFolder;
        private System.Windows.Forms.GroupBox Credentials;
        private Duplicati.Winforms.Controls.PasswordControl Password;
        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox UseCredentials;
        private System.Windows.Forms.TextBox Folder;
        private System.Windows.Forms.Label FolderLabel;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    }
}
