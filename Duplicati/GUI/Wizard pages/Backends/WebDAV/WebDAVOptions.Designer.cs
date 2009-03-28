namespace Duplicati.GUI.Wizard_pages.Backends.WebDAV
{
    partial class WebDAVOptions
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
            this.UseIntegratedAuth = new System.Windows.Forms.CheckBox();
            this.TestConnection = new System.Windows.Forms.Button();
            this.Username = new System.Windows.Forms.TextBox();
            this.Port = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.Path = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.Servername = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.PasswordSettings = new System.Windows.Forms.GroupBox();
            this.Password = new System.Windows.Forms.TextBox();
            this.CreateFolderButton = new System.Windows.Forms.Button();
            this.DigestAuth = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.Port)).BeginInit();
            this.PasswordSettings.SuspendLayout();
            this.SuspendLayout();
            // 
            // UseIntegratedAuth
            // 
            this.UseIntegratedAuth.AutoSize = true;
            this.UseIntegratedAuth.Location = new System.Drawing.Point(26, 72);
            this.UseIntegratedAuth.Name = "UseIntegratedAuth";
            this.UseIntegratedAuth.Size = new System.Drawing.Size(165, 17);
            this.UseIntegratedAuth.TabIndex = 24;
            this.UseIntegratedAuth.Text = "Use integrated authentication";
            this.UseIntegratedAuth.UseVisualStyleBackColor = true;
            this.UseIntegratedAuth.CheckedChanged += new System.EventHandler(this.UseIntegratedAuth_CheckedChanged);
            // 
            // TestConnection
            // 
            this.TestConnection.Location = new System.Drawing.Point(272, 192);
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.Size = new System.Drawing.Size(112, 24);
            this.TestConnection.TabIndex = 21;
            this.TestConnection.Text = "Test connection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // Username
            // 
            this.Username.Location = new System.Drawing.Point(88, 24);
            this.Username.Name = "Username";
            this.Username.Size = new System.Drawing.Size(280, 20);
            this.Username.TabIndex = 7;
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // Port
            // 
            this.Port.Location = new System.Drawing.Point(96, 160);
            this.Port.Maximum = new decimal(new int[] {
            65500,
            0,
            0,
            0});
            this.Port.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.Port.Name = "Port";
            this.Port.Size = new System.Drawing.Size(104, 20);
            this.Port.TabIndex = 20;
            this.Port.Value = new decimal(new int[] {
            80,
            0,
            0,
            0});
            this.Port.ValueChanged += new System.EventHandler(this.Port_ValueChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(16, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(38, 13);
            this.label1.TabIndex = 15;
            this.label1.Text = "Server";
            // 
            // Path
            // 
            this.Path.Location = new System.Drawing.Point(96, 40);
            this.Path.Name = "Path";
            this.Path.Size = new System.Drawing.Size(288, 20);
            this.Path.TabIndex = 19;
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(8, 48);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(53, 13);
            this.label4.TabIndex = 3;
            this.label4.Text = "Password";
            // 
            // Servername
            // 
            this.Servername.Location = new System.Drawing.Point(96, 16);
            this.Servername.Name = "Servername";
            this.Servername.Size = new System.Drawing.Size(288, 20);
            this.Servername.TabIndex = 18;
            this.Servername.TextChanged += new System.EventHandler(this.Servername_TextChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 24);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(55, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "Username";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(16, 160);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(72, 13);
            this.label5.TabIndex = 17;
            this.label5.Text = "Port (optional)";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 40);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(29, 13);
            this.label2.TabIndex = 16;
            this.label2.Text = "Path";
            // 
            // PasswordSettings
            // 
            this.PasswordSettings.Controls.Add(this.Username);
            this.PasswordSettings.Controls.Add(this.Password);
            this.PasswordSettings.Controls.Add(this.label4);
            this.PasswordSettings.Controls.Add(this.label3);
            this.PasswordSettings.Location = new System.Drawing.Point(16, 73);
            this.PasswordSettings.Name = "PasswordSettings";
            this.PasswordSettings.Size = new System.Drawing.Size(376, 71);
            this.PasswordSettings.TabIndex = 25;
            this.PasswordSettings.TabStop = false;
            // 
            // Password
            // 
            this.Password.Location = new System.Drawing.Point(88, 48);
            this.Password.Name = "Password";
            this.Password.PasswordChar = '*';
            this.Password.Size = new System.Drawing.Size(280, 20);
            this.Password.TabIndex = 8;
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // CreateFolderButton
            // 
            this.CreateFolderButton.Location = new System.Drawing.Point(392, 41);
            this.CreateFolderButton.Name = "CreateFolderButton";
            this.CreateFolderButton.Size = new System.Drawing.Size(88, 23);
            this.CreateFolderButton.TabIndex = 26;
            this.CreateFolderButton.Text = "Create folder";
            this.CreateFolderButton.UseVisualStyleBackColor = true;
            this.CreateFolderButton.Click += new System.EventHandler(this.CreateFolderButton_Click);
            // 
            // DigestAuth
            // 
            this.DigestAuth.AutoSize = true;
            this.DigestAuth.Location = new System.Drawing.Point(16, 184);
            this.DigestAuth.Name = "DigestAuth";
            this.DigestAuth.Size = new System.Drawing.Size(209, 17);
            this.DigestAuth.TabIndex = 27;
            this.DigestAuth.Text = "Autentication method must be \"Digest\"";
            this.DigestAuth.UseVisualStyleBackColor = true;
            this.DigestAuth.CheckedChanged += new System.EventHandler(this.DigestAuth_CheckedChanged);
            // 
            // WebDAVOptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.DigestAuth);
            this.Controls.Add(this.CreateFolderButton);
            this.Controls.Add(this.UseIntegratedAuth);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.Port);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Path);
            this.Controls.Add(this.Servername);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.PasswordSettings);
            this.Name = "WebDAVOptions";
            this.Size = new System.Drawing.Size(506, 242);
            ((System.ComponentModel.ISupportInitialize)(this.Port)).EndInit();
            this.PasswordSettings.ResumeLayout(false);
            this.PasswordSettings.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox UseIntegratedAuth;
        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.NumericUpDown Port;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Path;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox Servername;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.GroupBox PasswordSettings;
        private System.Windows.Forms.TextBox Password;
        private System.Windows.Forms.Button CreateFolderButton;
        private System.Windows.Forms.CheckBox DigestAuth;
    }
}
