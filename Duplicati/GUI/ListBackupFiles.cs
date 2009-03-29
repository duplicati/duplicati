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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI
{
    public partial class ListBackupFiles : Form
    {
        public ListBackupFiles()
        {
            InitializeComponent();

            imageList.Images.Add("folder", Properties.Resources.FolderOpen);
            imageList.Images.Add("newfolder", Properties.Resources.AddedFolder);
            imageList.Images.Add("removedfolder", Properties.Resources.DeletedFolder);
            imageList.Images.Add("file", Properties.Resources.AddedOrModifiedFile);
            imageList.Images.Add("controlfile", Properties.Resources.ControlFile);
            imageList.Images.Add("deletedfile", Properties.Resources.DeletedFile);
        }

        public void ShowList(Control owner, Datamodel.Schedule schedule, DateTime when)
        {
            backgroundWorker1.RunWorkerAsync(new object[] { schedule, when });
            this.ShowDialog(owner);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            object[] args = (object[])e.Argument;
            DuplicatiRunner r = new DuplicatiRunner();
            e.Result = r.ListActualFiles((Datamodel.Schedule)args[0], (DateTime)args[1]);
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
                this.Close();
            else if (e.Error != null || e.Result == null)
            {
                Exception ex = e.Error;
                if (ex == null)
                    ex = new Exception("No data returned");

                MessageBox.Show(this, "An error occured: " + ex.ToString(), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
            else
            {
                try
                {
                    ContentTree.BeginUpdate();
                    List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> entries = e.Result as List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>>;

                    List<string> addedfolders = new List<string>();
                    List<string> removedfolders = new List<string>();
                    List<string> addedfiles = new List<string>();
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
                            case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.FullOrPartialFile:
                                addedfiles.Add(x.Value);
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
                    addedfiles.Sort();
                    controlfiles.Sort();

                    foreach (string s in addedfolders)
                        AddTreeItem(s, 1);
                    foreach (string s in removedfolders)
                        AddTreeItem(s, 2);
                    foreach (string s in addedfiles)
                        AddTreeItem(s, 3);
                    foreach (string s in controlfiles)
                        AddTreeItem(s, 4);
                    foreach (string s in deletedfiles)
                        AddTreeItem(s, 5);
                }
                finally
                {
                    ContentTree.EndUpdate();
                    ContentPanel.Visible = true;
                }
            }

        }

        private void AddTreeItem(string value, int imagekey)
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
                    match = new TreeNode(items[i], i == items.Length - 1 ? imagekey : 0, i == items.Length - 1 ? imagekey : 0);
                    parent.Add(match);
                }

                parent = match.Nodes;
            }
        }
    }
}