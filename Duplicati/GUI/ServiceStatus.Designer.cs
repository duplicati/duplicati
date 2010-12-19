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
            this.SubProgressBar = new System.Windows.Forms.ProgressBar();
            this.WorkProgressbar = new System.Windows.Forms.ProgressBar();
            this.ProgressMessage = new System.Windows.Forms.Label();
            this.recentBackups = new System.Windows.Forms.ListView();
            this.columnHeader1 = new System.Windows.Forms.ColumnHeader();
            this.columnHeader2 = new System.Windows.Forms.ColumnHeader();
            this.columnHeader3 = new System.Windows.Forms.ColumnHeader();
            this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.viewLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.viewFilesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            this.statusImage = new System.Windows.Forms.PictureBox();
            this.backupTasks = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.pauseBackupToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pausePeriodMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration05Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration15Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration30Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration60Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.stopBackupToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ShowAdvanced = new System.Windows.Forms.Button();
            this.CurrentStatus = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.advancedPanel.SuspendLayout();
            this.simplePanel.SuspendLayout();
            this.contextMenuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.statusImage)).BeginInit();
            this.backupTasks.SuspendLayout();
            this.SuspendLayout();
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // scheduledBackups
            // 
            this.scheduledBackups.FormattingEnabled = true;
            resources.ApplyResources(this.scheduledBackups, "scheduledBackups");
            this.scheduledBackups.Name = "scheduledBackups";
            // 
            // pendingBackups
            // 
            this.pendingBackups.FormattingEnabled = true;
            resources.ApplyResources(this.pendingBackups, "pendingBackups");
            this.pendingBackups.Name = "pendingBackups";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // advancedPanel
            // 
            this.advancedPanel.Controls.Add(this.scheduledBackups);
            this.advancedPanel.Controls.Add(this.label3);
            this.advancedPanel.Controls.Add(this.pendingBackups);
            this.advancedPanel.Controls.Add(this.label5);
            resources.ApplyResources(this.advancedPanel, "advancedPanel");
            this.advancedPanel.Name = "advancedPanel";
            // 
            // simplePanel
            // 
            resources.ApplyResources(this.simplePanel, "simplePanel");
            this.simplePanel.Controls.Add(this.SubProgressBar);
            this.simplePanel.Controls.Add(this.WorkProgressbar);
            this.simplePanel.Controls.Add(this.ProgressMessage);
            this.simplePanel.Controls.Add(this.recentBackups);
            this.simplePanel.Controls.Add(this.statusImage);
            this.simplePanel.Controls.Add(this.ShowAdvanced);
            this.simplePanel.Controls.Add(this.CurrentStatus);
            this.simplePanel.Controls.Add(this.label1);
            this.simplePanel.Name = "simplePanel";
            // 
            // SubProgressBar
            // 
            resources.ApplyResources(this.SubProgressBar, "SubProgressBar");
            this.SubProgressBar.Name = "SubProgressBar";
            // 
            // WorkProgressbar
            // 
            resources.ApplyResources(this.WorkProgressbar, "WorkProgressbar");
            this.WorkProgressbar.Name = "WorkProgressbar";
            // 
            // ProgressMessage
            // 
            this.ProgressMessage.AutoEllipsis = true;
            resources.ApplyResources(this.ProgressMessage, "ProgressMessage");
            this.ProgressMessage.MaximumSize = new System.Drawing.Size(288, 13);
            this.ProgressMessage.Name = "ProgressMessage";
            this.ProgressMessage.TextChanged += new System.EventHandler(this.ProgressMessage_TextChanged);
            // 
            // recentBackups
            // 
            resources.ApplyResources(this.recentBackups, "recentBackups");
            this.recentBackups.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3});
            this.recentBackups.ContextMenuStrip = this.contextMenuStrip;
            this.recentBackups.FullRowSelect = true;
            this.recentBackups.MultiSelect = false;
            this.recentBackups.Name = "recentBackups";
            this.recentBackups.ShowItemToolTips = true;
            this.recentBackups.SmallImageList = this.imageList;
            this.recentBackups.UseCompatibleStateImageBehavior = false;
            this.recentBackups.View = System.Windows.Forms.View.Details;
            this.recentBackups.SelectedIndexChanged += new System.EventHandler(this.recentBackups_SelectedIndexChanged);
            this.recentBackups.DoubleClick += new System.EventHandler(this.recentBackups_DoubleClick);
            // 
            // columnHeader1
            // 
            resources.ApplyResources(this.columnHeader1, "columnHeader1");
            // 
            // columnHeader2
            // 
            resources.ApplyResources(this.columnHeader2, "columnHeader2");
            // 
            // columnHeader3
            // 
            resources.ApplyResources(this.columnHeader3, "columnHeader3");
            // 
            // contextMenuStrip
            // 
            this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.viewLogToolStripMenuItem,
            this.viewFilesToolStripMenuItem});
            this.contextMenuStrip.Name = "contextMenuStrip";
            resources.ApplyResources(this.contextMenuStrip, "contextMenuStrip");
            // 
            // viewLogToolStripMenuItem
            // 
            resources.ApplyResources(this.viewLogToolStripMenuItem, "viewLogToolStripMenuItem");
            this.viewLogToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.LogMenuIcon;
            this.viewLogToolStripMenuItem.Name = "viewLogToolStripMenuItem";
            this.viewLogToolStripMenuItem.Click += new System.EventHandler(this.viewLogToolStripMenuItem_Click);
            // 
            // viewFilesToolStripMenuItem
            // 
            this.viewFilesToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.ExamineMenuIcon;
            this.viewFilesToolStripMenuItem.Name = "viewFilesToolStripMenuItem";
            resources.ApplyResources(this.viewFilesToolStripMenuItem, "viewFilesToolStripMenuItem");
            this.viewFilesToolStripMenuItem.Click += new System.EventHandler(this.viewFilesToolStripMenuItem_Click);
            // 
            // imageList
            // 
            this.imageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            resources.ApplyResources(this.imageList, "imageList");
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // statusImage
            // 
            this.statusImage.ContextMenuStrip = this.backupTasks;
            resources.ApplyResources(this.statusImage, "statusImage");
            this.statusImage.Name = "statusImage";
            this.statusImage.TabStop = false;
            this.statusImage.DoubleClick += new System.EventHandler(this.statusImage_DoubleClick);
            this.statusImage.Click += new System.EventHandler(this.statusImage_Click);
            // 
            // backupTasks
            // 
            this.backupTasks.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.pauseBackupToolStripMenuItem,
            this.pausePeriodMenuItem,
            this.stopBackupToolStripMenuItem});
            this.backupTasks.Name = "backupTasks";
            resources.ApplyResources(this.backupTasks, "backupTasks");
            // 
            // pauseBackupToolStripMenuItem
            // 
            this.pauseBackupToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.Pause;
            this.pauseBackupToolStripMenuItem.Name = "pauseBackupToolStripMenuItem";
            resources.ApplyResources(this.pauseBackupToolStripMenuItem, "pauseBackupToolStripMenuItem");
            this.pauseBackupToolStripMenuItem.Click += new System.EventHandler(this.pauseBackupToolStripMenuItem_Click);
            // 
            // pausePeriodMenuItem
            // 
            this.pausePeriodMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.PauseDuration05Menu,
            this.PauseDuration15Menu,
            this.PauseDuration30Menu,
            this.PauseDuration60Menu});
            this.pausePeriodMenuItem.Name = "pausePeriodMenuItem";
            resources.ApplyResources(this.pausePeriodMenuItem, "pausePeriodMenuItem");
            // 
            // PauseDuration05Menu
            // 
            this.PauseDuration05Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock05;
            this.PauseDuration05Menu.Name = "PauseDuration05Menu";
            resources.ApplyResources(this.PauseDuration05Menu, "PauseDuration05Menu");
            this.PauseDuration05Menu.Tag = "5m";
            this.PauseDuration05Menu.Click += new System.EventHandler(this.PauseDurationMenu_Click);
            // 
            // PauseDuration15Menu
            // 
            this.PauseDuration15Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock15;
            this.PauseDuration15Menu.Name = "PauseDuration15Menu";
            resources.ApplyResources(this.PauseDuration15Menu, "PauseDuration15Menu");
            this.PauseDuration15Menu.Tag = "15m";
            this.PauseDuration15Menu.Click += new System.EventHandler(this.PauseDurationMenu_Click);
            // 
            // PauseDuration30Menu
            // 
            this.PauseDuration30Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock30;
            this.PauseDuration30Menu.Name = "PauseDuration30Menu";
            resources.ApplyResources(this.PauseDuration30Menu, "PauseDuration30Menu");
            this.PauseDuration30Menu.Tag = "30m";
            this.PauseDuration30Menu.Click += new System.EventHandler(this.PauseDurationMenu_Click);
            // 
            // PauseDuration60Menu
            // 
            this.PauseDuration60Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock60;
            this.PauseDuration60Menu.Name = "PauseDuration60Menu";
            resources.ApplyResources(this.PauseDuration60Menu, "PauseDuration60Menu");
            this.PauseDuration60Menu.Tag = "1h";
            this.PauseDuration60Menu.Click += new System.EventHandler(this.PauseDurationMenu_Click);
            // 
            // stopBackupToolStripMenuItem
            // 
            this.stopBackupToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.Stop;
            this.stopBackupToolStripMenuItem.Name = "stopBackupToolStripMenuItem";
            resources.ApplyResources(this.stopBackupToolStripMenuItem, "stopBackupToolStripMenuItem");
            this.stopBackupToolStripMenuItem.Click += new System.EventHandler(this.stopBackupToolStripMenuItem_Click);
            // 
            // ShowAdvanced
            // 
            resources.ApplyResources(this.ShowAdvanced, "ShowAdvanced");
            this.ShowAdvanced.Name = "ShowAdvanced";
            this.ShowAdvanced.UseVisualStyleBackColor = true;
            this.ShowAdvanced.Click += new System.EventHandler(this.ShowAdvanced_Click);
            // 
            // CurrentStatus
            // 
            resources.ApplyResources(this.CurrentStatus, "CurrentStatus");
            this.CurrentStatus.Name = "CurrentStatus";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // ServiceStatus
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.simplePanel);
            this.Controls.Add(this.advancedPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ServiceStatus";
            this.Load += new System.EventHandler(this.ServiceStatus_Load);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.ServiceStatus_KeyUp);
            this.Resize += new System.EventHandler(this.ServiceStatus_Resize);
            this.advancedPanel.ResumeLayout(false);
            this.advancedPanel.PerformLayout();
            this.simplePanel.ResumeLayout(false);
            this.simplePanel.PerformLayout();
            this.contextMenuStrip.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.statusImage)).EndInit();
            this.backupTasks.ResumeLayout(false);
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
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem viewFilesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem viewLogToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip backupTasks;
        private System.Windows.Forms.ToolStripMenuItem pauseBackupToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem stopBackupToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem pausePeriodMenuItem;
        private System.Windows.Forms.ToolStripMenuItem PauseDuration05Menu;
        private System.Windows.Forms.ToolStripMenuItem PauseDuration15Menu;
        private System.Windows.Forms.ToolStripMenuItem PauseDuration30Menu;
        private System.Windows.Forms.ToolStripMenuItem PauseDuration60Menu;
    }
}