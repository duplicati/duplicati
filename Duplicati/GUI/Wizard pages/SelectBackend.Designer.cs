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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectBackend));
            this.File = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.FTP = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.SSH = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.WebDAV = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.S3 = new System.Windows.Forms.Wizard.DoubleClickRadioButton();
            this.Question = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // File
            // 
            resources.ApplyResources(this.File, "File");
            this.File.Name = "File";
            this.File.TabStop = true;
            this.File.UseVisualStyleBackColor = true;
            this.File.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.File.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // FTP
            // 
            resources.ApplyResources(this.FTP, "FTP");
            this.FTP.Name = "FTP";
            this.FTP.TabStop = true;
            this.FTP.UseVisualStyleBackColor = true;
            this.FTP.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.FTP.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // SSH
            // 
            resources.ApplyResources(this.SSH, "SSH");
            this.SSH.Name = "SSH";
            this.SSH.TabStop = true;
            this.SSH.UseVisualStyleBackColor = true;
            this.SSH.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.SSH.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // WebDAV
            // 
            resources.ApplyResources(this.WebDAV, "WebDAV");
            this.WebDAV.Name = "WebDAV";
            this.WebDAV.TabStop = true;
            this.WebDAV.UseVisualStyleBackColor = true;
            this.WebDAV.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.WebDAV.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // S3
            // 
            resources.ApplyResources(this.S3, "S3");
            this.S3.Name = "S3";
            this.S3.TabStop = true;
            this.S3.UseVisualStyleBackColor = true;
            this.S3.DoubleClick += new System.EventHandler(this.RadioButton_DoubleClick);
            this.S3.CheckedChanged += new System.EventHandler(this.Item_CheckChanged);
            // 
            // Question
            // 
            resources.ApplyResources(this.Question, "Question");
            this.Question.Name = "Question";
            // 
            // SelectBackend
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Question);
            this.Controls.Add(this.S3);
            this.Controls.Add(this.WebDAV);
            this.Controls.Add(this.SSH);
            this.Controls.Add(this.FTP);
            this.Controls.Add(this.File);
            this.Name = "SelectBackend";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Wizard.DoubleClickRadioButton File;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton FTP;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton SSH;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton WebDAV;
        private System.Windows.Forms.Wizard.DoubleClickRadioButton S3;
        private System.Windows.Forms.Label Question;
    }
}
