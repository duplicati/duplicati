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

namespace Duplicati.Library.Backend
{
    public partial class FTPUI : UserControl
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";
        private const string PASSIVE = "Passive";

        private const string HASTESTED = "UI: HasTested";
        private const string HASWARNEDPATH = "UI: HasWarnedPath";
        private const string HASWARNEDUSERNAME = "UI: HasWarnedUsername";
        private const string HASWARNEDPASSWORD = "UI: HasWarnedPassword";

        private bool m_warnedPassword = false; 
        private bool m_warnedUsername = false;
        private bool m_hasTested;
        private bool m_warnedPath;

        private IDictionary<string, string> m_options;

        private FTPUI()
        {
            InitializeComponent();
        }

        public FTPUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
        }

        internal bool Save(bool validate)
        {
            SaveSettings();

            if (!validate)
                return true;

            if (!ValidateForm())
                return false;

            if (!m_hasTested)
                switch (MessageBox.Show(this, Backend.CommonStrings.ConfirmTestConnectionQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                { 
                    case DialogResult.Yes:
                        TestConnection_Click(null, null);
                        if (!m_hasTested)
                            return false ;
                        break;
                    case DialogResult.No:
                        break;
                    default: //Cancel
                        return false;
                }

            SaveSettings();

            return true;
        }

        void FTPUI_Load(object sender, EventArgs args)
        {
            LoadSettings();
        }

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Backend.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (Username.Text.Trim().Length <= 0 && !m_warnedUsername)
            {
                if (MessageBox.Show(this, Backend.CommonStrings.EmptyUsernameWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
                {
                    try { Username.Focus(); }
                    catch { }

                    return false;
                }

                m_warnedUsername = true;
            }

            if (Password.Text.Trim().Length <= 0 && !m_warnedPassword)
            {
                if (MessageBox.Show(this, Backend.CommonStrings.EmptyPasswordWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information) != DialogResult.Yes)
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
            m_options.Clear();
            m_options[HASTESTED] = m_hasTested.ToString();
            m_options[HASWARNEDPATH] = m_warnedPath.ToString();
            m_options[HASWARNEDUSERNAME] = m_warnedUsername.ToString();
            m_options[HASWARNEDPASSWORD] = m_warnedPassword.ToString();
            m_options[HOST] = Servername.Text;
            m_options[FOLDER] = Path.Text;
            m_options[USERNAME] = Username.Text;
            m_options[PASSWORD] = Password.Text;
            m_options[PORT] = ((int)Port.Value).ToString();
            m_options[PASSIVE] = PassiveConnection.Checked.ToString();
        }

        private void LoadSettings()
        {
            if (m_options.ContainsKey(HOST))
                Servername.Text = m_options[HOST];
            if (m_options.ContainsKey(FOLDER))
                Path.Text = m_options[FOLDER];
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            if (m_options.ContainsKey(PASSWORD))
                Password.Text = m_options[PASSWORD];

            bool b;
            if (!m_options.ContainsKey(PASSIVE) || !bool.TryParse(m_options[PASSIVE], out b))
                b = false;

            PassiveConnection.Checked = b;

            int i; ;
            if (!m_options.ContainsKey(PORT) || !int.TryParse(m_options[PORT], out i))
                i = 21;

            Port.Value = i;

            //Set internal testing flags
            if (!m_options.ContainsKey(HASTESTED) || !bool.TryParse(m_options[HASTESTED], out m_hasTested))
                m_hasTested = false;
            if (!m_options.ContainsKey(HASWARNEDPATH) || !bool.TryParse(m_options[HASWARNEDPATH], out m_warnedPath))
                m_warnedPath = false;
            if (!m_options.ContainsKey(HASWARNEDUSERNAME) || !bool.TryParse(m_options[HASWARNEDUSERNAME], out m_warnedUsername))
                m_warnedUsername = false;
            if (!m_options.ContainsKey(HASWARNEDPASSWORD) || !bool.TryParse(m_options[HASWARNEDPASSWORD], out m_warnedPassword))
                m_warnedPassword = false;
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    SaveSettings();

                    Dictionary<string, string> options = new Dictionary<string, string>();
                    string hostname = GetConfiguration(m_options, options);
                    FTP f = new FTP(hostname, options);
                    f.List();

                    MessageBox.Show(this, Backend.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                this.Cursor = c;
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
                    string url = "ftp://" + Servername.Text + ":" + Port.Value.ToString() + "/" + Path.Text;
                    System.Net.FtpWebRequest req = (System.Net.FtpWebRequest)System.Net.FtpWebRequest.Create(url);
                    req.Credentials = new System.Net.NetworkCredential(Username.Text, Password.Text);
                    req.Method = System.Net.WebRequestMethods.Ftp.MakeDirectory;
                    req.KeepAlive = false;
                    using (req.GetResponse())
                    { }

                    MessageBox.Show(this, Backend.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                }
                catch(Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["ftp-username"] = guiOptions[USERNAME];
            if (guiOptions.ContainsKey(PASSWORD) && !string.IsNullOrEmpty(guiOptions[PASSWORD]))
                commandlineOptions["ftp-password"] = guiOptions[PASSWORD];

            bool passive;
            if (!guiOptions.ContainsKey(PASSIVE) || !bool.TryParse(guiOptions[PASSIVE], out passive))
                passive = false;

            if (passive)
                commandlineOptions["ftp-passive"] = "";
            else
                commandlineOptions["ftp-regular"] = "";

            int port;
            if (!guiOptions.ContainsKey(PORT) || !int.TryParse(guiOptions[PORT], out port) || port < 0)
                port = 21;

            if (!guiOptions.ContainsKey(HOST))
                throw new Exception(string.Format(Backend.CommonStrings.ConfigurationIsMissingItemError, FOLDER));

            return "ftp://" + guiOptions[HOST] + ":" + port.ToString() + "/" + (guiOptions.ContainsKey(FOLDER) ? guiOptions[FOLDER] : "");
        }

    }
}
