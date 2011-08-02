#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
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
using System.Linq;
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// List the files in a backup
    /// </summary>
    public partial class FileListDialog : Form
    {
        private string itsURI;
        private Dictionary<string, string> itsOptions;
        /// <summary>
        /// List the files in a backup
        /// </summary>
        /// <param name="aName">Job name</param>
        /// <param name="aURI">Source</param>
        /// <param name="aOptions">Used by Duplicati</param>
        public FileListDialog(string aName, string aURI, Dictionary<string, string> aOptions)
        {
            InitializeComponent();
            itsURI = aURI;
            itsOptions = aOptions;
            this.toolStripLabel1.Text = aName;
            this.TimeToolStripLabel.Text = aOptions.ContainsKey("restore-time") ? aOptions["restore-time"] : string.Empty;
        }
        /// <summary>
        /// Initialize the display
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            MakeTree(itsURI, itsOptions);
        }
        /// <summary>
        /// Locate a node by name
        /// </summary>
        /// <param name="aName">Node name</param>
        /// <param name="aNodeCollection">Where to look</param>
        /// <returns>The node or null if not found</returns>
        private TreeNode FindNode(string aName, TreeNodeCollection aNodeCollection)
        {
            foreach (TreeNode N in aNodeCollection)
                if (N.Text.Equals(aName)) return N;
            return null;
        }
        /// <summary>
        /// Nodes
        /// </summary>
        public enum NodeType
        {
            Backup, // a root node
            File,   // a plain file
            Folder  // a directory
        }
        /// <summary>
        /// Replace the leading number with the corresponding source root
        /// </summary>
        /// <param name="aSource">List of source files</param>
        /// <param name="aPart">Index</param>
        /// <returns>Corresponding source root</returns>
        private string Expand(string[] aSource, string aPart)
        {
            int ix;
            if (aSource != null && int.TryParse(aPart, out ix) && ix < aSource.Length)
                return aSource[ix];
            return aPart;
        }
        // A list, handy for finding things without recursing the tree
        private List<TreeNode> itsNodes = new List<TreeNode>();
        /// <summary>
        /// A new tree node contains the name and the icon
        /// </summary>
        /// <param name="aName">File name</param>
        /// <param name="aType">Kind</param>
        /// <returns>new decorated node</returns>
        private TreeNode NewNode(string aName, NodeType aType)
        {
            TreeNode Result = new TreeNode(aName);
            // Get the correct icon from this cool set
            if (OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.Supported)
            {
                switch (aType)
                {
                    case NodeType.Backup:
                        Result.ImageIndex = Result.SelectedImageIndex = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.GetShellIcon("XXX.DupBaK");
                        break;
                    case NodeType.File:
                        Result.ImageIndex = Result.SelectedImageIndex = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.GetShellIcon(aName);
                        break;
                    case NodeType.Folder:
                        Result.ImageIndex = Result.SelectedImageIndex = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.GetFolderIcon(false);
                        break;
                }
            }
            itsNodes.Add(Result); // Easy access later
            return Result;
        }
        /// <summary>
        /// Get the source folders from Duplicati
        /// </summary>
        /// <param name="aURI">Source</param>
        /// <param name="aOptions">Duplicati options</param>
        /// <returns>a list of source folders</returns>
        private string[] ListSourceFolders(string aURI, Dictionary<string, string> aOptions)
        {
            string[] Result = null;
            Exception Ex = null;
            do
            {
                Utility.Tools.Background((Action)delegate()
                    {
                        try
                        {
                            Result = Duplicati.Library.Main.Interface.ListSourceFolders(aURI, FixOptions(aOptions));
                        }
                        catch (System.Security.Cryptography.CryptographicException Exc)
                        {
                            Ex = Exc;
                        }
                    });
                // If there was a crypto error, give user a chance to enter a different password
                // I'm not sure this works
                if (Ex != null && Ex is System.Security.Cryptography.CryptographicException)
                {
                    if (MessageBox.Show("The password was incorrect, enter a different one?", "PASSWORD", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) return null;
                    aOptions["Checksum"] = System.Convert.ToBase64String(EnterPassDialog.FetchProtectedPassword());
                }
            } while (Ex != null);
            return Result;
        }
        /// <summary>
        /// Convert internal options to Duplicati options
        /// </summary>
        /// <param name="aOptions"></param>
        /// <returns></returns>
        private Dictionary<string, string> FixOptions(Dictionary<string, string> aOptions)
        {
            Dictionary<string, string> Result = new Dictionary<string, string>(aOptions);
            // Try to find history entry
            if (aOptions.ContainsKey("restore-time"))
            {
                using (Duplicati.Scheduler.Data.HistoryDataSet hds = new Duplicati.Scheduler.Data.HistoryDataSet())
                {
                    hds.Load();
                    Duplicati.Scheduler.Data.HistoryDataSet.HistoryRow hRow = hds.History.FindByNameActionDate(
                        this.toolStripLabel1.Text, DateTime.Parse(aOptions["restore-time"]));
                    if (hRow != null)
                    {
                        Result["Checksum"] = System.Convert.ToBase64String( hRow.Checksum );
                        Result["encryption-module"] = hRow.CheckMod;
                    }
                }
            }
            // Duplicati should use SecureString
            if (Result.ContainsKey("Checksum"))
            {
                // Unprotect will only work if process is the same user as it was protected, which this one should be.
                Result["passphrase"] = System.Text.ASCIIEncoding.ASCII.GetString(Utility.Tools.Unprotect(System.Convert.FromBase64String(Result["Checksum"])));
                Result.Remove("Checksum");
            }
            return Result;
        }
        /// <summary>
        /// Ask duplicati to list the backup's files
        /// </summary>
        /// <param name="aURI">Source</param>
        /// <param name="aOptions">Duplicati options</param>
        /// <param name="aSourceFolders">Folders form backup</param>
        /// <returns></returns>
        private List<string> ListCurrentFiles(string aURI, Dictionary<string, string> aOptions, string[] aSourceFolders)
        {
            List<string> Result = null;
            // Background will run the action in a separate thread and keep the forms pump going
            Utility.Tools.Background((Action)
                delegate() 
                { 
                    Result = new List<string>(Duplicati.Library.Main.Interface.ListCurrentFiles(aURI, FixOptions(aOptions))); 
                } );
            // If there is only one source folder, Duplicati does not prepend indexes, so add them manually
            if (aSourceFolders != null && aSourceFolders.Length == 1)
            {
                for (int i = 0; i < Result.Count; i++)
                {
                    if (!System.Text.RegularExpressions.Regex.Match(Result[i], @"^[0-9]*\\").Success)
                        Result[i] = @"0\" + Result[i];
                }
            }
            return Result;
        }
        // Create the file tree
        private void MakeTree(string aURI, Dictionary<string, string> aOptions)
        {
            this.Cursor = Cursors.WaitCursor;
            this.RestoreToolStripButton.Enabled = false;
            treeView.Nodes.Clear();
            treeView.Visible = false;
            this.ProgressGroupBox.Visible = true;
            this.progressBar.Style = ProgressBarStyle.Marquee;
            this.MessageTextBox.Text = "Accessing files";
            string[] Sources = ListSourceFolders(aURI, aOptions);
            if (Sources == null) return;
            this.progressBar.Style = ProgressBarStyle.Continuous;
            // Basically stolen from Duplicati
            try
            {
                treeView.BeginUpdate();
                // Add root node icon
                if (OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.Supported)
                {
                    OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.AddIcon("XXX.DupBaK", this.Icon.ToBitmap());
                    treeView.ImageList = OSGeo.MapGuide.Maestro.ResourceEditors.ShellIcons.ImageList;
                }
                List<string> Files = ListCurrentFiles(aURI, aOptions, Sources);
                this.progressBar.Maximum = Files.Count;
                this.progressBar.Value = 0;
                foreach (string s in Files)
                {
                    TreeNodeCollection c = treeView.Nodes;
                    string[] parts = s.Split(System.IO.Path.DirectorySeparatorChar);
                    string[] sParts = (string[])parts.Clone();
                    parts[0] = Expand(Sources, parts[0]);
                    string Name = string.Empty;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        NodeType nt = (i == 0) ? NodeType.Backup : ((i == parts.Length - 1) ? NodeType.File : NodeType.Folder);
                        Name += sParts[i];
                        if (nt != NodeType.File) Name += System.IO.Path.DirectorySeparatorChar;
                        if (parts[i] != "")
                        {
                            TreeNode t = FindNode(parts[i], c);
                            if (t == null)
                            {
                                t = NewNode(parts[i], nt);
                                t.Tag = Name;
                                c.Add(t);
                            }
                            c = t.Nodes;
                        }
                    }
                    this.progressBar.Value++;
                    Application.DoEvents();
                }
                treeView.Sort();
            }
            finally
            {
                treeView.EndUpdate();
                this.ProgressGroupBox.Visible = false;
                treeView.Visible = true;
                CheckedNodes = 0;
                this.RestoreToolStripButton.Enabled = true;
                this.Cursor = Cursors.Default;
            }
        }
        private int CheckedNodes = 0;
        /// <summary>
        /// Duplicati style progress
        /// </summary>
        private void OperationProgress(Duplicati.Library.Main.Interface aCaller, Duplicati.Library.Main.DuplicatiOperation aOperation, Duplicati.Library.Main.DuplicatiOperationMode aSpecificOperation, int aProgress, int aSubprogress, string aMessage, string aSubmessage)
        {
            this.BeginInvoke((Action)delegate()
            {
                if (aProgress < 0)
                {
                    this.progressBar.Style = ProgressBarStyle.Marquee;
                }
                else
                {
                    this.progressBar.Style = ProgressBarStyle.Continuous;
                    this.progressBar.Value = Math.Min(this.progressBar.Maximum, aProgress);
                }
                this.MessageTextBox.Text = aMessage + ":" + aSubmessage;
                this.ProgressGroupBox.Update();
            });
        }
        /// <summary>
        /// Call Duplicati for the restore
        /// </summary>
        /// <param name="aSource">Source file</param>
        /// <param name="aTarget">Place to put it</param>
        /// <param name="aOptions">Duplicati Options</param>
        private void Restore(string aSource, string aTarget, Dictionary<string,string> aOptions)
        {
            this.ProgressGroupBox.Visible = true;
            this.ProgressGroupBox.BringToFront();
            this.progressBar.Style = ProgressBarStyle.Marquee;
            this.RestoreToolStripButton.Enabled = false;
            this.treeView.Enabled = false;
            string Result = null;
            Utility.Tools.Background((Action)
                delegate()
                {
                    using (Duplicati.Library.Main.Interface i = new Duplicati.Library.Main.Interface(aSource, FixOptions(aOptions)))
                        Result = i.Restore(new string[] { aTarget });
                });
            MessageBox.Show(Result);
            this.ProgressGroupBox.Visible = false;
            this.RestoreToolStripButton.Enabled = true;
            this.treeView.Enabled = true;
        }
        /// <summary>
        /// Restore button
        /// </summary>
        private void RestoreToolStripButton_Click(object sender, EventArgs e)
        {
            if (CheckedNodes <= 0)
            {
                MessageBox.Show("No files or folders checked.");
                return;
            }
            // Get where user wants to restore
            if (this.folderBrowserDialog.ShowDialog() == DialogResult.Cancel) return;
            string Items = string.Join(System.IO.Path.PathSeparator.ToString(), 
                itsNodes.Where(Q => Q.Checked).Select(Q => (string)Q.Tag).ToArray());
            if (Items.Length == 0) // Shouldn't happen
            {
                MessageBox.Show("No files selected.");
                CheckedNodes = 0;
                return;
            }
            // Do the restore
            try
            {
                itsOptions["file-to-restore"] = Items;
                Restore(itsURI, folderBrowserDialog.SelectedPath, itsOptions);
                MessageBox.Show("Restore to " + folderBrowserDialog.SelectedPath + " Done.");
            }
            catch (Exception Ex)
            {
                MessageBox.Show("Error restoring: " + Ex.Message);
            }
            finally
            {
                this.ProgressGroupBox.Visible = false;
            }
        }
        /// <summary>
        /// Check changes, keep count
        /// </summary>
        private void treeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            CheckedNodes = e.Node.Checked ? CheckedNodes + 1 : CheckedNodes - 1;
        }
    }
}
