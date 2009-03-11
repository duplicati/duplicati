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
namespace Duplicati.GUI
{
    partial class ServiceStatus
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ServiceStatus));
            this.label3 = new System.Windows.Forms.Label();
            this.scheduledBackups = new System.Windows.Forms.ListBox();
            this.pendingBackups = new System.Windows.Forms.ListBox();
            this.label5 = new System.Windows.Forms.Label();
            this.advancedPanel = new System.Windows.Forms.Panel();
            this.simplePanel = new System.Windows.Forms.Panel();
            this.WorkProgressbar = new System.Windows.Forms.ProgressBar();
            this.ProgressMessage = new System.Windows.Forms.Label();
            this.recentBackups = new System.Windows.Forms.ListView();
            this.columnHeader1 = new System.Windows.Forms.ColumnHeader();
            this.columnHeader2 = new System.Windows.Forms.ColumnHeader();
            this.columnHeader3 = new System.Windows.Forms.ColumnHeader();
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            this.statusImage = new System.Windows.Forms.PictureBox();
            this.ShowAdvanced = new System.Windows.Forms.Button();
            this.CurrentStatus = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.SubProgressBar = new System.Windows.Forms.ProgressBar();
            this.advancedPanel.SuspendLayout();
            this.simplePanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.statusImage)).BeginInit();
            this.SuspendLayout();
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(0, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(102, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "Scheduled backups";
            // 
            // scheduledBackups
            // 
            this.scheduledBackups.FormattingEnabled = true;
            this.scheduledBackups.Location = new System.Drawing.Point(0, 16);
            this.scheduledBackups.Name = "scheduledBackups";
            this.scheduledBackups.Size = new System.Drawing.Size(320, 56);
            this.scheduledBackups.TabIndex = 3;
            // 
            // pendingBackups
            // 
            this.pendingBackups.FormattingEnabled = true;
            this.pendingBackups.Location = new System.Drawing.Point(0, 96);
            this.pendingBackups.Name = "pendingBackups";
            this.pendingBackups.Size = new System.Drawing.Size(320, 56);
            this.pendingBackups.TabIndex = 7;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(0, 80);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(90, 13);
            this.label5.TabIndex = 6;
            this.label5.Text = "Pending backups";
            // 
            // advancedPanel
            // 
            this.advancedPanel.Controls.Add(this.scheduledBackups);
            this.advancedPanel.Controls.Add(this.label3);
            this.advancedPanel.Controls.Add(this.pendingBackups);
            this.advancedPanel.Controls.Add(this.label5);
            this.advancedPanel.Location = new System.Drawing.Point(8, 8);
            this.advancedPanel.Name = "advancedPanel";
            this.advancedPanel.Size = new System.Drawing.Size(328, 160);
            this.advancedPanel.TabIndex = 10;
            this.advancedPanel.Visible = false;
            // 
            // simplePanel
            // 
            this.simplePanel.Controls.Add(this.SubProgressBar);
            this.simplePanel.Controls.Add(this.WorkProgressbar);
            this.simplePanel.Controls.Add(this.ProgressMessage);
            this.simplePanel.Controls.Add(this.recentBackups);
            this.simplePanel.Controls.Add(this.statusImage);
            this.simplePanel.Controls.Add(this.ShowAdvanced);
            this.simplePanel.Controls.Add(this.CurrentStatus);
            this.simplePanel.Controls.Add(this.label1);
            this.simplePanel.Location = new System.Drawing.Point(8, 8);
            this.simplePanel.Name = "simplePanel";
            this.simplePanel.Size = new System.Drawing.Size(328, 160);
            this.simplePanel.TabIndex = 11;
            // 
            // WorkProgressbar
            // 
            this.WorkProgressbar.Location = new System.Drawing.Point(40, 32);
            this.WorkProgressbar.Name = "WorkProgressbar";
            this.WorkProgressbar.Size = new System.Drawing.Size(288, 16);
            this.WorkProgressbar.TabIndex = 17;
            this.WorkProgressbar.Visible = false;
            // 
            // ProgressMessage
            // 
            this.ProgressMessage.AutoEllipsis = true;
            this.ProgressMessage.AutoSize = true;
            this.ProgressMessage.Location = new System.Drawing.Point(40, 16);
            this.ProgressMessage.MaximumSize = new System.Drawing.Size(288, 13);
            this.ProgressMessage.Name = "ProgressMessage";
            this.ProgressMessage.Size = new System.Drawing.Size(35, 13);
            this.ProgressMessage.TabIndex = 16;
            this.ProgressMessage.Text = "label2";
            this.ProgressMessage.Visible = false;
            this.ProgressMessage.TextChanged += new System.EventHandler(this.ProgressMessage_TextChanged);
            // 
            // recentBackups
            // 
            this.recentBackups.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3});
            this.recentBackups.FullRowSelect = true;
            this.recentBackups.Location = new System.Drawing.Point(0, 80);
            this.recentBackups.MultiSelect = false;
            this.recentBackups.Name = "recentBackups";
            this.recentBackups.Size = new System.Drawing.Size(328, 80);
            this.recentBackups.SmallImageList = this.imageList;
            this.recentBackups.TabIndex = 15;
            this.recentBackups.UseCompatibleStateImageBehavior = false;
            this.recentBackups.View = System.Windows.Forms.View.Details;
            this.recentBackups.DoubleClick += new System.EventHandler(this.recentBackups_DoubleClick);
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "Time";
            this.columnHeader1.Width = 116;
            // 
            // columnHeader2
            // 
            this.columnHeader2.Text = "Action";
            this.columnHeader2.Width = 96;
            // 
            // columnHeader3
            // 
            this.columnHeader3.Text = "Size";
            this.columnHeader3.Width = 75;
            // 
            // imageList
            // 
            this.imageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList.ImageStream")));
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList.Images.SetKeyName(0, "OK");
            this.imageList.Images.SetKeyName(1, "Warning");
            this.imageList.Images.SetKeyName(2, "Error");
            // 
            // statusImage
            // 
            this.statusImage.Location = new System.Drawing.Point(0, 0);
            this.statusImage.Name = "statusImage";
            this.statusImage.Size = new System.Drawing.Size(32, 32);
            this.statusImage.TabIndex = 14;
            this.statusImage.TabStop = false;
            // 
            // ShowAdvanced
            // 
            this.ShowAdvanced.Location = new System.Drawing.Point(232, 56);
            this.ShowAdvanced.Name = "ShowAdvanced";
            this.ShowAdvanced.Size = new System.Drawing.Size(96, 24);
            this.ShowAdvanced.TabIndex = 13;
            this.ShowAdvanced.Text = "Advanced >>>";
            this.ShowAdvanced.UseVisualStyleBackColor = true;
            this.ShowAdvanced.Click += new System.EventHandler(this.ShowAdvanced_Click);
            // 
            // CurrentStatus
            // 
            this.CurrentStatus.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CurrentStatus.Location = new System.Drawing.Point(40, 0);
            this.CurrentStatus.Name = "CurrentStatus";
            this.CurrentStatus.Size = new System.Drawing.Size(288, 16);
            this.CurrentStatus.TabIndex = 12;
            this.CurrentStatus.Text = "Waiting for next backup";
            this.CurrentStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(0, 64);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(145, 13);
            this.label1.TabIndex = 10;
            this.label1.Text = "Recently completed backups";
            // 
            // SubProgressBar
            // 
            this.SubProgressBar.Location = new System.Drawing.Point(80, 16);
            this.SubProgressBar.Name = "SubProgressBar";
            this.SubProgressBar.Size = new System.Drawing.Size(248, 16);
            this.SubProgressBar.TabIndex = 18;
            this.SubProgressBar.Visible = false;
            // 
            // ServiceStatus
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(345, 175);
            this.Controls.Add(this.simplePanel);
            this.Controls.Add(this.advancedPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ServiceStatus";
            this.Text = "Duplicati Status";
            this.Load += new System.EventHandler(this.ServiceStatus_Load);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.ServiceStatus_KeyUp);
            this.advancedPanel.ResumeLayout(false);
            this.advancedPanel.PerformLayout();
            this.simplePanel.ResumeLayout(false);
            this.simplePanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.statusImage)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ListBox scheduledBackups;
        private System.Windows.Forms.ListBox pendingBackups;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Panel advancedPanel;
        private System.Windows.Forms.Panel simplePanel;
        private System.Windows.Forms.Label CurrentStatus;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button ShowAdvanced;
        private System.Windows.Forms.PictureBox statusImage;
        private System.Windows.Forms.ListView recentBackups;
        private System.Windows.Forms.ColumnHeader columnHeader1;
        private System.Windows.Forms.ColumnHeader columnHeader2;
        private System.Windows.Forms.ColumnHeader columnHeader3;
        private System.Windows.Forms.ImageList imageList;
        private System.Windows.Forms.ProgressBar WorkProgressbar;
        private System.Windows.Forms.Label ProgressMessage;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.ProgressBar SubProgressBar;
    }
}