namespace Duplicati.Library.Backend
{
    partial class SSHCommonOptions
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SSHCommonOptions));
            this.label1 = new System.Windows.Forms.Label();
            this.UseManagedAsDefault = new System.Windows.Forms.CheckBox();
            this.BrowseForSFTPButton = new System.Windows.Forms.Button();
            this.SFTPPath = new System.Windows.Forms.ComboBox();
            this.BrowseForSFTPDialog = new System.Windows.Forms.OpenFileDialog();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // UseManagedAsDefault
            // 
            resources.ApplyResources(this.UseManagedAsDefault, "UseManagedAsDefault");
            this.UseManagedAsDefault.Checked = true;
            this.UseManagedAsDefault.CheckState = System.Windows.Forms.CheckState.Checked;
            this.UseManagedAsDefault.Name = "UseManagedAsDefault";
            this.UseManagedAsDefault.UseVisualStyleBackColor = true;
            // 
            // BrowseForSFTPButton
            // 
            resources.ApplyResources(this.BrowseForSFTPButton, "BrowseForSFTPButton");
            this.BrowseForSFTPButton.Name = "BrowseForSFTPButton";
            this.BrowseForSFTPButton.UseVisualStyleBackColor = true;
            this.BrowseForSFTPButton.Click += new System.EventHandler(this.BrowseForSFTPButton_Click);
            // 
            // SFTPPath
            // 
            resources.ApplyResources(this.SFTPPath, "SFTPPath");
            this.SFTPPath.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.SFTPPath.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.FileSystem;
            this.SFTPPath.FormattingEnabled = true;
            this.SFTPPath.Name = "SFTPPath";
            this.SFTPPath.TextChanged += new System.EventHandler(this.SFTPPath_TextChanged);
            // 
            // BrowseForSFTPDialog
            // 
            this.BrowseForSFTPDialog.DefaultExt = "exe";
            this.BrowseForSFTPDialog.FileName = "psftp.exe";
            resources.ApplyResources(this.BrowseForSFTPDialog, "BrowseForSFTPDialog");
            // 
            // SSHCommonOptions
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.SFTPPath);
            this.Controls.Add(this.BrowseForSFTPButton);
            this.Controls.Add(this.UseManagedAsDefault);
            this.Controls.Add(this.label1);
            this.Name = "SSHCommonOptions";
            this.Load += new System.EventHandler(this.SSHCommonOptions_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox UseManagedAsDefault;
        private System.Windows.Forms.Button BrowseForSFTPButton;
        private System.Windows.Forms.ComboBox SFTPPath;
        private System.Windows.Forms.OpenFileDialog BrowseForSFTPDialog;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
