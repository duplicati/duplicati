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
            this.AWS_ID = new System.Windows.Forms.TextBox();
            this.AWS_KEY = new System.Windows.Forms.TextBox();
            this.BucketName = new System.Windows.Forms.TextBox();
            this.SignUpLink = new System.Windows.Forms.LinkLabel();
            this.label4 = new System.Windows.Forms.Label();
            this.UseEuroBuckets = new System.Windows.Forms.CheckBox();
            this.TestConnection = new System.Windows.Forms.Button();
            this.CreateBucket = new System.Windows.Forms.Button();
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
            // AWS_ID
            // 
            resources.ApplyResources(this.AWS_ID, "AWS_ID");
            this.AWS_ID.Name = "AWS_ID";
            this.AWS_ID.TextChanged += new System.EventHandler(this.AWS_ID_TextChanged);
            // 
            // AWS_KEY
            // 
            resources.ApplyResources(this.AWS_KEY, "AWS_KEY");
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
            // UseEuroBuckets
            // 
            resources.ApplyResources(this.UseEuroBuckets, "UseEuroBuckets");
            this.UseEuroBuckets.Name = "UseEuroBuckets";
            this.UseEuroBuckets.UseVisualStyleBackColor = true;
            this.UseEuroBuckets.CheckedChanged += new System.EventHandler(this.UseEuroBuckets_CheckedChanged);
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
            // S3UI
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.CreateBucket);
            this.Controls.Add(this.TestConnection);
            this.Controls.Add(this.UseEuroBuckets);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.SignUpLink);
            this.Controls.Add(this.BucketName);
            this.Controls.Add(this.AWS_KEY);
            this.Controls.Add(this.AWS_ID);
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
        private System.Windows.Forms.TextBox AWS_ID;
        private System.Windows.Forms.TextBox AWS_KEY;
        private System.Windows.Forms.TextBox BucketName;
        private System.Windows.Forms.LinkLabel SignUpLink;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.CheckBox UseEuroBuckets;
        private System.Windows.Forms.Button TestConnection;
        private System.Windows.Forms.Button CreateBucket;
    }
}
