namespace Duplicati.GUI.Wizard_pages.Backends.SSH
{
    partial class SSHOptions
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SSHOptions));
            this.TestConnection = new System.Windows.Forms.Button();
            this.Port = new System.Windows.Forms.NumericUpDown();
            this.Password = new System.Windows.Forms.TextBox();
            this.Username = new System.Windows.Forms.TextBox();
            this.Path = new System.Windows.Forms.TextBox();
            this.Servername = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.UsePassword = new System.Windows.Forms.CheckBox();
            this.GenerateDebugOutput = new System.Windows.Forms.CheckBox();
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
            22,
            0,
            0,
            0});
            this.Port.ValueChanged += new System.EventHandler(this.Port_ValueChanged);
            // 
            // Password
            // 
            resources.ApplyResources(this.Password, "Password");
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
            // SSHOptions
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
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
            this.Name = "SSHOptions";
            ((System.ComponentModel.ISupportInitialize)(this.Port)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.NumericUpDown Port;
        private System.Windows.Forms.TextBox Password;
        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.TextBox Path;
        private System.Windows.Forms.TextBox Servername;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox UsePassword;
        private System.Windows.Forms.CheckBox GenerateDebugOutput;
    }
}
