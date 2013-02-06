namespace Duplicati.GUI.HelperControls
{
    partial class FilterDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FilterDialog));
            this.Inclusive = new System.Windows.Forms.RadioButton();
            this.Exclusive = new System.Windows.Forms.RadioButton();
            this.label1 = new System.Windows.Forms.Label();
            this.FilterText = new System.Windows.Forms.TextBox();
            this.OKBtn = new System.Windows.Forms.Button();
            this.CancelBtn = new System.Windows.Forms.Button();
            this.IsRegExp = new System.Windows.Forms.CheckBox();
            this.BrowseFolderButton = new System.Windows.Forms.Button();
            this.BrowseFileButton = new System.Windows.Forms.Button();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.openFileDialog = new System.Windows.Forms.OpenFileDialog();
            this.HelpLink = new System.Windows.Forms.LinkLabel();
            this.HelpImage = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.HelpImage)).BeginInit();
            this.SuspendLayout();
            // 
            // Inclusive
            // 
            resources.ApplyResources(this.Inclusive, "Inclusive");
            this.Inclusive.Checked = true;
            this.Inclusive.Name = "Inclusive";
            this.Inclusive.TabStop = true;
            this.Inclusive.UseVisualStyleBackColor = true;
            // 
            // Exclusive
            // 
            resources.ApplyResources(this.Exclusive, "Exclusive");
            this.Exclusive.Name = "Exclusive";
            this.Exclusive.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // FilterText
            // 
            resources.ApplyResources(this.FilterText, "FilterText");
            this.FilterText.Name = "FilterText";
            this.FilterText.TextChanged += new System.EventHandler(this.FilterText_TextChanged);
            // 
            // OKBtn
            // 
            resources.ApplyResources(this.OKBtn, "OKBtn");
            this.OKBtn.Name = "OKBtn";
            this.OKBtn.UseVisualStyleBackColor = true;
            this.OKBtn.Click += new System.EventHandler(this.OKBtn_Click);
            // 
            // CancelBtn
            // 
            this.CancelBtn.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            resources.ApplyResources(this.CancelBtn, "CancelBtn");
            this.CancelBtn.Name = "CancelBtn";
            this.CancelBtn.UseVisualStyleBackColor = true;
            // 
            // IsRegExp
            // 
            resources.ApplyResources(this.IsRegExp, "IsRegExp");
            this.IsRegExp.Checked = true;
            this.IsRegExp.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IsRegExp.Name = "IsRegExp";
            this.IsRegExp.UseVisualStyleBackColor = true;
            // 
            // BrowseFolderButton
            // 
            resources.ApplyResources(this.BrowseFolderButton, "BrowseFolderButton");
            this.BrowseFolderButton.Image = global::Duplicati.GUI.Properties.Resources.FolderOpen;
            this.BrowseFolderButton.Name = "BrowseFolderButton";
            this.BrowseFolderButton.UseVisualStyleBackColor = true;
            this.BrowseFolderButton.Click += new System.EventHandler(this.BrowseFolderButton_Click);
            // 
            // BrowseFileButton
            // 
            resources.ApplyResources(this.BrowseFileButton, "BrowseFileButton");
            this.BrowseFileButton.Name = "BrowseFileButton";
            this.BrowseFileButton.UseVisualStyleBackColor = true;
            this.BrowseFileButton.Click += new System.EventHandler(this.BrowseFileButton_Click);
            // 
            // folderBrowserDialog
            // 
            resources.ApplyResources(this.folderBrowserDialog, "folderBrowserDialog");
            this.folderBrowserDialog.ShowNewFolderButton = false;
            // 
            // openFileDialog
            // 
            this.openFileDialog.AddExtension = false;
            this.openFileDialog.CheckFileExists = false;
            this.openFileDialog.CheckPathExists = false;
            this.openFileDialog.DereferenceLinks = false;
            resources.ApplyResources(this.openFileDialog, "openFileDialog");
            this.openFileDialog.SupportMultiDottedExtensions = true;
            this.openFileDialog.ValidateNames = false;
            // 
            // HelpLink
            // 
            resources.ApplyResources(this.HelpLink, "HelpLink");
            this.HelpLink.Name = "HelpLink";
            this.HelpLink.TabStop = true;
            this.HelpLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.HelpLink_LinkClicked);
            // 
            // HelpImage
            // 
            this.HelpImage.Cursor = System.Windows.Forms.Cursors.Hand;
            this.HelpImage.Image = global::Duplicati.GUI.Properties.Resources.help;
            resources.ApplyResources(this.HelpImage, "HelpImage");
            this.HelpImage.Name = "HelpImage";
            this.HelpImage.TabStop = false;
            this.HelpImage.Click += new System.EventHandler(this.HelpImage_Click);
            // 
            // FilterDialog
            // 
            this.AcceptButton = this.OKBtn;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBtn;
            this.Controls.Add(this.HelpImage);
            this.Controls.Add(this.HelpLink);
            this.Controls.Add(this.BrowseFileButton);
            this.Controls.Add(this.BrowseFolderButton);
            this.Controls.Add(this.FilterText);
            this.Controls.Add(this.IsRegExp);
            this.Controls.Add(this.CancelBtn);
            this.Controls.Add(this.OKBtn);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Exclusive);
            this.Controls.Add(this.Inclusive);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "FilterDialog";
            this.Activated += new System.EventHandler(this.FilterDialog_Activated);
            this.Load += new System.EventHandler(this.FilterDialog_Load);
            ((System.ComponentModel.ISupportInitialize)(this.HelpImage)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton Inclusive;
        private System.Windows.Forms.RadioButton Exclusive;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox FilterText;
        private System.Windows.Forms.Button OKBtn;
        private System.Windows.Forms.Button CancelBtn;
        private System.Windows.Forms.CheckBox IsRegExp;
        private System.Windows.Forms.Button BrowseFolderButton;
        private System.Windows.Forms.Button BrowseFileButton;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
        private System.Windows.Forms.OpenFileDialog openFileDialog;
        private System.Windows.Forms.LinkLabel HelpLink;
        private System.Windows.Forms.PictureBox HelpImage;
    }
}