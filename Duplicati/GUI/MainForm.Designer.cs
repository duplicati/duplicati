namespace Duplicati.GUI
{
    partial class MainForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.TrayIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.TrayMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.statusToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.wizardToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.controlToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pauseToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pausePeriodToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.DelayDuration05Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.DelayDuration15Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.DelayDuration30Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.DelayDuration60Menu = new System.Windows.Forms.ToolStripMenuItem();
            this.stopToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.throttleOptionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.quitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.label1 = new System.Windows.Forms.Label();
            this.TrayMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // TrayIcon
            // 
            this.TrayIcon.ContextMenuStrip = this.TrayMenu;
            resources.ApplyResources(this.TrayIcon, "TrayIcon");
            this.TrayIcon.BalloonTipClicked += new System.EventHandler(this.TrayIcon_BalloonTipClicked);
            this.TrayIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(this.TrayIcon_MouseClick);
            this.TrayIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.TrayIcon_MouseDoubleClick);
            // 
            // TrayMenu
            // 
            this.TrayMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusToolStripMenuItem,
            this.wizardToolStripMenuItem,
            this.toolStripSeparator1,
            this.optionsToolStripMenuItem,
            this.toolStripSeparator2,
            this.controlToolStripMenuItem,
            this.toolStripSeparator3,
            this.quitToolStripMenuItem});
            this.TrayMenu.Name = "TrayMenu";
            resources.ApplyResources(this.TrayMenu, "TrayMenu");
            this.TrayMenu.LocationChanged += new System.EventHandler(this.TrayMenu_LocationChanged);
            // 
            // statusToolStripMenuItem
            // 
            resources.ApplyResources(this.statusToolStripMenuItem, "statusToolStripMenuItem");
            this.statusToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.StatusMenuIcon;
            this.statusToolStripMenuItem.Name = "statusToolStripMenuItem";
            this.statusToolStripMenuItem.Click += new System.EventHandler(this.statusToolStripMenuItem_Click);
            // 
            // wizardToolStripMenuItem
            // 
            this.wizardToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.WizardMenuIcon;
            this.wizardToolStripMenuItem.Name = "wizardToolStripMenuItem";
            resources.ApplyResources(this.wizardToolStripMenuItem, "wizardToolStripMenuItem");
            this.wizardToolStripMenuItem.Click += new System.EventHandler(this.wizardToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            // 
            // optionsToolStripMenuItem
            // 
            this.optionsToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.SettingsMenuIcon;
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            resources.ApplyResources(this.optionsToolStripMenuItem, "optionsToolStripMenuItem");
            this.optionsToolStripMenuItem.Click += new System.EventHandler(this.optionsToolStripMenuItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
            // 
            // controlToolStripMenuItem
            // 
            this.controlToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.pauseToolStripMenuItem,
            this.pausePeriodToolStripMenuItem,
            this.stopToolStripMenuItem,
            this.throttleOptionsToolStripMenuItem});
            this.controlToolStripMenuItem.Name = "controlToolStripMenuItem";
            resources.ApplyResources(this.controlToolStripMenuItem, "controlToolStripMenuItem");
            // 
            // pauseToolStripMenuItem
            // 
            this.pauseToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.Pause;
            this.pauseToolStripMenuItem.Name = "pauseToolStripMenuItem";
            resources.ApplyResources(this.pauseToolStripMenuItem, "pauseToolStripMenuItem");
            this.pauseToolStripMenuItem.Click += new System.EventHandler(this.pauseToolStripMenuItem_Click);
            // 
            // pausePeriodToolStripMenuItem
            // 
            this.pausePeriodToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.DelayDuration05Menu,
            this.DelayDuration15Menu,
            this.DelayDuration30Menu,
            this.DelayDuration60Menu});
            this.pausePeriodToolStripMenuItem.Name = "pausePeriodToolStripMenuItem";
            resources.ApplyResources(this.pausePeriodToolStripMenuItem, "pausePeriodToolStripMenuItem");
            // 
            // DelayDuration05Menu
            // 
            this.DelayDuration05Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock05;
            this.DelayDuration05Menu.Name = "DelayDuration05Menu";
            resources.ApplyResources(this.DelayDuration05Menu, "DelayDuration05Menu");
            this.DelayDuration05Menu.Tag = "5m";
            // 
            // DelayDuration15Menu
            // 
            this.DelayDuration15Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock15;
            this.DelayDuration15Menu.Name = "DelayDuration15Menu";
            resources.ApplyResources(this.DelayDuration15Menu, "DelayDuration15Menu");
            this.DelayDuration15Menu.Tag = "15m";
            // 
            // DelayDuration30Menu
            // 
            this.DelayDuration30Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock30;
            this.DelayDuration30Menu.Name = "DelayDuration30Menu";
            resources.ApplyResources(this.DelayDuration30Menu, "DelayDuration30Menu");
            this.DelayDuration30Menu.Tag = "30m";
            // 
            // DelayDuration60Menu
            // 
            this.DelayDuration60Menu.Image = global::Duplicati.GUI.Properties.Resources.Clock60;
            this.DelayDuration60Menu.Name = "DelayDuration60Menu";
            resources.ApplyResources(this.DelayDuration60Menu, "DelayDuration60Menu");
            this.DelayDuration60Menu.Tag = "1h";
            this.DelayDuration60Menu.Click += new System.EventHandler(this.DelayDurationMenu_Click);
            // 
            // stopToolStripMenuItem
            // 
            resources.ApplyResources(this.stopToolStripMenuItem, "stopToolStripMenuItem");
            this.stopToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.Stop;
            this.stopToolStripMenuItem.Name = "stopToolStripMenuItem";
            this.stopToolStripMenuItem.Click += new System.EventHandler(this.stopToolStripMenuItem_Click);
            // 
            // throttleOptionsToolStripMenuItem
            // 
            this.throttleOptionsToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.Throttle;
            this.throttleOptionsToolStripMenuItem.Name = "throttleOptionsToolStripMenuItem";
            resources.ApplyResources(this.throttleOptionsToolStripMenuItem, "throttleOptionsToolStripMenuItem");
            this.throttleOptionsToolStripMenuItem.Click += new System.EventHandler(this.throttleOptionsToolStripMenuItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
            // 
            // quitToolStripMenuItem
            // 
            this.quitToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.CloseMenuIcon;
            this.quitToolStripMenuItem.Name = "quitToolStripMenuItem";
            resources.ApplyResources(this.quitToolStripMenuItem, "quitToolStripMenuItem");
            this.quitToolStripMenuItem.Click += new System.EventHandler(this.quitToolStripMenuItem_Click);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // MainForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "MainForm";
            this.ShowInTaskbar = false;
            this.WindowState = System.Windows.Forms.FormWindowState.Minimized;
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.TrayMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.NotifyIcon TrayIcon;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ContextMenuStrip TrayMenu;
        private System.Windows.Forms.ToolStripMenuItem statusToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem wizardToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem controlToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripMenuItem quitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem pauseToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem pausePeriodToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration05Menu;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration15Menu;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration30Menu;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration60Menu;
        private System.Windows.Forms.ToolStripMenuItem stopToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem throttleOptionsToolStripMenuItem;
    }
}