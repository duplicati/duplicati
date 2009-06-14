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
using System.Windows.Forms.Wizard;

namespace Duplicati.GUI.Wizard_pages.Backends.WebDAV
{
    public partial class WebDAVOptions : WizardControl
    {
        private bool m_warnedPassword = false;
        private bool m_warnedUsername = false;
        private bool m_hasTested;
        private bool m_warnedPath;

        private WEBDAVSettings m_wrapper;

        public WebDAVOptions()
            : base("Backup storage options", "On this page you can select where to store the backup data.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(WebDAVOptions_PageEnter);
            base.PageLeave += new PageChangeHandler(WebDAVOptions_PageLeave);
        }

        void WebDAVOptions_PageLeave(object sender, PageChangedArgs args)
        {
            SaveSettings();

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!ValidateForm())
            {
                args.Cancel = true;
                return;
            }

            if (!m_hasTested)
                switch (MessageBox.Show(this, "Do you want to test the connection?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        TestConnection_Click(null, null);
                        if (!m_hasTested)
                        {
                            args.Cancel = true;
                            return;
                        }
                        break;
                    case DialogResult.No:
                        break;
                    default: //Cancel
                        args.Cancel = true;
                        return;
                }

            SaveSettings();

            m_wrapper.Server = Servername.Text;
            m_wrapper.Path = Path.Text;
            if (UseIntegratedAuth.Checked)
            {
                m_wrapper.IntegratedAuthentication = true;
                m_wrapper.Username = "";
                m_wrapper.Password = "";
            }
            else
            {
                m_wrapper.IntegratedAuthentication = false;
                m_wrapper.Username = Username.Text;
                m_wrapper.Password = Password.Text;
            }
            m_wrapper.Port = (int)Port.Value;
            m_wrapper.ForceDigestAuthentication = DigestAuth.Checked;

            if (new WizardSettingsWrapper(m_settings).PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                args.NextPage = new Add_backup.GeneratedFilenameOptions();
            else
                args.NextPage = new Add_backup.AdvancedOptions();
            
        }

        void WebDAVOptions_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings).WEBDAVSettings;

            if (m_settings.ContainsKey("WEBDAV:HasTested"))
                m_hasTested = (bool)m_settings["WEBDAV:HasTested"];

            if (m_settings.ContainsKey("WEBDAV:WarnedPath"))
                m_warnedPath = (bool)m_settings["WEBDAV:WarnedPath"];

            if (m_settings.ContainsKey("WEBDAV:WarnedUsername"))
                m_warnedUsername = (bool)m_settings["WEBDAV:WarnedUsername"];

            if (m_settings.ContainsKey("WEBDAV:WarnedPassword"))
                m_warnedPassword = (bool)m_settings["WEBDAV:WarnedPassword"];

            if (!m_valuesAutoLoaded)
            {
                Servername.Text = m_wrapper.Server;
                Path.Text = m_wrapper.Path;
                Username.Text = m_wrapper.Username;
                Password.Text = m_wrapper.Password;
                Port.Value = m_wrapper.Port;
                UseIntegratedAuth.Checked = m_wrapper.IntegratedAuthentication;
                DigestAuth.Checked = m_wrapper.ForceDigestAuthentication;
            }
        }

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter the name of the server", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (!UseIntegratedAuth.Checked)
            {
                if (Username.Text.Trim().Length <= 0 && !m_warnedUsername)
                {
                    if (MessageBox.Show(this, "You have not entered a username.\nThis is fine if the server allows anonymous uploads, but likely a username is required\nProceed without a password?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                    {
                        try { Username.Focus(); }
                        catch { }

                        return false;
                    }

                    m_warnedUsername = true;
                }

                if (Password.Text.Trim().Length <= 0 && !m_warnedPassword)
                {
                    if (MessageBox.Show(this, "You have not entered a password.\nProceed without a password?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                    {
                        try { Password.Focus(); }
                        catch { }

                        return false;
                    }
                    m_warnedPassword = true;
                }
            }

            return true;
        }

        private void SaveSettings()
        {
            m_settings["WEBDAV:HasWarned"] = m_hasTested;
            m_settings["WEBDAV:WarnedPath"] = m_warnedPath;
            m_settings["WEBDAV:WarnedUsername"] = m_warnedUsername;
            m_settings["WEBDAV:WarnedPassword"] = m_warnedPassword;
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    System.Data.LightDatamodel.IDataFetcherCached con = new System.Data.LightDatamodel.DataFetcherNested(Program.DataConnection);
                    Datamodel.Backends.WEBDAV webdav = new Duplicati.Datamodel.Backends.WEBDAV(con.Add<Datamodel.Task>());

                    webdav.Host = Servername.Text;
                    webdav.Username = Username.Text;
                    webdav.Password = Password.Text;
                    webdav.Port = (int)Port.Value;
                    webdav.Folder = Path.Text;
                    webdav.IntegratedAuthentication = UseIntegratedAuth.Checked;
                    webdav.ForceDigestAuthentication = DigestAuth.Checked;

                    string hostname = webdav.GetDestinationPath();
                    Dictionary<string, string> options = new Dictionary<string, string>();
                    webdav.GetOptions(options);
                    string[] files = Duplicati.Library.Main.Interface.List(hostname, options);

                    MessageBox.Show(this, "Connection succeeded!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                this.Cursor = c;
            }
        }

        private void Port_ValueChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Servername_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPath = false;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedUsername = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPassword = false;
        }

        private void UseIntegratedAuth_CheckedChanged(object sender, EventArgs e)
        {
            PasswordSettings.Enabled = !UseIntegratedAuth.Checked;
            m_hasTested = false;
        }

        private void CreateFolderButton_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                try
                {
                    string p = Path.Text;
                    string url = "http://" + Servername.Text + ":" + Port.Value.ToString() + "/" + Path.Text;
                    System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url);
                    if (UseIntegratedAuth.Checked)
                        req.UseDefaultCredentials = true;
                    else if (DigestAuth.Checked)
                    {
                        System.Net.CredentialCache cred = new System.Net.CredentialCache();
                        cred.Add(new Uri(url), "Digest", new System.Net.NetworkCredential(Username.Text, Password.Text));
                        req.Credentials = cred;
                    }
                    else
                        req.Credentials = new System.Net.NetworkCredential(Username.Text, Password.Text);

                    req.Method = System.Net.WebRequestMethods.Http.MkCol;
                    req.KeepAlive = false;
                    using (req.GetResponse())
                    { }

                    MessageBox.Show(this, "Folder created!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DigestAuth_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }
    }
}
