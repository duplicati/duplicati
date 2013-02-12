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
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.pendingView = new System.Windows.Forms.ListView();
            this.columnHeader4 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader5 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.pendingTasks = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.runBackupNowToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.label5 = new System.Windows.Forms.Label();
            this.recentBackups = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader3 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.viewLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.viewFilesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.runBackupNowToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            this.label1_2 = new System.Windows.Forms.Label();
            this.SubProgressBar = new System.Windows.Forms.ProgressBar();
            this.WorkProgressbar = new System.Windows.Forms.ProgressBar();
            this.ProgressMessage = new System.Windows.Forms.Label();
            this.statusImage = new System.Windows.Forms.PictureBox();
            this.backupTasks = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.pauseBackupToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pausePeriodMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration05Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration15Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration30Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.PauseDuration60Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.stopBackupToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.CurrentStatus = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.wizardLink = new System.Windows.Forms.LinkLabel();
            this.linkLabel1 = new System.Windows.Forms.LinkLabel();
            this.wizardHelpLabel = new System.Windows.Forms.Label();
            this.optionsLabel = new System.Windows.Forms.Label();
            this.simplePanel = new System.Windows.Forms.Panel();
            this.QuickActions = new System.Windows.Forms.ComboBox();
            this.quickLabel = new System.Windows.Forms.Label();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.pendingTasks.SuspendLayout();
            this.contextMenuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.statusImage)).BeginInit();
            this.backupTasks.SuspendLayout();
            this.simplePanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitContainer1
            // 
            resources.ApplyResources(this.splitContainer1, "splitContainer1");
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.pendingView);
            this.splitContainer1.Panel1.Controls.Add(this.label5);
            resources.ApplyResources(this.splitContainer1.Panel1, "splitContainer1.Panel1");
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.recentBackups);
            this.splitContainer1.Panel2.Controls.Add(this.label1_2);
            resources.ApplyResources(this.splitContainer1.Panel2, "splitContainer1.Panel2");
            // 
            // pendingView
            // 
            this.pendingView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader4,
            this.columnHeader5});
            this.pendingView.ContextMenuStrip = this.pendingTasks;
            resources.ApplyResources(this.pendingView, "pendingView");
            this.pendingView.FullRowSelect = true;
            this.pendingView.MultiSelect = false;
            this.pendingView.Name = "pendingView";
            this.pendingView.ShowItemToolTips = true;
            this.pendingView.UseCompatibleStateImageBehavior = false;
            this.pendingView.View = System.Windows.Forms.View.Details;
            // 
            // columnHeader4
            // 
            resources.ApplyResources(this.columnHeader4, "columnHeader4");
            // 
            // columnHeader5
            // 
            resources.ApplyResources(this.columnHeader5, "columnHeader5");
            // 
            // pendingTasks
            // 
            this.pendingTasks.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.runBackupNowToolStripMenuItem1});
            this.pendingTasks.Name = "pendingTasks";
            resources.ApplyResources(this.pendingTasks, "pendingTasks");
            // 
            // runBackupNowToolStripMenuItem1
            // 
            this.runBackupNowToolStripMenuItem1.Image = global::Duplicati.GUI.Properties.Resources.Play;
            this.runBackupNowToolStripMenuItem1.Name = "runBackupNowToolStripMenuItem1";
            resources.ApplyResources(this.runBackupNowToolStripMenuItem1, "runBackupNowToolStripMenuItem1");
            this.runBackupNowToolStripMenuItem1.Click += new System.EventHandler(this.runBackupNowToolStripMenuItem1_Click);
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // recentBackups
            // 
            this.recentBackups.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3});
            this.recentBackups.ContextMenuStrip = this.contextMenuStrip;
            resources.ApplyResources(this.recentBackups, "recentBackups");
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
            this.viewFilesToolStripMenuItem,
            this.runBackupNowToolStripMenuItem});
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
            // runBackupNowToolStripMenuItem
            // 
            this.runBackupNowToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.Play;
            this.runBackupNowToolStripMenuItem.Name = "runBackupNowToolStripMenuItem";
            resources.ApplyResources(this.runBackupNowToolStripMenuItem, "runBackupNowToolStripMenuItem");
            this.runBackupNowToolStripMenuItem.Click += new System.EventHandler(this.runBackupNowToolStripMenuItem_Click);
            // 
            // imageList
            // 
            this.imageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            resources.ApplyResources(this.imageList, "imageList");
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // label1_2
            // 
            resources.ApplyResources(this.label1_2, "label1_2");
            this.label1_2.Name = "label1_2";
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
            // statusImage
            // 
            this.statusImage.ContextMenuStrip = this.backupTasks;
            resources.ApplyResources(this.statusImage, "statusImage");
            this.statusImage.Name = "statusImage";
            this.statusImage.TabStop = false;
            this.statusImage.Click += new System.EventHandler(this.statusImage_Click);
            this.statusImage.DoubleClick += new System.EventHandler(this.statusImage_DoubleClick);
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
            // CurrentStatus
            // 
            resources.ApplyResources(this.CurrentStatus, "CurrentStatus");
            this.CurrentStatus.Name = "CurrentStatus";
            // 
            // wizardLink
            // 
            resources.ApplyResources(this.wizardLink, "wizardLink");
            this.wizardLink.Name = "wizardLink";
            this.wizardLink.TabStop = true;
            this.wizardLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.wizardLink_LinkClicked);
            // 
            // linkLabel1
            // 
            resources.ApplyResources(this.linkLabel1, "linkLabel1");
            this.linkLabel1.Name = "linkLabel1";
            this.linkLabel1.TabStop = true;
            this.linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
            // 
            // wizardHelpLabel
            // 
            resources.ApplyResources(this.wizardHelpLabel, "wizardHelpLabel");
            this.wizardHelpLabel.Name = "wizardHelpLabel";
            // 
            // optionsLabel
            // 
            resources.ApplyResources(this.optionsLabel, "optionsLabel");
            this.optionsLabel.Name = "optionsLabel";
            // 
            // simplePanel
            // 
            this.simplePanel.Controls.Add(this.QuickActions);
            this.simplePanel.Controls.Add(this.quickLabel);
            this.simplePanel.Controls.Add(this.CurrentStatus);
            this.simplePanel.Controls.Add(this.optionsLabel);
            this.simplePanel.Controls.Add(this.statusImage);
            this.simplePanel.Controls.Add(this.wizardHelpLabel);
            this.simplePanel.Controls.Add(this.ProgressMessage);
            this.simplePanel.Controls.Add(this.linkLabel1);
            this.simplePanel.Controls.Add(this.WorkProgressbar);
            this.simplePanel.Controls.Add(this.wizardLink);
            this.simplePanel.Controls.Add(this.SubProgressBar);
            resources.ApplyResources(this.simplePanel, "simplePanel");
            this.simplePanel.Name = "simplePanel";
            // 
            // QuickActions
            // 
            this.QuickActions.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.QuickActions.FormattingEnabled = true;
            resources.ApplyResources(this.QuickActions, "QuickActions");
            this.QuickActions.Name = "QuickActions";
            this.QuickActions.SelectedIndexChanged += new System.EventHandler(this.QuickActions_SelectedIndexChanged);
            // 
            // quickLabel
            // 
            resources.ApplyResources(this.quickLabel, "quickLabel");
            this.quickLabel.Name = "quickLabel";
            // 
            // ServiceStatus
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.simplePanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ServiceStatus";
            this.Activated += new System.EventHandler(this.ServiceStatus_Activated);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ServiceStatus_FormClosing);
            this.Load += new System.EventHandler(this.ServiceStatus_Load);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.ServiceStatus_KeyUp);
            this.Resize += new System.EventHandler(this.ServiceStatus_Resize);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            this.splitContainer1.ResumeLayout(false);
            this.pendingTasks.ResumeLayout(false);
            this.contextMenuStrip.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.statusImage)).EndInit();
            this.backupTasks.ResumeLayout(false);
            this.simplePanel.ResumeLayout(false);
            this.simplePanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label CurrentStatus;
        private System.Windows.Forms.Label label1_2;
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
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.LinkLabel wizardLink;
        private System.Windows.Forms.LinkLabel linkLabel1;
        private System.Windows.Forms.Label wizardHelpLabel;
        private System.Windows.Forms.Label optionsLabel;
        private System.Windows.Forms.Panel simplePanel;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.ListView pendingView;
        private System.Windows.Forms.ColumnHeader columnHeader4;
        private System.Windows.Forms.ColumnHeader columnHeader5;
        private System.Windows.Forms.ComboBox QuickActions;
        private System.Windows.Forms.Label quickLabel;
        private System.Windows.Forms.ToolStripMenuItem runBackupNowToolStripMenuItem;
        private System.Windows.Forms.ContextMenuStrip pendingTasks;
        private System.Windows.Forms.ToolStripMenuItem runBackupNowToolStripMenuItem1;
    }
}