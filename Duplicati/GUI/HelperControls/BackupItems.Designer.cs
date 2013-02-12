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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BackupItems));
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
            resources.ApplyResources(this.contextMenuStrip, "contextMenuStrip");
            // 
            // viewFilesToolStripMenuItem
            // 
            this.viewFilesToolStripMenuItem.Image = global::Duplicati.GUI.Properties.Resources.ExamineMenuIcon;
            this.viewFilesToolStripMenuItem.Name = "viewFilesToolStripMenuItem";
            resources.ApplyResources(this.viewFilesToolStripMenuItem, "viewFilesToolStripMenuItem");
            this.viewFilesToolStripMenuItem.Click += new System.EventHandler(this.viewFilesToolStripMenuItem_Click);
            // 
            // statusLabel
            // 
            resources.ApplyResources(this.statusLabel, "statusLabel");
            this.statusLabel.Name = "statusLabel";
            // 
            // progressBar
            // 
            resources.ApplyResources(this.progressBar, "progressBar");
            this.progressBar.Name = "progressBar";
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            // 
            // WaitPanel
            // 
            resources.ApplyResources(this.WaitPanel, "WaitPanel");
            this.WaitPanel.Controls.Add(this.statusLabel);
            this.WaitPanel.Controls.Add(this.progressBar);
            this.WaitPanel.Name = "WaitPanel";
            // 
            // imageList
            // 
            this.imageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            resources.ApplyResources(this.imageList, "imageList");
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // backgroundWorker
            // 
            this.backgroundWorker.WorkerSupportsCancellation = true;
            this.backgroundWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this.backgroundWorker_DoWork);
            this.backgroundWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.backgroundWorker_RunWorkerCompleted);
            // 
            // listView
            // 
            this.listView.ContextMenuStrip = this.contextMenuStrip;
            resources.ApplyResources(this.listView, "listView");
            this.listView.FullRowSelect = true;
            this.listView.MultiSelect = false;
            this.listView.Name = "listView";
            this.listView.ShowItemToolTips = true;
            this.listView.SmallImageList = this.imageList;
            this.listView.UseCompatibleStateImageBehavior = false;
            this.listView.View = System.Windows.Forms.View.List;
            this.listView.DoubleClick += new System.EventHandler(this.listView_DoubleClick);
            // 
            // BackupItems
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.WaitPanel);
            this.Controls.Add(this.listView);
            this.Name = "BackupItems";
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
