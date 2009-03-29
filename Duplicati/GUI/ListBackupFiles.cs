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

            imageList1.Images.Add("folder", Properties.Resources.FolderOpen);
            imageList1.Images.Add("newfolder", Properties.Resources.AddedFolder);
            imageList1.Images.Add("removedfolder", Properties.Resources.DeletedFolder);
            imageList1.Images.Add("file", Properties.Resources.AddedOrModifiedFile);
            imageList1.Images.Add("controlfile", Properties.Resources.ControlFile);
            imageList1.Images.Add("deletedfile", Properties.Resources.DeletedFile);
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
                ContentPanel.Visible = true;
                List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>> entries = e.Result as List<KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string>>;
                foreach (KeyValuePair<Library.Main.RSync.RSyncDir.PatchFileType, string> x in entries)
                {
                    ListViewItem lvi = new ListViewItem(x.Value);
                    switch (x.Key)
                    {
                        case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.AddedFolder:
                            lvi.ImageKey = "newfolder";
                            break;
                        case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.DeletedFolder:
                            lvi.ImageKey = "removefolder";
                            break;
                        case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.FullOrPartialFile:
                            lvi.ImageKey = "file";
                            break;
                        case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.ControlFile:
                            lvi.ImageKey = "controlfile";
                            break;
                        case Duplicati.Library.Main.RSync.RSyncDir.PatchFileType.DeletedFile:
                            lvi.ImageKey = "deletedfile";
                            break;
                    }

                    ContentList.Items.Add(lvi);
                }

                ContentList.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
                ContentList.Sorting = SortOrder.Ascending;
                ContentList.Sort();
            }
        }
    }
}