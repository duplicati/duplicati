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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Library.Backend
{
    public partial class TahoeUI : UserControl
    {
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PORT = "Port";

        private const string USE_SSL = "Use SSL";
        private const string ACCEPT_ANY_CERTIFICATE = "Accept Any Server Certificate";
        private const string ACCEPT_SPECIFIC_CERTIFICATE = "Accept Specific Server Certificate";

        private const string HAS_TESTED = "UI: Has tested";

        private bool m_hasTested;

        private static System.Text.RegularExpressions.Regex HashRegEx = new System.Text.RegularExpressions.Regex("[^0-9a-fA-F]");

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        private IDictionary<string, string> m_options;

        public TahoeUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
        }

        private TahoeUI()
        {
            InitializeComponent();
        }

        internal bool Save(bool validate)
        {
            Save();

            if (!validate)
                return true;

            if (!ValidateForm())
                return false;

            if (!m_hasTested)
                switch (MessageBox.Show(this, Interface.CommonStrings.ConfirmTestConnectionQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        TestConnection_Click(null, null);
                        if (!m_hasTested)
                            return false;
                        break;
                    case DialogResult.No:
                        break;
                    default: //Cancel
                        return false;
                }

            Save();
            return true;
        }

        void TahoeUI_PageLoad(object sender, EventArgs args)
        {
            if (m_options.ContainsKey(HOST))
                Servername.Text = m_options[HOST];
            if (m_options.ContainsKey(FOLDER))
                Path.Text = m_options[FOLDER];

            int port;
            if (!m_options.ContainsKey(PORT) || !int.TryParse(m_options[PORT], out port))
                port = 3456;

            bool useSSL;
            if (!m_options.ContainsKey(USE_SSL) || !bool.TryParse(m_options[USE_SSL], out useSSL))
                useSSL = false;

            bool acceptAnyCertificate;
            if (!m_options.ContainsKey(ACCEPT_ANY_CERTIFICATE) || !bool.TryParse(m_options[ACCEPT_ANY_CERTIFICATE], out acceptAnyCertificate))
                acceptAnyCertificate = false;

            UseSSL.Checked = useSSL;
            AcceptAnyHash.Checked = acceptAnyCertificate;
            
            if (m_options.ContainsKey(ACCEPT_SPECIFIC_CERTIFICATE))
                SpecifiedHash.Text = m_options[ACCEPT_SPECIFIC_CERTIFICATE];
            AcceptSpecifiedHash.Checked = !string.IsNullOrEmpty(SpecifiedHash.Text);

            Port.Value = port;

            if (!m_options.ContainsKey(HAS_TESTED) || !bool.TryParse(m_options[HAS_TESTED], out m_hasTested))
                m_hasTested = false;

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
        }

        private bool ValidateForm()
        {
            if (Servername.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Interface.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (!Library.Utility.Utility.IsValidHostname(Servername.Text))
            {
                MessageBox.Show(this, string.Format(Library.Interface.CommonStrings.InvalidServernameError, Servername.Text), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servername.Focus(); }
                catch { }

                return false;
            }

            if (Path.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.TahoeUI.EmptyFolderPathError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Path.Focus(); }
                catch { }

                return false;
            }

            if (!Path.Text.StartsWith("/uri/URI:DIR2:"))
            {
                MessageBox.Show(this, String.Format(Strings.TahoeUI.InvalidFolderPathError, "/uri/URI:DIR2:"), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Path.Focus(); }
                catch { }

                return false;
            }

            if (UseSSL.Checked && AcceptSpecifiedHash.Checked)
            {
                if (SpecifiedHash.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, Strings.TahoeUI.EmptyHashError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { SpecifiedHash.Focus(); }
                    catch { }
                    return false;
                }

                if (SpecifiedHash.Text.Length % 2 > 0 || HashRegEx.Match(SpecifiedHash.Text).Success)
                {
                    MessageBox.Show(this, Strings.TahoeUI.InvalidHashError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { SpecifiedHash.Focus(); }
                    catch { }
                    return false;
                }
            }

            return true;
        }

        private void Save()
        {
            m_options.Clear();
            m_options[HAS_TESTED] = m_hasTested.ToString();

            m_options[HOST] = Servername.Text;
            m_options[FOLDER] = Path.Text;
            m_options[PORT] = ((int)Port.Value).ToString();
            
            m_options[USE_SSL] = UseSSL.Checked.ToString();
            m_options[ACCEPT_ANY_CERTIFICATE] = AcceptAnyHash.Checked.ToString();
            if (AcceptSpecifiedHash.Checked)
                m_options[ACCEPT_SPECIFIC_CERTIFICATE] = SpecifiedHash.Text;
            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                bool retry = true;
                while (retry == true)
                {
                    retry = false;
                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;

                        Save();
                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string destination = GetConfiguration(m_options, options);

                        bool existingBackup = false;
                        using (Duplicati.Library.Modules.Builtin.HttpOptions httpconf = new Duplicati.Library.Modules.Builtin.HttpOptions())
                        {
                            httpconf.Configure(options);
                            using (TahoeBackend tahoe = new TahoeBackend(destination, options))
                                foreach (Interface.IFileEntry n in tahoe.List())
                                    if (n.Name.StartsWith("duplicati-"))
                                    {
                                        existingBackup = true;
                                        break;
                                    }
                        }

                        bool isUiAdd = string.IsNullOrEmpty(m_uiAction) || string.Equals(m_uiAction, "add", StringComparison.InvariantCultureIgnoreCase);
                        if (existingBackup && isUiAdd)
                        {
                            if (MessageBox.Show(this, string.Format(Interface.CommonStrings.ExistingBackupDetectedQuestion), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                                return;
                        }
                        else
                        {
                            MessageBox.Show(this, Interface.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        m_hasTested = true;
                    }
                    catch (Interface.FolderMissingException)
                    {
                        switch (MessageBox.Show(this, Strings.TahoeUI.CreateMissingFolderQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                        {
                            case DialogResult.Yes:
                                CreateFolderButton.PerformClick();
                                TestConnection.PerformClick();
                                return;
                            default:
                                return;
                        }
                    }
                    catch (Utility.SslCertificateValidator.InvalidCertificateException cex)
                    {
                        if (string.IsNullOrEmpty(cex.Certificate))
                            MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, cex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        else
                        {
                            if (MessageBox.Show(this, string.Format(Strings.TahoeUI.ApproveCertificateHashQuestion, cex.SslError), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                            {
                                retry = true;
                                AcceptSpecifiedHash.Checked = true;
                                SpecifiedHash.Text = cex.Certificate;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        this.Cursor = c;
                    }
                }
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
        }

        private void CreateFolderButton_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                bool retry = true;
                while (retry == true)
                {
                    retry = false;
                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;
                        Save();

                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string destination = GetConfiguration(m_options, options);

                        using (Duplicati.Library.Modules.Builtin.HttpOptions httpconf = new Duplicati.Library.Modules.Builtin.HttpOptions())
                        {
                            httpconf.Configure(options);
                            TahoeBackend tahoe = new TahoeBackend(destination, options);
                            tahoe.CreateFolder();
                        }

                        MessageBox.Show(this, Interface.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        m_hasTested = true;
                    }
                    catch (Utility.SslCertificateValidator.InvalidCertificateException cex)
                    {
                        if (string.IsNullOrEmpty(cex.Certificate))
                            MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, cex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        else
                        {
                            if (MessageBox.Show(this, string.Format(Strings.TahoeUI.ApproveCertificateHashQuestion, cex.SslError), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                            {
                                retry = true;
                                AcceptSpecifiedHash.Checked = true;
                                SpecifiedHash.Text = cex.Certificate;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        this.Cursor = c;
                    }
                }
            }
        }

        internal static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            int port;
            if (!guiOptions.ContainsKey(PORT) || !int.TryParse(guiOptions[PORT], out port))
                port = 3456;

            bool useSSL;
            if (!guiOptions.ContainsKey(USE_SSL) || !bool.TryParse(guiOptions[USE_SSL], out useSSL))
                useSSL = false;

            bool acceptAnyCertificate;
            if (!guiOptions.ContainsKey(ACCEPT_ANY_CERTIFICATE) || !bool.TryParse(guiOptions[ACCEPT_ANY_CERTIFICATE], out acceptAnyCertificate))
                acceptAnyCertificate = false;

            if (useSSL)
                commandlineOptions["use-ssl"] = "";
            if (acceptAnyCertificate)
                commandlineOptions["accept-any-ssl-certificate"] = "";
            if (guiOptions.ContainsKey(ACCEPT_SPECIFIC_CERTIFICATE))
                commandlineOptions["accept-specified-ssl-hash"] = guiOptions[ACCEPT_SPECIFIC_CERTIFICATE];

            if (!guiOptions.ContainsKey(HOST))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, HOST));

            if (!guiOptions.ContainsKey(FOLDER))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, FOLDER));

            return "tahoe://" + guiOptions[HOST] + ":" + port.ToString() + (guiOptions[FOLDER].StartsWith("/") ? "" : "/") + guiOptions[FOLDER];
        }

        public static string PageTitle
        {
            get { return Strings.TahoeUI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.TahoeUI.PageDescription; }
        }

        private void UseSSL_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            SSLGroup.Enabled = UseSSL.Checked;
            if (Port.Value == 3456 || Port.Value == 443)
                Port.Value = UseSSL.Checked ? 443 : 3456;
        }

        private void AcceptSpecifiedHash_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            SpecifiedHash.Enabled = AcceptSpecifiedHash.Checked;
        }

        private void SpecifiedHash_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void AcceptAnyHash_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

    }
}
