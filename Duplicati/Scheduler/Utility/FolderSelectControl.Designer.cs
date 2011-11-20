namespace Duplicati.Scheduler.Utility
{
    partial class FolderSelectControl
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FolderSelectControl));
            this.treeListView = new BrightIdeasSoftware.TreeListView();
            this.treeColumnName = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.treeColumnModified = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.treeColumnCreated = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.treeColumnAttributes = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.treeListView)).BeginInit();
            this.SuspendLayout();
            // 
            // treeListView
            // 
            this.treeListView.AllColumns.Add(this.treeColumnName);
            this.treeListView.AllColumns.Add(this.treeColumnModified);
            this.treeListView.AllColumns.Add(this.treeColumnCreated);
            this.treeListView.AllColumns.Add(this.treeColumnAttributes);
            this.treeListView.AllowColumnReorder = true;
            this.treeListView.AllowDrop = true;
            this.treeListView.BackColor = System.Drawing.Color.Gainsboro;
            this.treeListView.CellEditActivation = BrightIdeasSoftware.ObjectListView.CellEditActivateMode.SingleClick;
            this.treeListView.CellEditEnterChangesRows = true;
            this.treeListView.CellEditTabChangesRows = true;
            this.treeListView.CheckBoxes = false;
            this.treeListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.treeColumnName,
            this.treeColumnModified,
            this.treeColumnCreated,
            this.treeColumnAttributes});
            this.treeListView.Cursor = System.Windows.Forms.Cursors.Default;
            this.treeListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeListView.EmptyListMsg = "This folder is empty.";
            this.treeListView.EmptyListMsgFont = new System.Drawing.Font("Comic Sans MS", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeListView.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeListView.FullRowSelect = true;
            this.treeListView.GridLines = true;
            this.treeListView.HideSelection = false;
            this.treeListView.IsSimpleDragSource = true;
            this.treeListView.IsSimpleDropSink = true;
            this.treeListView.Location = new System.Drawing.Point(0, 0);
            this.treeListView.Name = "treeListView";
            this.treeListView.OwnerDraw = true;
            this.treeListView.SelectColumnsOnRightClickBehaviour = BrightIdeasSoftware.ObjectListView.ColumnSelectBehaviour.Submenu;
            this.treeListView.ShowCommandMenuOnRightClick = true;
            this.treeListView.ShowFilterMenuOnRightClick = false;
            this.treeListView.ShowGroups = false;
            this.treeListView.ShowImagesOnSubItems = true;
            this.treeListView.ShowItemToolTips = true;
            this.treeListView.Size = new System.Drawing.Size(446, 280);
            this.treeListView.SmallImageList = this.imageList;
            this.treeListView.TabIndex = 15;
            this.treeListView.UseCompatibleStateImageBehavior = false;
            this.treeListView.UseFiltering = true;
            this.treeListView.UseHotItem = true;
            this.treeListView.View = System.Windows.Forms.View.Details;
            this.treeListView.VirtualMode = true;
            this.treeListView.CellClick += new System.EventHandler<BrightIdeasSoftware.CellClickEventArgs>(this.treeListView_CellClick);
            this.treeListView.CellRightClick += new System.EventHandler<BrightIdeasSoftware.CellRightClickEventArgs>(this.AttributesRightClick);
            this.treeListView.ItemActivate += new System.EventHandler(this.treeListView_ItemActivate);
            // 
            // treeColumnName
            // 
            this.treeColumnName.AspectName = "Name";
            this.treeColumnName.Hideable = false;
            this.treeColumnName.IsEditable = false;
            this.treeColumnName.IsTileViewColumn = true;
            this.treeColumnName.Text = "Name";
            this.treeColumnName.UseInitialLetterForGroup = true;
            this.treeColumnName.Width = 180;
            // 
            // treeColumnModified
            // 
            this.treeColumnModified.AspectName = "LastWriteTime";
            this.treeColumnModified.IsEditable = false;
            this.treeColumnModified.IsTileViewColumn = true;
            this.treeColumnModified.Text = "Modified";
            this.treeColumnModified.Width = 145;
            // 
            // treeColumnCreated
            // 
            this.treeColumnCreated.IsTileViewColumn = true;
            this.treeColumnCreated.Text = "Created";
            this.treeColumnCreated.Width = 145;
            // 
            // treeColumnAttributes
            // 
            this.treeColumnAttributes.IsEditable = false;
            this.treeColumnAttributes.MinimumWidth = 20;
            this.treeColumnAttributes.Text = "Attributes";
            // 
            // imageList
            // 
            this.imageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList.ImageStream")));
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList.Images.SetKeyName(0, "compass");
            this.imageList.Images.SetKeyName(1, "down");
            this.imageList.Images.SetKeyName(2, "user");
            this.imageList.Images.SetKeyName(3, "find");
            this.imageList.Images.SetKeyName(4, "folder");
            this.imageList.Images.SetKeyName(5, "movie");
            this.imageList.Images.SetKeyName(6, "music");
            this.imageList.Images.SetKeyName(7, "no");
            this.imageList.Images.SetKeyName(8, "readonly");
            this.imageList.Images.SetKeyName(9, "public");
            this.imageList.Images.SetKeyName(10, "recycle");
            this.imageList.Images.SetKeyName(11, "spanner");
            this.imageList.Images.SetKeyName(12, "star");
            this.imageList.Images.SetKeyName(13, "tick");
            this.imageList.Images.SetKeyName(14, "archive");
            this.imageList.Images.SetKeyName(15, "system");
            this.imageList.Images.SetKeyName(16, "hidden");
            this.imageList.Images.SetKeyName(17, "temporary");
            this.imageList.Images.SetKeyName(18, "Unchecked");
            this.imageList.Images.SetKeyName(19, "Indeterminate");
            this.imageList.Images.SetKeyName(20, "Checked");
            // 
            // FolderSelectControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.treeListView);
            this.Name = "FolderSelectControl";
            this.Size = new System.Drawing.Size(446, 280);
            ((System.ComponentModel.ISupportInitialize)(this.treeListView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private BrightIdeasSoftware.TreeListView treeListView;
        private BrightIdeasSoftware.OLVColumn treeColumnName;
        private BrightIdeasSoftware.OLVColumn treeColumnModified;
        private BrightIdeasSoftware.OLVColumn treeColumnAttributes;
        private BrightIdeasSoftware.OLVColumn treeColumnCreated;
        private System.Windows.Forms.ImageList imageList;
        
    }
}
