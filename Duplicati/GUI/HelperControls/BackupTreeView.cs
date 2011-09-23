#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;
using System.Data.LightDatamodel;

namespace Duplicati.GUI.HelperControls
{
    public partial class BackupTreeView : UserControl
    {
        private IDataFetcher m_connection;
        private bool m_allowEdit;
        public event EventHandler TreeDoubleClicked;
        public event EventHandler SelectedBackupChanged;

        public BackupTreeView()
        {
            InitializeComponent();
        }

        public string SelectedFolder
        {
            get 
            {
                if (treeView.SelectedNode == null)
                    return "";
                else if (treeView.SelectedNode.Tag == null)
                    return treeView.SelectedNode.FullPath;
                else
                {
                    string s = treeView.SelectedNode.FullPath;
                    if (s == treeView.SelectedNode.Text)
                        return "";
                    else
                        return s.Substring(0, s.Length - treeView.SelectedNode.Text.Length - treeView.PathSeparator.Length);
                }
            }

            set
            {
                TreeNode match;
                FindNode(value, false, out match);
                if (match != null)
                    match.TreeView.SelectedNode = match;
                else
                    treeView.SelectedNode = null;
            }
        }

        public Schedule SelectedBackup
        {
            get
            {
                if (treeView.SelectedNode == null)
                    return null;
                else
                    return treeView.SelectedNode.Tag as Schedule;
            }
            set
            {
                if (value == null)
                    treeView.SelectedNode = null;
                else
                {
                    TreeNode t;
                    FindNode((string.IsNullOrEmpty(value.Path) ? "" : value.Path + treeView.PathSeparator) + value.Name, false, out t);
                    treeView.SelectedNode = t;
                }
            }
        }


        public void Setup(IDataFetcher connection, bool allowEdit, bool onlyFolders)
        {
            m_connection = connection;
            m_allowEdit = allowEdit;
            List<Schedule> lst;
            lock (Program.DataConnection)
                lst = new List<Schedule>(m_connection.GetObjects<Schedule>());

            treeView.Nodes.Clear();
            foreach (Schedule s in lst)
            {
                TreeNode t = new TreeNode(s.Name);
                t.ImageIndex = t.SelectedImageIndex = imageList.Images.IndexOfKey("Backup");
                t.Tag = s;

                TreeNodeCollection col;

                if (string.IsNullOrEmpty(s.Path))
                    col = treeView.Nodes;
                else
                    col = FindNode(s.Path, true);

                if (!onlyFolders)
                    col.Add(t);
            }

            treeView.LabelEdit = m_allowEdit;
        }

        private TreeNodeCollection FindNode(string path, bool allowCreate)
        {
            TreeNode dummy;
            return FindNode(path, allowCreate, out dummy);
        }

        private TreeNodeCollection FindNode(string path, bool allowCreate, out TreeNode match)
        {
            TreeNodeCollection col = treeView.Nodes;
            match = null;
            if (string.IsNullOrEmpty(path))
                return col;

            foreach (string p in path.Split(treeView.PathSeparator[0]))
            {
                match = null;

                foreach (TreeNode n in col)
                    if (n.Text == p)
                    {
                        match = n;
                        col = n.Nodes;
                        break;
                    }

                if (match == null)
                    if (allowCreate)
                    {
                        match = new TreeNode(p);
                        match.ImageIndex = match.SelectedImageIndex = imageList.Images.IndexOfKey("Folder");
                        col.Add(match);
                        col = match.Nodes;
                        match.Expand();
                    }
                    else
                        return treeView.Nodes;
            }

            return col;
        }

        private TreeNodeCollection GetParentFolder(TreeNode n)
        {
            if (n == null)
                return treeView.Nodes;
            else if (n.Tag as Schedule == null)
                return n.Nodes;
            else
                if (n.Parent == null)
                    return treeView.Nodes;
                else
                    return n.Parent.Nodes;
        }

        public TreeNode AddFolder(string foldername)
        {
            TreeNode t = new TreeNode(string.IsNullOrEmpty(foldername) ? Strings.BackupTreeView.NewFolder : foldername);
            t.ImageIndex = t.SelectedImageIndex = imageList.Images.IndexOfKey("Folder");

            GetParentFolder(treeView.SelectedNode).Add(t);
            return t;
        }

        public void AddBackup(string name)
        {
            Schedule s;
            lock(Program.MainLock)
                s = m_connection.Add<Schedule>();
            s.Task.FullAfter = "6M";
            s.Task.KeepFull = 4;
            s.Path = string.IsNullOrEmpty(name) ? Strings.BackupTreeView.NewBackup : name;
            s.Repeat = "1W";
            s.Weekdays = "sun,mon,tue,wed,thu,fri,sat";
            s.When = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour + 1, 0, 0);

            TreeNode t = new TreeNode(s.Path);
            t.Tag = s;
            t.ImageIndex = t.SelectedImageIndex = imageList.Images.IndexOfKey("Backup");

            GetParentFolder(treeView.SelectedNode).Add(t);
            UpdatePathAndName(t);

            treeView.SelectedNode = t;
            t.BeginEdit();
        }

        private void UpdatePathAndName(TreeNode n)
        {
            if (n == null || n.Tag as Schedule == null)
                return;

            (n.Tag as Schedule).Name = n.Text;
            if (n.FullPath != n.Text)
                (n.Tag as Schedule).Path = n.FullPath.Substring(0, n.FullPath.Length - n.Text.Length - treeView.PathSeparator.Length);
        }

        public void RestoreNode(TreeNode n)
        {
            if (n == null || n.Tag as Schedule == null)
                return;

            Schedule s = n.Tag as Schedule;

            RestoreBackup dlg = new RestoreBackup();
            dlg.Setup(s);
            dlg.ShowDialog(this);

        }


        public void BackupNode(TreeNode n)
        {
            if (n == null)
                return;

            if (n.Tag as Schedule == null)
            {
                foreach(TreeNode nx in FlattenTree(n.Nodes, false))
                    Program.WorkThread.AddTask(new IncrementalBackupTask(nx.Tag as Schedule));
            }
            else
                Program.WorkThread.AddTask(new IncrementalBackupTask(n.Tag as Schedule));
        }

        public void RemoveNode(TreeNode n)
        {
            if (n == null)
                return;

            if (n.Tag as Schedule != null)
            {
                if (MessageBox.Show(this, string.Format(Strings.BackupTreeView.ConfirmRemoveBackup, (n.Tag as Schedule).Name), Application.ProductName, MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
                {
                    lock(Program.MainLock)
                        m_connection.DeleteObject(n.Tag);
                    n.Remove();
                }
            }
            else
            {
                if (MessageBox.Show(this, string.Format(Strings.BackupTreeView.ConfirmDeleteFolder, n.Text), Application.ProductName, MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
                {
                    foreach(TreeNode nx in FlattenTree(n.Nodes, false))
                        lock (Program.MainLock)
                            m_connection.DeleteObject(nx.Tag);

                    n.Remove();
                }
            }
        }

        private void treeView_KeyUp(object sender, KeyEventArgs e)
        {
            if (m_allowEdit && e.KeyCode == Keys.Delete)
            {
                RemoveNode(treeView.SelectedNode);
                e.Handled = true;
            }
        }

        private void treeView_DragDrop(object sender, DragEventArgs e)
        {
            TreeNode source;
            TreeNode target;

            if (GetDragDropItems(e, out source, out target) == DragDropEffects.Move)
            {
                source.Remove();
                if (target == null)
                    treeView.Nodes.Add(source);
                else
                    target.Nodes.Add(source);

                foreach(TreeNode n in FlattenTree(source.Nodes, false))
                    UpdatePathAndName(n);

            }
        }

        private List<TreeNode> FlattenTree(TreeNodeCollection entry, bool includeFolders)
        {
            List<TreeNode> res = new List<TreeNode>();
            Queue<TreeNodeCollection> nodelist = new Queue<TreeNodeCollection>();
            nodelist.Enqueue(entry);
            while (nodelist.Count > 0)
            {
                TreeNodeCollection col = nodelist.Dequeue();
                foreach (TreeNode nx in col)
                {
                    if (nx.Nodes.Count > 0)
                        nodelist.Enqueue(nx.Nodes);
                    if (nx.Tag as Schedule != null || includeFolders)
                        res.Add(nx);
                }
            }
            return res;
        }

        private DragDropEffects GetDragDropItems(DragEventArgs e, out TreeNode source, out TreeNode target)
        {
            source = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            target = null;

            if (source == null)
                return DragDropEffects.None;

            Point pt = treeView.PointToClient(Cursor.Position);
            target = treeView.GetNodeAt(pt.X, pt.Y);
            if (target != null && target.Tag as Schedule != null)
                return DragDropEffects.None;

            //Drag to the root
            if (target == null)
                return DragDropEffects.Move;

            //We now have a valid source and target, make sure the target is not a subnode of the source
            if (source == target)
                return DragDropEffects.None;

            foreach(TreeNode nx in FlattenTree(source.Nodes, true))
                if (nx == target)
                    return DragDropEffects.None;

            return DragDropEffects.Move;
        }

        private void treeView_DragOver(object sender, DragEventArgs e)
        {
            TreeNode source;
            TreeNode target;
            e.Effect = GetDragDropItems(e, out source, out target);
            treeView.SelectedNode = target;
        }

        private void treeView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (!m_allowEdit)
                return;

            if (e.Item as TreeNode != null && (e.Item as TreeNode).TreeView == sender)
                treeView.DoDragDrop(e.Item, DragDropEffects.Move); 
        }

        private void treeView_DoubleClick(object sender, EventArgs e)
        {
            if (this.SelectedBackup != null || this.SelectedFolder != null)
                if (TreeDoubleClicked != null)
                    TreeDoubleClicked(this, e);

        }

        private void treeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (SelectedBackupChanged != null)
                SelectedBackupChanged(this, null);
        }

    }
}
