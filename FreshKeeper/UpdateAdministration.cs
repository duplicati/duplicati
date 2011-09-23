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

namespace FreshKeeper
{
    public partial class UpdateAdministration : Form
    {
        public string UpdateFile = null;
        private bool m_isUpdating = false;
        private string m_privateKey = null;

        public UpdateAdministration()
        {
            InitializeComponent();
        }

        private void UpdateAdministration_Load(object sender, EventArgs e)
        {
            if (!System.IO.File.Exists("releasekey.xml"))
            {
                if (MessageBox.Show("Signature key does not exist, create it?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    Application.Exit();
                    return;
                }

                System.Security.Cryptography.RSACryptoServiceProvider rsa = new System.Security.Cryptography.RSACryptoServiceProvider();
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter("releasekey.xml", false))
                    sw.Write(rsa.ToXmlString(true));
            }

            using (System.IO.StreamReader rd = new System.IO.StreamReader("releasekey.xml"))
                m_privateKey = rd.ReadToEnd();

            List<Update> updates = new List<Update>();
            System.Xml.Serialization.XmlSerializer sr = new System.Xml.Serialization.XmlSerializer(typeof(UpdateList));

            UpdateList lst = null;
            if (System.IO.File.Exists(UpdateFile))
                using (System.IO.FileStream fs = new System.IO.FileStream(UpdateFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    lst = (UpdateList)sr.Deserialize(fs);

            SortedList<string, string> applicationNames = new SortedList<string,string>();
            SortedList<string, string> architectures = new SortedList<string, string>();

            if (lst != null && lst.Updates != null)
                foreach (Update u in lst.Updates)
                {
                    listBox1.Items.Add(u);
                    if (!string.IsNullOrEmpty(u.ApplicationName))
                        applicationNames[u.ApplicationName.ToLower().Trim()] = u.ApplicationName;
                    if (!string.IsNullOrEmpty(u.Architecture))
                        architectures[u.Architecture.ToLower().Trim()] = u.Architecture;
                }

            foreach (string s in architectures.Values)
                if (UpdateArchitecture.FindString(s) < 0)
                    UpdateArchitecture.Items.Add(s);

            foreach (string s in applicationNames.Values)
                if (UpdateApplication.FindString(s) < 0)
                    UpdateApplication.Items.Add(s);
        }

        private void AddUpdateButton_Click(object sender, EventArgs e)
        {
            Update u = new Update();
            u.ReleaseDate = DateTime.Now.ToUniversalTime();
            u.SignedFileHash = GetFileHash();
            u.BugfixUpdate = true;
            if (u.SignedFileHash != null)
                listBox1.SelectedIndex = listBox1.Items.Add(u);
        }

        private string GetFileHash()
        {
            if (SelectPackageFile.ShowDialog(this) == DialogResult.OK)
            {
                System.Security.Cryptography.RSACryptoServiceProvider rsa = new System.Security.Cryptography.RSACryptoServiceProvider();
                rsa.FromXmlString(m_privateKey);

                System.Security.Cryptography.SHA1 sha = System.Security.Cryptography.SHA1.Create();
                using (System.IO.FileStream fs = new System.IO.FileStream(SelectPackageFile.FileName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    return Convert.ToBase64String(rsa.SignHash(sha.ComputeHash(fs), System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA1")));
            }

            return null;
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                m_isUpdating = true;
                Update u = listBox1.SelectedItem as Update;
                if (u == null)
                {
                    PropertiesGroup.Enabled = false;
                    UpdateVersion.Text =
                    UpdateArchitecture.Text =
                    UpdateCaption.Text =
                    UpdateChangelog.Text =
                    UpdateDownloadUrls.Text = "";
                    UpdateApplication.Text = "";
                    UpdateDate.Value = UpdateDate.MinDate;

                    UpdateBugfix.Checked = UpdateSecurity.Checked = false;
                }
                else
                {
                    PropertiesGroup.Enabled = true;
                    UpdateVersion.Text = u.Version.ToString();
                    UpdateArchitecture.Text = u.Architecture;
                    UpdateCaption.Text = u.Caption;
                    UpdateChangelog.Text = u.Changelog;
                    UpdateApplication.Text = u.ApplicationName;

                    DateTime date = u.ReleaseDate;
                    if (date > UpdateDate.MaxDate)
                        date = UpdateDate.MaxDate;
                    if (date < UpdateDate.MinDate)
                        date = UpdateDate.MinDate;

                    UpdateDate.Value = date;
                    UpdateDownloadUrls.Text = string.Join("\r\n", u.Urls == null ? new string[] { } : u.Urls);

                    UpdateBugfix.Checked = u.BugfixUpdate;
                    UpdateSecurity.Checked = u.SecurityUpdate;
                }
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void ResetSignatureButton_Click(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null)
                return;

            string hash = GetFileHash();
            if (hash != null)
                u.SignedFileHash = hash;
        }

        private void UpdateVersion_TextChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            try
            {
                u.Version = new Version(UpdateVersion.Text);
                errorProvider.SetError(UpdateVersion, "");
            }
            catch(Exception ex)
            {
                errorProvider.SetIconAlignment(UpdateVersion, ErrorIconAlignment.MiddleLeft);
                errorProvider.SetError(UpdateVersion, ex.Message);
            }
        }

        private void UpdateArchitecture_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateArchitecture_TextChanged(sender, e);
        }

        private void UpdateArchitecture_TextChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.Architecture = UpdateArchitecture.Text;
        }

        private void UpdateBugfix_CheckedChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.BugfixUpdate = UpdateBugfix.Checked;
        }

        private void UpdateSecurity_CheckedChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.SecurityUpdate = UpdateSecurity.Checked;
        }

        private void UpdateCaption_TextChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.Caption = UpdateCaption.Text;
        }

        private void UpdateChangelog_TextChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.Changelog = UpdateChangelog.Text;
        }

        private void UpdateDownloadUrls_TextChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.Urls = UpdateDownloadUrls.Text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            errorProvider.SetError(UpdateDownloadUrls, "");
            foreach(string s in u.Urls)
                try
                {
                    new Uri(s);
                }
                catch (Exception ex)
                {
                    errorProvider.SetError(UpdateDownloadUrls, ex.Message);
                    break;
                }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            UpdateList lst = new UpdateList();
            List<Update> updates = new List<Update>();
            foreach (Update u in listBox1.Items)
                updates.Add(u);
            lst.Updates = updates.ToArray();
            lst.SignedHash = "";

            System.Security.Cryptography.RSACryptoServiceProvider rsa = new System.Security.Cryptography.RSACryptoServiceProvider();
            rsa.FromXmlString(m_privateKey);

            System.Xml.Serialization.XmlSerializer sr = new System.Xml.Serialization.XmlSerializer(typeof(UpdateList));
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                sr.Serialize(ms, lst);
                ms.Position = 0;
                lst.SignedHash = Convert.ToBase64String(rsa.SignData(ms, System.Security.Cryptography.SHA1.Create()));
            }

            using (System.IO.FileStream fs = new System.IO.FileStream(UpdateFile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                sr.Serialize(fs, lst);
        }

        private void UpdateDate_ValueChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.ReleaseDate = UpdateDate.Value.ToUniversalTime();
        }

        private void UpdateApplication_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void UpdateApplication_TextChanged(object sender, EventArgs e)
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null || m_isUpdating)
                return;

            u.ApplicationName = UpdateApplication.Text;
        }

        private void RebuildVersionList()
        {
            Update u = listBox1.SelectedItem as Update;
            if (u == null)
                return;

            bool backset = m_isUpdating;
            try
            {
                m_isUpdating = true;
                UpdateLogAppend.Items.Clear();

                foreach (Update ux in listBox1.Items)
                    if (ux != u && ux.Architecture == u.Architecture && ux.ApplicationName == u.ApplicationName)
                        UpdateLogAppend.Items.Add(ux.Version);
            }
            finally
            {
                backset = m_isUpdating;
            }
        }

        private void createClientFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenClientFile.ShowDialog(this) == DialogResult.OK)
            {
                List<string> archs = new List<string>();
                List<string> apps = new List<string>();
                SortedList<string, string> vers = new SortedList<string, string>();

                foreach (string s in UpdateArchitecture.Items)
                    archs.Add(s);
                foreach (string s in UpdateApplication.Items)
                    apps.Add(s);

                Version v = new Version();
                foreach (Update u in listBox1.Items)
                {
                    if (u.Version > v)
                        v = u.Version;
                    vers[u.VersionString.ToLower()] = u.VersionString;                    
                }

                System.Security.Cryptography.RSA cp = System.Security.Cryptography.RSACryptoServiceProvider.Create();
                cp.FromXmlString(m_privateKey);
                string pubkey = cp.ToXmlString(false);

                ClientFileEditor cfe = new ClientFileEditor();
                cfe.Setup(OpenClientFile.FileName, apps, archs, new List<string>(vers.Values), v, pubkey);
            }
        }
    }
}