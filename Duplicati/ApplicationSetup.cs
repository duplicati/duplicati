using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Data.LightDatamodel;
using Duplicati.Datamodel;

namespace Duplicati
{
    public partial class ApplicationSetup : Form
    {
        private IDataFetcher m_connection;
        private ApplicationSettings m_settings;
        private bool m_isUpdating = false;

        public ApplicationSetup()
        {
            InitializeComponent();
            m_connection = new DataFetcherNested(Program.DataConnection);
            m_settings = new ApplicationSettings(m_connection);

            try
            {
                m_isUpdating = true;
                RecentDuration.Text = m_settings.RecentBackupDuration;
                PGPPath.Text = m_settings.PGPPath;
                PythonPath.Text = m_settings.PythonPath;
                DuplicityPath.Text = m_settings.DuplicityPath;
                NcFTPPath.Text = m_settings.NcFTPPath;
            }
            finally
            {
                m_isUpdating = false;
            }
        }


        private void BrowsePGP_Click(object sender, EventArgs e)
        {
            if (PGPBrowser.ShowDialog(this) == DialogResult.OK)
                PGPPath.Text = PGPBrowser.SelectedPath;
        }

        private void BrowsePython_Click(object sender, EventArgs e)
        {
            if (PythonFileDialog.ShowDialog(this) == DialogResult.OK)
                PythonPath.Text = PythonFileDialog.FileName;
        }

        private void BrowseDuplicity_Click(object sender, EventArgs e)
        {
            if (DuplicityFileDialog.ShowDialog(this) == DialogResult.OK)
                DuplicityPath.Text = DuplicityFileDialog.FileName;
        }

        private void RecentDuration_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.RecentBackupDuration = RecentDuration.Text;
        }

        private void PGPPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.PGPPath = PGPPath.Text;
        }

        private void PythonPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.PythonPath = PythonPath.Text;
        }

        private void DuplicityPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.DuplicityPath = DuplicityPath.Text;
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            //TODO: Fix once CommitRecursive is done
            m_connection.CommitAll();
            Program.DataConnection.CommitAll();

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BrowseNcFTP_Click(object sender, EventArgs e)
        {
            if (NcFTPBrowser.ShowDialog(this) == DialogResult.OK)
                NcFTPPath.Text = NcFTPBrowser.SelectedPath;
       }

        private void NcFTPPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.NcFTPPath  = DuplicityPath.Text;
        }
    }
}