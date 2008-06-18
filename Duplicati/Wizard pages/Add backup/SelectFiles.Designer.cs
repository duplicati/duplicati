namespace Duplicati.Wizard_pages.Add_backup
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
            this.label1 = new System.Windows.Forms.Label();
            this.TargetFolder = new System.Windows.Forms.TextBox();
            this.BrowseFolderButton = new System.Windows.Forms.Button();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.DocumentGroup = new System.Windows.Forms.GroupBox();
            this.appdataSize = new System.Windows.Forms.Label();
            this.desktopSize = new System.Windows.Forms.Label();
            this.myMoviesSize = new System.Windows.Forms.Label();
            this.myMusicSize = new System.Windows.Forms.Label();
            this.myPicturesSize = new System.Windows.Forms.Label();
            this.myDocumentsSize = new System.Windows.Forms.Label();
            this.checkBox6 = new System.Windows.Forms.CheckBox();
            this.checkBox5 = new System.Windows.Forms.CheckBox();
            this.checkBox4 = new System.Windows.Forms.CheckBox();
            this.checkBox3 = new System.Windows.Forms.CheckBox();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.DocumentsRadio = new System.Windows.Forms.RadioButton();
            this.FolderGroup = new System.Windows.Forms.GroupBox();
            this.customSize = new System.Windows.Forms.Label();
            this.FolderRadio = new System.Windows.Forms.RadioButton();
            this.totalSize = new System.Windows.Forms.Label();
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
            this.TargetFolder.Location = new System.Drawing.Point(152, 24);
            this.TargetFolder.Name = "TargetFolder";
            this.TargetFolder.Size = new System.Drawing.Size(248, 20);
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
            this.DocumentGroup.Controls.Add(this.myMoviesSize);
            this.DocumentGroup.Controls.Add(this.myMusicSize);
            this.DocumentGroup.Controls.Add(this.myPicturesSize);
            this.DocumentGroup.Controls.Add(this.myDocumentsSize);
            this.DocumentGroup.Controls.Add(this.checkBox6);
            this.DocumentGroup.Controls.Add(this.checkBox5);
            this.DocumentGroup.Controls.Add(this.checkBox4);
            this.DocumentGroup.Controls.Add(this.checkBox3);
            this.DocumentGroup.Controls.Add(this.checkBox2);
            this.DocumentGroup.Controls.Add(this.checkBox1);
            this.DocumentGroup.Location = new System.Drawing.Point(24, 16);
            this.DocumentGroup.Name = "DocumentGroup";
            this.DocumentGroup.Size = new System.Drawing.Size(448, 104);
            this.DocumentGroup.TabIndex = 3;
            this.DocumentGroup.TabStop = false;
            // 
            // appdataSize
            // 
            this.appdataSize.AutoSize = true;
            this.appdataSize.Location = new System.Drawing.Point(392, 72);
            this.appdataSize.Name = "appdataSize";
            this.appdataSize.Size = new System.Drawing.Size(22, 13);
            this.appdataSize.TabIndex = 11;
            this.appdataSize.Text = "(...)";
            // 
            // desktopSize
            // 
            this.desktopSize.AutoSize = true;
            this.desktopSize.Location = new System.Drawing.Point(392, 48);
            this.desktopSize.Name = "desktopSize";
            this.desktopSize.Size = new System.Drawing.Size(22, 13);
            this.desktopSize.TabIndex = 10;
            this.desktopSize.Text = "(...)";
            // 
            // myMoviesSize
            // 
            this.myMoviesSize.AutoSize = true;
            this.myMoviesSize.Location = new System.Drawing.Point(392, 24);
            this.myMoviesSize.Name = "myMoviesSize";
            this.myMoviesSize.Size = new System.Drawing.Size(22, 13);
            this.myMoviesSize.TabIndex = 9;
            this.myMoviesSize.Text = "(...)";
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
            // checkBox6
            // 
            this.checkBox6.AutoSize = true;
            this.checkBox6.Checked = true;
            this.checkBox6.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox6.Location = new System.Drawing.Point(32, 24);
            this.checkBox6.Name = "checkBox6";
            this.checkBox6.Size = new System.Drawing.Size(116, 17);
            this.checkBox6.TabIndex = 5;
            this.checkBox6.Text = "Include documents";
            this.checkBox6.UseVisualStyleBackColor = true;
            // 
            // checkBox5
            // 
            this.checkBox5.AutoSize = true;
            this.checkBox5.Location = new System.Drawing.Point(224, 72);
            this.checkBox5.Name = "checkBox5";
            this.checkBox5.Size = new System.Drawing.Size(154, 17);
            this.checkBox5.TabIndex = 4;
            this.checkBox5.Text = "Include application settings";
            this.checkBox5.UseVisualStyleBackColor = true;
            // 
            // checkBox4
            // 
            this.checkBox4.AutoSize = true;
            this.checkBox4.Checked = true;
            this.checkBox4.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox4.Location = new System.Drawing.Point(224, 48);
            this.checkBox4.Name = "checkBox4";
            this.checkBox4.Size = new System.Drawing.Size(156, 17);
            this.checkBox4.TabIndex = 3;
            this.checkBox4.Text = "Include files on the desktop";
            this.checkBox4.UseVisualStyleBackColor = true;
            // 
            // checkBox3
            // 
            this.checkBox3.AutoSize = true;
            this.checkBox3.Location = new System.Drawing.Point(224, 24);
            this.checkBox3.Name = "checkBox3";
            this.checkBox3.Size = new System.Drawing.Size(113, 17);
            this.checkBox3.TabIndex = 2;
            this.checkBox3.Text = "Include my movies";
            this.checkBox3.UseVisualStyleBackColor = true;
            // 
            // checkBox2
            // 
            this.checkBox2.AutoSize = true;
            this.checkBox2.Location = new System.Drawing.Point(32, 72);
            this.checkBox2.Name = "checkBox2";
            this.checkBox2.Size = new System.Drawing.Size(107, 17);
            this.checkBox2.TabIndex = 1;
            this.checkBox2.Text = "Include my music";
            this.checkBox2.UseVisualStyleBackColor = true;
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Checked = true;
            this.checkBox1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox1.Location = new System.Drawing.Point(32, 48);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(113, 17);
            this.checkBox1.TabIndex = 0;
            this.checkBox1.Text = "Include my images";
            this.checkBox1.UseVisualStyleBackColor = true;
            // 
            // DocumentsRadio
            // 
            this.DocumentsRadio.AutoSize = true;
            this.DocumentsRadio.Checked = true;
            this.DocumentsRadio.Location = new System.Drawing.Point(40, 16);
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
            this.FolderGroup.Location = new System.Drawing.Point(24, 128);
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
            this.FolderRadio.Location = new System.Drawing.Point(40, 128);
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
            this.totalSize.Location = new System.Drawing.Point(40, 200);
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
        private System.Windows.Forms.Label myMoviesSize;
        private System.Windows.Forms.Label myMusicSize;
        private System.Windows.Forms.Label myPicturesSize;
        private System.Windows.Forms.Label myDocumentsSize;
        private System.Windows.Forms.CheckBox checkBox6;
        private System.Windows.Forms.CheckBox checkBox5;
        private System.Windows.Forms.CheckBox checkBox4;
        private System.Windows.Forms.CheckBox checkBox3;
        private System.Windows.Forms.CheckBox checkBox2;
        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.GroupBox FolderGroup;
        private System.Windows.Forms.Label customSize;
        private System.Windows.Forms.RadioButton FolderRadio;
        private System.Windows.Forms.Label totalSize;
    }
}
