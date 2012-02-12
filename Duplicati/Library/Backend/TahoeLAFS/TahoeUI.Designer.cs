namespace Duplicati.Library.Backend
{
    partial class TahoeUI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TahoeUI));
            this.TestConnection = new System.Windows.Forms.Button();
            this.Port = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.Path = new System.Windows.Forms.TextBox();
            this.Servername = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.CreateFolderButton = new System.Windows.Forms.Button();
            this.UseSSL = new System.Windows.Forms.CheckBox();
            this.AcceptAnyHash = new System.Windows.Forms.CheckBox();
            this.AcceptSpecifiedHash = new System.Windows.Forms.CheckBox();
            this.SpecifiedHash = new System.Windows.Forms.TextBox();
            this.SSLGroup = new System.Windows.Forms.GroupBox();
            ((System.ComponentModel.ISupportInitialize)(this.Port)).BeginInit();
            this.SSLGroup.SuspendLayout();
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
            3456,
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
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
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
            // AcceptAnyHash
            // 
            resources.ApplyResources(this.AcceptAnyHash, "AcceptAnyHash");
            this.AcceptAnyHash.Name = "AcceptAnyHash";
            this.AcceptAnyHash.UseVisualStyleBackColor = true;
            this.AcceptAnyHash.CheckedChanged += new System.EventHandler(this.AcceptAnyHash_CheckedChanged);
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
            this.SpecifiedHash.TextChanged += new System.EventHandler(this.SpecifiedHash_TextChanged);
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
            // TahoeUI
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.UseSSL);
            this.Controls.Add(this.SSLGroup);
            this.Controls.Add(this.CreateFolderButton);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.Port);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Path);
            this.Controls.Add(this.Servername);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label2);
            this.Name = "TahoeUI";
            this.Load += new System.EventHandler(this.TahoeUI_PageLoad);
            ((System.ComponentModel.ISupportInitialize)(this.Port)).EndInit();
            this.SSLGroup.ResumeLayout(false);
            this.SSLGroup.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.NumericUpDown Port;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox Path;
        private System.Windows.Forms.TextBox Servername;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button CreateFolderButton;
        private System.Windows.Forms.CheckBox UseSSL;
        private System.Windows.Forms.CheckBox AcceptAnyHash;
        private System.Windows.Forms.CheckBox AcceptSpecifiedHash;
        private System.Windows.Forms.TextBox SpecifiedHash;
        private System.Windows.Forms.GroupBox SSLGroup;
    }
}
