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
using System.Collections;

namespace Duplicati.GUI.HelperControls
{
    public partial class BackupFileList : UserControl
    {
        private DateTime m_when;
        private List<string> m_files;
        private List<string> m_sourcefolders;
        private List<string> m_targetfolders;
        private string m_defaultTarget;
        private TreeNode[] m_rootnodes;

        private Schedule m_schedule;
        private bool m_isInCheck = false;

        private string m_localizedLoadingText;

        private object m_lock = new object();
        private System.Threading.Thread m_workerThread = null;

        public event EventHandler FileListLoaded;

        public BackupFileList()
        {
            InitializeComponent();
            m_localizedLoadingText = LoadingIndicator.Text;
        }

        public void LoadFileList(Schedule schedule, DateTime when, List<string> filelist, List<string> targetFolders, string defaultTarget)
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
            m_defaultTarget = defaultTarget;
            m_targetfolders = targetFolders;
            m_rootnodes = null;

            if (!backgroundWorker.IsBusy)
                backgroundWorker.RunWorkerAsync();
        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                lock (m_lock)
                    m_workerThread = System.Threading.Thread.CurrentThread;

                DuplicatiRunner r = new DuplicatiRunner();
                //TODO: Speed up by returning the source folders from ListFiles somehow?
                IList<string> sourcefolders = r.ListSourceFolders(m_schedule, m_when);
                if (r.IsAborted || backgroundWorker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                if (m_files == null || m_files.Count == 0)
                {
                    IList<string> files = r.ListFiles(m_schedule, m_when);
                    if (backgroundWorker.CancellationPending || r.IsAborted)
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

                if (m_sourcefolders != null)
                {
                    m_sourcefolders.Clear();
                    m_sourcefolders.AddRange(sourcefolders);
                }
                else
                    m_sourcefolders = new List<string>(sourcefolders);

                if (m_targetfolders == null)
                    m_targetfolders = new List<string>();

                if (m_targetfolders.Count != m_sourcefolders.Count)
                {
                    m_targetfolders.Clear();
                    for (int i = 0; i < m_sourcefolders.Count; i++)
                        m_targetfolders.Add(null);
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
                System.Threading.Thread.ResetAbort();
                e.Cancel = true;
            }
            finally
            {
                lock (m_lock)
                    m_workerThread = null;
            }
        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            treeView.Nodes.Clear();
            if (e != null && e.Error != null)
            {
                LoadingIndicator.Visible = true;
                treeView.Visible = false;
                progressBar.Visible = false;
                LoadingIndicator.Text = e.Error.Message;
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

                //At this point the folders are named 0..n if there are multiple
                if (m_sourcefolders.Count > 1)
                {
                    m_rootnodes = new TreeNode[m_sourcefolders.Count];
                    foreach (TreeNode tn in treeView.Nodes)
                    {
                        int ix;
                        if (!int.TryParse(tn.Text, out ix))
                            ix = -1;

                        if (ix < 0 || ix >= m_rootnodes.Length)
                        {
                            //TODO: Translate
                            MessageBox.Show(this, string.Format("A source folder with index {0} ({1}) was found, but the list of source folders have the following {2} entries: {3}{4}", ix, tn.Text, m_sourcefolders.Count, Environment.NewLine, string.Join(Environment.NewLine, m_sourcefolders.ToArray())), "Internal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                            m_rootnodes[ix] = tn;
                    }

                    for (int i = 0; i < m_rootnodes.Length; i++)
                        if (m_rootnodes[i] == null)
                        {
                            TreeNode tn = new TreeNode(i.ToString());
                            treeView.Nodes.Add(tn);
                            m_rootnodes[i] = tn;
                        }
                }

                //This call updates the names of the nodes to fit their source/target
                RefreshRootDisplay(true);

                //TODO: Apparently it is much faster to sort nodes when the tree is detached
                treeView.Sort();
            }
            finally
            {
                treeView.EndUpdate();
            }

            if (FileListLoaded != null)
                FileListLoaded(this, null);

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
                if ((x as TreeNode) == null || (y as TreeNode) == null)
                    return 0;
                    
                if ((x as TreeNode).Tag == null || (y as TreeNode).Tag == null)
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

        public List<string> TargetFolders { get { return m_targetfolders; } }
        public string DefaultTarget
        {
            get { return m_defaultTarget; }
            set 
            { 
                m_defaultTarget = value;
                try
                {
                    treeView.BeginUpdate();
                    RefreshRootDisplay(false);
                }
                finally
                {
                    treeView.EndUpdate();
                }
            }
        }

        public List<string> LoadedFileList { get { return m_files; } }

        public List<string> CheckedFiles
        {
            get
            {
                try
                {
                    treeView.BeginUpdate();
                    
                    //HACK: Re-name the root nodes to match their real path
                    if (m_sourcefolders != null && m_sourcefolders.Count > 1)
                        for (int i = 0; i < m_rootnodes.Length; i++)
                            m_rootnodes[i].Text = i.ToString();
                    
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
                                files.Add(Library.Utility.Utility.AppendDirSeparator(t.FullPath));
                            else
                                files.Add(t.FullPath);
                        }
                    }

                    return files;
                }
                finally
                {
                    //HACK: Rename them back
                    RefreshRootDisplay(false);
                    treeView.EndUpdate();
                }
            }
            set
            {
                try
                {
                    treeView.BeginUpdate();

                    //HACK: Re-name the root nodes to match their real path
                    if (m_sourcefolders != null && m_sourcefolders.Count > 1)
                        for (int i = 0; i < m_rootnodes.Length; i++)
                            m_rootnodes[i].Text = i.ToString();

                    if (value != null)
                        foreach (string s in value)
                        {
                            TreeNodeCollection col = treeView.Nodes;
                            TreeNode p = null;
                            foreach (string e in s.Split(System.IO.Path.DirectorySeparatorChar))
                            {
                                foreach (TreeNode n in col)
                                    if (n.Text.Equals(e, Library.Utility.Utility.ClientFilenameStringComparision))
                                    {
                                        p = n;
                                        col = n.Nodes;
                                        break;
                                    }
                            }

                            if (p != null)
                            {
                                p.Checked = true;
                                while (p.Parent != null)
                                {
                                    p.Parent.Expand();
                                    p = p.Parent;
                                }
                            }
                        }
                }
                finally
                {
                    //HACK: Rename them back
                    RefreshRootDisplay(false);
                    treeView.EndUpdate();
                }
            }
        }


        public string CheckedAsFilter
        {
            get
            {
                List<KeyValuePair<bool, string>> filter = new List<KeyValuePair<bool, string>>();

                treeView.PathSeparator = System.IO.Path.DirectorySeparatorChar.ToString();

                foreach(string path in this.CheckedFiles)
                    filter.Add(new KeyValuePair<bool, string>(true, Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(treeView.PathSeparator + path)));

                //Exclude everything else
                filter.Add(new KeyValuePair<bool, string>(false, ".*"));

                return Library.Utility.FilenameFilter.EncodeAsFilter(filter);
            }
        }

        public int CheckedCount { get { return this.CheckedFiles.Count; } }

        private void RefreshRootDisplay(bool sort)
        {
            if (m_sourcefolders == null || m_sourcefolders.Count <= 1 || m_rootnodes == null)
                return;

            string[] targets = this.TargetSuggestions;
            for (int index = 0; index < m_rootnodes.Length; index++)
            {
                m_rootnodes[index].Text = m_sourcefolders[index] + " => " + (!string.IsNullOrEmpty(m_targetfolders[index]) ? m_targetfolders[index] : targets[index]);
                m_rootnodes[index].ToolTipText = string.Format(Strings.BackupFileList.RootNodeTooltip, m_sourcefolders[index], string.IsNullOrEmpty(m_targetfolders[index]) ? targets[index] : m_targetfolders[index]);
            }

            if (sort)
                treeView.Sort();
        }

        public string[] TargetSuggestions
        {
            get
            {
                if (m_sourcefolders == null || m_sourcefolders.Count <= 1)
                    return null;

                List<string> suggestions = new List<string>();
                for (int i = 0; i < m_sourcefolders.Count; i++)
                {
                    string s = m_sourcefolders[i];
                    //HACK: We use a leading / in the path name to detect source OS
                    // all paths are absolute, so this detects all unix like systems
                    string dirSepChar = m_sourcefolders[i].StartsWith("/") ? "/" : "\\";

                    if (s.EndsWith(dirSepChar))
                        s = s.Substring(0, s.Length - 1);

                    int lix = s.LastIndexOf(dirSepChar);
                    if (lix < 0 || lix + 1 >= s.Length)
                        s = i.ToString();
                    else
                        s = s.Substring(lix + 1);

                    foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                        s = s.Replace(c, '_');

                    suggestions.Add(s);
                }

                Dictionary<string, int> duplicates = new Dictionary<string, int>(Library.Utility.Utility.ClientFilenameStringComparer);
                for (int i = 0; i < suggestions.Count; i++)
                    if (duplicates.ContainsKey(suggestions[i]))
                        duplicates[suggestions[i]]++;
                    else
                        duplicates[suggestions[i]] = 1;

                string[] targets = new string[m_rootnodes.Length];

                for (int index = 0; index < m_rootnodes.Length; index++)
                {
                    string suffix = duplicates[suggestions[index]] > 1 ? index.ToString() : suggestions[index];
                    targets[index] = string.IsNullOrEmpty(m_defaultTarget) ? "" : Library.Utility.Utility.AppendDirSeparator(m_defaultTarget) + suffix;
                }

                return targets;
            }
        }

        private void treeView_BeforeLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            e.CancelEdit = true;
            if (m_sourcefolders != null && m_sourcefolders.Count > 1 && Array.IndexOf<TreeNode>(m_rootnodes, e.Node) >= 0)
                browseForTargetFolder(e.Node);
        }

        private void changeDestinationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            browseForTargetFolder(changeDestinationToolStripMenuItem.Tag as TreeNode);
        }

        private void browseForTargetFolder(TreeNode node)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
            {
                m_targetfolders[Array.IndexOf<TreeNode>(m_rootnodes, node)] = folderBrowserDialog.SelectedPath;
                try
                {
                    treeView.BeginUpdate();
                    RefreshRootDisplay(false);
                }
                finally
                {
                    treeView.EndUpdate();
                }
            }
        }

        private void contextMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            TreeNode node = treeView.GetNodeAt(this.PointToClient(Cursor.Position));
            if (node == null || m_sourcefolders == null || m_sourcefolders.Count <= 1 || Array.IndexOf<TreeNode>(m_rootnodes, node) < 0)
                e.Cancel = true;
            else
                changeDestinationToolStripMenuItem.Tag = node;

        }

        private void treeView_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2)
                treeView_BeforeLabelEdit(treeView, new NodeLabelEditEventArgs(treeView.SelectedNode));
        }

        private void treeView_DoubleClick(object sender, EventArgs e)
        {
            TreeNode node = treeView.GetNodeAt(this.PointToClient(Cursor.Position));
            if (node != null)
                treeView_BeforeLabelEdit(treeView, new NodeLabelEditEventArgs(treeView.SelectedNode));
        }
    }
}
