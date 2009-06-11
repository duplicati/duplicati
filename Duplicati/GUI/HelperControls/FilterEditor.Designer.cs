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
            this.columnHeader1 = new System.Windows.Forms.ColumnHeader();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.AddFilterButton = new System.Windows.Forms.ToolStripButton();
            this.RemoveFilterButton = new System.Windows.Forms.ToolStripButton();
            this.EditFilterButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.MoveFilterUpButton = new System.Windows.Forms.ToolStripButton();
            this.MoveFilterDownButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.MoveFilterTopButton = new System.Windows.Forms.ToolStripButton();
            this.MoveFilterBottomButton = new System.Windows.Forms.ToolStripButton();
            this.label1 = new System.Windows.Forms.Label();
            this.FilenameTester = new System.Windows.Forms.TextBox();
            this.TestResults = new System.Windows.Forms.PictureBox();
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
            this.listView.SmallImageList = this.imageList1;
            this.listView.UseCompatibleStateImageBehavior = false;
            this.listView.View = System.Windows.Forms.View.Details;
            this.listView.SelectedIndexChanged += new System.EventHandler(this.listView_SelectedIndexChanged);
            this.listView.DoubleClick += new System.EventHandler(this.listView_DoubleClick);
            // 
            // columnHeader1
            // 
            resources.ApplyResources(this.columnHeader1, "columnHeader1");
            // 
            // imageList1
            // 
            this.imageList1.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList1.ImageStream")));
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList1.Images.SetKeyName(0, "Inclusion.ico");
            this.imageList1.Images.SetKeyName(1, "Exclusion.ico");
            // 
            // toolStrip1
            // 
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddFilterButton,
            this.RemoveFilterButton,
            this.EditFilterButton,
            this.toolStripSeparator1,
            this.MoveFilterUpButton,
            this.MoveFilterDownButton,
            this.toolStripSeparator2,
            this.MoveFilterTopButton,
            this.MoveFilterBottomButton});
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
            // FilterEditor
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
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
        private System.Windows.Forms.ImageList imageList1;
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
    }
}
