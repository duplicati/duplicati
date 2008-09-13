#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
namespace Duplicati.Service_controls
{
    partial class TaskSettingsBase
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
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.ServicePanel = new System.Windows.Forms.Panel();
            this.sshSettings = new Duplicati.Service_controls.SSHSettings();
            this.s3Settings = new Duplicati.Service_controls.S3Settings();
            this.fileSettings = new Duplicati.Service_controls.FileSettings();
            this.EditFilterButton = new System.Windows.Forms.Button();
            this.BrowseSourceFolder = new System.Windows.Forms.Button();
            this.SourceFolder = new System.Windows.Forms.TextBox();
            this.GenerateSignatureKey = new System.Windows.Forms.Button();
            this.GenerateEncryptionKey = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.ServiceTypeCombo = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.SignatureKey = new System.Windows.Forms.TextBox();
            this.SignatureCheckbox = new System.Windows.Forms.CheckBox();
            this.EncrytionKey = new System.Windows.Forms.TextBox();
            this.EncryptionCheckbox = new System.Windows.Forms.CheckBox();
            this.groupBox1.SuspendLayout();
            this.ServicePanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.ServicePanel);
            this.groupBox1.Controls.Add(this.EditFilterButton);
            this.groupBox1.Controls.Add(this.BrowseSourceFolder);
            this.groupBox1.Controls.Add(this.SourceFolder);
            this.groupBox1.Controls.Add(this.GenerateSignatureKey);
            this.groupBox1.Controls.Add(this.GenerateEncryptionKey);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.ServiceTypeCombo);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.SignatureKey);
            this.groupBox1.Controls.Add(this.SignatureCheckbox);
            this.groupBox1.Controls.Add(this.EncrytionKey);
            this.groupBox1.Controls.Add(this.EncryptionCheckbox);
            this.groupBox1.Location = new System.Drawing.Point(0, 0);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(280, 416);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Backup properties";
            // 
            // ServicePanel
            // 
            this.ServicePanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.ServicePanel.Controls.Add(this.sshSettings);
            this.ServicePanel.Controls.Add(this.s3Settings);
            this.ServicePanel.Controls.Add(this.fileSettings);
            this.ServicePanel.Location = new System.Drawing.Point(0, 144);
            this.ServicePanel.Name = "ServicePanel";
            this.ServicePanel.Size = new System.Drawing.Size(280, 272);
            this.ServicePanel.TabIndex = 15;
            // 
            // sshSettings
            // 
            this.sshSettings.Location = new System.Drawing.Point(96, 32);
            this.sshSettings.Name = "sshSettings";
            this.sshSettings.Size = new System.Drawing.Size(88, 56);
            this.sshSettings.TabIndex = 14;
            this.sshSettings.Visible = false;
            // 
            // s3Settings
            // 
            this.s3Settings.Location = new System.Drawing.Point(176, 32);
            this.s3Settings.Name = "s3Settings";
            this.s3Settings.Size = new System.Drawing.Size(96, 56);
            this.s3Settings.TabIndex = 13;
            this.s3Settings.Visible = false;
            // 
            // fileSettings
            // 
            this.fileSettings.Location = new System.Drawing.Point(16, 32);
            this.fileSettings.Name = "fileSettings";
            this.fileSettings.Size = new System.Drawing.Size(88, 64);
            this.fileSettings.TabIndex = 12;
            this.fileSettings.Visible = false;
            // 
            // EditFilterButton
            // 
            this.EditFilterButton.Location = new System.Drawing.Point(168, 88);
            this.EditFilterButton.Name = "EditFilterButton";
            this.EditFilterButton.Size = new System.Drawing.Size(96, 24);
            this.EditFilterButton.TabIndex = 11;
            this.EditFilterButton.Text = "Edit filter";
            this.EditFilterButton.UseVisualStyleBackColor = true;
            this.EditFilterButton.Click += new System.EventHandler(this.EditFilterButton_Click);
            // 
            // BrowseSourceFolder
            // 
            this.BrowseSourceFolder.Location = new System.Drawing.Point(240, 64);
            this.BrowseSourceFolder.Name = "BrowseSourceFolder";
            this.BrowseSourceFolder.Size = new System.Drawing.Size(24, 20);
            this.BrowseSourceFolder.TabIndex = 10;
            this.BrowseSourceFolder.Text = "...";
            this.BrowseSourceFolder.UseVisualStyleBackColor = true;
            this.BrowseSourceFolder.Click += new System.EventHandler(this.BrowseSourceFolder_Click);
            // 
            // SourceFolder
            // 
            this.SourceFolder.Location = new System.Drawing.Point(112, 64);
            this.SourceFolder.Name = "SourceFolder";
            this.SourceFolder.Size = new System.Drawing.Size(128, 20);
            this.SourceFolder.TabIndex = 9;
            this.SourceFolder.TextChanged += new System.EventHandler(this.SourceFolder_TextChanged);
            // 
            // GenerateSignatureKey
            // 
            this.GenerateSignatureKey.Location = new System.Drawing.Point(240, 40);
            this.GenerateSignatureKey.Name = "GenerateSignatureKey";
            this.GenerateSignatureKey.Size = new System.Drawing.Size(24, 20);
            this.GenerateSignatureKey.TabIndex = 8;
            this.GenerateSignatureKey.UseVisualStyleBackColor = true;
            this.GenerateSignatureKey.Click += new System.EventHandler(this.GenerateSignatureKey_Click);
            // 
            // GenerateEncryptionKey
            // 
            this.GenerateEncryptionKey.Location = new System.Drawing.Point(240, 16);
            this.GenerateEncryptionKey.Name = "GenerateEncryptionKey";
            this.GenerateEncryptionKey.Size = new System.Drawing.Size(24, 20);
            this.GenerateEncryptionKey.TabIndex = 7;
            this.GenerateEncryptionKey.UseVisualStyleBackColor = true;
            this.GenerateEncryptionKey.Click += new System.EventHandler(this.GenerateEncryptionKey_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 64);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(87, 13);
            this.label2.TabIndex = 6;
            this.label2.Text = "Folder to backup";
            // 
            // ServiceTypeCombo
            // 
            this.ServiceTypeCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ServiceTypeCombo.FormattingEnabled = true;
            this.ServiceTypeCombo.Items.AddRange(new object[] {
            "Backup to network folder or drive",
            "Backup via SSH ",
            "Backup via FTP",
            "Backup to Amazon S3",
            "Backup to WebDAV",
            "Custom "});
            this.ServiceTypeCombo.Location = new System.Drawing.Point(112, 120);
            this.ServiceTypeCombo.Name = "ServiceTypeCombo";
            this.ServiceTypeCombo.Size = new System.Drawing.Size(160, 21);
            this.ServiceTypeCombo.TabIndex = 5;
            this.ServiceTypeCombo.SelectedIndexChanged += new System.EventHandler(this.ServiceTypeCombo_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(8, 120);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(81, 13);
            this.label1.TabIndex = 4;
            this.label1.Text = "Backup service";
            // 
            // SignatureKey
            // 
            this.SignatureKey.Location = new System.Drawing.Point(112, 40);
            this.SignatureKey.Name = "SignatureKey";
            this.SignatureKey.Size = new System.Drawing.Size(128, 20);
            this.SignatureKey.TabIndex = 3;
            this.SignatureKey.TextChanged += new System.EventHandler(this.SignatureKey_TextChanged);
            // 
            // SignatureCheckbox
            // 
            this.SignatureCheckbox.AutoSize = true;
            this.SignatureCheckbox.Location = new System.Drawing.Point(8, 40);
            this.SignatureCheckbox.Name = "SignatureCheckbox";
            this.SignatureCheckbox.Size = new System.Drawing.Size(86, 17);
            this.SignatureCheckbox.TabIndex = 2;
            this.SignatureCheckbox.Text = "Sign backup";
            this.SignatureCheckbox.UseVisualStyleBackColor = true;
            // 
            // EncrytionKey
            // 
            this.EncrytionKey.Location = new System.Drawing.Point(112, 16);
            this.EncrytionKey.Name = "EncrytionKey";
            this.EncrytionKey.Size = new System.Drawing.Size(128, 20);
            this.EncrytionKey.TabIndex = 1;
            this.EncrytionKey.TextChanged += new System.EventHandler(this.EncrytionKey_TextChanged);
            // 
            // EncryptionCheckbox
            // 
            this.EncryptionCheckbox.AutoSize = true;
            this.EncryptionCheckbox.Location = new System.Drawing.Point(8, 16);
            this.EncryptionCheckbox.Name = "EncryptionCheckbox";
            this.EncryptionCheckbox.Size = new System.Drawing.Size(101, 17);
            this.EncryptionCheckbox.TabIndex = 0;
            this.EncryptionCheckbox.Text = "Encrypt backup";
            this.EncryptionCheckbox.UseVisualStyleBackColor = true;
            // 
            // TaskSettingsBase
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox1);
            this.Name = "TaskSettingsBase";
            this.Size = new System.Drawing.Size(283, 418);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ServicePanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox SignatureKey;
        private System.Windows.Forms.CheckBox SignatureCheckbox;
        private System.Windows.Forms.TextBox EncrytionKey;
        private System.Windows.Forms.CheckBox EncryptionCheckbox;
        private System.Windows.Forms.ComboBox ServiceTypeCombo;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button BrowseSourceFolder;
        private System.Windows.Forms.TextBox SourceFolder;
        private System.Windows.Forms.Button GenerateSignatureKey;
        private System.Windows.Forms.Button GenerateEncryptionKey;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button EditFilterButton;
        private SSHSettings sshSettings;
        private S3Settings s3Settings;
        private FileSettings fileSettings;
        private System.Windows.Forms.Panel ServicePanel;
    }
}
