namespace Duplicati.GUI.Wizard_pages
{
    partial class SelectBackend
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
            this.File = new System.Windows.Forms.RadioButton();
            this.FTP = new System.Windows.Forms.RadioButton();
            this.SSH = new System.Windows.Forms.RadioButton();
            this.WebDAV = new System.Windows.Forms.RadioButton();
            this.S3 = new System.Windows.Forms.RadioButton();
            this.Question = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // File
            // 
            this.File.AutoSize = true;
            this.File.Location = new System.Drawing.Point(40, 48);
            this.File.Name = "File";
            this.File.Size = new System.Drawing.Size(179, 17);
            this.File.TabIndex = 0;
            this.File.TabStop = true;
            this.File.Text = "An external disk or network drive";
            this.File.UseVisualStyleBackColor = true;
            this.File.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // FTP
            // 
            this.FTP.AutoSize = true;
            this.FTP.Location = new System.Drawing.Point(40, 80);
            this.FTP.Name = "FTP";
            this.FTP.Size = new System.Drawing.Size(133, 17);
            this.FTP.TabIndex = 1;
            this.FTP.TabStop = true;
            this.FTP.Text = "FTP to a remote server";
            this.FTP.UseVisualStyleBackColor = true;
            this.FTP.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // SSH
            // 
            this.SSH.AutoSize = true;
            this.SSH.Location = new System.Drawing.Point(40, 112);
            this.SSH.Name = "SSH";
            this.SSH.Size = new System.Drawing.Size(171, 17);
            this.SSH.TabIndex = 2;
            this.SSH.TabStop = true;
            this.SSH.Text = "SSH (SFTP) to a remote server";
            this.SSH.UseVisualStyleBackColor = true;
            this.SSH.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // WebDAV
            // 
            this.WebDAV.AutoSize = true;
            this.WebDAV.Location = new System.Drawing.Point(40, 144);
            this.WebDAV.Name = "WebDAV";
            this.WebDAV.Size = new System.Drawing.Size(158, 17);
            this.WebDAV.TabIndex = 3;
            this.WebDAV.TabStop = true;
            this.WebDAV.Text = "WebDAV to a remote server";
            this.WebDAV.UseVisualStyleBackColor = true;
            this.WebDAV.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // S3
            // 
            this.S3.AutoSize = true;
            this.S3.Location = new System.Drawing.Point(40, 176);
            this.S3.Name = "S3";
            this.S3.Size = new System.Drawing.Size(117, 17);
            this.S3.TabIndex = 4;
            this.S3.TabStop = true;
            this.S3.Text = "Amazon S3 storage";
            this.S3.UseVisualStyleBackColor = true;
            this.S3.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // Question
            // 
            this.Question.AutoSize = true;
            this.Question.Location = new System.Drawing.Point(40, 16);
            this.Question.Name = "Question";
            this.Question.Size = new System.Drawing.Size(206, 13);
            this.Question.TabIndex = 5;
            this.Question.Text = "Where do you want to store the backups?";
            // 
            // SelectBackend
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Question);
            this.Controls.Add(this.S3);
            this.Controls.Add(this.WebDAV);
            this.Controls.Add(this.SSH);
            this.Controls.Add(this.FTP);
            this.Controls.Add(this.File);
            this.Name = "SelectBackend";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton File;
        private System.Windows.Forms.RadioButton FTP;
        private System.Windows.Forms.RadioButton SSH;
        private System.Windows.Forms.RadioButton WebDAV;
        private System.Windows.Forms.RadioButton S3;
        private System.Windows.Forms.Label Question;
    }
}
