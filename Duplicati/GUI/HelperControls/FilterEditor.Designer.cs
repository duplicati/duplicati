namespace Duplicati.GUI.HelperControls
{
    partial class FilterEditor
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FilterEditor));
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.listView = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.AddFilterButton = new System.Windows.Forms.ToolStripButton();
            this.RemoveFilterButton = new System.Windows.Forms.ToolStripButton();
            this.EditFilterButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.IncludeFolderButton = new System.Windows.Forms.ToolStripButton();
            this.ExcludeFolderButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.MoveFilterUpButton = new System.Windows.Forms.ToolStripButton();
            this.MoveFilterDownButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.MoveFilterTopButton = new System.Windows.Forms.ToolStripButton();
            this.MoveFilterBottomButton = new System.Windows.Forms.ToolStripButton();
            this.HelpButton = new System.Windows.Forms.ToolStripButton();
            this.btnTestSearch = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.FilenameTester = new System.Windows.Forms.TextBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.TestResults = new System.Windows.Forms.PictureBox();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.groupBox1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.TestResults)).BeginInit();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Controls.Add(this.listView);
            this.groupBox1.Controls.Add(this.toolStrip1);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            // 
            // listView
            // 
            this.listView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1});
            resources.ApplyResources(this.listView, "listView");
            this.listView.FullRowSelect = true;
            this.listView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.listView.Name = "listView";
            this.listView.SmallImageList = this.imageList;
            this.listView.UseCompatibleStateImageBehavior = false;
            this.listView.View = System.Windows.Forms.View.Details;
            this.listView.SelectedIndexChanged += new System.EventHandler(this.listView_SelectedIndexChanged);
            this.listView.DoubleClick += new System.EventHandler(this.listView_DoubleClick);
            // 
            // columnHeader1
            // 
            resources.ApplyResources(this.columnHeader1, "columnHeader1");
            // 
            // imageList
            // 
            this.imageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList.ImageStream")));
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList.Images.SetKeyName(0, "globbing-include");
            this.imageList.Images.SetKeyName(1, "globbing-exclude");
            this.imageList.Images.SetKeyName(2, "regexp-include");
            this.imageList.Images.SetKeyName(3, "regexp-exclude");
            // 
            // toolStrip1
            // 
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddFilterButton,
            this.RemoveFilterButton,
            this.EditFilterButton,
            this.toolStripSeparator3,
            this.IncludeFolderButton,
            this.ExcludeFolderButton,
            this.toolStripSeparator1,
            this.MoveFilterUpButton,
            this.MoveFilterDownButton,
            this.toolStripSeparator2,
            this.MoveFilterTopButton,
            this.MoveFilterBottomButton,
            this.HelpButton});
            resources.ApplyResources(this.toolStrip1, "toolStrip1");
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.RenderMode = System.Windows.Forms.ToolStripRenderMode.System;
            // 
            // AddFilterButton
            // 
            this.AddFilterButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.AddFilterButton, "AddFilterButton");
            this.AddFilterButton.Name = "AddFilterButton";
            this.AddFilterButton.Click += new System.EventHandler(this.AddFilterButton_Click);
            // 
            // RemoveFilterButton
            // 
            this.RemoveFilterButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.RemoveFilterButton, "RemoveFilterButton");
            this.RemoveFilterButton.Name = "RemoveFilterButton";
            this.RemoveFilterButton.Click += new System.EventHandler(this.RemoveFilterButton_Click);
            // 
            // EditFilterButton
            // 
            this.EditFilterButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.EditFilterButton, "EditFilterButton");
            this.EditFilterButton.Name = "EditFilterButton";
            this.EditFilterButton.Click += new System.EventHandler(this.EditFilterButton_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
            // 
            // IncludeFolderButton
            // 
            this.IncludeFolderButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.IncludeFolderButton.Image = global::Duplicati.GUI.Properties.Resources.AddedFolder;
            resources.ApplyResources(this.IncludeFolderButton, "IncludeFolderButton");
            this.IncludeFolderButton.Name = "IncludeFolderButton";
            this.IncludeFolderButton.Click += new System.EventHandler(this.IncludeFolderButton_Click);
            // 
            // ExcludeFolderButton
            // 
            this.ExcludeFolderButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.ExcludeFolderButton.Image = global::Duplicati.GUI.Properties.Resources.DeletedFolder;
            resources.ApplyResources(this.ExcludeFolderButton, "ExcludeFolderButton");
            this.ExcludeFolderButton.Name = "ExcludeFolderButton";
            this.ExcludeFolderButton.Click += new System.EventHandler(this.ExcludeFolderButton_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            // 
            // MoveFilterUpButton
            // 
            this.MoveFilterUpButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.MoveFilterUpButton, "MoveFilterUpButton");
            this.MoveFilterUpButton.Name = "MoveFilterUpButton";
            this.MoveFilterUpButton.Click += new System.EventHandler(this.MoveFilterUpButton_Click);
            // 
            // MoveFilterDownButton
            // 
            this.MoveFilterDownButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.MoveFilterDownButton, "MoveFilterDownButton");
            this.MoveFilterDownButton.Name = "MoveFilterDownButton";
            this.MoveFilterDownButton.Click += new System.EventHandler(this.MoveFilterDownButton_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
            // 
            // MoveFilterTopButton
            // 
            this.MoveFilterTopButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.MoveFilterTopButton, "MoveFilterTopButton");
            this.MoveFilterTopButton.Name = "MoveFilterTopButton";
            this.MoveFilterTopButton.Click += new System.EventHandler(this.MoveFilterTopButton_Click);
            // 
            // MoveFilterBottomButton
            // 
            this.MoveFilterBottomButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.MoveFilterBottomButton, "MoveFilterBottomButton");
            this.MoveFilterBottomButton.Name = "MoveFilterBottomButton";
            this.MoveFilterBottomButton.Click += new System.EventHandler(this.MoveFilterBottomButton_Click);
            // 
            // HelpButton
            // 
            this.HelpButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.HelpButton.Image = global::Duplicati.GUI.Properties.Resources.help;
            resources.ApplyResources(this.HelpButton, "HelpButton");
            this.HelpButton.Name = "HelpButton";
            this.HelpButton.Click += new System.EventHandler(this.HelpButton_Click);
            // 
            // btnTestSearch
            // 
            resources.ApplyResources(this.btnTestSearch, "btnTestSearch");
            this.btnTestSearch.Name = "btnTestSearch";
            this.btnTestSearch.UseVisualStyleBackColor = true;
            this.btnTestSearch.Click += new System.EventHandler(this.btnTestSearch_Click);
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // FilenameTester
            // 
            resources.ApplyResources(this.FilenameTester, "FilenameTester");
            this.FilenameTester.Name = "FilenameTester";
            this.FilenameTester.TextChanged += new System.EventHandler(this.FilenameTester_TextChanged);
            // 
            // TestResults
            // 
            resources.ApplyResources(this.TestResults, "TestResults");
            this.TestResults.Name = "TestResults";
            this.TestResults.TabStop = false;
            // 
            // folderBrowserDialog
            // 
            this.folderBrowserDialog.ShowNewFolderButton = false;
            // 
            // FilterEditor
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.btnTestSearch);
            this.Controls.Add(this.TestResults);
            this.Controls.Add(this.FilenameTester);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.groupBox1);
            this.Name = "FilterEditor";
            this.Load += new System.EventHandler(this.FilterEditor_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.TestResults)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ListView listView;
        private System.Windows.Forms.ColumnHeader columnHeader1;
        private System.Windows.Forms.ImageList imageList;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton AddFilterButton;
        private System.Windows.Forms.ToolStripButton RemoveFilterButton;
        private System.Windows.Forms.ToolStripButton EditFilterButton;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton MoveFilterUpButton;
        private System.Windows.Forms.ToolStripButton MoveFilterDownButton;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripButton MoveFilterTopButton;
        private System.Windows.Forms.ToolStripButton MoveFilterBottomButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox FilenameTester;
        private System.Windows.Forms.PictureBox TestResults;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.ToolStripButton HelpButton;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripButton IncludeFolderButton;
        private System.Windows.Forms.ToolStripButton ExcludeFolderButton;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
				private System.Windows.Forms.Button btnTestSearch;
    }
}
