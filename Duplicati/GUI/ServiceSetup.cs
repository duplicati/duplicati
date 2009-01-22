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

namespace Duplicati.GUI
{
    public partial class ServiceSetup : Form
    {
        private IDataFetcherCached m_connection;
        public ServiceSetup()
        {
            InitializeComponent();
            m_connection = new DataFetcherNested(Program.DataConnection);

            backupTreeView.Setup(m_connection, true, false);
            backupTreeView.treeView.AfterSelect += new TreeViewEventHandler(treeView_AfterSelect);
            backupTreeView.treeView.ContextMenuStrip = TreeMenuStrip;
        }

        void treeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            RemoveButton.Enabled = backupTreeView.treeView.SelectedNode != null;
            if (backupTreeView.treeView.SelectedNode == null || backupTreeView.treeView.SelectedNode.Tag as Schedule == null)
            {
                PropertyTabs.Visible = false;
                return;
            }

            PropertyTabs.Visible = true;

            Schedule s = backupTreeView.treeView.SelectedNode.Tag as Schedule;
            scheduleSettings.Setup(s);
            taskSettings.Setup(s);
        }

        private void AddFolderMenu_Click(object sender, EventArgs e)
        {
            backupTreeView.AddFolder(null);
        }

        private void AddBackupMenu_Click(object sender, EventArgs e)
        {
            backupTreeView.AddBackup(null);
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


        private void playToolStripMenuItem_Click(object sender, EventArgs e)
        {
            backupTreeView.BackupNode(playToolStripMenuItem.Tag as TreeNode);
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            backupTreeView.RemoveNode(backupTreeView.treeView.SelectedNode);
        }

        private void TreeMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            Point p = backupTreeView.treeView.PointToClient(Cursor.Position);
            TreeNode n = backupTreeView.treeView.GetNodeAt(p);
            backupTreeView.treeView.SelectedNode = n;
            playToolStripMenuItem.Enabled = n != null;
            playToolStripMenuItem.Tag = n;

            restoreFilesToolStripMenuItem.Enabled = n != null && n.Tag as Schedule != null;
            restoreFilesToolStripMenuItem.Tag = n;
        }

        private void restoreFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            backupTreeView.RestoreNode(restoreFilesToolStripMenuItem.Tag as TreeNode);
        }

        private void scheduleSettings_Load(object sender, EventArgs e)
        {

        }
    }
}