namespace Duplicati.Wizard_pages.Backends.S3
{
    partial class S3Options
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
            this.AWS_ID = new System.Windows.Forms.TextBox();
            this.AWS_KEY = new System.Windows.Forms.TextBox();
            this.BucketName = new System.Windows.Forms.TextBox();
            this.SignUpLink = new System.Windows.Forms.LinkLabel();
            this.label4 = new System.Windows.Forms.Label();
            this.UseEuroBuckets = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 8);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(84, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "AWS Access ID";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(24, 32);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(87, 13);
            this.label2.TabIndex = 1;
            this.label2.Text = "AWS Secret Key";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(24, 56);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(86, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "S3 Bucket name";
            // 
            // AWS_ID
            // 
            this.AWS_ID.Location = new System.Drawing.Point(136, 8);
            this.AWS_ID.Name = "AWS_ID";
            this.AWS_ID.Size = new System.Drawing.Size(144, 20);
            this.AWS_ID.TabIndex = 4;
            // 
            // AWS_KEY
            // 
            this.AWS_KEY.Location = new System.Drawing.Point(136, 32);
            this.AWS_KEY.Name = "AWS_KEY";
            this.AWS_KEY.Size = new System.Drawing.Size(144, 20);
            this.AWS_KEY.TabIndex = 5;
            // 
            // BucketName
            // 
            this.BucketName.Location = new System.Drawing.Point(136, 56);
            this.BucketName.Name = "BucketName";
            this.BucketName.Size = new System.Drawing.Size(144, 20);
            this.BucketName.TabIndex = 6;
            // 
            // SignUpLink
            // 
            this.SignUpLink.AutoSize = true;
            this.SignUpLink.Location = new System.Drawing.Point(296, 8);
            this.SignUpLink.Name = "SignUpLink";
            this.SignUpLink.Size = new System.Drawing.Size(151, 13);
            this.SignUpLink.TabIndex = 7;
            this.SignUpLink.TabStop = true;
            this.SignUpLink.Text = "Click here for the sign up page";
            this.SignUpLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.SignUpLink_LinkClicked);
            // 
            // label4
            // 
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(24, 80);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(440, 32);
            this.label4.TabIndex = 9;
            this.label4.Text = "A bucket can be thought of as a root folder. Use / in the bucket name, to access " +
                "subfolders in the bucket";
            // 
            // UseEuroBuckets
            // 
            this.UseEuroBuckets.AutoSize = true;
            this.UseEuroBuckets.Location = new System.Drawing.Point(24, 128);
            this.UseEuroBuckets.Name = "UseEuroBuckets";
            this.UseEuroBuckets.Size = new System.Drawing.Size(329, 17);
            this.UseEuroBuckets.TabIndex = 10;
            this.UseEuroBuckets.Text = "Use buckets on european server (advanced, not recommended)";
            this.UseEuroBuckets.UseVisualStyleBackColor = true;
            // 
            // S3Options
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.UseEuroBuckets);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.SignUpLink);
            this.Controls.Add(this.BucketName);
            this.Controls.Add(this.AWS_KEY);
            this.Controls.Add(this.AWS_ID);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Name = "S3Options";
            this.Size = new System.Drawing.Size(506, 242);
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
    }
}
