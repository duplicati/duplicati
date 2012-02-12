namespace Duplicati.Library.Backend
{
    partial class SSHUI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SSHUI));
            this.TestConnection = new System.Windows.Forms.Button();
            this.Port = new System.Windows.Forms.NumericUpDown();
            this.Password = new Duplicati.Winforms.Controls.PasswordControl();
            this.Username = new System.Windows.Forms.TextBox();
            this.Path = new System.Windows.Forms.TextBox();
            this.Servername = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.UsePassword = new System.Windows.Forms.CheckBox();
            this.GenerateDebugOutput = new System.Windows.Forms.CheckBox();
            this.UseUnmanagedSSH = new System.Windows.Forms.CheckBox();
            this.CreateFolderButton = new System.Windows.Forms.Button();
            this.Keyfile = new System.Windows.Forms.TextBox();
            this.Keyfilelabel = new System.Windows.Forms.Label();
            this.BrowseForKeyFileButton = new System.Windows.Forms.Button();
            this.OpenSSHKeyFileDialog = new System.Windows.Forms.OpenFileDialog();
            ((System.ComponentModel.ISupportInitialize)(this.Port)).BeginInit();
            this.SuspendLayout();
            // 
            // TestConnection
            // 
            resources.ApplyResources(this.TestConnection, "TestConnection");
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // Port
            // 
            resources.ApplyResources(this.Port, "Port");
            this.Port.Maximum = new decimal(new int[] {
            65535,
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
            22,
            0,
            0,
            0});
            this.Port.ValueChanged += new System.EventHandler(this.Port_ValueChanged);
            // 
            // Password
            // 
            this.Password.AskToEnterNewPassword = false;
            this.Password.IsPasswordVisible = false;
            resources.ApplyResources(this.Password, "Password");
            this.Password.MaximumSize = new System.Drawing.Size(5000, 20);
            this.Password.MinimumSize = new System.Drawing.Size(150, 20);
            this.Password.Name = "Password";
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // Username
            // 
            resources.ApplyResources(this.Username, "Username");
            this.Username.Name = "Username";
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // Path
            // 
            resources.ApplyResources(this.Path, "Path");
            this.Path.Name = "Path";
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // Servername
            // 
            resources.ApplyResources(this.Servername, "Servername");
            this.Servername.Name = "Servername";
            this.Servername.TextChanged += new System.EventHandler(this.Servername_TextChanged);
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
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
            // UsePassword
            // 
            resources.ApplyResources(this.UsePassword, "UsePassword");
            this.UsePassword.Checked = true;
            this.UsePassword.CheckState = System.Windows.Forms.CheckState.Checked;
            this.UsePassword.Name = "UsePassword";
            this.UsePassword.UseVisualStyleBackColor = true;
            this.UsePassword.CheckedChanged += new System.EventHandler(this.UsePassword_CheckedChanged);
            // 
            // GenerateDebugOutput
            // 
            resources.ApplyResources(this.GenerateDebugOutput, "GenerateDebugOutput");
            this.GenerateDebugOutput.Name = "GenerateDebugOutput";
            this.GenerateDebugOutput.UseVisualStyleBackColor = true;
            // 
            // UseUnmanagedSSH
            // 
            resources.ApplyResources(this.UseUnmanagedSSH, "UseUnmanagedSSH");
            this.UseUnmanagedSSH.Name = "UseUnmanagedSSH";
            this.UseUnmanagedSSH.UseVisualStyleBackColor = true;
            this.UseUnmanagedSSH.CheckedChanged += new System.EventHandler(this.UseUnmanagedSSH_CheckedChanged);
            // 
            // CreateFolderButton
            // 
            resources.ApplyResources(this.CreateFolderButton, "CreateFolderButton");
            this.CreateFolderButton.Name = "CreateFolderButton";
            this.CreateFolderButton.UseVisualStyleBackColor = true;
            this.CreateFolderButton.Click += new System.EventHandler(this.CreateFolderButton_Click);
            // 
            // Keyfile
            // 
            resources.ApplyResources(this.Keyfile, "Keyfile");
            this.Keyfile.Name = "Keyfile";
            this.Keyfile.TextChanged += new System.EventHandler(this.Keyfile_TextChanged);
            // 
            // Keyfilelabel
            // 
            resources.ApplyResources(this.Keyfilelabel, "Keyfilelabel");
            this.Keyfilelabel.Name = "Keyfilelabel";
            // 
            // BrowseForKeyFileButton
            // 
            resources.ApplyResources(this.BrowseForKeyFileButton, "BrowseForKeyFileButton");
            this.BrowseForKeyFileButton.Name = "BrowseForKeyFileButton";
            this.BrowseForKeyFileButton.UseVisualStyleBackColor = true;
            this.BrowseForKeyFileButton.Click += new System.EventHandler(this.BrowseForKeyFileButton_Click);
            // 
            // OpenSSHKeyFileDialog
            // 
            resources.ApplyResources(this.OpenSSHKeyFileDialog, "OpenSSHKeyFileDialog");
            // 
            // SSHUI
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.BrowseForKeyFileButton);
            this.Controls.Add(this.Keyfile);
            this.Controls.Add(this.Keyfilelabel);
            this.Controls.Add(this.CreateFolderButton);
            this.Controls.Add(this.UseUnmanagedSSH);
            this.Controls.Add(this.GenerateDebugOutput);
            this.Controls.Add(this.UsePassword);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.Port);
            this.Controls.Add(this.Password);
            this.Controls.Add(this.Username);
            this.Controls.Add(this.Path);
            this.Controls.Add(this.Servername);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Name = "SSHUI";
            this.Load += new System.EventHandler(this.SSHUI_PageLoad);
            ((System.ComponentModel.ISupportInitialize)(this.Port)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.NumericUpDown Port;
        private Duplicati.Winforms.Controls.PasswordControl Password;
        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.TextBox Path;
        private System.Windows.Forms.TextBox Servername;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox UsePassword;
        private System.Windows.Forms.CheckBox GenerateDebugOutput;
        private System.Windows.Forms.CheckBox UseUnmanagedSSH;
        private System.Windows.Forms.Button CreateFolderButton;
        private System.Windows.Forms.TextBox Keyfile;
        private System.Windows.Forms.Label Keyfilelabel;
        private System.Windows.Forms.Button BrowseForKeyFileButton;
        private System.Windows.Forms.OpenFileDialog OpenSSHKeyFileDialog;
    }
}
