namespace Duplicati.Library.Encryption
{
    partial class GPGCommonOptions
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GPGCommonOptions));
            this.label1 = new System.Windows.Forms.Label();
            this.GPGPath = new System.Windows.Forms.ComboBox();
            this.BrowseForGPGPathButton = new System.Windows.Forms.Button();
            this.BrowseForGPGDialog = new System.Windows.Forms.OpenFileDialog();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // GPGPath
            // 
            resources.ApplyResources(this.GPGPath, "GPGPath");
            this.GPGPath.FormattingEnabled = true;
            this.GPGPath.Name = "GPGPath";
            this.GPGPath.TextChanged += new System.EventHandler(this.GPGPath_TextChanged);
            // 
            // BrowseForGPGPathButton
            // 
            resources.ApplyResources(this.BrowseForGPGPathButton, "BrowseForGPGPathButton");
            this.BrowseForGPGPathButton.Name = "BrowseForGPGPathButton";
            this.BrowseForGPGPathButton.UseVisualStyleBackColor = true;
            this.BrowseForGPGPathButton.Click += new System.EventHandler(this.BrowseForGPGPathButton_Click);
            // 
            // BrowseForGPGDialog
            // 
            this.BrowseForGPGDialog.DefaultExt = "exe";
            this.BrowseForGPGDialog.FileName = "gpg.exe";
            resources.ApplyResources(this.BrowseForGPGDialog, "BrowseForGPGDialog");
            // 
            // GPGCommonOptions
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.BrowseForGPGPathButton);
            this.Controls.Add(this.GPGPath);
            this.Controls.Add(this.label1);
            this.Name = "GPGCommonOptions";
            this.Load += new System.EventHandler(this.GPGCommonOptions_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox GPGPath;
        private System.Windows.Forms.Button BrowseForGPGPathButton;
        private System.Windows.Forms.OpenFileDialog BrowseForGPGDialog;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
