namespace Duplicati.GUI.Wizard_pages.Backends.FTP
{
    partial class FTPOptions
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
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.Servername = new System.Windows.Forms.TextBox();
            this.Path = new System.Windows.Forms.TextBox();
            this.Username = new System.Windows.Forms.TextBox();
            this.Password = new System.Windows.Forms.TextBox();
            this.Port = new System.Windows.Forms.NumericUpDown();
            this.TestConnection = new System.Windows.Forms.Button();
            this.PassiveConnection = new System.Windows.Forms.CheckBox();
            this.CreateFolderButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.Port)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(16, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(38, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Server";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 40);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(29, 13);
            this.label2.TabIndex = 1;
            this.label2.Text = "Path";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(16, 64);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(55, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "Username";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(16, 88);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(53, 13);
            this.label4.TabIndex = 3;
            this.label4.Text = "Password";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(16, 112);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(72, 13);
            this.label5.TabIndex = 4;
            this.label5.Text = "Port (optional)";
            // 
            // Servername
            // 
            this.Servername.Location = new System.Drawing.Point(96, 16);
            this.Servername.Name = "Servername";
            this.Servername.Size = new System.Drawing.Size(288, 20);
            this.Servername.TabIndex = 5;
            this.Servername.TextChanged += new System.EventHandler(this.Servername_TextChanged);
            // 
            // Path
            // 
            this.Path.Location = new System.Drawing.Point(96, 40);
            this.Path.Name = "Path";
            this.Path.Size = new System.Drawing.Size(288, 20);
            this.Path.TabIndex = 6;
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // Username
            // 
            this.Username.Location = new System.Drawing.Point(96, 64);
            this.Username.Name = "Username";
            this.Username.Size = new System.Drawing.Size(288, 20);
            this.Username.TabIndex = 7;
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // Password
            // 
            this.Password.Location = new System.Drawing.Point(96, 88);
            this.Password.Name = "Password";
            this.Password.PasswordChar = '*';
            this.Password.Size = new System.Drawing.Size(288, 20);
            this.Password.TabIndex = 8;
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
            // 
            // Port
            // 
            this.Port.Location = new System.Drawing.Point(96, 112);
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
            this.Port.TabIndex = 9;
            this.Port.Value = new decimal(new int[] {
            21,
            0,
            0,
            0});
            this.Port.ValueChanged += new System.EventHandler(this.Port_ValueChanged);
            // 
            // TestConnection
            // 
            this.TestConnection.Location = new System.Drawing.Point(272, 160);
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.Size = new System.Drawing.Size(112, 24);
            this.TestConnection.TabIndex = 10;
            this.TestConnection.Text = "Test connection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // PassiveConnection
            // 
            this.PassiveConnection.AutoSize = true;
            this.PassiveConnection.Location = new System.Drawing.Point(16, 136);
            this.PassiveConnection.Name = "PassiveConnection";
            this.PassiveConnection.Size = new System.Drawing.Size(140, 17);
            this.PassiveConnection.TabIndex = 11;
            this.PassiveConnection.Text = "Use passive connection";
            this.PassiveConnection.UseVisualStyleBackColor = true;
            this.PassiveConnection.CheckedChanged += new System.EventHandler(this.PassiveConnection_CheckedChanged);
            // 
            // CreateFolderButton
            // 
            this.CreateFolderButton.Location = new System.Drawing.Point(392, 40);
            this.CreateFolderButton.Name = "CreateFolderButton";
            this.CreateFolderButton.Size = new System.Drawing.Size(88, 23);
            this.CreateFolderButton.TabIndex = 12;
            this.CreateFolderButton.Text = "Create folder";
            this.CreateFolderButton.UseVisualStyleBackColor = true;
            this.CreateFolderButton.Click += new System.EventHandler(this.CreateFolderButton_Click);
            // 
            // FTPOptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Username);
            this.Controls.Add(this.Password);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.CreateFolderButton);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.PassiveConnection);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.Port);
            this.Controls.Add(this.Path);
            this.Controls.Add(this.Servername);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Name = "FTPOptions";
            this.Size = new System.Drawing.Size(506, 242);
            ((System.ComponentModel.ISupportInitialize)(this.Port)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox Servername;
        private System.Windows.Forms.TextBox Path;
        private System.Windows.Forms.TextBox Username;
        private System.Windows.Forms.TextBox Password;
        private System.Windows.Forms.NumericUpDown Port;
        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.CheckBox PassiveConnection;
        private System.Windows.Forms.Button CreateFolderButton;
    }
}
