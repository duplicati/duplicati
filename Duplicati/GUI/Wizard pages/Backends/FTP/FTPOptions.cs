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
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Backends.FTP
{
    public partial class FTPOptions : WizardControl
    {
        private bool m_warnedPassword = false; 
        private bool m_warnedUsername = false;
        private bool m_hasTested;
        private bool m_warnedPath;

        FTPSettings m_wrapper;

        public FTPOptions()
            : base("Backup storage options", "On this page you can select where to store the backup data.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(FTPOptions_PageEnter);
            base.PageLeave += new PageChangeHandler(FTPOptions_PageLeave);
        }

        void FTPOptions_PageLeave(object sender, PageChangedArgs args)
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
            m_wrapper.Username = Username.Text;
            m_wrapper.Password = Password.Text;
            m_wrapper.Port = (int)Port.Value;
            m_wrapper.Passive = PassiveConnection.Checked;

            if (new WizardSettingsWrapper(m_settings).PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                args.NextPage = new RestoreSetup.FinishedRestoreSetup();
            else
                args.NextPage = new Add_backup.AdvancedOptions();
        }

        void FTPOptions_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings).FTPSettings;

            if (m_settings.ContainsKey("FTP:HasTested"))
                m_hasTested = (bool)m_settings["FTP:HasTested"];

            if (m_settings.ContainsKey("FTP:WarnedPath"))
                m_warnedPath = (bool)m_settings["FTP:WarnedPath"];

            if (m_settings.ContainsKey("FTP:WarnedUsername"))
                m_warnedUsername = (bool)m_settings["FTP:WarnedUsername"];

            if (m_settings.ContainsKey("FTP:WarnedPassword"))
                m_warnedPassword = (bool)m_settings["FTP:WarnedPassword"];

            if (!m_valuesAutoLoaded)
            {
                Servername.Text = m_wrapper.Server;
                Path.Text = m_wrapper.Path;
                Username.Text = m_wrapper.Username;
                Password.Text = m_wrapper.Password;
                Port.Value = m_wrapper.Port;
                PassiveConnection.Checked = m_wrapper.Passive;
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

            if (Username.Text.Trim().Length <= 0 && !m_warnedUsername)
            {
                if (MessageBox.Show(this, "You have not entered a username.\nThis is fine if the server allows anonymous uploads, but likely a username is required\nProceed without a password?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                {
                    try { Username.Focus(); }
                    catch { }

                    return false;
                }
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

            return true;
        }

        private void SaveSettings()
        {
            m_settings["FTP:HasWarned"] = m_hasTested;
            m_settings["FTP:WarnedPath"] = m_warnedPath;
            m_settings["FTP:WarnedUsername"] = m_warnedUsername;
            m_settings["FTP:WarnedPassword"] = m_warnedPassword;
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                try
                {
                    System.Data.LightDatamodel.IDataFetcherCached con = new System.Data.LightDatamodel.DataFetcherNested(Program.DataConnection);
                    Datamodel.Backends.FTP ftp = new Duplicati.Datamodel.Backends.FTP(con.Add<Task>());

                    ftp.Host = Servername.Text;
                    ftp.Username = Username.Text;
                    ftp.Password = Password.Text;
                    ftp.Port = (int)Port.Value;
                    ftp.Folder = Path.Text;
                    ftp.Passive = PassiveConnection.Checked;

                    string hostname = ftp.GetDestinationPath();
                    Dictionary<string, string> options = new Dictionary<string, string>();
                    ftp.GetOptions(options);
                    string[] files = Duplicati.Library.Main.Interface.List(hostname, options);

                    MessageBox.Show(this, "Connection succeeded!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Port_ValueChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPassword = false;
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedUsername = false;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_warnedPath = false;
        }

        private void Servername_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void PassiveConnection_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void CreateFolderButton_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                try
                {
                    string p = Path.Text;
                    string url = "ftp://" + Servername.Text + ":" + Port.Value.ToString() + "/" + Path.Text;
                    System.Net.FtpWebRequest req = (System.Net.FtpWebRequest)System.Net.FtpWebRequest.Create(url);
                    req.Credentials = new System.Net.NetworkCredential(Username.Text, Password.Text);
                    req.Method = System.Net.WebRequestMethods.Ftp.MakeDirectory;
                    req.KeepAlive = false;
                    using (req.GetResponse())
                    { }

                    MessageBox.Show(this, "Folder created!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                catch(Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
