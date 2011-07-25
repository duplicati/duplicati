namespace Duplicati.Library.Backend
{
    partial class SkyDriveUI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SkyDriveUI));
            this.CreateFolderButton = new System.Windows.Forms.Button();
            this.TestConnection = new System.Windows.Forms.Button();
            this.SignUpLink = new System.Windows.Forms.LinkLabel();
            this.Path = new System.Windows.Forms.TextBox();
            this.Password = new Duplicati.Winforms.Controls.PasswordControl();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.Username = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // CreateFolderButton
            // 
            resources.ApplyResources(this.CreateFolderButton, "CreateFolderButton");
            this.CreateFolderButton.Name = "CreateFolderButton";
            this.CreateFolderButton.UseVisualStyleBackColor = true;
            this.CreateFolderButton.Click += new System.EventHandler(this.CreateFolderButton_Click);
            // 
            // TestConnection
            // 
            resources.ApplyResources(this.TestConnection, "TestConnection");
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // SignUpLink
            // 
            resources.ApplyResources(this.SignUpLink, "SignUpLink");
            this.SignUpLink.Name = "SignUpLink";
            this.SignUpLink.TabStop = true;
            this.SignUpLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.SignUpLink_LinkClicked);
            // 
            // Path
            // 
            resources.ApplyResources(this.Path, "Path");
            this.Path.Name = "Path";
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // Password
            // 
            this.Password.AskToEnterNewPassword = false;
            this.Password.InitialPassword = null;
            this.Password.IsPasswordVisible = false;
            resources.ApplyResources(this.Password, "Password");
            this.Password.MaximumSize = new System.Drawing.Size(5000, 20);
            this.Password.MinimumSize = new System.Drawing.Size(150, 20);
            this.Password.Name = "Password";
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
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
            // Username
            // 
            resources.ApplyResources(this.Username, "Username");
            this.Username.Name = "Username";
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // SkyDriveUI
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label4);
            this.Controls.Add(this.Username);
            this.Controls.Add(this.CreateFolderButton);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.SignUpLink);
            this.Controls.Add(this.Path);
            this.Controls.Add(this.Password);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Name = "SkyDriveUI";
            this.Load += new System.EventHandler(this.SkyDriveUI_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CreateFolderButton;
        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.LinkLabel SignUpLink;
        private System.Windows.Forms.TextBox Path;
        private Duplicati.Winforms.Controls.PasswordControl Password;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.Label label4;
    }
}
