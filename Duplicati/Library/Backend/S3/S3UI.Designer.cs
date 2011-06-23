namespace Duplicati.Library.Backend
{
    partial class S3UI
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(S3UI));
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.AWS_KEY = new Duplicati.Winforms.Controls.PasswordControl();
            this.BucketName = new System.Windows.Forms.TextBox();
            this.SignUpLink = new System.Windows.Forms.LinkLabel();
            this.label4 = new System.Windows.Forms.Label();
            this.TestConnection = new System.Windows.Forms.Button();
            this.CreateBucket = new System.Windows.Forms.Button();
            this.AWS_ID = new System.Windows.Forms.ComboBox();
            this.UseRRS = new System.Windows.Forms.CheckBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.Bucketregions = new System.Windows.Forms.ComboBox();
            this.Servernames = new System.Windows.Forms.ComboBox();
            this.label7 = new System.Windows.Forms.Label();
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
            // AWS_KEY
            // 
            this.AWS_KEY.AskToEnterNewPassword = false;
            this.AWS_KEY.InitialPassword = null;
            this.AWS_KEY.IsPasswordVisible = false;
            resources.ApplyResources(this.AWS_KEY, "AWS_KEY");
            this.AWS_KEY.MaximumSize = new System.Drawing.Size(5000, 20);
            this.AWS_KEY.MinimumSize = new System.Drawing.Size(150, 20);
            this.AWS_KEY.Name = "AWS_KEY";
            this.AWS_KEY.TextChanged += new System.EventHandler(this.AWS_KEY_TextChanged);
            // 
            // BucketName
            // 
            resources.ApplyResources(this.BucketName, "BucketName");
            this.BucketName.Name = "BucketName";
            this.BucketName.TextChanged += new System.EventHandler(this.BucketName_TextChanged);
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
            // CreateBucket
            // 
            resources.ApplyResources(this.CreateBucket, "CreateBucket");
            this.CreateBucket.Name = "CreateBucket";
            this.CreateBucket.UseVisualStyleBackColor = true;
            this.CreateBucket.Click += new System.EventHandler(this.CreateBucket_Click);
            // 
            // AWS_ID
            // 
            this.AWS_ID.FormattingEnabled = true;
            resources.ApplyResources(this.AWS_ID, "AWS_ID");
            this.AWS_ID.Name = "AWS_ID";
            this.AWS_ID.SelectedIndexChanged += new System.EventHandler(this.AWS_ID_SelectedIndexChanged);
            this.AWS_ID.TextChanged += new System.EventHandler(this.AWS_ID_TextChanged);
            // 
            // UseRRS
            // 
            resources.ApplyResources(this.UseRRS, "UseRRS");
            this.UseRRS.Name = "UseRRS";
            this.UseRRS.UseVisualStyleBackColor = true;
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // Bucketregions
            // 
            this.Bucketregions.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.Bucketregions.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.Bucketregions.FormattingEnabled = true;
            resources.ApplyResources(this.Bucketregions, "Bucketregions");
            this.Bucketregions.Name = "Bucketregions";
            this.Bucketregions.SelectedIndexChanged += new System.EventHandler(this.Bucketregions_SelectedIndexChanged);
            // 
            // Servernames
            // 
            this.Servernames.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.Servernames.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.Servernames.FormattingEnabled = true;
            resources.ApplyResources(this.Servernames, "Servernames");
            this.Servernames.Name = "Servernames";
            this.Servernames.SelectedIndexChanged += new System.EventHandler(this.Servernames_SelectedIndexChanged);
            this.Servernames.TextChanged += new System.EventHandler(this.Servernames_TextChanged);
            // 
            // label7
            // 
            resources.ApplyResources(this.label7, "label7");
            this.label7.Name = "label7";
            // 
            // S3UI
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Servernames);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.Bucketregions);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.UseRRS);
            this.Controls.Add(this.AWS_ID);
            this.Controls.Add(this.CreateBucket);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.SignUpLink);
            this.Controls.Add(this.BucketName);
            this.Controls.Add(this.AWS_KEY);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Name = "S3UI";
            this.Load += new System.EventHandler(this.S3UI_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private Duplicati.Winforms.Controls.PasswordControl AWS_KEY;
        private System.Windows.Forms.TextBox BucketName;
        private System.Windows.Forms.LinkLabel SignUpLink;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.Button CreateBucket;
        private System.Windows.Forms.ComboBox AWS_ID;
        private System.Windows.Forms.CheckBox UseRRS;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.ComboBox Bucketregions;
        private System.Windows.Forms.ComboBox Servernames;
        private System.Windows.Forms.Label label7;
    }
}
