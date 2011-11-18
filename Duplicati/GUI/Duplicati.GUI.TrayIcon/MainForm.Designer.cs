namespace Duplicati.GUI.TrayIcon
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
            this.label1 = new System.Windows.Forms.Label();
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
            this.TrayIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.TrayMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label1.Location = new System.Drawing.Point(8, 8);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(123, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Duplicati dummy window";
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
            this.TrayMenu.Size = new System.Drawing.Size(126, 132);
            // 
            // statusToolStripMenuItem
            // 
            this.statusToolStripMenuItem.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.statusToolStripMenuItem.Name = "statusToolStripMenuItem";
            this.statusToolStripMenuItem.Size = new System.Drawing.Size(125, 22);
            this.statusToolStripMenuItem.Text = "Status";
            // 
            // wizardToolStripMenuItem
            // 
            this.wizardToolStripMenuItem.Name = "wizardToolStripMenuItem";
            this.wizardToolStripMenuItem.Size = new System.Drawing.Size(125, 22);
            this.wizardToolStripMenuItem.Text = "Wizard...";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(122, 6);
            // 
            // optionsToolStripMenuItem
            // 
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            this.optionsToolStripMenuItem.Size = new System.Drawing.Size(125, 22);
            this.optionsToolStripMenuItem.Text = "Options...";
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(122, 6);
            // 
            // controlToolStripMenuItem
            // 
            this.controlToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.pauseToolStripMenuItem,
            this.pausePeriodToolStripMenuItem,
            this.stopToolStripMenuItem,
            this.throttleOptionsToolStripMenuItem});
            this.controlToolStripMenuItem.Name = "controlToolStripMenuItem";
            this.controlToolStripMenuItem.Size = new System.Drawing.Size(125, 22);
            this.controlToolStripMenuItem.Text = "Control";
            // 
            // pauseToolStripMenuItem
            // 
            this.pauseToolStripMenuItem.Name = "pauseToolStripMenuItem";
            this.pauseToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
            this.pauseToolStripMenuItem.Text = "Pause";
            // 
            // pausePeriodToolStripMenuItem
            // 
            this.pausePeriodToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.DelayDuration05Menu,
            this.DelayDuration15Menu,
            this.DelayDuration30Menu,
            this.DelayDuration60Menu});
            this.pausePeriodToolStripMenuItem.Name = "pausePeriodToolStripMenuItem";
            this.pausePeriodToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
            this.pausePeriodToolStripMenuItem.Text = "Pause period";
            // 
            // DelayDuration05Menu
            // 
            this.DelayDuration05Menu.Name = "DelayDuration05Menu";
            this.DelayDuration05Menu.Size = new System.Drawing.Size(132, 22);
            this.DelayDuration05Menu.Tag = "5m";
            this.DelayDuration05Menu.Text = "5 minutes";
            // 
            // DelayDuration15Menu
            // 
            this.DelayDuration15Menu.Name = "DelayDuration15Menu";
            this.DelayDuration15Menu.Size = new System.Drawing.Size(132, 22);
            this.DelayDuration15Menu.Tag = "15m";
            this.DelayDuration15Menu.Text = "15 minutes";
            // 
            // DelayDuration30Menu
            // 
            this.DelayDuration30Menu.Name = "DelayDuration30Menu";
            this.DelayDuration30Menu.Size = new System.Drawing.Size(132, 22);
            this.DelayDuration30Menu.Tag = "30m";
            this.DelayDuration30Menu.Text = "30 minutes";
            // 
            // DelayDuration60Menu
            // 
            this.DelayDuration60Menu.Name = "DelayDuration60Menu";
            this.DelayDuration60Menu.Size = new System.Drawing.Size(132, 22);
            this.DelayDuration60Menu.Tag = "1h";
            this.DelayDuration60Menu.Text = "1 hour";
            // 
            // stopToolStripMenuItem
            // 
            this.stopToolStripMenuItem.Enabled = false;
            this.stopToolStripMenuItem.Name = "stopToolStripMenuItem";
            this.stopToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
            this.stopToolStripMenuItem.Text = "Stop";
            // 
            // throttleOptionsToolStripMenuItem
            // 
            this.throttleOptionsToolStripMenuItem.Name = "throttleOptionsToolStripMenuItem";
            this.throttleOptionsToolStripMenuItem.Size = new System.Drawing.Size(168, 22);
            this.throttleOptionsToolStripMenuItem.Text = "Throttle options...";
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(122, 6);
            // 
            // quitToolStripMenuItem
            // 
            this.quitToolStripMenuItem.Name = "quitToolStripMenuItem";
            this.quitToolStripMenuItem.Size = new System.Drawing.Size(125, 22);
            this.quitToolStripMenuItem.Text = "Quit";
            // 
            // TrayIcon
            // 
            this.TrayIcon.ContextMenuStrip = this.TrayMenu;
            this.TrayIcon.Icon = ((System.Drawing.Icon)(resources.GetObject("TrayIcon.Icon")));
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(142, 29);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Duplicati Control Window";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.TrayMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ContextMenuStrip TrayMenu;
        private System.Windows.Forms.ToolStripMenuItem statusToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem wizardToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem controlToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem pauseToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem pausePeriodToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration05Menu;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration15Menu;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration30Menu;
        private System.Windows.Forms.ToolStripMenuItem DelayDuration60Menu;
        private System.Windows.Forms.ToolStripMenuItem stopToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem throttleOptionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripMenuItem quitToolStripMenuItem;
        private System.Windows.Forms.NotifyIcon TrayIcon;
    }
}

