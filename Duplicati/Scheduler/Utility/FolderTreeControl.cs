using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Scheduler.Utility
{
    /// <summary>
    /// A folder tree with 3-state checkboxes
    /// </summary>
    public partial class FolderTreeControl : UserControl
    {
        /// <summary>
        /// A file tree with 3-state checkboxes
        /// </summary>
        public FolderTreeControl() 
        {
            InitializeComponent();
            InitializeTree();
            this.treeView.BackColor = this.BackColor;
        }
        /// <summary>
        /// Make the tree, only populate root nodes
        /// </summary>
        private void InitializeTree()
        {
            this.treeView.Nodes.Clear();
            TreeNode Computer = UpdateNode(this.treeView.Nodes, "Computer", false);
            foreach (System.IO.DriveInfo Drive in System.IO.DriveInfo.GetDrives())
                if (Drive.IsReady) UpdateNode(Computer.Nodes, Drive.RootDirectory.Name, false);
            Computer.ForeColor = this.ForeColor;
        }
        private bool InHere = false; // No re-entry
        /// <summary>
        /// Expanding, make the children nodes
        /// </summary>
        private void treeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (!InHere)
            {
                InHere = true;
                this.treeView.BeginUpdate();
                Populate((string)e.Node.Tag, e.Node, e.Node.Checked && e.Node.ImageIndex != 1);
                this.treeView.Sort();
                e.Node.Expand();    // Sort collapses all, so we gotta force expand, which is why we need that InHere thing
                this.treeView.EndUpdate();
                InHere = false;
            }
        }
        public string[] itsSelectedFolders = new string[0];
        /// <summary>
        /// List of selected folders
        /// </summary>
        public string[] SelectedFolders 
        { 
            get { return Selected(this.treeView.Nodes[0]); }
        }
        /// <summary>
        /// Sets the list as selected
        /// </summary>
        /// <param name="aFolderList">Folders to select</param>
        public void SetSelectedFolders(string[] aFolderList)
        {
            itsSelectedFolders = aFolderList;
            AddSelections();
        }
        /// <summary>
        /// Get list of selected nodes [recursive]
        /// </summary>
        /// <param name="aNode">Node to search</param>
        /// <returns>List of selected nodes</returns>
        private string[] Selected(TreeNode aNode)
        {
            if (aNode.Checked && aNode.ImageIndex != 1) return new string[] { (string)aNode.Tag };
            List<string> Result = new List<string>();
            foreach (TreeNode Node in aNode.Nodes)
                Result.AddRange(Selected(Node));
            return Result.ToArray();
        }
        /// <summary>
        /// Returns true if node is selected
        /// </summary>
        /// <param name="aNode">Node</param>
        /// <returns>true if node is selected</returns>
        private bool IsChecked(TreeNode aNode)
        {
            return aNode.Checked && aNode.ImageIndex == 2;
        }
        /// <summary>
        /// A handy look up
        /// </summary>
        private Dictionary<string, TreeNode> NodesByPath = new Dictionary<string, TreeNode>();
        /// <summary>
        /// Gets a list of sub directories, returns exception if encountered
        /// </summary>
        /// <param name="aRoot">Path to list</param>
        /// <param name="outError">null of OK, otherwise error</param>
        /// <returns>List of subs</returns>
        private string[] GetDirectories(string aRoot, out Exception outError)
        {
            string[] Result = new string[0];
            outError = Duplicati.Scheduler.Utility.Tools.TryCatch((Action)delegate() { Result = System.IO.Directory.GetDirectories(aRoot); });
            return Result;
        }
        /// <summary>
        /// Fill a node with child nodes if any
        /// </summary>
        /// <param name="aRoot">Path to fill</param>
        /// <param name="aParent">Parent node</param>
        /// <param name="aChecked">Check the nodes or not</param>
        private void Populate(string aRoot, TreeNode aParent, bool aChecked)
        {
            Exception Ignored;
            foreach (string Folder in GetDirectories(aRoot, out Ignored))
                UpdateNode(aParent.Nodes, Folder, aChecked);
        }
        /// <summary>
        /// Gets the name of the node, special case for Recycle Bin
        /// </summary>
        /// <param name="aFolder">Folder to get name</param>
        /// <returns>Name</returns>
        private string NodeName(string aFolder)
        {
            string Result = System.IO.Path.GetFileName(aFolder);
            if (string.IsNullOrEmpty(Result)) Result = aFolder;
            return Result.Replace("$Recycle.Bin", " Recycle Bin");
        }
        /// <summary>
        /// Add/Change a node
        /// </summary>
        /// <param name="aNodes">Nodes for this one to join</param>
        /// <param name="aFolder">Folder to use</param>
        /// <param name="aChecked">Checked or not</param>
        /// <returns>The new node</returns>
        private TreeNode UpdateNode(TreeNodeCollection aNodes, string aFolder, bool aChecked)
        {
            if (aNodes.Count > 0 && string.IsNullOrEmpty(aNodes[0].Text)) aNodes.RemoveAt(0); // Remove the fake
            TreeNode NewNode = null;
            if (NodesByPath.ContainsKey(aFolder))
                NewNode = NodesByPath[aFolder]; // We already got one...
            else
                NewNode = 
                aNodes[ 
                    aNodes.Add(new TreeNode(NodeName( aFolder ))
                        {
                            Tag = aFolder,
                            Checked = aChecked,
                            SelectedImageIndex = aChecked ? 2 : 0,
                            ImageIndex = aChecked ? 2 : 0,
                        })];
            NodesByPath[aFolder] = NewNode;
            Exception HasErrs;
            bool HasSubs = GetDirectories(aFolder, out HasErrs).Length > 0;
            if (HasErrs != null)
            {
                NewNode.Tag = null;
                NewNode.ForeColor = Color.DarkGray;
                NewNode.ToolTipText = HasErrs.Message;
            }
            // Add a fake node so that the little '+' will show
            if (HasSubs)
                NewNode.Nodes.Add(string.Empty);
            return NewNode;
        }
        /// <summary>
        /// Breaks the path into its parts
        /// </summary>
        /// <param name="aPath">Path to parse</param>
        /// <returns>Components of the path</returns>
        private string[] PathParts(string aPath)
        {
            List<string> Result = new List<string>();
            for (string Part = System.IO.Path.GetDirectoryName(aPath); !string.IsNullOrEmpty(Part); Part = System.IO.Path.GetDirectoryName(Part) )
                Result.Add(Part);
            Result.Reverse();
            return Result.ToArray();
        }
        /// <summary>
        /// This adds a list of nodes to the tree
        /// </summary>
        private void AddSelections()
        {
            foreach (string Entry in itsSelectedFolders)
            {
                TreeNode Parent = this.treeView.Nodes[0];
                foreach (string Part in PathParts(Entry))
                {
                    Parent = UpdateNode(Parent.Nodes, Part, true);
                    Parent.SelectedImageIndex = Parent.ImageIndex = 1;
                }
                UpdateNode(Parent.Nodes, Entry, true);
            }
        }
        /// <summary>
        /// This goes up the tree setting any partial checks
        /// </summary>
        /// <param name="aNode">Node to start</param>
        private void Propagate(TreeNode aNode)
        {
            if (aNode.Parent == null) return;
            for (TreeNode Up = aNode.Parent; Up != null; Up = Up.Parent)
            {
                // Sets partial check if some of the nodes are not checked
                int CheckCount = (from TreeNode qN in Up.Nodes where qN.Checked select qN).Count();
                Up.Checked = CheckCount != 0;
                Up.SelectedImageIndex = Up.ImageIndex = CheckCount == 0 ? 0 : (CheckCount == aNode.Parent.Nodes.Count ? 2 : 1);
            }
        }
        /// <summary>
        /// Set a node checked or not
        /// </summary>
        /// <param name="aNode">Node to set</param>
        /// <param name="aPropagate">Set up-tree nodes</param>
        private void CheckNodes(TreeNode aNode, bool aPropagate)
        {
            if (aPropagate) Propagate(aNode);
            SetChecked(aNode, aNode.Checked);
        }
        /// <summary>
        /// Sets a node and children checked or not [recursive]
        /// </summary>
        /// <param name="aNode">Node to set</param>
        /// <param name="aValue">Checked or not</param>
        private void SetChecked(TreeNode aNode, bool aValue)
        {
            aNode.SelectedImageIndex = aNode.ImageIndex = aValue ? 2 : 0;
            aNode.Checked = aValue;
            foreach (TreeNode Node in aNode.Nodes)
                SetChecked(Node, aValue);
        }
        private TreeNode itsContextNode;
        /// <summary>
        /// Pressed NODE - Determine if User has tried to check a node
        /// </summary>
        private void treeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            itsContextNode = e.Node;
            if (e.Button != MouseButtons.Left) return;
            // See if clicked inside checked zone (36 pixels left of node bounds)
            Rectangle ExpandedBounds = Rectangle.FromLTRB(e.Node.Bounds.Left - 36, e.Node.Bounds.Top, e.Node.Bounds.Right, e.Node.Bounds.Bottom);
            if (ExpandedBounds.Contains(e.Location) && e.Node.Tag != null)
            {
                e.Node.Checked = !e.Node.Checked;
                CheckNodes(e.Node, true);
            }
        }
        /// <summary>
        /// Context menu allows explorer to open
        /// </summary>
        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (itsContextNode != null)
                System.Diagnostics.Process.Start((string)itsContextNode.Tag);
        }
    }
}
