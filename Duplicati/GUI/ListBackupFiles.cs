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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI
{
    public partial class ListBackupFiles : Form
    {

        private object m_lock = new object();
        private System.Threading.Thread m_thread = null;

        private const string FOLDER_IMAGE_KEY = "folder";
        private const string NEW_FOLDER_IMAGE_KEY = "new folder";
        private const string REMOVED_FOLDER_IMAGE_KEY = "removed folder";
        private const string ADDED_OR_MODIFIED_FILE_IMAGE_KEY = "added or modified file";
        private const string CONTROL_FILE_IMAGE_KEY = "control file";
        private const string DELETED_FILE_IMAGE_KEY = "deleted file";
        private const string ADDED_FILE_IMAGE_KEY = "added file";
        private const string MODIFIED_FILE_IMAGE_KEY = "modified file";
        private const string INCOMPLETE_FILE_IMAGE_KEY = "incomplete file";

        public ListBackupFiles()
        {
            InitializeComponent();

            imageList.Images.Add(FOLDER_IMAGE_KEY, Properties.Resources.FolderOpen);
            imageList.Images.Add(NEW_FOLDER_IMAGE_KEY, Properties.Resources.AddedFolder);
            imageList.Images.Add(REMOVED_FOLDER_IMAGE_KEY, Properties.Resources.DeletedFolder);
            imageList.Images.Add(ADDED_OR_MODIFIED_FILE_IMAGE_KEY, Properties.Resources.AddedOrModifiedFile);
            imageList.Images.Add(CONTROL_FILE_IMAGE_KEY, Properties.Resources.ControlFile);
            imageList.Images.Add(DELETED_FILE_IMAGE_KEY, Properties.Resources.DeletedFile);
            imageList.Images.Add(ADDED_FILE_IMAGE_KEY, Properties.Resources.AddedFile);
            imageList.Images.Add(MODIFIED_FILE_IMAGE_KEY, Properties.Resources.ModifiedFile);
            imageList.Images.Add(INCOMPLETE_FILE_IMAGE_KEY, Properties.Resources.IncompleteFile);
            this.Icon = Properties.Resources.TrayNormal;

#if DEBUG
            this.Text += " (DEBUG)";
#endif
        }

        public void ShowList(Control owner, Datamodel.Schedule schedule, DateTime when)
        {
            backgroundWorker1.RunWorkerAsync(new object[] { schedule, when });
            this.ShowDialog(owner);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            lock (m_lock)
                m_thread = System.Threading.Thread.CurrentThread;

            try
            {
                object[] args = (object[])e.Argument;
                DuplicatiRunner r = new DuplicatiRunner();
                IList<string> sourceFolders = r.ListSourceFolders((Datamodel.Schedule)args[0], (DateTime)args[1]);
                if (r.IsAborted)
                    e.Cancel = true;
                else
                {
                    r = new DuplicatiRunner();
                    List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> files = r.ListActualFiles((Datamodel.Schedule)args[0], (DateTime)args[1]);

                    e.Result = new KeyValuePair<IList<string>, List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>>>(sourceFolders, files);

                    if (r.IsAborted)
                        e.Cancel = true;
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
                    m_thread = null;
            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //If the user has closed, ignore any results
            if (!this.Visible)
                return;

            if (e.Cancelled)
                this.Close();
            else if (e.Error != null || e.Result == null)
            {
                Exception ex = e.Error;
                if (ex == null)
                    ex = new Exception(Strings.ListBackupFiles.NoDataError);

                MessageBox.Show(this, string.Format(Strings.Common.GenericError, ex.ToString()), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
            else
            {
                try
                {
                    ContentTree.BeginUpdate();
                    KeyValuePair<IList<string>, List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>>> res = (KeyValuePair<IList<string>, List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>>>)e.Result;
                    
                    IList<string> sourcefolders = res.Key;
                    List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> entries = res.Value;

                    List<string> addedfolders = new List<string>();
                    List<string> removedfolders = new List<string>();
                    List<string> addedOrUpdatedfiles = new List<string>();
                    List<string> updatedfiles = new List<string>();
                    List<string> addedfiles = new List<string>();
                    List<string> incompletefiles = new List<string>();
                    List<string> deletedfiles = new List<string>();
                    List<string> controlfiles = new List<string>();

                    foreach (KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string> x in entries)
                        switch (x.Key)
                        {
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedFolder:
                                addedfolders.Add(x.Value);
                                break;
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.DeletedFolder:
                                removedfolders.Add(x.Value);
                                break;
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedOrUpdatedFile:
                                addedOrUpdatedfiles.Add(x.Value);
                                break;
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedFile:
                                addedfiles.Add(x.Value);
                                break;
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.UpdatedFile:
                                updatedfiles.Add(x.Value);
                                break;
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.IncompleteFile:
                                incompletefiles.Add(x.Value);
                                break;
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.ControlFile:
                                controlfiles.Add(x.Value);
                                break;
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.DeletedFile:
                                deletedfiles.Add(x.Value);
                                break;
                        }


                    addedfolders.Sort();
                    removedfolders.Sort();
                    deletedfiles.Sort();
                    addedOrUpdatedfiles.Sort();
                    addedfiles.Sort();
                    updatedfiles.Sort();
                    incompletefiles.Sort();
                    controlfiles.Sort();

                    foreach (string s in addedfolders)
                        AddTreeItem(s, NEW_FOLDER_IMAGE_KEY);
                    foreach (string s in removedfolders)
                        AddTreeItem(s, REMOVED_FOLDER_IMAGE_KEY);
                    foreach (string s in addedOrUpdatedfiles)
                        AddTreeItem(s, ADDED_OR_MODIFIED_FILE_IMAGE_KEY);
                    foreach (string s in addedfiles)
                        AddTreeItem(s, ADDED_FILE_IMAGE_KEY);
                    foreach (string s in updatedfiles)
                        AddTreeItem(s, MODIFIED_FILE_IMAGE_KEY);
                    foreach (string s in incompletefiles)
                        AddTreeItem(s, INCOMPLETE_FILE_IMAGE_KEY);
                    foreach (string s in controlfiles)
                        AddTreeItem(s, CONTROL_FILE_IMAGE_KEY);
                    foreach (string s in deletedfiles)
                        AddTreeItem(s, DELETED_FILE_IMAGE_KEY);

                    //Patch display to show actual source folder rather than the internal enumeration system
                    if (sourcefolders != null && sourcefolders.Count > 1)
                    {
                        foreach (TreeNode t in ContentTree.Nodes)
                        {
                            int ix;
                            if (int.TryParse(t.Text, out ix))
                            {
                                if (ix >= 0 && ix < sourcefolders.Count)
                                    t.Text = sourcefolders[ix];
                            }
                        }
                    }



                }
                finally
                {
                    ContentTree.EndUpdate();
                    ContentPanel.Visible = true;
                }
            }

        }

        private void AddTreeItem(string value, string imagekey)
        {
            if (value.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                value = value.Substring(0, value.Length - 1);

            string[] items = value.Split(System.IO.Path.DirectorySeparatorChar);
            TreeNodeCollection parent = ContentTree.Nodes;

            for (int i = 0; i < items.Length; i++)
            {
                TreeNode match = null;
                foreach (TreeNode n in parent)
                    if (n.Text == items[i])
                    {
                        match = n;
                        break;
                    }

                if (match == null)
                {
                    match = new TreeNode(items[i]);
                    match.ImageKey = match.SelectedImageKey = 
                        i == items.Length - 1 ? imagekey : FOLDER_IMAGE_KEY;

                    switch (match.ImageKey)
                    {
                        case FOLDER_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipExistingFolder;
                            break;
                        case NEW_FOLDER_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipAddedFolder;
                            break;
                        case REMOVED_FOLDER_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipDeletedFolder;
                            break;
                        case ADDED_OR_MODIFIED_FILE_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipAddedOrModifiedFile;
                            break;
                        case ADDED_FILE_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipAddedFile;
                            break;
                        case MODIFIED_FILE_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipModifiedFile;
                            break;
                        case INCOMPLETE_FILE_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipIncompleteFile;
                            break;
                        case CONTROL_FILE_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipControlFile;
                            break;
                        case DELETED_FILE_IMAGE_KEY:
                            match.ToolTipText = Strings.ListBackupFiles.TooltipDeletedFile;
                            break;
                    }
                    parent.Add(match);
                }

                parent = match.Nodes;
            }
        }

        private void ListBackupFiles_FormClosing(object sender, FormClosingEventArgs e)
        {
            lock (m_lock)
                if (m_thread != null)
                    m_thread.Abort();
        }
    }
}