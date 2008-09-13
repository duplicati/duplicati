namespace Duplicati.GUI.Wizard_pages.Backends.File
{
    partial class FileOptions
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
            this.UsePath = new System.Windows.Forms.RadioButton();
            this.UseDisk = new System.Windows.Forms.RadioButton();
            this.TargetFolder = new System.Windows.Forms.TextBox();
            this.TargetDrive = new System.Windows.Forms.ComboBox();
            this.BrowseTargetFolder = new System.Windows.Forms.Button();
            this.Credentials = new System.Windows.Forms.GroupBox();
            this.Password = new System.Windows.Forms.TextBox();
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
            this.UsePath.AutoSize = true;
            this.UsePath.Checked = true;
            this.UsePath.Location = new System.Drawing.Point(24, 16);
            this.UsePath.Name = "UsePath";
            this.UsePath.Size = new System.Drawing.Size(47, 17);
            this.UsePath.TabIndex = 0;
            this.UsePath.TabStop = true;
            this.UsePath.Text = "Path";
            this.UsePath.UseVisualStyleBackColor = true;
            this.UsePath.CheckedChanged += new System.EventHandler(this.UsePath_CheckedChanged);
            // 
            // UseDisk
            // 
            this.UseDisk.AutoSize = true;
            this.UseDisk.Location = new System.Drawing.Point(24, 48);
            this.UseDisk.Name = "UseDisk";
            this.UseDisk.Size = new System.Drawing.Size(101, 17);
            this.UseDisk.TabIndex = 1;
            this.UseDisk.Text = "Removable disk";
            this.UseDisk.UseVisualStyleBackColor = true;
            this.UseDisk.CheckedChanged += new System.EventHandler(this.UseDisk_CheckedChanged);
            // 
            // TargetFolder
            // 
            this.TargetFolder.Location = new System.Drawing.Point(136, 16);
            this.TargetFolder.Name = "TargetFolder";
            this.TargetFolder.Size = new System.Drawing.Size(264, 20);
            this.TargetFolder.TabIndex = 2;
            // 
            // TargetDrive
            // 
            this.TargetDrive.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TargetDrive.Enabled = false;
            this.TargetDrive.FormattingEnabled = true;
            this.TargetDrive.Location = new System.Drawing.Point(136, 48);
            this.TargetDrive.Name = "TargetDrive";
            this.TargetDrive.Size = new System.Drawing.Size(72, 21);
            this.TargetDrive.TabIndex = 3;
            // 
            // BrowseTargetFolder
            // 
            this.BrowseTargetFolder.Location = new System.Drawing.Point(400, 16);
            this.BrowseTargetFolder.Name = "BrowseTargetFolder";
            this.BrowseTargetFolder.Size = new System.Drawing.Size(24, 20);
            this.BrowseTargetFolder.TabIndex = 4;
            this.BrowseTargetFolder.Text = "...";
            this.BrowseTargetFolder.UseVisualStyleBackColor = true;
            this.BrowseTargetFolder.Click += new System.EventHandler(this.BrowseTargetFolder_Click);
            // 
            // Credentials
            // 
            this.Credentials.Controls.Add(this.Password);
            this.Credentials.Controls.Add(this.Username);
            this.Credentials.Controls.Add(this.label2);
            this.Credentials.Controls.Add(this.label1);
            this.Credentials.Enabled = false;
            this.Credentials.Location = new System.Drawing.Point(16, 120);
            this.Credentials.Name = "Credentials";
            this.Credentials.Size = new System.Drawing.Size(456, 80);
            this.Credentials.TabIndex = 5;
            this.Credentials.TabStop = false;
            // 
            // Password
            // 
            this.Password.Location = new System.Drawing.Point(120, 48);
            this.Password.Name = "Password";
            this.Password.PasswordChar = '*';
            this.Password.Size = new System.Drawing.Size(264, 20);
            this.Password.TabIndex = 8;
            // 
            // Username
            // 
            this.Username.Location = new System.Drawing.Point(120, 24);
            this.Username.Name = "Username";
            this.Username.Size = new System.Drawing.Size(264, 20);
            this.Username.TabIndex = 7;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 48);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(53, 13);
            this.label2.TabIndex = 1;
            this.label2.Text = "Password";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(16, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(55, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Username";
            // 
            // UseCredentials
            // 
            this.UseCredentials.AutoSize = true;
            this.UseCredentials.Location = new System.Drawing.Point(32, 120);
            this.UseCredentials.Name = "UseCredentials";
            this.UseCredentials.Size = new System.Drawing.Size(143, 17);
            this.UseCredentials.TabIndex = 6;
            this.UseCredentials.Text = "Use alternate credentials";
            this.UseCredentials.UseVisualStyleBackColor = true;
            this.UseCredentials.CheckedChanged += new System.EventHandler(this.UseCredentials_CheckedChanged);
            // 
            // Folder
            // 
            this.Folder.Enabled = false;
            this.Folder.Location = new System.Drawing.Point(280, 48);
            this.Folder.Name = "Folder";
            this.Folder.Size = new System.Drawing.Size(144, 20);
            this.Folder.TabIndex = 7;
            // 
            // FolderLabel
            // 
            this.FolderLabel.AutoSize = true;
            this.FolderLabel.Enabled = false;
            this.FolderLabel.Location = new System.Drawing.Point(224, 48);
            this.FolderLabel.Name = "FolderLabel";
            this.FolderLabel.Size = new System.Drawing.Size(36, 13);
            this.FolderLabel.TabIndex = 8;
            this.FolderLabel.Text = "Folder";
            // 
            // folderBrowserDialog
            // 
            this.folderBrowserDialog.Description = "Select the folder where the backups will be stored";
            // 
            // FileOptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.FolderLabel);
            this.Controls.Add(this.Folder);
            this.Controls.Add(this.UseCredentials);
            this.Controls.Add(this.Credentials);
            this.Controls.Add(this.BrowseTargetFolder);
            this.Controls.Add(this.TargetDrive);
            this.Controls.Add(this.TargetFolder);
            this.Controls.Add(this.UseDisk);
            this.Controls.Add(this.UsePath);
            this.Name = "FileOptions";
            this.Size = new System.Drawing.Size(506, 242);
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
        private System.Windows.Forms.TextBox Password;
        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox UseCredentials;
        private System.Windows.Forms.TextBox Folder;
        private System.Windows.Forms.Label FolderLabel;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    }
}
