namespace Duplicati.GUI.HelperControls
{
    partial class FolderPathEntry
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FolderPathEntry));
            this.BrowseFolderButton = new System.Windows.Forms.Button();
            this.FolderPath = new System.Windows.Forms.TextBox();
            this.SizeLabel = new System.Windows.Forms.Label();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.DeleteButton = new System.Windows.Forms.Button();
            this.FolderStateImage = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.FolderStateImage)).BeginInit();
            this.SuspendLayout();
            // 
            // BrowseFolderButton
            // 
            resources.ApplyResources(this.BrowseFolderButton, "BrowseFolderButton");
            this.BrowseFolderButton.Name = "BrowseFolderButton";
            this.toolTip.SetToolTip(this.BrowseFolderButton, resources.GetString("BrowseFolderButton.ToolTip"));
            this.BrowseFolderButton.UseVisualStyleBackColor = true;
            // 
            // FolderPath
            // 
            resources.ApplyResources(this.FolderPath, "FolderPath");
            this.FolderPath.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.FolderPath.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.FileSystemDirectories;
            this.FolderPath.Name = "FolderPath";
            this.toolTip.SetToolTip(this.FolderPath, resources.GetString("FolderPath.ToolTip"));
            this.FolderPath.TextChanged += new System.EventHandler(this.FolderPath_TextChanged);
            this.FolderPath.Leave += new System.EventHandler(this.FolderPath_Leave);
            this.FolderPath.KeyUp += new System.Windows.Forms.KeyEventHandler(this.FolderPath_KeyUp);
            // 
            // SizeLabel
            // 
            resources.ApplyResources(this.SizeLabel, "SizeLabel");
            this.SizeLabel.Name = "SizeLabel";
            this.toolTip.SetToolTip(this.SizeLabel, resources.GetString("SizeLabel.ToolTip"));
            // 
            // DeleteButton
            // 
            resources.ApplyResources(this.DeleteButton, "DeleteButton");
            this.DeleteButton.Image = global::Duplicati.GUI.Properties.Resources.Trash;
            this.DeleteButton.Name = "DeleteButton";
            this.toolTip.SetToolTip(this.DeleteButton, resources.GetString("DeleteButton.ToolTip"));
            this.DeleteButton.UseVisualStyleBackColor = true;
            // 
            // FolderStateImage
            // 
            this.FolderStateImage.Image = global::Duplicati.GUI.Properties.Resources.AddedFolder;
            resources.ApplyResources(this.FolderStateImage, "FolderStateImage");
            this.FolderStateImage.Name = "FolderStateImage";
            this.FolderStateImage.TabStop = false;
            // 
            // FolderPathEntry
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.FolderStateImage);
            this.Controls.Add(this.FolderPath);
            this.Controls.Add(this.SizeLabel);
            this.Controls.Add(this.DeleteButton);
            this.Controls.Add(this.BrowseFolderButton);
            this.MinimumSize = new System.Drawing.Size(178, 20);
            this.Name = "FolderPathEntry";
            ((System.ComponentModel.ISupportInitialize)(this.FolderStateImage)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button BrowseFolderButton;
        private System.Windows.Forms.TextBox FolderPath;
        private System.Windows.Forms.Label SizeLabel;
        private System.Windows.Forms.Button DeleteButton;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.PictureBox FolderStateImage;
    }
}
