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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GeneratedFilenameOptions));
            this.label1 = new System.Windows.Forms.Label();
            this.FilePrefixEnabled = new System.Windows.Forms.CheckBox();
            this.UseShortFilenames = new System.Windows.Forms.CheckBox();
            this.FilePrefix = new System.Windows.Forms.TextBox();
            this.FileTimeSeperator = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // FilePrefixEnabled
            // 
            resources.ApplyResources(this.FilePrefixEnabled, "FilePrefixEnabled");
            this.FilePrefixEnabled.Name = "FilePrefixEnabled";
            this.FilePrefixEnabled.UseVisualStyleBackColor = true;
            this.FilePrefixEnabled.CheckedChanged += new System.EventHandler(this.FilePrefixEnabled_CheckedChanged);
            // 
            // UseShortFilenames
            // 
            resources.ApplyResources(this.UseShortFilenames, "UseShortFilenames");
            this.UseShortFilenames.Name = "UseShortFilenames";
            this.UseShortFilenames.UseVisualStyleBackColor = true;
            // 
            // FilePrefix
            // 
            resources.ApplyResources(this.FilePrefix, "FilePrefix");
            this.FilePrefix.Name = "FilePrefix";
            // 
            // FileTimeSeperator
            // 
            resources.ApplyResources(this.FileTimeSeperator, "FileTimeSeperator");
            this.FileTimeSeperator.FormattingEnabled = true;
            this.FileTimeSeperator.Items.AddRange(new object[] {
            resources.GetString("FileTimeSeperator.Items"),
            resources.GetString("FileTimeSeperator.Items1"),
            resources.GetString("FileTimeSeperator.Items2")});
            this.FileTimeSeperator.Name = "FileTimeSeperator";
            // 
            // GeneratedFilenameOptions
            // 
            this.Controls.Add(this.FileTimeSeperator);
            this.Controls.Add(this.FilePrefix);
            this.Controls.Add(this.UseShortFilenames);
            this.Controls.Add(this.FilePrefixEnabled);
            this.Controls.Add(this.label1);
            this.Name = "GeneratedFilenameOptions";
            resources.ApplyResources(this, "$this");
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
