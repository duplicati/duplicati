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
            this.TestConnection.Location = new System.Drawing.Point(296, 168);
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.Size = new System.Drawing.Size(112, 24);
            this.TestConnection.TabIndex = 21;
            this.TestConnection.Text = "Test connection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // Port
            // 
            this.Port.Location = new System.Drawing.Point(120, 112);
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
            22,
            0,
            0,
            0});
            this.Port.ValueChanged += new System.EventHandler(this.Port_ValueChanged);
            // 
            // Password
            // 
            this.Password.Location = new System.Drawing.Point(120, 88);
            this.Password.Name = "Password";
            this.Password.PasswordChar = '*';
            this.Password.Size = new System.Drawing.Size(288, 20);
            this.Password.TabIndex = 19;
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // Username
            // 
            this.Username.Location = new System.Drawing.Point(120, 64);
            this.Username.Name = "Username";
            this.Username.Size = new System.Drawing.Size(288, 20);
            this.Username.TabIndex = 18;
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // Path
            // 
            this.Path.Location = new System.Drawing.Point(120, 40);
            this.Path.Name = "Path";
            this.Path.Size = new System.Drawing.Size(288, 20);
            this.Path.TabIndex = 17;
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // Servername
            // 
            this.Servername.Location = new System.Drawing.Point(120, 16);
            this.Servername.Name = "Servername";
            this.Servername.Size = new System.Drawing.Size(288, 20);
            this.Servername.TabIndex = 16;
            this.Servername.TextChanged += new System.EventHandler(this.Servername_TextChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(16, 112);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(72, 13);
            this.label5.TabIndex = 15;
            this.label5.Text = "Port (optional)";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(16, 64);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(55, 13);
            this.label3.TabIndex = 13;
            this.label3.Text = "Username";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 40);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(29, 13);
            this.label2.TabIndex = 12;
            this.label2.Text = "Path";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(16, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(38, 13);
            this.label1.TabIndex = 11;
            this.label1.Text = "Server";
            // 
            // UsePassword
            // 
            this.UsePassword.AutoSize = true;
            this.UsePassword.Checked = true;
            this.UsePassword.CheckState = System.Windows.Forms.CheckState.Checked;
            this.UsePassword.Location = new System.Drawing.Point(20, 88);
            this.UsePassword.Name = "UsePassword";
            this.UsePassword.Size = new System.Drawing.Size(72, 17);
            this.UsePassword.TabIndex = 22;
            this.UsePassword.Text = "Password";
            this.UsePassword.UseVisualStyleBackColor = true;
            this.UsePassword.CheckedChanged += new System.EventHandler(this.UsePassword_CheckedChanged);
            // 
            // GenerateDebugOutput
            // 
            this.GenerateDebugOutput.AutoSize = true;
            this.GenerateDebugOutput.Location = new System.Drawing.Point(20, 144);
            this.GenerateDebugOutput.Name = "GenerateDebugOutput";
            this.GenerateDebugOutput.Size = new System.Drawing.Size(136, 17);
            this.GenerateDebugOutput.TabIndex = 23;
            this.GenerateDebugOutput.Text = "Generate debug output";
            this.GenerateDebugOutput.UseVisualStyleBackColor = true;
            // 
            // SSHOptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
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
            this.Size = new System.Drawing.Size(506, 242);
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
