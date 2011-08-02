#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    public partial class BackupListDialog : Form
    {
        private string itsURI;
        private Dictionary<string, string> itsOptions;
        /// <summary>
        /// Shows a list of backups. Allows selection and file listing
        /// </summary>
        /// <param name="aName">Job name</param>
        /// <param name="aURI">Source</param>
        /// <param name="aOptions">Used by Duplicati</param>
        public BackupListDialog(string aName, string aURI, Dictionary<string, string> aOptions)
        {
            InitializeComponent();
            itsURI = aURI;
            itsOptions = aOptions;
            this.toolStripLabel1.Text = aURI;
            this.toolStripLabel2.Text = aName;
            this.progressBar.Visible = true;
            this.progressBar.BringToFront();
            this.treeView1.TreeViewNodeSorter = new NodeSorter();
        }
        /// <summary>
        /// Initialize list
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            MakeList();
        }
        /// <summary>
        /// Create new node with backup type and date
        /// </summary>
        private TreeNode NewNode(Duplicati.Library.Main.ManifestEntry aEntry)
        {
            TreeNode Result = new TreeNode((aEntry.IsFull ? "Full: " : "Inc: ") + aEntry.Time.ToString("ddd MM/dd/yyyy hh:mm tt"));
            Result.Tag = aEntry;
            return Result;
        }
        /// <summary>
        /// Creates the display
        /// </summary>
        private void MakeList()
        {
            this.ContentsToolStripButton.Enabled = false;
            this.Cursor = Cursors.WaitCursor;
            this.progressBar.Visible = true;
            this.progressBar.Style = ProgressBarStyle.Marquee;
            List<Duplicati.Library.Main.ManifestEntry> Result = null;
            // Get the list.  Background runs task as separate thread, continues to call DoEvents until finished
            Utility.Tools.Background((Action)
                delegate()
                {
                    using (Duplicati.Library.Main.Interface i = new Duplicati.Library.Main.Interface(itsURI, itsOptions))
                        Result = i.GetBackupSets();
                });
            // Make the tree
            this.treeView1.BeginUpdate();
            foreach (Duplicati.Library.Main.ManifestEntry M in Result)
            {
                TreeNode N = NewNode(M);
                N.Nodes.AddRange((from Duplicati.Library.Main.ManifestEntry Mi in M.Incrementals select NewNode(Mi)).ToArray());
                this.treeView1.Nodes.Add(N);
            }
            this.treeView1.Sort();
            this.treeView1.EndUpdate();
            this.progressBar.Visible = false;
            this.ContentsToolStripButton.Enabled = true;
            this.Cursor = Cursors.Default;
        }
        /// <summary>
        ///  User selected a backup, list the files
        /// </summary>
        private void ContentsToolStripButton_Click(object sender, EventArgs e)
        {
            if (this.treeView1.Nodes.Count == 0) return;
            itsOptions["restore-time"] = ((this.treeView1.SelectedNode == null) ? DateTime.Now :
                ((Duplicati.Library.Main.ManifestEntry)this.treeView1.SelectedNode.Tag).Time).ToString("s"); // should be "o"
            FileListDialog fld = new FileListDialog(this.toolStripLabel2.Text, itsURI, itsOptions);
            fld.ShowDialog();
        }
    }
    /// <summary>
    /// Create a node sorter that implements the IComparer interface.
    /// </summary>
    public class NodeSorter : System.Collections.IComparer
    {
        // Sort by backup time
        public int Compare(object x, object y)
        {
            TreeNode tx = x as TreeNode;
            TreeNode ty = y as TreeNode;
            return DateTime.Compare(((Duplicati.Library.Main.ManifestEntry)tx.Tag).Time, ((Duplicati.Library.Main.ManifestEntry)ty.Tag).Time);
        }
    }
}
