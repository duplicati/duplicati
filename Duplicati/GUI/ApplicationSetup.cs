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
using System.Data.LightDatamodel;
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    public partial class ApplicationSetup : Form
    {
        private IDataFetcherCached m_connection;
        private ApplicationSettings m_settings;
        private bool m_isUpdating = false;

        public ApplicationSetup()
        {
            InitializeComponent();
            m_connection = new DataFetcherNested(Program.DataConnection);
            m_settings = new ApplicationSettings(m_connection);

            RecentDuration.SetIntervals(new List<KeyValuePair<string, string>>(
                new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>("One week", "1W"),
                    new KeyValuePair<string, string>("Two weeks", "2W"),
                    new KeyValuePair<string, string>("One month", "1M"),
                    new KeyValuePair<string, string>("Three months", "3M"),
                }));

            try
            {
                m_isUpdating = true;
                RecentDuration.Value = m_settings.RecentBackupDuration;
                GPGPath.Text = m_settings.GPGPath;
                SFTPPath.Text = m_settings.SFtpPath;
                SCPPath.Text = m_settings.ScpPath;
                TempPath.Text = m_settings.TempPath;
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private bool TestForFiles(string folder, params string[] files)
        {
            try
            {
                foreach(string file in files)
                    if (!System.IO.File.Exists(System.IO.Path.Combine(folder, file)))
                        if (MessageBox.Show(this, "The folder selected does not contain the file: " + file + ".\r\nDo you want to use that folder anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                            return false;
            }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, "An exception occured while examining the folder: "+ ex.Message + ".\r\nDo you want to use that folder anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                    return false;
            }

            return true;
        }

        private void BrowseGPG_Click(object sender, EventArgs e)
        {
            if (BrowseGPGDialog.ShowDialog(this) == DialogResult.OK)
                GPGPath.Text = BrowseGPGDialog.FileName;
        }

        private void RecentDuration_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.RecentBackupDuration = RecentDuration.Value;
        }

        private void GPGPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.GPGPath = GPGPath.Text;
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            m_connection.CommitRecursive(m_connection.GetObjects<ApplicationSetting>());

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BrowseSFTP_Click(object sender, EventArgs e)
        {
            if (BrowseSFTPDialog.ShowDialog(this) == DialogResult.OK)
                SFTPPath.Text = BrowseSFTPDialog.FileName;
        }

        private void SFTPPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.SFtpPath = SFTPPath.Text;
        }

        private void SCPPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.ScpPath = SCPPath.Text;

        }

        private void BrowseSCP_Click(object sender, EventArgs e)
        {
            if (BrowseSCPDialog.ShowDialog(this) == DialogResult.OK)
                SCPPath.Text = BrowseSCPDialog.FileName;
        }

        private void RecentDuration_ValueChanged(object sender, EventArgs e)
        {
            RecentDuration_TextChanged(sender, e);
        }

        private void TempPathBrowse_Click(object sender, EventArgs e)
        {
            if (BrowseTempPath.ShowDialog(this) == DialogResult.OK)
                TempPath.Text = BrowseTempPath.SelectedPath;
        }

        private void TempPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.TempPath = TempPath.Text;
        }

        private void ApplicationSetup_Load(object sender, EventArgs e)
        {

        }

    }
}