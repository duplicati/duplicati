#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;
using System.Data.LightDatamodel;

namespace Duplicati
{
    public partial class ServiceSetup : Form
    {
        private IDataFetcher m_connection;
        public ServiceSetup()
        {
            InitializeComponent();
            m_connection = new DataFetcherNested(Program.DataConnection);

            MainTree.Nodes.Clear();
            foreach (Schedule s in m_connection.GetObjects<Schedule>())
            {
                TreeNode t = new TreeNode(s.Name);
                t.ImageIndex = t.SelectedImageIndex = imageList.Images.IndexOfKey("Backup");
                t.Tag = s;

                if (string.IsNullOrEmpty(s.Path))
                    MainTree.Nodes.Add(t);
                else
                    FindNode(s.Path, true).Add(t);
            }
        }

        private TreeNodeCollection FindNode(string path, bool allowCreate)
        {
            TreeNodeCollection col = MainTree.Nodes;
            TreeNode match = null;

            foreach (string p in path.Split(MainTree.PathSeparator[0]))
            {
                match = null;

                foreach(TreeNode n in col)
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
                        return MainTree.Nodes;
            }

            return col;
        }

        private void AddFolderMenu_Click(object sender, EventArgs e)
        {
            TreeNode t = new TreeNode("New folder");
            t.ImageIndex = t.SelectedImageIndex = imageList.Images.IndexOfKey("Folder");
            if (MainTree.SelectedNode == null || MainTree.SelectedNode.Tag != null)
                MainTree.Nodes.Add(t);
            else
                MainTree.SelectedNode.Nodes.Add(t);
        }

        private void AddBackupMenu_Click(object sender, EventArgs e)
        {
            Schedule s = m_connection.Add<Schedule>();
            s.FullAfter = "6M";
            s.KeepFull = 4;
            s.Path = "New backup";
            s.Repeat = "1W";
            s.Weekdays = "sun,mon,tue,wed,thu,fri,sat";
            s.When = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour + 1, 0, 0);

            TreeNode t = new TreeNode(s.Path);
            t.Tag = s;
            t.ImageIndex = t.SelectedImageIndex = imageList.Images.IndexOfKey("Backup");

            if (MainTree.SelectedNode == null || MainTree.SelectedNode.Tag != null)
                MainTree.Nodes.Add(t);
            else
            {
                MainTree.SelectedNode.Nodes.Add(t);
                s.Path = t.FullPath.Substring(0, t.FullPath.Length - t.Text.Length - MainTree.PathSeparator.Length);
            }

            MainTree.SelectedNode = t;
            t.BeginEdit();
        }

        private void MainTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            RemoveButton.Enabled = MainTree.SelectedNode != null;
            if (MainTree.SelectedNode == null || MainTree.SelectedNode.Tag as Schedule == null)
            {
                PropertyTabs.Visible = false;
                return;
            }

            PropertyTabs.Visible = true;

            Schedule s = MainTree.SelectedNode.Tag as Schedule;
            scheduleSettings.Setup(s);
            taskSettings.Setup(s);

        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            //TODO: fix this when the "commit recursive" method is implemented
            lock (Program.MainLock)
            {
                m_connection.CommitAll();
                Program.DataConnection.CommitAll();
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void MainTree_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e == null || e.Node == null)
                return;

            e.Node.Text = e.Label;

            if (e.Node.Tag as Schedule == null)
            {
                Queue<TreeNodeCollection> nodelist = new Queue<TreeNodeCollection>();
                nodelist.Enqueue(e.Node.Nodes);
                while (nodelist.Count > 0)
                {
                    TreeNodeCollection col = nodelist.Dequeue();
                    foreach (TreeNode n in col)
                        if (n.Nodes.Count > 0)
                            nodelist.Enqueue(n.Nodes);
                        else if (n.Tag as Schedule != null)
                            UpdatePathAndName(n);
                }
            }
            else
                UpdatePathAndName(e.Node);
        }

        private void UpdatePathAndName(TreeNode n)
        {
            if (n == null || n.Tag as Schedule == null)
                return;

            (n.Tag as Schedule).Name = n.Text;
            if (n.FullPath != n.Text)
                (n.Tag as Schedule).Path = n.FullPath.Substring(0, n.FullPath.Length - n.Text.Length - MainTree.PathSeparator.Length);
        }

        private void playToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode n = playToolStripMenuItem.Tag as TreeNode;
            if (n == null)
                return;

            if (n.Tag as Schedule == null)
            {
                Queue<TreeNodeCollection> nodelist = new Queue<TreeNodeCollection>();
                nodelist.Enqueue(n.Nodes);
                while (nodelist.Count > 0)
                {
                    TreeNodeCollection col = nodelist.Dequeue();
                    foreach (TreeNode nx in col)
                        if (nx.Nodes.Count > 0)
                            nodelist.Enqueue(nx.Nodes);
                        else if (nx.Tag as Schedule != null)
                            Program.WorkThread.AddTask(nx.Tag as Schedule);
                }
            }
            else
                Program.WorkThread.AddTask(n.Tag as Schedule);
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            if (MainTree.SelectedNode == null)
                return;

            if (MainTree.SelectedNode.Tag as Schedule != null)
            {
                if (MessageBox.Show(this, "Remove the backup '" + (MainTree.SelectedNode.Tag as Schedule).Name + "' ?", Application.ProductName, MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
                {
                    m_connection.DeleteObject(MainTree.SelectedNode.Tag);
                    MainTree.SelectedNode.Remove();
                }
            }
            else
            {
                if (MessageBox.Show(this, "Do you want to delete this folder and all the backups contained in it?", Application.ProductName, MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
                {
                    Queue<TreeNodeCollection> nodelist = new Queue<TreeNodeCollection>();
                    nodelist.Enqueue(MainTree.SelectedNode.Nodes);
                    while (nodelist.Count > 0)
                    {
                        TreeNodeCollection col = nodelist.Dequeue();
                        foreach (TreeNode n in col)
                            if (n.Nodes.Count > 0)
                                nodelist.Enqueue(n.Nodes);
                            else if (n.Tag as Schedule != null)
                                m_connection.DeleteObject(n.Tag);
                    }

                    MainTree.SelectedNode.Remove();
                }
            }

        }

        private void TreeMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            Point p = MainTree.PointToClient(Cursor.Position);
            TreeNode n = MainTree.GetNodeAt(p);
            MainTree.SelectedNode = n;
            playToolStripMenuItem.Enabled = n != null;
            playToolStripMenuItem.Tag = n;
        }
    }
}