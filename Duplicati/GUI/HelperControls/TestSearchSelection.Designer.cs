

namespace Duplicati.GUI.HelperControls {

	partial class TestSearchSelection {
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing) {
			if( disposing && ( components != null ) ) {
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TestSearchSelection));
            this.ctxSelection = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.miInclude = new System.Windows.Forms.ToolStripMenuItem();
            this.miIncludeFileName = new System.Windows.Forms.ToolStripMenuItem();
            this.miIncludeFileExt = new System.Windows.Forms.ToolStripMenuItem();
            this.miIncludeFilePath = new System.Windows.Forms.ToolStripMenuItem();
            this.miExclude = new System.Windows.Forms.ToolStripMenuItem();
            this.miExcludeFileName = new System.Windows.Forms.ToolStripMenuItem();
            this.miExcludeFileExt = new System.Windows.Forms.ToolStripMenuItem();
            this.miExcludeFilePath = new System.Windows.Forms.ToolStripMenuItem();
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            this.label1 = new System.Windows.Forms.Label();
            this.btnCancel = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.lvFilters = new System.Windows.Forms.ListView();
            this.ctxFilters = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.removeFilterToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.scanWorker = new System.ComponentModel.BackgroundWorker();
            this.lbTotalSize = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.tbSearch = new System.Windows.Forms.TextBox();
            this.btnSearch = new System.Windows.Forms.PictureBox();
            this.btnClearSearch = new System.Windows.Forms.PictureBox();
            this.lvFiles = new System.Windows.Forms.ListView();
            this.chFile = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chFileExt = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chSize = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.chPath = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.lbLoading = new System.Windows.Forms.Label();
            this.btnOK = new System.Windows.Forms.Button();
            this.ctxSelection.SuspendLayout();
            this.ctxFilters.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.btnSearch)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearSearch)).BeginInit();
            this.SuspendLayout();
            // 
            // ctxSelection
            // 
            this.ctxSelection.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.miInclude,
            this.miExclude});
            this.ctxSelection.Name = "ctxSelection";
            resources.ApplyResources(this.ctxSelection, "ctxSelection");
            this.ctxSelection.Opened += new System.EventHandler(this.ctxSelection_Opened);
            // 
            // miInclude
            // 
            this.miInclude.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.miIncludeFileName,
            this.miIncludeFileExt,
            this.miIncludeFilePath});
            this.miInclude.Name = "miInclude";
            resources.ApplyResources(this.miInclude, "miInclude");
            // 
            // miIncludeFileName
            // 
            this.miIncludeFileName.Name = "miIncludeFileName";
            resources.ApplyResources(this.miIncludeFileName, "miIncludeFileName");
            this.miIncludeFileName.Tag = "1";
            this.miIncludeFileName.Click += new System.EventHandler(this.miIncludeFileName_Click);
            // 
            // miIncludeFileExt
            // 
            this.miIncludeFileExt.Name = "miIncludeFileExt";
            resources.ApplyResources(this.miIncludeFileExt, "miIncludeFileExt");
            this.miIncludeFileExt.Click += new System.EventHandler(this.miIncludeFileExt_Click);
            // 
            // miIncludeFilePath
            // 
            this.miIncludeFilePath.Name = "miIncludeFilePath";
            resources.ApplyResources(this.miIncludeFilePath, "miIncludeFilePath");
            // 
            // miExclude
            // 
            this.miExclude.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.miExcludeFileName,
            this.miExcludeFileExt,
            this.miExcludeFilePath});
            this.miExclude.Name = "miExclude";
            resources.ApplyResources(this.miExclude, "miExclude");
            // 
            // miExcludeFileName
            // 
            this.miExcludeFileName.Name = "miExcludeFileName";
            resources.ApplyResources(this.miExcludeFileName, "miExcludeFileName");
            this.miExcludeFileName.Click += new System.EventHandler(this.addFilenameFilterToolStripMenuItem_Click);
            // 
            // miExcludeFileExt
            // 
            this.miExcludeFileExt.Name = "miExcludeFileExt";
            resources.ApplyResources(this.miExcludeFileExt, "miExcludeFileExt");
            this.miExcludeFileExt.Click += new System.EventHandler(this.addFileExtensionFilterToolStripMenuItem1_Click);
            // 
            // miExcludeFilePath
            // 
            this.miExcludeFilePath.Name = "miExcludeFilePath";
            resources.ApplyResources(this.miExcludeFilePath, "miExcludeFilePath");
            this.miExcludeFilePath.Click += new System.EventHandler(this.addFilePathFilerToolStripMenuItem_Click);
            // 
            // imageList
            // 
            this.imageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList.ImageStream")));
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList.Images.SetKeyName(0, "globbing-include");
            this.imageList.Images.SetKeyName(1, "globbing-exclude");
            this.imageList.Images.SetKeyName(2, "icon_up_sort_arrow[1].png");
            this.imageList.Images.SetKeyName(3, "icon_down_sort_arrow[1].png");
            this.imageList.Images.SetKeyName(4, "sort_ascending[1].png");
            this.imageList.Images.SetKeyName(5, "sort_descending[1].png");
            this.imageList.Images.SetKeyName(6, "sort_up[1].png");
            this.imageList.Images.SetKeyName(7, "sort_down[1].png");
            this.imageList.Images.SetKeyName(8, "regexp-include");
            this.imageList.Images.SetKeyName(9, "regexp-exclude");
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // btnCancel
            // 
            resources.ApplyResources(this.btnCancel, "btnCancel");
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.ForeColor = System.Drawing.Color.DimGray;
            this.label2.Name = "label2";
            // 
            // lvFilters
            // 
            resources.ApplyResources(this.lvFilters, "lvFilters");
            this.lvFilters.ContextMenuStrip = this.ctxFilters;
            this.lvFilters.FullRowSelect = true;
            this.lvFilters.Name = "lvFilters";
            this.lvFilters.SmallImageList = this.imageList;
            this.lvFilters.UseCompatibleStateImageBehavior = false;
            this.lvFilters.View = System.Windows.Forms.View.List;
            // 
            // ctxFilters
            // 
            this.ctxFilters.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.removeFilterToolStripMenuItem});
            this.ctxFilters.Name = "ctxFilters";
            resources.ApplyResources(this.ctxFilters, "ctxFilters");
            // 
            // removeFilterToolStripMenuItem
            // 
            this.removeFilterToolStripMenuItem.Name = "removeFilterToolStripMenuItem";
            resources.ApplyResources(this.removeFilterToolStripMenuItem, "removeFilterToolStripMenuItem");
            this.removeFilterToolStripMenuItem.Click += new System.EventHandler(this.removeFilterToolStripMenuItem_Click);
            // 
            // scanWorker
            // 
            this.scanWorker.WorkerSupportsCancellation = true;
            this.scanWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this.scanWorker_DoWork);
            this.scanWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.scanWorker_RunWorkerCompleted);
            // 
            // lbTotalSize
            // 
            resources.ApplyResources(this.lbTotalSize, "lbTotalSize");
            this.lbTotalSize.Name = "lbTotalSize";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // tbSearch
            // 
            resources.ApplyResources(this.tbSearch, "tbSearch");
            this.tbSearch.Name = "tbSearch";
            this.tbSearch.TextChanged += new System.EventHandler(this.tbSearch_TextChanged);
            // 
            // btnSearch
            // 
            resources.ApplyResources(this.btnSearch, "btnSearch");
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.TabStop = false;
            // 
            // btnClearSearch
            // 
            resources.ApplyResources(this.btnClearSearch, "btnClearSearch");
            this.btnClearSearch.BackColor = System.Drawing.SystemColors.Window;
            this.btnClearSearch.Name = "btnClearSearch";
            this.btnClearSearch.TabStop = false;
            this.btnClearSearch.Click += new System.EventHandler(this.btnClearSearch_Click);
            // 
            // lvFiles
            // 
            resources.ApplyResources(this.lvFiles, "lvFiles");
            this.lvFiles.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chFile,
            this.chFileExt,
            this.chSize,
            this.chPath});
            this.lvFiles.ContextMenuStrip = this.ctxSelection;
            this.lvFiles.FullRowSelect = true;
            this.lvFiles.Name = "lvFiles";
            this.lvFiles.SmallImageList = this.imageList;
            this.lvFiles.UseCompatibleStateImageBehavior = false;
            this.lvFiles.View = System.Windows.Forms.View.Details;
            this.lvFiles.VirtualMode = true;
            this.lvFiles.CacheVirtualItems += new System.Windows.Forms.CacheVirtualItemsEventHandler(this.lvFiles_CacheVirtualItems);
            this.lvFiles.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.lvFiles_ColumnClick);
            this.lvFiles.RetrieveVirtualItem += new System.Windows.Forms.RetrieveVirtualItemEventHandler(this.lvFiles_RetrieveVirtualItem);
            // 
            // chFile
            // 
            resources.ApplyResources(this.chFile, "chFile");
            // 
            // chFileExt
            // 
            resources.ApplyResources(this.chFileExt, "chFileExt");
            // 
            // chSize
            // 
            resources.ApplyResources(this.chSize, "chSize");
            // 
            // chPath
            // 
            resources.ApplyResources(this.chPath, "chPath");
            // 
            // lbLoading
            // 
            resources.ApplyResources(this.lbLoading, "lbLoading");
            this.lbLoading.BackColor = System.Drawing.SystemColors.Control;
            this.lbLoading.Name = "lbLoading";
            // 
            // btnOK
            // 
            resources.ApplyResources(this.btnOK, "btnOK");
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnOK.Name = "btnOK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // TestSearchSelection
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.lbLoading);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.btnClearSearch);
            this.Controls.Add(this.lvFiles);
            this.Controls.Add(this.lvFilters);
            this.Controls.Add(this.btnSearch);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.tbSearch);
            this.Controls.Add(this.lbTotalSize);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnCancel);
            this.MinimizeBox = false;
            this.Name = "TestSearchSelection";
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.Load += new System.EventHandler(this.dlgTestSearchSelection_Load);
            this.ctxSelection.ResumeLayout(false);
            this.ctxFilters.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.btnSearch)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.btnClearSearch)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ContextMenuStrip ctxSelection;
		private System.Windows.Forms.ToolStripMenuItem miInclude;
		private System.Windows.Forms.ToolStripMenuItem miIncludeFileName;
		private System.Windows.Forms.ToolStripMenuItem miIncludeFileExt;
		private System.Windows.Forms.ToolStripMenuItem miExclude;
		private System.Windows.Forms.ToolStripMenuItem miExcludeFileName;
		private System.Windows.Forms.ToolStripMenuItem miExcludeFileExt;
		private System.Windows.Forms.Button btnCancel;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.ImageList imageList;
		private System.Windows.Forms.ListView lvFilters;
		private System.ComponentModel.BackgroundWorker scanWorker;
		private System.Windows.Forms.ContextMenuStrip ctxFilters;
		private System.Windows.Forms.ToolStripMenuItem removeFilterToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem miExcludeFilePath;
		private System.Windows.Forms.Label lbTotalSize;
		private System.Windows.Forms.ToolStripMenuItem miIncludeFilePath;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.TextBox tbSearch;
		private System.Windows.Forms.PictureBox btnSearch;
		private System.Windows.Forms.PictureBox btnClearSearch;
		private System.Windows.Forms.ListView lvFiles;
		private System.Windows.Forms.ColumnHeader chFile;
		private System.Windows.Forms.ColumnHeader chFileExt;
		private System.Windows.Forms.ColumnHeader chSize;
		private System.Windows.Forms.ColumnHeader chPath;
		private System.Windows.Forms.Label lbLoading;
        private System.Windows.Forms.Button btnOK;
	}
}