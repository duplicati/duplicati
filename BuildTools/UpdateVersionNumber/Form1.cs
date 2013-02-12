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

namespace UpdateVersionNumber
{
    public partial class Form1 : Form
    {
        private readonly System.Text.RegularExpressions.Regex RXP = new System.Text.RegularExpressions.Regex(@"\[assembly\: AssemblyVersion\(\""(?<version>\d+\.\d+\.\d+\.\d+)\""\)\]");
        private readonly System.Text.RegularExpressions.Regex RXP2 = new System.Text.RegularExpressions.Regex(@"\[assembly\: AssemblyFileVersion\(\""(?<version>\d+\.\d+\.\d+\.\d+)\""\)\]");
        private readonly System.Text.RegularExpressions.Regex RXP3 = new System.Text.RegularExpressions.Regex(@"\[assembly\: AssemblyFileVersionAttribute\(\""(?<version>\d+\.\d+\.\d+\.\d+)\""\)\]");

        private readonly System.Text.RegularExpressions.Regex RXP4 = new System.Text.RegularExpressions.Regex(@"\<\?define ProductVersion\=\""(?<version>\d+\.\d+\.\d+\.\d+)\"" \?\>");

        private readonly System.Text.RegularExpressions.Regex RXP5 = new System.Text.RegularExpressions.Regex(@"newVersion\=\""(?<version>\d+\.\d+\.\d+\.\d+)\""");

        public Form1()
        {
            InitializeComponent();
            panel1.Dock = DockStyle.Fill;
            panel2.Dock = DockStyle.Fill;
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            panel1.Visible = false;
            panel2.Visible = true;

            backgroundWorker1.RunWorkerAsync(textBox2.Text);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            textBox2.Text = Application.StartupPath;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog(this) == DialogResult.OK)
                if (textBox2.Text == folderBrowserDialog1.SelectedPath)
                    textBox2_TextChanged(sender, e);
                else
                    textBox2.Text = folderBrowserDialog1.SelectedPath;
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            List<DisplayItem> results = new List<DisplayItem>();
            e.Result = results;
            Queue<string> folders = new Queue<string>();
            string startfolder = e.Argument as string;
            if (string.IsNullOrEmpty(startfolder))
                return;

            folders.Enqueue(startfolder);

            while (folders.Count > 0)
            {
                string folder = folders.Dequeue();

                foreach (string f in System.IO.Directory.GetDirectories(folder))
                    folders.Enqueue(f);

                if (backgroundWorker1.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                string file = System.IO.Path.Combine(folder, "AssemblyInfo.cs");
                if (System.IO.File.Exists(file))
                {
                    string content = System.IO.File.ReadAllText(file);
                    System.Text.RegularExpressions.Match m = RXP.Match(content);
                    if (!m.Success)
                        m = RXP2.Match(content);
                    if (!m.Success)
                        m = RXP3.Match(content);

                    if (m.Success)
                    {
                        string v = m.Groups["version"].Value;
                        results.Add(new DisplayItem(file, new Version(v)));
                    }
                    
                }
                
                file = System.IO.Path.Combine(folder, "UpgradeData.wxi");
                if (System.IO.File.Exists(file))
                {
                    string content = System.IO.File.ReadAllText(file);
                    System.Text.RegularExpressions.Match m = RXP4.Match(content);

                    if (m.Success)
                    {
                        string v = m.Groups["version"].Value;
                        results.Add(new DisplayItem(file, new Version(v)));
                    }
                }

                file = System.IO.Path.Combine(folder, "AssemblyRedirects.xml");
                if (System.IO.File.Exists(file))
                {
                    string content = System.IO.File.ReadAllText(file);
                    System.Text.RegularExpressions.Match m = RXP5.Match(content);

                    if (m.Success)
                    {
                        string v = m.Groups["version"].Value;
                        results.Add(new DisplayItem(file, new Version(v)));
                    }
                }

            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            panel1.Visible =true;
            panel2.Visible = false;

            if (e.Error != null)
                MessageBox.Show(this, "An error occured: " + e.Error.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

            if (e.Cancelled || e.Error != null)
            {
                checkedListBox1.Items.Clear();
                button2.Enabled = false;
                return;
            }

            Dictionary<string, bool> overrideDefault = new Dictionary<string, bool>(System.Environment.OSVersion.Platform == PlatformID.MacOSX || System.Environment.OSVersion.Platform == PlatformID.Unix ? StringComparer.InvariantCulture : StringComparer.InvariantCultureIgnoreCase );
            string configfile = null;
            bool updateDefault = true;
            try
            {
                configfile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "UpdateVersionNumber.config");
                if (System.IO.File.Exists(configfile))
                {
                    System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                    doc.Load(configfile);

                    System.Xml.XmlNode root = doc["root"];
                    if (root.Attributes["update_as_default"] != null)
                        updateDefault = bool.Parse(root.Attributes["update_as_default"].Value);

                    foreach (System.Xml.XmlNode file in root.SelectNodes("file"))
                    {
                        string filename = file.InnerText;
                        if (!System.IO.Path.IsPathRooted(filename))
                            filename = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), filename);

                        overrideDefault[filename] = bool.Parse(file.Attributes["update"].Value);
                    }
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(this, string.Format("An error occured while reading config file \"{0}\": {1}", configfile, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            button2.Enabled = true;
            List<DisplayItem> files = (List<DisplayItem>)e.Result;
            checkedListBox1.Items.Clear();
            Version vmax = new Version();
            foreach (DisplayItem d in files)
            {
                checkedListBox1.Items.Add(d, overrideDefault.ContainsKey(d.File) ? overrideDefault[d.File] : updateDefault);
                vmax = d.Version > vmax ? d.Version : vmax;
            }

            textBox1.Text = vmax.ToString();

        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (!backgroundWorker1.CancellationPending && backgroundWorker1.IsBusy)
                backgroundWorker1.CancelAsync();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (checkedListBox1.CheckedItems.Count == 0)
            {
                MessageBox.Show(this, "No files selected", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Version v;

            try
            {
                v = new Version(textBox1.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Invalid version number: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string replacement = "[assembly: AssemblyVersion(\"" + v.ToString() + "\")]";
            string replacement2 = "[assembly: AssemblyFileVersion(\"" + v.ToString() + "\")]";
            string replacement3 = "[assembly: AssemblyFileVersionAttribute(\"" + v.ToString() + "\")]";
            string replacement4 = "<?define ProductVersion=\"" + v.ToString() + "\" ?>";
            string replacement5 = "newVersion=\"" + v.ToString() + "\"";
            bool errors = false;

            foreach (DisplayItem d in checkedListBox1.CheckedItems)
            {
                try
                {
                    string content = System.IO.File.ReadAllText(d.File);
                    if (d.File.ToLower().EndsWith(".wxi"))
                        content = RXP4.Replace(content, replacement4);
                    else if (System.IO.Path.GetFileName(d.File).Equals("AssemblyRedirects.xml", StringComparison.InvariantCultureIgnoreCase))
                    {
                        content = RXP5.Replace(content, replacement5);
                    }
                    else
                    {
                        content = RXP.Replace(content, replacement);
                        content = RXP2.Replace(content, replacement2);
                        content = RXP3.Replace(content, replacement3);
                    }
                    System.IO.File.WriteAllText(d.File, content);
                }
                catch (Exception ex)
                {
                    errors = true;
                    MessageBox.Show(this, "Failed to update file: " + d.File + "\r\nError: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (!errors)
                this.Close();
        }
    }
}
