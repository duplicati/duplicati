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
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;

namespace LocalizationTool
{
    public partial class UpdateGUI : Form
    {
        private string m_reportfile;
        private int m_totalcount;
        private Dictionary<string, XElement> m_updates = new Dictionary<string, XElement>();

        public UpdateGUI(string reportfile)
        {
            InitializeComponent();
            m_reportfile = reportfile;
        }

        private void UpdateGUI_Load(object sender, EventArgs e)
        {
            panel3.Dock = DockStyle.Fill;
            string culturename = System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(m_reportfile)).Substring(1);
            this.Text = "Localizing culture " + culturename;

            var files = XDocument.Load(m_reportfile).Element("root").Elements("file").Where(n =>
                    (n.Element("missing") != null && n.Element("missing").Elements().Count() > 0)
                    ||
                    (n.Element("not-updated") != null && n.Element("not-updated").Elements().Count() > 0)
                );

            string rootfolder = Duplicati.Library.Utility.Utility.AppendDirSeparator(System.IO.Path.Combine(Application.StartupPath, culturename));

            treeView1.BeginUpdate();
            treeView1.Nodes.Clear();
            foreach (XElement x in files)
            {
                TreeNodeCollection cl = treeView1.Nodes;
                string relpath = x.Attribute("filename").Value.Substring(rootfolder.Length);
                foreach (string s in relpath.Split(System.IO.Path.DirectorySeparatorChar))
                {
                    bool found = false;
                    foreach (TreeNode n in cl)
                        if (n.Text == s)
                        {
                            cl = n.Nodes;
                            found = true;
                            break;
                        }

                    if (!found)
                    {
                        TreeNode nn = new TreeNode(s);
                        nn.ForeColor = Color.DarkBlue;
                        cl.Add(nn);
                        cl = nn.Nodes;
                    }
                }

                foreach (XElement el in x.Element("missing").Elements("item").Union(x.Element("not-updated").Elements("item")))
                {
                    //These show up occasionally, but mut not be translated
                    if (
                        el.Attribute("name").Value.EndsWith(".Name")
                            ||
                        el.Attribute("name").Value.EndsWith(".MappingName")
                    )
                        continue;

                    string val = el.Value;
                    if (val.Length > 100)
                        val = val.Substring(0, 97) + "...";
                    TreeNode tn = new TreeNode(el.Attribute("name").Value + ": " + val);
                    tn.ForeColor = Color.DarkRed;
                    tn.Tag = el;
                    cl.Add(tn);
                    m_totalcount++;
                }
            }
            treeView1.EndUpdate();
            treeView1.ExpandAll();
            if (treeView1.Nodes.Count > 0)
            {
                TreeNode top = treeView1.Nodes[0];
                while (top.Nodes.Count > 0)
                    top = top.Nodes[0];
                treeView1.SelectedNode = top;
            }
            label4.Text = m_totalcount.ToString();
            label6.Text = System.IO.Path.Combine(Application.StartupPath, culturename);
            this.WindowState = FormWindowState.Maximized;
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeView1.SelectedNode == null || treeView1.SelectedNode.Tag as XElement == null)
                panel3.Visible = false;
            else
            {
                panel3.Visible = true;
                XElement el = treeView1.SelectedNode.Tag as XElement;
                panel3.Tag = null;
                textBox1.Text = el.Attribute("name").Value;
                textBox2.Text = el.Value.Replace(Environment.NewLine, "\n").Replace("\n", Environment.NewLine);
                try 
                {
                    textBox2.Focus();
                    textBox2.SelectAll();
                }
                catch { }
                panel3.Tag = el;
            }

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            if (panel3.Tag as XElement == null)
                return;

            XElement el = panel3.Tag as XElement;
            el.Value = textBox2.Text;
            string dictkey = el.Parent.Parent.Attribute("filename").Value + "@" + el.Attribute("name").Value;
            m_updates[dictkey] = el;
            label4.Text = (m_totalcount - m_updates.Count).ToString();

            if (treeView1.SelectedNode != null && treeView1.SelectedNode.Tag == el)
                treeView1.SelectedNode.ForeColor = Color.DarkGreen;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (m_updates.Count > 0)
                if (MessageBox.Show(this, "Close and loose all changes?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                    return;

            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            TreeNode t = treeView1.SelectedNode;
            if (t == null)
                return;

            List<TreeNode> visited = new List<TreeNode>();

            while (t != null)
            {
                if (t.Nodes.Count > 0 && !visited.Contains(t))
                    t = t.Nodes[0];
                else if (t.NextNode != null)
                    t = t.NextNode;
                else
                {
                    if (t.Parent != null && !visited.Contains(t.Parent))
                        visited.Add(t.Parent);
                    t = t.Parent;
                }

                if (t != null && t.ForeColor == Color.DarkRed)
                {
                    treeView1.SelectedNode = t;
                    return;
                }
            }

            MessageBox.Show(this, "No more items", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            foreach (XElement el in m_updates.Values)
            {
                XDocument file = XDocument.Load(el.Parent.Parent.Attribute("filename").Value);
                var insertEl = file.Element("root").Elements("data").Where(c => c.Attribute("name").Value == el.Attribute("name").Value).FirstOrDefault();
                if (insertEl != null)
                    insertEl.Element("value").Value = el.Value;
                else
                    file.Element("root").Elements().Last().AddAfterSelf(
                        new XElement("data",
                            new XAttribute("name", el.Attribute("name").Value),
                            new XAttribute(XNamespace.Xml + "space", "preserve"),
                            new XElement("value", el.Value)
                        )
                    );

                file.Save(el.Parent.Parent.Attribute("filename").Value);
            }

            m_updates.Clear();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void UpdateGUI_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.FormOwnerClosing)
                if (m_updates.Count > 0)
                    if (MessageBox.Show(this, "Close and loose all changes?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }

        }
    }
}
