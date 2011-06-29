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
            this.CreateFolderButton.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.CreateFolderButton.Location = new System.Drawing.Point(362, 79);
            this.CreateFolderButton.Name = "CreateFolderButton";
            this.CreateFolderButton.Size = new System.Drawing.Size(112, 24);
            this.CreateFolderButton.TabIndex = 25;
            this.CreateFolderButton.Text = "Create folder";
            this.CreateFolderButton.UseVisualStyleBackColor = true;
            this.CreateFolderButton.Click += new System.EventHandler(this.CreateFolderButton_Click);
            // 
            // TestConnection
            // 
            this.TestConnection.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.TestConnection.Location = new System.Drawing.Point(360, 160);
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.Size = new System.Drawing.Size(112, 24);
            this.TestConnection.TabIndex = 26;
            this.TestConnection.Text = "Test Connection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // SignUpLink
            // 
            this.SignUpLink.AutoSize = true;
            this.SignUpLink.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.SignUpLink.Location = new System.Drawing.Point(330, 39);
            this.SignUpLink.Name = "SignUpLink";
            this.SignUpLink.Size = new System.Drawing.Size(151, 13);
            this.SignUpLink.TabIndex = 24;
            this.SignUpLink.TabStop = true;
            this.SignUpLink.Text = "Click here for the sign up page";
            this.SignUpLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.SignUpLink_LinkClicked);
            // 
            // Path
            // 
            this.Path.Location = new System.Drawing.Point(136, 80);
            this.Path.Name = "Path";
            this.Path.Size = new System.Drawing.Size(152, 20);
            this.Path.TabIndex = 22;
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // Password
            // 
            this.Password.AskToEnterNewPassword = false;
            this.Password.InitialPassword = null;
            this.Password.IsPasswordVisible = false;
            this.Password.Location = new System.Drawing.Point(136, 56);
            this.Password.MaximumSize = new System.Drawing.Size(5000, 20);
            this.Password.MinimumSize = new System.Drawing.Size(150, 20);
            this.Password.Name = "Password";
            this.Password.Size = new System.Drawing.Size(152, 20);
            this.Password.TabIndex = 20;
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label3.Location = new System.Drawing.Point(24, 80);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(103, 13);
            this.label3.TabIndex = 21;
            this.label3.Text = "SkyDrive folder path";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label2.Location = new System.Drawing.Point(24, 56);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(97, 13);
            this.label2.TabIndex = 19;
            this.label2.Text = "Passport Password";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label1.Location = new System.Drawing.Point(24, 32);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(99, 13);
            this.label1.TabIndex = 17;
            this.label1.Text = "Passport Username";
            // 
            // Username
            // 
            this.Username.Location = new System.Drawing.Point(136, 32);
            this.Username.Name = "Username";
            this.Username.Size = new System.Drawing.Size(152, 20);
            this.Username.TabIndex = 27;
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // label4
            // 
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(24, 144);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(304, 48);
            this.label4.TabIndex = 28;
            this.label4.Text = "Please note that there is no official API and accessing SkyDrive through this bac" +
                "kend may violate the ToS and may stop to work if Microsoft decides to change the" +
                " API.";
            // 
            // SkyDriveUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
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
            this.Size = new System.Drawing.Size(506, 242);
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
