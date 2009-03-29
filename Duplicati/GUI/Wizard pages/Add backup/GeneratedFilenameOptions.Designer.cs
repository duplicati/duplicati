namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class GeneratedFilenameOptions
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.FilePrefixEnabled = new System.Windows.Forms.CheckBox();
            this.UseShortFilenames = new System.Windows.Forms.CheckBox();
            this.FilePrefix = new System.Windows.Forms.TextBox();
            this.FileTimeSeperator = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(40, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(92, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "File time seperator";
            // 
            // FilePrefixEnabled
            // 
            this.FilePrefixEnabled.AutoSize = true;
            this.FilePrefixEnabled.Location = new System.Drawing.Point(24, 56);
            this.FilePrefixEnabled.Name = "FilePrefixEnabled";
            this.FilePrefixEnabled.Size = new System.Drawing.Size(133, 17);
            this.FilePrefixEnabled.TabIndex = 1;
            this.FilePrefixEnabled.Text = "Change filename prefix";
            this.FilePrefixEnabled.UseVisualStyleBackColor = true;
            this.FilePrefixEnabled.CheckedChanged += new System.EventHandler(this.FilePrefixEnabled_CheckedChanged);
            // 
            // UseShortFilenames
            // 
            this.UseShortFilenames.AutoSize = true;
            this.UseShortFilenames.Location = new System.Drawing.Point(24, 88);
            this.UseShortFilenames.Name = "UseShortFilenames";
            this.UseShortFilenames.Size = new System.Drawing.Size(118, 17);
            this.UseShortFilenames.TabIndex = 2;
            this.UseShortFilenames.Text = "Use short filenames";
            this.UseShortFilenames.UseVisualStyleBackColor = true;
            // 
            // FilePrefix
            // 
            this.FilePrefix.Enabled = false;
            this.FilePrefix.Location = new System.Drawing.Point(168, 56);
            this.FilePrefix.Name = "FilePrefix";
            this.FilePrefix.Size = new System.Drawing.Size(128, 20);
            this.FilePrefix.TabIndex = 3;
            // 
            // FileTimeSeperator
            // 
            this.FileTimeSeperator.Font = new System.Drawing.Font("Courier New", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FileTimeSeperator.FormattingEnabled = true;
            this.FileTimeSeperator.Items.AddRange(new object[] {
            "\'",
            ":",
            "_"});
            this.FileTimeSeperator.Location = new System.Drawing.Point(168, 24);
            this.FileTimeSeperator.Name = "FileTimeSeperator";
            this.FileTimeSeperator.Size = new System.Drawing.Size(56, 22);
            this.FileTimeSeperator.TabIndex = 4;
            // 
            // GeneratedFilenameOptions
            // 
            this.Controls.Add(this.FileTimeSeperator);
            this.Controls.Add(this.FilePrefix);
            this.Controls.Add(this.UseShortFilenames);
            this.Controls.Add(this.FilePrefixEnabled);
            this.Controls.Add(this.label1);
            this.Name = "GeneratedFilenameOptions";
            this.Size = new System.Drawing.Size(506, 242);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox FilePrefixEnabled;
        private System.Windows.Forms.CheckBox UseShortFilenames;
        private System.Windows.Forms.TextBox FilePrefix;
        private System.Windows.Forms.ComboBox FileTimeSeperator;
    }
}
