using System;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Scheduler.Utility
{
    public partial class FolderSelectControl : UserControl
    {
        private HashSet<string> SelectedFolderList = new HashSet<string>();
        /// <summary>
        /// List of selected folders
        /// </summary>
        public string[] SelectedFolders 
        { 
            get { return this.SelectedFolderList.ToArray(); }
            set { this.SelectedFolderList = new HashSet<string>(value); } 
        }
        public string State
        {
            get { return System.Convert.ToBase64String(this.treeListView.SaveState()); }
            set { this.treeListView.RestoreState(System.Convert.FromBase64String(value)); }
        }
        public FolderSelectControl()
        {
            InitializeComponent();
            // Blank();
            InitializeTreeList();
        }

        /// <summary>
        /// True if folder has sub folders
        /// </summary>
        /// <param name="aDir">Folder to check</param>
        /// <returns>True if folder has sub folders</returns>
        bool HasSubs(System.IO.DirectoryInfo aDir)
        {
            bool Result = false;
            try
            {
                Result = aDir.GetDirectories().Any();
            }
            catch
            {
            }
            return Result;
        }
        private string[] Parents(string aFolder)
        {
            List<string> Result = new List<string>();
            for (System.IO.DirectoryInfo Info = new System.IO.DirectoryInfo(aFolder); Info != null; Info = Info.Parent)
                Result.Add(Info.FullName);
            return Result.ToArray();
        }
        private string[] Children(string aFolder)
        {
            return System.IO.Directory.GetDirectories(aFolder, "*.*", System.IO.SearchOption.AllDirectories);
        }
        /// <summary>
        /// Set up the tree view
        /// </summary>
        private void InitializeTreeList()
        {
            // Tells tree view if this node can expand, here means contins subdirectory
            this.treeListView.CanExpandGetter = delegate(object x)
            {
                return (x is System.IO.DirectoryInfo) && HasSubs((System.IO.DirectoryInfo)x);
            };
            // Get children of a node, here we get files and subs
            this.treeListView.ChildrenGetter = delegate(object x)
            {
                System.IO.DirectoryInfo dir = (System.IO.DirectoryInfo)x;
                try
                {
                    return new ArrayList(dir.GetFileSystemInfos());
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show(this, ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return new ArrayList();
                }
            };
            // You can change the way the connection lines are drawn by changing the pen
            BrightIdeasSoftware.TreeListView.TreeRenderer renderer = this.treeListView.TreeColumnRenderer;
            renderer.LinePen = new Pen(Color.Firebrick, 0.5f);
            renderer.LinePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;

            // Draw the system icon next to the name
            this.treeColumnName.ImageGetter = delegate(object x)
            {
                CheckState State = CheckState.Unchecked;
                string Name = ((System.IO.FileSystemInfo)x).FullName;
                if (this.SelectedFolders.Contains(Name) || 
                    Parents(Name).Any(qN => this.SelectedFolderList.Contains(qN)))
                    State = CheckState.Checked;
                else if (this.SelectedFolderList.Any(qC=>qC.StartsWith(Name+System.IO.Path.DirectorySeparatorChar)))
                    State = CheckState.Indeterminate;

                return this.imageList.Images.IndexOfKey(State.ToString());
            };
            this.treeListView.ModelFilter = new BrightIdeasSoftware.ModelFilter(delegate(object x)
            {
                return ((System.IO.FileSystemInfo)x).Attributes.HasFlag(System.IO.FileAttributes.Directory);
            });
            // Show the file attributes for this object
            this.treeColumnAttributes.AspectGetter = delegate(object x)
            {
                return ((System.IO.FileSystemInfo)x).Attributes;
            };
            // Format the date last mod
            this.treeColumnModified.AspectGetter = delegate(object x)
            {
                return ((System.IO.FileSystemInfo)x).LastWriteTime.ToString("yyyy MM/dd hh:mm tt");
            };
            // Format the date last access
            this.treeColumnCreated.AspectGetter = delegate(object x)
            {
                return ((System.IO.FileSystemInfo)x).CreationTime.ToString("yyyy MM/dd hh:mm tt");
            };
            BrightIdeasSoftware.FlagRenderer attributesRenderer = new BrightIdeasSoftware.FlagRenderer();
            attributesRenderer.Add(System.IO.FileAttributes.Archive, "archive");
            attributesRenderer.Add(System.IO.FileAttributes.ReadOnly, "readonly");
            attributesRenderer.Add(System.IO.FileAttributes.System, "system");
            attributesRenderer.Add(System.IO.FileAttributes.Hidden, "hidden");
            attributesRenderer.Add(System.IO.FileAttributes.Temporary, "temporary");
            this.treeColumnAttributes.Renderer = attributesRenderer;
            // Take care of size
            //this.treeColumnSize.AspectGetter = delegate(object x)
            //{
            //    return System.IO.Directory.GetFiles(((System.IO.FileSystemInfo)x).FullName, "*.*", System.IO.SearchOption.AllDirectories)
            //        .Select(qF => new System.IO.FileInfo(qF).Length).Sum();
            //};
            // List all drives as the roots of the tree
            ArrayList roots = new ArrayList();
            foreach (System.IO.DriveInfo di in System.IO.DriveInfo.GetDrives())
            {
                if (di.IsReady && di.DriveType == System.IO.DriveType.Fixed ||
                    di.DriveType == System.IO.DriveType.Network)
                {
                    roots.Add(new System.IO.DirectoryInfo(di.Name));
                }
            }
            this.treeListView.Roots = roots;
            this.treeListView.Expand(roots[0]);
        }
        /// <summary>
        /// Double clicked on folder - open in Explorer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void treeListView_ItemActivate(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(((System.IO.FileSystemInfo)this.treeListView.SelectedObject).FullName);
        }

        private void treeListView_CellClick(object sender, BrightIdeasSoftware.CellClickEventArgs e)
        {
            if (e.Column.Index != this.treeColumnName.Index || 
                this.treeListView.HitTest(e.Location.X, e.Location.Y).Location != ListViewHitTestLocations.Image) 
                return;
            string Name = ((System.IO.FileSystemInfo)e.Model).FullName;
            if (this.SelectedFolderList.Contains(Name)) this.SelectedFolderList.Remove(Name);
            else this.SelectedFolderList.Add(Name);
            this.treeListView.RefreshItem(e.Item);
        }
        private Bitmap Blank()
        {
            Bitmap Result = new Bitmap(16, 16);
            using (Graphics Gr = Graphics.FromImage(Result))
            {
                Gr.Clear(Color.White);
                using (Pen P = new Pen(Color.Green, 4f))
                    Gr.DrawRectangle(P, 0, 0, Result.Width, Result.Height);
                Gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                using (Pen P = new Pen(Color.LightGray, 4f))
                {
                    Gr.DrawLine(P, 2, 8, 8, 14);
                    Gr.DrawLine(P, 8, 14, 14, 2);
                }
            }
            Result.Save(@"C:\Temp\Intermediate.png", System.Drawing.Imaging.ImageFormat.Png);
            using (Graphics Gr = Graphics.FromImage(Result))
            {
                Gr.Clear(Color.White);
                using (Pen P = new Pen(Color.Green, 4f))
                    Gr.DrawRectangle(P, 0, 0, Result.Width, Result.Height);
            }
            Result.Save(@"C:\Temp\Unchecked.png", System.Drawing.Imaging.ImageFormat.Png);
            using (Graphics Gr = Graphics.FromImage(Result))
            {
                Gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                using (Pen P = new Pen(Color.Black, 4f))
                {
                    Gr.DrawLine(P, 2, 8, 8, 14);
                    Gr.DrawLine(P, 8, 14, 14, 2);
                }
            }
            Result.Save(@"C:\Temp\Checked.png", System.Drawing.Imaging.ImageFormat.Png);
            return Result;
        }

        ContextMenuStrip AttributesContextMenuStrip = null;
        private void AttributesRightClick(object sender, BrightIdeasSoftware.CellRightClickEventArgs e)
        {
            if (this.AttributesContextMenuStrip == null)
            {
                this.AttributesContextMenuStrip = new ContextMenuStrip();
                this.AttributesContextMenuStrip.Items.AddRange(System.Enum.GetNames(typeof(System.IO.FileAttributes))
                    .Where(qC => this.imageList.Images.ContainsKey(qC.ToLower()))
                    .Select(qC => new ToolStripMenuItem(qC.ToString(), this.imageList.Images[qC.ToString().ToLower()])).ToArray());
            }
            // cms.Show(Cursor.Position);
            e.MenuStrip = this.AttributesContextMenuStrip;
        }
    }


    public static class EnumExt
    {
        public static bool HasFlag(this Enum aKeys, Enum aFlag)
        {
            UInt64 FlagValue = Convert.ToUInt64(aFlag);
            return (Convert.ToUInt64(aKeys) & FlagValue) == FlagValue;
        }
    }
}
