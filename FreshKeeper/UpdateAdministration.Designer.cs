namespace FreshKeeper
{
    partial class UpdateAdministration
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(UpdateAdministration));
            this.panel1 = new System.Windows.Forms.Panel();
            this.CloseButton = new System.Windows.Forms.Button();
            this.SaveButton = new System.Windows.Forms.Button();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.AddUpdateButton = new System.Windows.Forms.ToolStripButton();
            this.DeleteUpdateButton = new System.Windows.Forms.ToolStripButton();
            this.PropertiesGroup = new System.Windows.Forms.GroupBox();
            this.UpdateLogAppend = new System.Windows.Forms.ComboBox();
            this.label8 = new System.Windows.Forms.Label();
            this.UpdateApplication = new System.Windows.Forms.ComboBox();
            this.label7 = new System.Windows.Forms.Label();
            this.UpdateDate = new System.Windows.Forms.DateTimePicker();
            this.label6 = new System.Windows.Forms.Label();
            this.UpdateArchitecture = new System.Windows.Forms.ComboBox();
            this.ResetSignatureButton = new System.Windows.Forms.Button();
            this.UpdateDownloadUrls = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.UpdateChangelog = new System.Windows.Forms.TextBox();
            this.UpdateCaption = new System.Windows.Forms.TextBox();
            this.UpdateVersion = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.UpdateSecurity = new System.Windows.Forms.CheckBox();
            this.UpdateBugfix = new System.Windows.Forms.CheckBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.SelectPackageFile = new System.Windows.Forms.OpenFileDialog();
            this.errorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.createClientFileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.changePrivateKeyPasswordToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.OpenClientFile = new System.Windows.Forms.OpenFileDialog();
            this.panel1.SuspendLayout();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.PropertiesGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.CloseButton);
            this.panel1.Controls.Add(this.SaveButton);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 443);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(465, 37);
            this.panel1.TabIndex = 0;
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.CloseButton.Location = new System.Drawing.Point(240, 8);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 23);
            this.CloseButton.TabIndex = 1;
            this.CloseButton.Text = "Close";
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // SaveButton
            // 
            this.SaveButton.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.SaveButton.Location = new System.Drawing.Point(152, 8);
            this.SaveButton.Name = "SaveButton";
            this.SaveButton.Size = new System.Drawing.Size(75, 23);
            this.SaveButton.TabIndex = 0;
            this.SaveButton.Text = "Save";
            this.SaveButton.UseVisualStyleBackColor = true;
            this.SaveButton.Click += new System.EventHandler(this.SaveButton_Click);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 24);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.groupBox1);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.PropertiesGroup);
            this.splitContainer1.Size = new System.Drawing.Size(465, 419);
            this.splitContainer1.SplitterDistance = 210;
            this.splitContainer1.TabIndex = 1;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.listBox1);
            this.groupBox1.Controls.Add(this.toolStrip1);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox1.Location = new System.Drawing.Point(0, 0);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(210, 419);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Items in file";
            // 
            // listBox1
            // 
            this.listBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBox1.FormattingEnabled = true;
            this.listBox1.IntegralHeight = false;
            this.listBox1.Location = new System.Drawing.Point(3, 41);
            this.listBox1.Name = "listBox1";
            this.listBox1.Size = new System.Drawing.Size(204, 375);
            this.listBox1.TabIndex = 0;
            this.listBox1.SelectedIndexChanged += new System.EventHandler(this.listBox1_SelectedIndexChanged);
            // 
            // toolStrip1
            // 
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddUpdateButton,
            this.DeleteUpdateButton});
            this.toolStrip1.Location = new System.Drawing.Point(3, 16);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            this.toolStrip1.Size = new System.Drawing.Size(204, 25);
            this.toolStrip1.TabIndex = 1;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // AddUpdateButton
            // 
            this.AddUpdateButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.AddUpdateButton.Image = ((System.Drawing.Image)(resources.GetObject("AddUpdateButton.Image")));
            this.AddUpdateButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.AddUpdateButton.Name = "AddUpdateButton";
            this.AddUpdateButton.Size = new System.Drawing.Size(23, 22);
            this.AddUpdateButton.ToolTipText = "Add a new update to the list";
            this.AddUpdateButton.Click += new System.EventHandler(this.AddUpdateButton_Click);
            // 
            // DeleteUpdateButton
            // 
            this.DeleteUpdateButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.DeleteUpdateButton.Image = ((System.Drawing.Image)(resources.GetObject("DeleteUpdateButton.Image")));
            this.DeleteUpdateButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.DeleteUpdateButton.Name = "DeleteUpdateButton";
            this.DeleteUpdateButton.Size = new System.Drawing.Size(23, 22);
            this.DeleteUpdateButton.ToolTipText = "Remove an update from the list";
            // 
            // PropertiesGroup
            // 
            this.PropertiesGroup.Controls.Add(this.UpdateLogAppend);
            this.PropertiesGroup.Controls.Add(this.label8);
            this.PropertiesGroup.Controls.Add(this.UpdateApplication);
            this.PropertiesGroup.Controls.Add(this.label7);
            this.PropertiesGroup.Controls.Add(this.UpdateDate);
            this.PropertiesGroup.Controls.Add(this.label6);
            this.PropertiesGroup.Controls.Add(this.UpdateArchitecture);
            this.PropertiesGroup.Controls.Add(this.ResetSignatureButton);
            this.PropertiesGroup.Controls.Add(this.UpdateDownloadUrls);
            this.PropertiesGroup.Controls.Add(this.label5);
            this.PropertiesGroup.Controls.Add(this.UpdateChangelog);
            this.PropertiesGroup.Controls.Add(this.UpdateCaption);
            this.PropertiesGroup.Controls.Add(this.UpdateVersion);
            this.PropertiesGroup.Controls.Add(this.label4);
            this.PropertiesGroup.Controls.Add(this.UpdateSecurity);
            this.PropertiesGroup.Controls.Add(this.UpdateBugfix);
            this.PropertiesGroup.Controls.Add(this.label3);
            this.PropertiesGroup.Controls.Add(this.label2);
            this.PropertiesGroup.Controls.Add(this.label1);
            this.PropertiesGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PropertiesGroup.Enabled = false;
            this.PropertiesGroup.Location = new System.Drawing.Point(0, 0);
            this.PropertiesGroup.Name = "PropertiesGroup";
            this.PropertiesGroup.Size = new System.Drawing.Size(251, 419);
            this.PropertiesGroup.TabIndex = 0;
            this.PropertiesGroup.TabStop = false;
            this.PropertiesGroup.Text = "Update properties";
            // 
            // UpdateLogAppend
            // 
            this.UpdateLogAppend.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateLogAppend.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.UpdateLogAppend.FormattingEnabled = true;
            this.UpdateLogAppend.Location = new System.Drawing.Point(96, 272);
            this.UpdateLogAppend.Name = "UpdateLogAppend";
            this.UpdateLogAppend.Size = new System.Drawing.Size(144, 21);
            this.UpdateLogAppend.TabIndex = 19;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(8, 272);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(84, 13);
            this.label8.TabIndex = 18;
            this.label8.Text = "Append log from";
            // 
            // UpdateApplication
            // 
            this.UpdateApplication.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateApplication.FormattingEnabled = true;
            this.UpdateApplication.Location = new System.Drawing.Point(96, 72);
            this.UpdateApplication.Name = "UpdateApplication";
            this.UpdateApplication.Size = new System.Drawing.Size(144, 21);
            this.UpdateApplication.TabIndex = 17;
            this.UpdateApplication.SelectedIndexChanged += new System.EventHandler(this.UpdateApplication_SelectedIndexChanged);
            this.UpdateApplication.TextChanged += new System.EventHandler(this.UpdateApplication_TextChanged);
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(8, 72);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(59, 13);
            this.label7.TabIndex = 16;
            this.label7.Text = "Application";
            // 
            // UpdateDate
            // 
            this.UpdateDate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateDate.Location = new System.Drawing.Point(96, 160);
            this.UpdateDate.Name = "UpdateDate";
            this.UpdateDate.Size = new System.Drawing.Size(144, 20);
            this.UpdateDate.TabIndex = 15;
            this.UpdateDate.ValueChanged += new System.EventHandler(this.UpdateDate_ValueChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(8, 160);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(30, 13);
            this.label6.TabIndex = 14;
            this.label6.Text = "Date";
            // 
            // UpdateArchitecture
            // 
            this.UpdateArchitecture.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateArchitecture.FormattingEnabled = true;
            this.UpdateArchitecture.Items.AddRange(new object[] {
            "Windows",
            "Windows x86",
            "Windows ALPHA",
            "Windows x86 32bit",
            "Windows x86 64bit",
            "Linux",
            "Linux x86",
            "Linux x86 32bit",
            "Linux x86 64bit",
            "Linux ALPHA",
            "Linux MIPS"});
            this.UpdateArchitecture.Location = new System.Drawing.Point(96, 48);
            this.UpdateArchitecture.Name = "UpdateArchitecture";
            this.UpdateArchitecture.Size = new System.Drawing.Size(144, 21);
            this.UpdateArchitecture.TabIndex = 13;
            this.UpdateArchitecture.SelectedIndexChanged += new System.EventHandler(this.UpdateArchitecture_SelectedIndexChanged);
            this.UpdateArchitecture.TextChanged += new System.EventHandler(this.UpdateArchitecture_TextChanged);
            // 
            // ResetSignatureButton
            // 
            this.ResetSignatureButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ResetSignatureButton.Location = new System.Drawing.Point(88, 392);
            this.ResetSignatureButton.Name = "ResetSignatureButton";
            this.ResetSignatureButton.Size = new System.Drawing.Size(155, 23);
            this.ResetSignatureButton.TabIndex = 12;
            this.ResetSignatureButton.Text = "Reset download signature";
            this.ResetSignatureButton.UseVisualStyleBackColor = true;
            this.ResetSignatureButton.Click += new System.EventHandler(this.ResetSignatureButton_Click);
            // 
            // UpdateDownloadUrls
            // 
            this.UpdateDownloadUrls.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateDownloadUrls.Location = new System.Drawing.Point(8, 320);
            this.UpdateDownloadUrls.Multiline = true;
            this.UpdateDownloadUrls.Name = "UpdateDownloadUrls";
            this.UpdateDownloadUrls.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.UpdateDownloadUrls.Size = new System.Drawing.Size(232, 72);
            this.UpdateDownloadUrls.TabIndex = 11;
            this.UpdateDownloadUrls.TextChanged += new System.EventHandler(this.UpdateDownloadUrls_TextChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(8, 304);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(145, 13);
            this.label5.TabIndex = 10;
            this.label5.Text = "Download URL\'s, one pr. line";
            // 
            // UpdateChangelog
            // 
            this.UpdateChangelog.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateChangelog.Location = new System.Drawing.Point(8, 200);
            this.UpdateChangelog.Multiline = true;
            this.UpdateChangelog.Name = "UpdateChangelog";
            this.UpdateChangelog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.UpdateChangelog.Size = new System.Drawing.Size(232, 72);
            this.UpdateChangelog.TabIndex = 9;
            this.UpdateChangelog.TextChanged += new System.EventHandler(this.UpdateChangelog_TextChanged);
            // 
            // UpdateCaption
            // 
            this.UpdateCaption.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateCaption.Location = new System.Drawing.Point(96, 136);
            this.UpdateCaption.Name = "UpdateCaption";
            this.UpdateCaption.Size = new System.Drawing.Size(144, 20);
            this.UpdateCaption.TabIndex = 8;
            this.UpdateCaption.TextChanged += new System.EventHandler(this.UpdateCaption_TextChanged);
            // 
            // UpdateVersion
            // 
            this.UpdateVersion.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateVersion.Location = new System.Drawing.Point(96, 24);
            this.UpdateVersion.Name = "UpdateVersion";
            this.UpdateVersion.Size = new System.Drawing.Size(144, 20);
            this.UpdateVersion.TabIndex = 6;
            this.UpdateVersion.TextChanged += new System.EventHandler(this.UpdateVersion_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(8, 184);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(58, 13);
            this.label4.TabIndex = 5;
            this.label4.Text = "Changelog";
            // 
            // UpdateSecurity
            // 
            this.UpdateSecurity.AutoSize = true;
            this.UpdateSecurity.Location = new System.Drawing.Point(112, 104);
            this.UpdateSecurity.Name = "UpdateSecurity";
            this.UpdateSecurity.Size = new System.Drawing.Size(100, 17);
            this.UpdateSecurity.TabIndex = 4;
            this.UpdateSecurity.Text = "Security update";
            this.UpdateSecurity.UseVisualStyleBackColor = true;
            this.UpdateSecurity.CheckedChanged += new System.EventHandler(this.UpdateSecurity_CheckedChanged);
            // 
            // UpdateBugfix
            // 
            this.UpdateBugfix.AutoSize = true;
            this.UpdateBugfix.Location = new System.Drawing.Point(8, 104);
            this.UpdateBugfix.Name = "UpdateBugfix";
            this.UpdateBugfix.Size = new System.Drawing.Size(91, 17);
            this.UpdateBugfix.TabIndex = 3;
            this.UpdateBugfix.Text = "Bugfix update";
            this.UpdateBugfix.UseVisualStyleBackColor = true;
            this.UpdateBugfix.CheckedChanged += new System.EventHandler(this.UpdateBugfix_CheckedChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(8, 48);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(64, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "Architecture";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 136);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(43, 13);
            this.label2.TabIndex = 1;
            this.label2.Text = "Caption";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 24);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(42, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Version";
            // 
            // SelectPackageFile
            // 
            this.SelectPackageFile.Filter = "Any package (*.msi;*.zip;*.tar.bz2;*.bz2)|*.msi;*.zip;*.tar.bz2;*.bz2|All files (" +
                "*.*)|*.*";
            this.SelectPackageFile.Title = "Select update package for hash generation";
            // 
            // errorProvider
            // 
            this.errorProvider.ContainerControl = this;
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(465, 24);
            this.menuStrip1.TabIndex = 2;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.createClientFileToolStripMenuItem,
            this.changePrivateKeyPasswordToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(40, 20);
            this.fileToolStripMenuItem.Text = "File";
            // 
            // createClientFileToolStripMenuItem
            // 
            this.createClientFileToolStripMenuItem.Name = "createClientFileToolStripMenuItem";
            this.createClientFileToolStripMenuItem.Size = new System.Drawing.Size(273, 22);
            this.createClientFileToolStripMenuItem.Text = "Create client file ...";
            this.createClientFileToolStripMenuItem.Click += new System.EventHandler(this.createClientFileToolStripMenuItem_Click);
            // 
            // changePrivateKeyPasswordToolStripMenuItem
            // 
            this.changePrivateKeyPasswordToolStripMenuItem.Name = "changePrivateKeyPasswordToolStripMenuItem";
            this.changePrivateKeyPasswordToolStripMenuItem.Size = new System.Drawing.Size(273, 22);
            this.changePrivateKeyPasswordToolStripMenuItem.Text = "Change private key password ...";
            // 
            // OpenClientFile
            // 
            this.OpenClientFile.CheckFileExists = false;
            this.OpenClientFile.DefaultExt = "xml";
            this.OpenClientFile.FileName = "FreshKeeper.xml";
            this.OpenClientFile.Filter = "Xml files (*.xml)|*.xml|All files (*.*)|*.*";
            // 
            // UpdateAdministration
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(465, 480);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "UpdateAdministration";
            this.Text = "Update Administration";
            this.Load += new System.EventHandler(this.UpdateAdministration_Load);
            this.panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.PropertiesGroup.ResumeLayout(false);
            this.PropertiesGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button SaveButton;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ListBox listBox1;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.GroupBox PropertiesGroup;
        private System.Windows.Forms.TextBox UpdateVersion;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.CheckBox UpdateSecurity;
        private System.Windows.Forms.CheckBox UpdateBugfix;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox UpdateChangelog;
        private System.Windows.Forms.TextBox UpdateCaption;
        private System.Windows.Forms.ToolStripButton AddUpdateButton;
        private System.Windows.Forms.ToolStripButton DeleteUpdateButton;
        private System.Windows.Forms.Button ResetSignatureButton;
        private System.Windows.Forms.TextBox UpdateDownloadUrls;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.OpenFileDialog SelectPackageFile;
        private System.Windows.Forms.ComboBox UpdateArchitecture;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.ErrorProvider errorProvider;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem createClientFileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem changePrivateKeyPasswordToolStripMenuItem;
        private System.Windows.Forms.DateTimePicker UpdateDate;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.ComboBox UpdateApplication;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.ComboBox UpdateLogAppend;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.OpenFileDialog OpenClientFile;
    }
}