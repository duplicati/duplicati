namespace Duplicati.Library.Backend
{
    partial class CloudFilesUI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CloudFilesUI));
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.Username = new System.Windows.Forms.TextBox();
            this.API_KEY = new Duplicati.Winforms.Controls.PasswordControl();
            this.ContainerName = new System.Windows.Forms.TextBox();
            this.SignUpLink = new System.Windows.Forms.LinkLabel();
            this.label4 = new System.Windows.Forms.Label();
            this.TestConnection = new System.Windows.Forms.Button();
            this.CreateContainer = new System.Windows.Forms.Button();
            this.label5_2 = new System.Windows.Forms.Label();
            this.Servernames = new System.Windows.Forms.ComboBox();
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
            // Username
            // 
            resources.ApplyResources(this.Username, "Username");
            this.Username.Name = "Username";
            this.Username.TextChanged += new System.EventHandler(this.Username_TextChanged);
            // 
            // API_KEY
            // 
            this.API_KEY.AskToEnterNewPassword = false;
            this.API_KEY.InitialPassword = null;
            this.API_KEY.IsPasswordVisible = false;
            resources.ApplyResources(this.API_KEY, "API_KEY");
            this.API_KEY.MaximumSize = new System.Drawing.Size(5000, 20);
            this.API_KEY.MinimumSize = new System.Drawing.Size(150, 20);
            this.API_KEY.Name = "API_KEY";
            this.API_KEY.TextChanged += new System.EventHandler(this.API_KEY_TextChanged);
            // 
            // ContainerName
            // 
            resources.ApplyResources(this.ContainerName, "ContainerName");
            this.ContainerName.Name = "ContainerName";
            this.ContainerName.TextChanged += new System.EventHandler(this.ContainerName_TextChanged);
            // 
            // SignUpLink
            // 
            resources.ApplyResources(this.SignUpLink, "SignUpLink");
            this.SignUpLink.Name = "SignUpLink";
            this.SignUpLink.TabStop = true;
            this.SignUpLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.SignUpLink_LinkClicked);
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // TestConnection
            // 
            resources.ApplyResources(this.TestConnection, "TestConnection");
            this.TestConnection.Name = "TestConnection";
            this.TestConnection.UseVisualStyleBackColor = true;
            this.TestConnection.Click += new System.EventHandler(this.TestConnection_Click);
            // 
            // CreateContainer
            // 
            resources.ApplyResources(this.CreateContainer, "CreateContainer");
            this.CreateContainer.Name = "CreateContainer";
            this.CreateContainer.UseVisualStyleBackColor = true;
            this.CreateContainer.Click += new System.EventHandler(this.CreateContainer_Click);
            // 
            // label5_2
            // 
            resources.ApplyResources(this.label5_2, "label5_2");
            this.label5_2.Name = "label5_2";
            // 
            // Servernames
            // 
            this.Servernames.FormattingEnabled = true;
            resources.ApplyResources(this.Servernames, "Servernames");
            this.Servernames.Name = "Servernames";
            this.Servernames.SelectedIndexChanged += new System.EventHandler(this.Servernames_SelectedIndexChanged);
            this.Servernames.TextChanged += new System.EventHandler(this.Servernames_TextChanged);
            // 
            // CloudFilesUI
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Servernames);
            this.Controls.Add(this.label5_2);
            this.Controls.Add(this.CreateContainer);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.SignUpLink);
            this.Controls.Add(this.ContainerName);
            this.Controls.Add(this.API_KEY);
            this.Controls.Add(this.Username);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Name = "CloudFilesUI";
            this.Load += new System.EventHandler(this.CloudFilesUI_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox Username;
        private Duplicati.Winforms.Controls.PasswordControl API_KEY;
        private System.Windows.Forms.TextBox ContainerName;
        private System.Windows.Forms.LinkLabel SignUpLink;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.Button CreateContainer;
        private System.Windows.Forms.Label label5_2;
        private System.Windows.Forms.ComboBox Servernames;
    }
}
