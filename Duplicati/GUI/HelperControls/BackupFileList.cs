using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati.GUI.HelperControls
{
    public partial class BackupFileList : UserControl
    {
        private DateTime m_when;
        private IList<string> m_files;
        private Exception m_exception;
        private Schedule m_schedule;

        public BackupFileList()
        {
            InitializeComponent();
        }

        public void LoadFileList(Schedule schedule, DateTime when, IList<string> filelist)
        {
            backgroundWorker.CancelAsync();
            LoadingIndicator.Visible = true;
            progressBar.Visible = true;
            treeView.Visible = false;
            LoadingIndicator.Text = "Loading filelist, please wait ...";

            m_files = filelist;
            m_when = when;
            m_schedule = schedule;

            if (m_files != null && m_files.Count != 0)
                backgroundWorker_RunWorkerCompleted(null, null);
            else
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
                m_files = files;
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
            }

            if (e != null && e.Cancelled)
                return;

            try
            {
                treeView.BeginUpdate();
                foreach (string s in m_files)
                {
                    TreeNodeCollection c = treeView.Nodes;
                    foreach (string p in s.Split('/'))
                    {
                        if (p != "")
                        {
                            TreeNode t = FindNode(p, c);
                            if (t == null)
                            {
                                t = new TreeNode(p);
                                c.Add(t);
                            }
                            c = t.Nodes;
                        }
                    }
                }
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
    }
}
