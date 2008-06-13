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
                MainTree.Nodes.Add(t);
            }
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
            m_connection.CommitAll();
            Program.DataConnection.CommitAll();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void MainTree_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (e == null || e.Node == null || e.Node.Tag as Schedule == null)
                return;

            (e.Node.Tag as Schedule).Name = e.Node.Text;
            if (e.Node.FullPath != e.Node.Text)
                (e.Node.Tag as Schedule).Path = e.Node.FullPath.Substring(0, e.Node.FullPath.Length - e.Node.Text.Length - MainTree.PathSeparator.Length);
        }

        private void playToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode n = MainTree.SelectedNode;
            if (n == null || n.Tag as Schedule == null)
                return;

            /*string pgp_path = System.IO.Path.Combine(Application.StartupPath, "pgp");
            string duplicity_path = System.IO.Path.Combine(Application.StartupPath, "duplicity\\duplicity.py");
            string duplicity_lib_path = System.IO.Path.Combine(Application.StartupPath, "duplicity\\duplicity");
            string python_path = System.IO.Path.Combine(Application.StartupPath, "python25\\python.exe");
            System.Collections.Specialized.StringDictionary env = new System.Collections.Specialized.StringDictionary();

            DuplicityRunner runner = new DuplicityRunner(duplicity_path, pgp_path, python_path, duplicity_lib_path, env);
            runner.Execute(n.Tag as Schedule);*/

            Program.WorkThread.AddTask(n.Tag as Schedule);
        }
    }
}