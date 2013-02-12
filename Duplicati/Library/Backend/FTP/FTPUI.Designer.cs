namespace Duplicati.Library.Backend
{
    partial class FTPUI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FTPUI));
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.Servername = new System.Windows.Forms.TextBox();
            this.Path = new System.Windows.Forms.TextBox();
            this.Username = new System.Windows.Forms.TextBox();
            this.Password = new Duplicati.Winforms.Controls.PasswordControl();
            this.Port = new System.Windows.Forms.NumericUpDown();
            this.TestConnection = new System.Windows.Forms.Button();
            this.PassiveConnection = new System.Windows.Forms.CheckBox();
            this.CreateFolderButton = new System.Windows.Forms.Button();
            this.UseSSL = new System.Windows.Forms.CheckBox();
            this.SSLGroup = new System.Windows.Forms.GroupBox();
            this.AcceptSpecifiedHash = new System.Windows.Forms.CheckBox();
            this.SpecifiedHash = new System.Windows.Forms.TextBox();
            this.AcceptAnyHash = new System.Windows.Forms.CheckBox();
            this.label6 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.Port)).BeginInit();
            this.SSLGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // Servername
            // 
            resources.ApplyResources(this.Servername, "Servername");
            this.Servername.Name = "Servername";
            this.Servername.TextChanged += new System.EventHandler(this.Servername_TextChanged);
            // 
            // Path
            // 
            resources.ApplyResources(this.Path, "Path");
            this.Path.Name = "Path";
            this.Path.TextChanged += new System.EventHandler(this.Path_TextChanged);
            // 
            // Username
            // 
            resources.ApplyResources(this.Username, "Username");
            this.Username.Name = "Username";
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // Password
            // 
            resources.ApplyResources(this.Password, "Password");
            this.Password.Name = "Password";
            this.Password.TextChanged += new System.EventHandler(this.Password_TextChanged);
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
            21,
            0,
            0,
            0});
            this.Port.ValueChanged += new System.EventHandler(this.Port_ValueChanged);
            // 
            // TestConnection
            // 
            resources.ApplyResources(this.TestConnection, "TestConnection");
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // PassiveConnection
            // 
            resources.ApplyResources(this.PassiveConnection, "PassiveConnection");
            this.PassiveConnection.Name = "PassiveConnection";
            this.PassiveConnection.UseVisualStyleBackColor = true;
            this.PassiveConnection.CheckedChanged += new System.EventHandler(this.PassiveConnection_CheckedChanged);
            // 
            // CreateFolderButton
            // 
            resources.ApplyResources(this.CreateFolderButton, "CreateFolderButton");
            this.CreateFolderButton.Name = "CreateFolderButton";
            this.CreateFolderButton.UseVisualStyleBackColor = true;
            this.CreateFolderButton.Click += new System.EventHandler(this.CreateFolderButton_Click);
            // 
            // UseSSL
            // 
            resources.ApplyResources(this.UseSSL, "UseSSL");
            this.UseSSL.Name = "UseSSL";
            this.UseSSL.UseVisualStyleBackColor = true;
            this.UseSSL.CheckedChanged += new System.EventHandler(this.UseSSL_CheckedChanged);
            // 
            // SSLGroup
            // 
            this.SSLGroup.Controls.Add(this.AcceptSpecifiedHash);
            this.SSLGroup.Controls.Add(this.SpecifiedHash);
            this.SSLGroup.Controls.Add(this.AcceptAnyHash);
            resources.ApplyResources(this.SSLGroup, "SSLGroup");
            this.SSLGroup.Name = "SSLGroup";
            this.SSLGroup.TabStop = false;
            // 
            // AcceptSpecifiedHash
            // 
            resources.ApplyResources(this.AcceptSpecifiedHash, "AcceptSpecifiedHash");
            this.AcceptSpecifiedHash.Name = "AcceptSpecifiedHash";
            this.AcceptSpecifiedHash.UseVisualStyleBackColor = true;
            this.AcceptSpecifiedHash.CheckedChanged += new System.EventHandler(this.AcceptSpecifiedHash_CheckedChanged);
            // 
            // SpecifiedHash
            // 
            resources.ApplyResources(this.SpecifiedHash, "SpecifiedHash");
            this.SpecifiedHash.Name = "SpecifiedHash";
            // 
            // AcceptAnyHash
            // 
            resources.ApplyResources(this.AcceptAnyHash, "AcceptAnyHash");
            this.AcceptAnyHash.Name = "AcceptAnyHash";
            this.AcceptAnyHash.UseVisualStyleBackColor = true;
            this.AcceptAnyHash.CheckedChanged += new System.EventHandler(this.AcceptAnyHash_CheckedChanged);
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // FTPUI
            // 
            this.Controls.Add(this.label6);
            this.Controls.Add(this.UseSSL);
            this.Controls.Add(this.SSLGroup);
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
            this.Name = "FTPUI";
            resources.ApplyResources(this, "$this");
            this.Load += new System.EventHandler(this.FTPUI_Load);
            ((System.ComponentModel.ISupportInitialize)(this.Port)).EndInit();
            this.SSLGroup.ResumeLayout(false);
            this.SSLGroup.PerformLayout();
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
        private Duplicati.Winforms.Controls.PasswordControl Password;
        private System.Windows.Forms.NumericUpDown Port;
        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.CheckBox PassiveConnection;
        private System.Windows.Forms.Button CreateFolderButton;
        private System.Windows.Forms.CheckBox UseSSL;
        private System.Windows.Forms.GroupBox SSLGroup;
        private System.Windows.Forms.CheckBox AcceptSpecifiedHash;
        private System.Windows.Forms.TextBox SpecifiedHash;
        private System.Windows.Forms.CheckBox AcceptAnyHash;
        private System.Windows.Forms.Label label6;
    }
}
