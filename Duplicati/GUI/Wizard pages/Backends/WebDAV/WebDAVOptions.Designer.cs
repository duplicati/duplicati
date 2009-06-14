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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WebDAVOptions));
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
            resources.ApplyResources(this.UseIntegratedAuth, "UseIntegratedAuth");
            this.UseIntegratedAuth.Name = "UseIntegratedAuth";
            this.UseIntegratedAuth.UseVisualStyleBackColor = true;
            this.UseIntegratedAuth.CheckedChanged += new System.EventHandler(this.UseIntegratedAuth_CheckedChanged);
            // 
            // TestConnection
            // 
            resources.ApplyResources(this.TestConnection, "TestConnection");
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // Username
            // 
            resources.ApplyResources(this.Username, "Username");
            this.Username.Name = "Username";
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // Port
            // 
            resources.ApplyResources(this.Port, "Port");
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
            this.Port.Value = new decimal(new int[] {
            80,
            0,
            0,
            0});
            this.Port.ValueChanged += new System.EventHandler(this.Port_ValueChanged);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // Path
            // 
            resources.ApplyResources(this.Path, "Path");
            this.Path.Name = "Path";
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // Servername
            // 
            resources.ApplyResources(this.Servername, "Servername");
            this.Servername.Name = "Servername";
            this.Servername.TextChanged += new System.EventHandler(this.Servername_TextChanged);
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // PasswordSettings
            // 
            this.PasswordSettings.Controls.Add(this.Username);
            this.PasswordSettings.Controls.Add(this.Password);
            this.PasswordSettings.Controls.Add(this.label4);
            this.PasswordSettings.Controls.Add(this.label3);
            resources.ApplyResources(this.PasswordSettings, "PasswordSettings");
            this.PasswordSettings.Name = "PasswordSettings";
            this.PasswordSettings.TabStop = false;
            // 
            // Password
            // 
            resources.ApplyResources(this.Password, "Password");
            this.Password.Name = "Password";
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // CreateFolderButton
            // 
            resources.ApplyResources(this.CreateFolderButton, "CreateFolderButton");
            this.CreateFolderButton.Name = "CreateFolderButton";
            this.CreateFolderButton.UseVisualStyleBackColor = true;
            this.CreateFolderButton.Click += new System.EventHandler(this.CreateFolderButton_Click);
            // 
            // DigestAuth
            // 
            resources.ApplyResources(this.DigestAuth, "DigestAuth");
            this.DigestAuth.Name = "DigestAuth";
            this.DigestAuth.UseVisualStyleBackColor = true;
            this.DigestAuth.CheckedChanged += new System.EventHandler(this.DigestAuth_CheckedChanged);
            // 
            // WebDAVOptions
            // 
            resources.ApplyResources(this, "$this");
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
