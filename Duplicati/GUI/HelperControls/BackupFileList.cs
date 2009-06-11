#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using System.Collections;

namespace Duplicati.GUI.HelperControls
{
    public partial class BackupFileList : UserControl
    {
        private DateTime m_when;
        private List<string> m_files;
        private Exception m_exception;
        private Schedule m_schedule;
        private bool m_isInCheck = false;

        private string m_localizedLoadingText;

        public BackupFileList()
        {
            InitializeComponent();
            m_localizedLoadingText = LoadingIndicator.Text;

        }

        public void LoadFileList(Schedule schedule, DateTime when, List<string> filelist)
        {
            //backgroundWorker.CancelAsync();
            LoadingIndicator.Visible = true;
            progressBar.Visible = true;
            treeView.Visible = false;
            treeView.TreeViewNodeSorter = new NodeSorter();
            LoadingIndicator.Text = m_localizedLoadingText;

            m_files = filelist;
            m_when = when;
            m_schedule = schedule;

            if (m_files != null && m_files.Count != 0)
                backgroundWorker_RunWorkerCompleted(null, null);
            else if (!backgroundWorker.IsBusy)
                backgroundWorker.RunWorkerAsync();
        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                m_exception = null;
                DuplicatiRunner r = new DuplicatiRunner();
                IList<string> files = r.ListFiles (m_schedule, m_when);
                if (backgroundWorker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }
                if (m_files != null)
                {
                    m_files.Clear();
                    m_files.AddRange(files);
                }
                else
                    m_files = new List<string>(files);
            }
            catch (Exception ex)
            {
                m_exception = ex;
            }
        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            treeView.Nodes.Clear();
            if (m_exception != null)
            {
                LoadingIndicator.Visible = true;
                treeView.Visible = false;
                progressBar.Visible = false;
                LoadingIndicator.Text = m_exception.Message;
                return;
            }

            if (e != null && e.Cancelled)
                return;

            try
            {
                treeView.BeginUpdate();

                bool supported = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.Supported;
                if (supported)
                    treeView.ImageList = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.ImageList;

                foreach (string s in m_files)
                {
                    TreeNodeCollection c = treeView.Nodes;
                    string[] parts = s.Split(System.IO.Path.DirectorySeparatorChar);
                    for(int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] != "")
                        {
                            TreeNode t = FindNode(parts[i], c);
                            if (t == null)
                            {
                                t = new TreeNode(parts[i]);
                                if (supported)
                                {
                                    if (i == parts.Length - 1)
                                        t.ImageIndex = t.SelectedImageIndex = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.GetShellIcon(s);

                                    else
                                        t.ImageIndex = t.SelectedImageIndex = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.GetFolderIcon(false);
                                }

                                //tag = IsFolder
                                t.Tag = !(i == parts.Length - 1);
                                c.Add(t);
                            }
                            c = t.Nodes;
                        }
                    }
                }

                //TODO: Apparently it is much faster to sort nodes when the tree is detached
                treeView.Sort();
            }
            finally
            {
                treeView.EndUpdate();
            }


            LoadingIndicator.Visible = false;
            treeView.Visible = true;
        }

        private TreeNode FindNode(string name, TreeNodeCollection items)
        {
            foreach(TreeNode t in items)
                if (t.Text == name)
                    return t;

            return null;
        }

        private class NodeSorter : IComparer
        {
            #region IComparer Members

            public int Compare(object x, object y)
            {
                if (!(x is TreeNode) || !(y is TreeNode))
                    return 0;

                if ((bool)((TreeNode)x).Tag == (bool)((TreeNode)y).Tag)
                    return string.Compare(((TreeNode)x).Text, ((TreeNode)y).Text);
                else
                    return (bool)((TreeNode)x).Tag ? -1 : 1;
            }

            #endregion
        }

        private void treeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (!m_isInCheck)
            {
                try
                {
                    m_isInCheck = true;
                    Queue<TreeNode> nodes = new Queue<TreeNode>();
                    nodes.Enqueue(e.Node);
                    while (nodes.Count > 0)
                    {
                        TreeNode n = nodes.Dequeue();
                        foreach (TreeNode nx in n.Nodes)
                            nodes.Enqueue(nx);

                        n.Checked = e.Node.Checked;

                    }

                    /*TreeNode nf = e.Node;

                    while (nf.Parent != null)
                    {
                        bool oneChecked = false;
                        bool noneChecked = true;

                        foreach (TreeNode nx in nf.Parent.Nodes)
                        {
                            oneChecked |= nx.Checked;
                            noneChecked &= !nx.Checked;
                        }

                        if (oneChecked && !nf.Parent.Checked)
                            nf.Parent.Checked = true;

                        if (noneChecked && nf.Parent.Checked)
                            nf.Parent.Checked = false;

                        nf = nf.Parent;
                    }*/


                }
                finally
                {
                    m_isInCheck = false;
                }
            }
        }

        private void treeView_AfterCollapse(object sender, TreeViewEventArgs e)
        {
            if (e.Node != null && e.Node.Tag != null && e.Node.Tag is bool)
                e.Node.ImageIndex = e.Node.SelectedImageIndex = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.GetFolderIcon(false);
        }

        private void treeView_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (e.Node != null && e.Node.Tag != null && e.Node.Tag is bool)
                e.Node.ImageIndex = e.Node.SelectedImageIndex = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.GetFolderIcon(true);
        }

        public List<string> CheckedFiles
        {
            get
            {
                List<string> files = new List<string>();
                Queue<TreeNode> items = new Queue<TreeNode>();
                foreach (TreeNode t in treeView.Nodes)
                    items.Enqueue(t);

                treeView.PathSeparator = System.IO.Path.DirectorySeparatorChar.ToString();
                
                while (items.Count > 0)
                {
                    TreeNode t = items.Dequeue();

                    foreach (TreeNode tn in t.Nodes)
                        items.Enqueue(tn);

                    if (t.Checked)
                    {
                        if (t.Tag != null && (bool)t.Tag == true)
                            files.Add(Library.Core.Utility.AppendDirSeperator(t.FullPath));
                        else
                            files.Add(t.FullPath);
                    }
                }

                return files;
            }
        }

        public string CheckedAsFilter
        {
            get
            {
                List<KeyValuePair<bool, string>> filter = new List<KeyValuePair<bool, string>>();

                treeView.PathSeparator = System.IO.Path.DirectorySeparatorChar.ToString();

                foreach(string path in this.CheckedFiles)
                    filter.Add(new KeyValuePair<bool, string>(true, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(treeView.PathSeparator + path)));

                //Exclude everything else
                filter.Add(new KeyValuePair<bool, string>(false, ".*"));

                return Library.Core.FilenameFilter.EncodeAsFilter(filter);
            }
        }

        public int CheckedCount { get { return this.CheckedFiles.Count; } }
    }
}
