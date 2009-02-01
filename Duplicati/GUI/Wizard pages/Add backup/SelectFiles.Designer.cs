namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    partial class SelectFiles
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
            this.label1 = new System.Windows.Forms.Label();
            this.TargetFolder = new System.Windows.Forms.TextBox();
            this.BrowseFolderButton = new System.Windows.Forms.Button();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.DocumentGroup = new System.Windows.Forms.GroupBox();
            this.appdataSize = new System.Windows.Forms.Label();
            this.desktopSize = new System.Windows.Forms.Label();
            this.myMusicSize = new System.Windows.Forms.Label();
            this.myPicturesSize = new System.Windows.Forms.Label();
            this.myDocumentsSize = new System.Windows.Forms.Label();
            this.IncludeDocuments = new System.Windows.Forms.CheckBox();
            this.IncludeSettings = new System.Windows.Forms.CheckBox();
            this.IncludeDesktop = new System.Windows.Forms.CheckBox();
            this.IncludeMusic = new System.Windows.Forms.CheckBox();
            this.IncludeImages = new System.Windows.Forms.CheckBox();
            this.DocumentsRadio = new System.Windows.Forms.RadioButton();
            this.FolderGroup = new System.Windows.Forms.GroupBox();
            this.customSize = new System.Windows.Forms.Label();
            this.FolderRadio = new System.Windows.Forms.RadioButton();
            this.totalSize = new System.Windows.Forms.Label();
            this.FolderTooltip = new System.Windows.Forms.ToolTip(this.components);
            this.DocumentGroup.SuspendLayout();
            this.FolderGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(87, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Folder to backup";
            // 
            // TargetFolder
            // 
            this.TargetFolder.Location = new System.Drawing.Point(192, 24);
            this.TargetFolder.Name = "TargetFolder";
            this.TargetFolder.Size = new System.Drawing.Size(208, 20);
            this.TargetFolder.TabIndex = 1;
            this.TargetFolder.Leave += new System.EventHandler(this.TargetFolder_Leave);
            // 
            // BrowseFolderButton
            // 
            this.BrowseFolderButton.Location = new System.Drawing.Point(400, 24);
            this.BrowseFolderButton.Name = "BrowseFolderButton";
            this.BrowseFolderButton.Size = new System.Drawing.Size(24, 20);
            this.BrowseFolderButton.TabIndex = 2;
            this.BrowseFolderButton.Text = "...";
            this.BrowseFolderButton.UseVisualStyleBackColor = true;
            this.BrowseFolderButton.Click += new System.EventHandler(this.BrowseFolderButton_Click);
            // 
            // DocumentGroup
            // 
            this.DocumentGroup.Controls.Add(this.appdataSize);
            this.DocumentGroup.Controls.Add(this.desktopSize);
            this.DocumentGroup.Controls.Add(this.myMusicSize);
            this.DocumentGroup.Controls.Add(this.myPicturesSize);
            this.DocumentGroup.Controls.Add(this.myDocumentsSize);
            this.DocumentGroup.Controls.Add(this.IncludeDocuments);
            this.DocumentGroup.Controls.Add(this.IncludeSettings);
            this.DocumentGroup.Controls.Add(this.IncludeDesktop);
            this.DocumentGroup.Controls.Add(this.IncludeMusic);
            this.DocumentGroup.Controls.Add(this.IncludeImages);
            this.DocumentGroup.Location = new System.Drawing.Point(24, 8);
            this.DocumentGroup.Name = "DocumentGroup";
            this.DocumentGroup.Size = new System.Drawing.Size(448, 104);
            this.DocumentGroup.TabIndex = 3;
            this.DocumentGroup.TabStop = false;
            // 
            // appdataSize
            // 
            this.appdataSize.AutoSize = true;
            this.appdataSize.Location = new System.Drawing.Point(392, 48);
            this.appdataSize.Name = "appdataSize";
            this.appdataSize.Size = new System.Drawing.Size(22, 13);
            this.appdataSize.TabIndex = 11;
            this.appdataSize.Text = "(...)";
            // 
            // desktopSize
            // 
            this.desktopSize.AutoSize = true;
            this.desktopSize.Location = new System.Drawing.Point(392, 24);
            this.desktopSize.Name = "desktopSize";
            this.desktopSize.Size = new System.Drawing.Size(22, 13);
            this.desktopSize.TabIndex = 10;
            this.desktopSize.Text = "(...)";
            // 
            // myMusicSize
            // 
            this.myMusicSize.AutoSize = true;
            this.myMusicSize.Location = new System.Drawing.Point(160, 72);
            this.myMusicSize.Name = "myMusicSize";
            this.myMusicSize.Size = new System.Drawing.Size(22, 13);
            this.myMusicSize.TabIndex = 8;
            this.myMusicSize.Text = "(...)";
            // 
            // myPicturesSize
            // 
            this.myPicturesSize.AutoSize = true;
            this.myPicturesSize.Location = new System.Drawing.Point(160, 48);
            this.myPicturesSize.Name = "myPicturesSize";
            this.myPicturesSize.Size = new System.Drawing.Size(22, 13);
            this.myPicturesSize.TabIndex = 7;
            this.myPicturesSize.Text = "(...)";
            // 
            // myDocumentsSize
            // 
            this.myDocumentsSize.AutoSize = true;
            this.myDocumentsSize.Location = new System.Drawing.Point(160, 24);
            this.myDocumentsSize.Name = "myDocumentsSize";
            this.myDocumentsSize.Size = new System.Drawing.Size(22, 13);
            this.myDocumentsSize.TabIndex = 6;
            this.myDocumentsSize.Text = "(...)";
            // 
            // IncludeDocuments
            // 
            this.IncludeDocuments.AutoSize = true;
            this.IncludeDocuments.Checked = true;
            this.IncludeDocuments.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeDocuments.Location = new System.Drawing.Point(32, 24);
            this.IncludeDocuments.Name = "IncludeDocuments";
            this.IncludeDocuments.Size = new System.Drawing.Size(116, 17);
            this.IncludeDocuments.TabIndex = 5;
            this.IncludeDocuments.Text = "Include documents";
            this.IncludeDocuments.UseVisualStyleBackColor = true;
            this.IncludeDocuments.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeSettings
            // 
            this.IncludeSettings.AutoSize = true;
            this.IncludeSettings.Location = new System.Drawing.Point(224, 48);
            this.IncludeSettings.Name = "IncludeSettings";
            this.IncludeSettings.Size = new System.Drawing.Size(154, 17);
            this.IncludeSettings.TabIndex = 4;
            this.IncludeSettings.Text = "Include application settings";
            this.IncludeSettings.UseVisualStyleBackColor = true;
            this.IncludeSettings.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeDesktop
            // 
            this.IncludeDesktop.AutoSize = true;
            this.IncludeDesktop.Checked = true;
            this.IncludeDesktop.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeDesktop.Location = new System.Drawing.Point(224, 24);
            this.IncludeDesktop.Name = "IncludeDesktop";
            this.IncludeDesktop.Size = new System.Drawing.Size(156, 17);
            this.IncludeDesktop.TabIndex = 3;
            this.IncludeDesktop.Text = "Include files on the desktop";
            this.IncludeDesktop.UseVisualStyleBackColor = true;
            this.IncludeDesktop.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeMusic
            // 
            this.IncludeMusic.AutoSize = true;
            this.IncludeMusic.Location = new System.Drawing.Point(32, 72);
            this.IncludeMusic.Name = "IncludeMusic";
            this.IncludeMusic.Size = new System.Drawing.Size(107, 17);
            this.IncludeMusic.TabIndex = 1;
            this.IncludeMusic.Text = "Include my music";
            this.IncludeMusic.UseVisualStyleBackColor = true;
            this.IncludeMusic.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeImages
            // 
            this.IncludeImages.AutoSize = true;
            this.IncludeImages.Checked = true;
            this.IncludeImages.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeImages.Location = new System.Drawing.Point(32, 48);
            this.IncludeImages.Name = "IncludeImages";
            this.IncludeImages.Size = new System.Drawing.Size(113, 17);
            this.IncludeImages.TabIndex = 0;
            this.IncludeImages.Text = "Include my images";
            this.IncludeImages.UseVisualStyleBackColor = true;
            this.IncludeImages.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // DocumentsRadio
            // 
            this.DocumentsRadio.AutoSize = true;
            this.DocumentsRadio.Checked = true;
            this.DocumentsRadio.Location = new System.Drawing.Point(40, 8);
            this.DocumentsRadio.Name = "DocumentsRadio";
            this.DocumentsRadio.Size = new System.Drawing.Size(94, 17);
            this.DocumentsRadio.TabIndex = 4;
            this.DocumentsRadio.TabStop = true;
            this.DocumentsRadio.Text = "My documents";
            this.DocumentsRadio.UseVisualStyleBackColor = true;
            this.DocumentsRadio.CheckedChanged += new System.EventHandler(this.TargetType_CheckedChanged);
            // 
            // FolderGroup
            // 
            this.FolderGroup.Controls.Add(this.customSize);
            this.FolderGroup.Controls.Add(this.BrowseFolderButton);
            this.FolderGroup.Controls.Add(this.TargetFolder);
            this.FolderGroup.Controls.Add(this.label1);
            this.FolderGroup.Enabled = false;
            this.FolderGroup.Location = new System.Drawing.Point(24, 120);
            this.FolderGroup.Name = "FolderGroup";
            this.FolderGroup.Size = new System.Drawing.Size(448, 56);
            this.FolderGroup.TabIndex = 5;
            this.FolderGroup.TabStop = false;
            // 
            // customSize
            // 
            this.customSize.AutoSize = true;
            this.customSize.Location = new System.Drawing.Point(96, 24);
            this.customSize.Name = "customSize";
            this.customSize.Size = new System.Drawing.Size(22, 13);
            this.customSize.TabIndex = 7;
            this.customSize.Text = "(...)";
            // 
            // FolderRadio
            // 
            this.FolderRadio.AutoSize = true;
            this.FolderRadio.Location = new System.Drawing.Point(40, 120);
            this.FolderRadio.Name = "FolderRadio";
            this.FolderRadio.Size = new System.Drawing.Size(100, 17);
            this.FolderRadio.TabIndex = 6;
            this.FolderRadio.TabStop = true;
            this.FolderRadio.Text = "A specific folder";
            this.FolderRadio.UseVisualStyleBackColor = true;
            this.FolderRadio.CheckedChanged += new System.EventHandler(this.TargetType_CheckedChanged);
            // 
            // totalSize
            // 
            this.totalSize.AutoSize = true;
            this.totalSize.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.totalSize.Location = new System.Drawing.Point(40, 192);
            this.totalSize.Name = "totalSize";
            this.totalSize.Size = new System.Drawing.Size(96, 13);
            this.totalSize.TabIndex = 6;
            this.totalSize.Text = "Calculating size";
            // 
            // SelectFiles
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.totalSize);
            this.Controls.Add(this.FolderRadio);
            this.Controls.Add(this.FolderGroup);
            this.Controls.Add(this.DocumentsRadio);
            this.Controls.Add(this.DocumentGroup);
            this.Name = "SelectFiles";
            this.Size = new System.Drawing.Size(506, 242);
            this.VisibleChanged += new System.EventHandler(this.SelectFiles_VisibleChanged);
            this.DocumentGroup.ResumeLayout(false);
            this.DocumentGroup.PerformLayout();
            this.FolderGroup.ResumeLayout(false);
            this.FolderGroup.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox TargetFolder;
        private System.Windows.Forms.Button BrowseFolderButton;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
        private System.Windows.Forms.GroupBox DocumentGroup;
        private System.Windows.Forms.RadioButton DocumentsRadio;
        private System.Windows.Forms.Label appdataSize;
        private System.Windows.Forms.Label desktopSize;
        private System.Windows.Forms.Label myMusicSize;
        private System.Windows.Forms.Label myPicturesSize;
        private System.Windows.Forms.Label myDocumentsSize;
        private System.Windows.Forms.CheckBox IncludeDocuments;
        private System.Windows.Forms.CheckBox IncludeSettings;
        private System.Windows.Forms.CheckBox IncludeDesktop;
        private System.Windows.Forms.CheckBox IncludeMusic;
        private System.Windows.Forms.CheckBox IncludeImages;
        private System.Windows.Forms.GroupBox FolderGroup;
        private System.Windows.Forms.Label customSize;
        private System.Windows.Forms.RadioButton FolderRadio;
        private System.Windows.Forms.Label totalSize;
        private System.Windows.Forms.ToolTip FolderTooltip;
    }
}
