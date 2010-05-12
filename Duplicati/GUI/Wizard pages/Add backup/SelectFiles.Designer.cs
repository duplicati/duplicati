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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectFiles));
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
            this.InnerControlContainer = new System.Windows.Forms.Panel();
            this.FolderRadio = new System.Windows.Forms.RadioButton();
            this.totalSize = new System.Windows.Forms.Label();
            this.FolderTooltip = new System.Windows.Forms.ToolTip(this.components);
            this.LayoutControlPanel = new System.Windows.Forms.Panel();
            this.DocumentGroup.SuspendLayout();
            this.FolderGroup.SuspendLayout();
            this.LayoutControlPanel.SuspendLayout();
            this.SuspendLayout();
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
            resources.ApplyResources(this.DocumentGroup, "DocumentGroup");
            this.DocumentGroup.Name = "DocumentGroup";
            this.DocumentGroup.TabStop = false;
            // 
            // appdataSize
            // 
            resources.ApplyResources(this.appdataSize, "appdataSize");
            this.appdataSize.Name = "appdataSize";
            // 
            // desktopSize
            // 
            resources.ApplyResources(this.desktopSize, "desktopSize");
            this.desktopSize.Name = "desktopSize";
            // 
            // myMusicSize
            // 
            resources.ApplyResources(this.myMusicSize, "myMusicSize");
            this.myMusicSize.Name = "myMusicSize";
            // 
            // myPicturesSize
            // 
            resources.ApplyResources(this.myPicturesSize, "myPicturesSize");
            this.myPicturesSize.Name = "myPicturesSize";
            // 
            // myDocumentsSize
            // 
            resources.ApplyResources(this.myDocumentsSize, "myDocumentsSize");
            this.myDocumentsSize.Name = "myDocumentsSize";
            // 
            // IncludeDocuments
            // 
            resources.ApplyResources(this.IncludeDocuments, "IncludeDocuments");
            this.IncludeDocuments.Checked = true;
            this.IncludeDocuments.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeDocuments.Name = "IncludeDocuments";
            this.IncludeDocuments.UseVisualStyleBackColor = true;
            this.IncludeDocuments.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeSettings
            // 
            resources.ApplyResources(this.IncludeSettings, "IncludeSettings");
            this.IncludeSettings.Name = "IncludeSettings";
            this.IncludeSettings.UseVisualStyleBackColor = true;
            this.IncludeSettings.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeDesktop
            // 
            resources.ApplyResources(this.IncludeDesktop, "IncludeDesktop");
            this.IncludeDesktop.Checked = true;
            this.IncludeDesktop.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeDesktop.Name = "IncludeDesktop";
            this.IncludeDesktop.UseVisualStyleBackColor = true;
            this.IncludeDesktop.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeMusic
            // 
            resources.ApplyResources(this.IncludeMusic, "IncludeMusic");
            this.IncludeMusic.Name = "IncludeMusic";
            this.IncludeMusic.UseVisualStyleBackColor = true;
            this.IncludeMusic.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // IncludeImages
            // 
            resources.ApplyResources(this.IncludeImages, "IncludeImages");
            this.IncludeImages.Checked = true;
            this.IncludeImages.CheckState = System.Windows.Forms.CheckState.Checked;
            this.IncludeImages.Name = "IncludeImages";
            this.IncludeImages.UseVisualStyleBackColor = true;
            this.IncludeImages.CheckedChanged += new System.EventHandler(this.m_calculator_CompletedWork);
            // 
            // DocumentsRadio
            // 
            resources.ApplyResources(this.DocumentsRadio, "DocumentsRadio");
            this.DocumentsRadio.Name = "DocumentsRadio";
            this.DocumentsRadio.UseVisualStyleBackColor = true;
            this.DocumentsRadio.CheckedChanged += new System.EventHandler(this.TargetType_CheckedChanged);
            // 
            // FolderGroup
            // 
            this.FolderGroup.Controls.Add(this.InnerControlContainer);
            resources.ApplyResources(this.FolderGroup, "FolderGroup");
            this.FolderGroup.Name = "FolderGroup";
            this.FolderGroup.TabStop = false;
            // 
            // InnerControlContainer
            // 
            resources.ApplyResources(this.InnerControlContainer, "InnerControlContainer");
            this.InnerControlContainer.Name = "InnerControlContainer";
            // 
            // FolderRadio
            // 
            resources.ApplyResources(this.FolderRadio, "FolderRadio");
            this.FolderRadio.Checked = true;
            this.FolderRadio.Name = "FolderRadio";
            this.FolderRadio.TabStop = true;
            this.FolderRadio.UseVisualStyleBackColor = true;
            this.FolderRadio.CheckedChanged += new System.EventHandler(this.TargetType_CheckedChanged);
            // 
            // totalSize
            // 
            resources.ApplyResources(this.totalSize, "totalSize");
            this.totalSize.Name = "totalSize";
            // 
            // LayoutControlPanel
            // 
            this.LayoutControlPanel.Controls.Add(this.DocumentsRadio);
            this.LayoutControlPanel.Controls.Add(this.DocumentGroup);
            this.LayoutControlPanel.Controls.Add(this.FolderRadio);
            this.LayoutControlPanel.Controls.Add(this.FolderGroup);
            resources.ApplyResources(this.LayoutControlPanel, "LayoutControlPanel");
            this.LayoutControlPanel.Name = "LayoutControlPanel";
            // 
            // SelectFiles
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.totalSize);
            this.Controls.Add(this.LayoutControlPanel);
            this.Name = "SelectFiles";
            this.VisibleChanged += new System.EventHandler(this.SelectFiles_VisibleChanged);
            this.DocumentGroup.ResumeLayout(false);
            this.DocumentGroup.PerformLayout();
            this.FolderGroup.ResumeLayout(false);
            this.LayoutControlPanel.ResumeLayout(false);
            this.LayoutControlPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

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
        private System.Windows.Forms.RadioButton FolderRadio;
        private System.Windows.Forms.Label totalSize;
        private System.Windows.Forms.ToolTip FolderTooltip;
        private System.Windows.Forms.Panel LayoutControlPanel;
        private System.Windows.Forms.Panel InnerControlContainer;
    }
}
