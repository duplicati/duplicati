namespace Duplicati.GUI.HelperControls
{
    partial class BackupItems
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
            this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.viewFilesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.statusLabel = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.WaitPanel = new System.Windows.Forms.Panel();
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            this.backgroundWorker = new System.ComponentModel.BackgroundWorker();
            this.listView = new System.Windows.Forms.ListView();
            this.contextMenuStrip.SuspendLayout();
            this.WaitPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // contextMenuStrip
            // 
            this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.viewFilesToolStripMenuItem});
            this.contextMenuStrip.Name = "contextMenuStrip";
            this.contextMenuStrip.Size = new System.Drawing.Size(168, 26);
            // 
            // viewFilesToolStripMenuItem
            // 
            this.viewFilesToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.ExamineMenuIcon;
            this.viewFilesToolStripMenuItem.Name = "viewFilesToolStripMenuItem";
            this.viewFilesToolStripMenuItem.Size = new System.Drawing.Size(167, 22);
            this.viewFilesToolStripMenuItem.Text = "View contents ...";
            this.viewFilesToolStripMenuItem.Click += new System.EventHandler(this.viewFilesToolStripMenuItem_Click);
            // 
            // statusLabel
            // 
            this.statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.statusLabel.Location = new System.Drawing.Point(0, 8);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(184, 16);
            this.statusLabel.TabIndex = 1;
            this.statusLabel.Text = "Loading list of backups";
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(0, 56);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(184, 16);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 2;
            // 
            // WaitPanel
            // 
            this.WaitPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.WaitPanel.Controls.Add(this.progressBar);
            this.WaitPanel.Controls.Add(this.statusLabel);
            this.WaitPanel.Location = new System.Drawing.Point(8, 16);
            this.WaitPanel.Name = "WaitPanel";
            this.WaitPanel.Size = new System.Drawing.Size(184, 72);
            this.WaitPanel.TabIndex = 3;
            // 
            // imageList
            // 
            this.imageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            this.imageList.ImageSize = new System.Drawing.Size(16, 16);
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // backgroundWorker
            // 
            this.backgroundWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this.backgroundWorker_DoWork);
            this.backgroundWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.backgroundWorker_RunWorkerCompleted);
            // 
            // listView
            // 
            this.listView.ContextMenuStrip = this.contextMenuStrip;
            this.listView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listView.FullRowSelect = true;
            this.listView.Location = new System.Drawing.Point(0, 0);
            this.listView.MultiSelect = false;
            this.listView.Name = "listView";
            this.listView.ShowItemToolTips = true;
            this.listView.Size = new System.Drawing.Size(203, 139);
            this.listView.SmallImageList = this.imageList;
            this.listView.TabIndex = 4;
            this.listView.UseCompatibleStateImageBehavior = false;
            this.listView.View = System.Windows.Forms.View.List;
            // 
            // BackupItems
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.WaitPanel);
            this.Controls.Add(this.listView);
            this.Name = "BackupItems";
            this.Size = new System.Drawing.Size(203, 139);
            this.contextMenuStrip.ResumeLayout(false);
            this.WaitPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Panel WaitPanel;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem viewFilesToolStripMenuItem;
        private System.Windows.Forms.ImageList imageList;
        private System.ComponentModel.BackgroundWorker backgroundWorker;
        public System.Windows.Forms.ListView listView;
    }
}
