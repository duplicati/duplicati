#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
using Duplicati.Datamodel;

namespace Duplicati.GUI.Service_controls
{
    public partial class TaskSettingsBase : UserControl
    {
        private Schedule m_schedule;
        private Task m_task;
        private bool m_isUpdating = false;

        private enum ServiceTypes
        {
            File,
            SSH,
            FTP,
            S3,
            WebDAV,
            Custom
        }

        public TaskSettingsBase()
        {
            InitializeComponent();
        }

        public void Setup(Schedule schedule)
        {
            try
            {
                m_isUpdating = true;
                m_schedule = schedule;
                if (m_schedule.Tasks.Count == 0)
                {
                    m_task = m_schedule.DataParent.Add<Task>();
                    m_schedule.Tasks.Add(m_task);
                }
                else if (m_schedule.Tasks.Count == 1)
                    m_task = m_schedule.Tasks[0];
                else
                {
                    MessageBox.Show(this, "This schedule is using a feature that is not supported by the editor. If you save this setup, the schedule may be damaged", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    m_task = m_schedule.Tasks[0];
                }

                if (!m_task.RelationManager.ExistsInDb(m_task))
                {
                    if (string.IsNullOrEmpty(m_task.Encryptionkey))
                        m_task.Encryptionkey = KeyGenerator.GenerateKey(64, 128);
                    if (string.IsNullOrEmpty(m_task.Signaturekey))
                        m_task.Signaturekey = KeyGenerator.GenerateSignKey();
                }

                EncrytionKey.Text = m_task.Encryptionkey;
                SignatureKey.Text = m_task.Signaturekey;
                SourceFolder.Text = m_task.SourcePath;

                switch (m_task.Service.Trim().ToLower())
                {
                    case "file":
                        ServiceTypeCombo.SelectedIndex = (int)ServiceTypes.File;
                        break;
                    case "ssh":
                        ServiceTypeCombo.SelectedIndex = (int)ServiceTypes.SSH;
                        break;
                    case "s3":
                        ServiceTypeCombo.SelectedIndex = (int)ServiceTypes.S3;
                        break;
                    default:
                        ServiceTypeCombo.SelectedIndex = -1;
                        break;
                }

                                
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void GenerateEncryptionKey_Click(object sender, EventArgs e)
        {
            if (m_task.RelationManager.ExistsInDb(m_task) && !string.IsNullOrEmpty(m_task.Encryptionkey))
                if (MessageBox.Show(this, "If you modify the encryption key, you can no longer recover any existing backups. Please make sure you have a copy of the key. Do you want to continue?", Application.ProductName, MessageBoxButtons.YesNoCancel) != DialogResult.Yes)
                    return;

            EncrytionKey.Text = KeyGenerator.GenerateKey(64, 128);
        }

        private void GenerateSignatureKey_Click(object sender, EventArgs e)
        {
            if (m_task.RelationManager.ExistsInDb(m_task) && !string.IsNullOrEmpty(m_task.Encryptionkey))
                if (MessageBox.Show(this, "If you modify the signature key, you can no longer verify any existing backups. Please make sure you have a copy of the key. Do you want to continue?", Application.ProductName, MessageBoxButtons.YesNoCancel) != DialogResult.Yes)
                    return;

            SignatureKey.Text = KeyGenerator.GenerateSignKey();
        }

        private void EncrytionKey_TextChanged(object sender, EventArgs e)
        {
            EncryptionCheckbox.Checked = EncrytionKey.Text.Trim().Length > 0;
            if (m_isUpdating || m_task == null)
                return;

            m_task.Encryptionkey = EncrytionKey.Text;
        }

        private void SignatureKey_TextChanged(object sender, EventArgs e)
        {
            SignatureCheckbox.Checked = SignatureKey.Text.Trim().Length > 0;
            if (m_isUpdating || m_task == null)
                return;

            m_task.Signaturekey = SignatureKey.Text;
        }

        private void BrowseSourceFolder_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "Please select the folder to backup";
            if (dlg.ShowDialog() == DialogResult.OK)
                SourceFolder.Text = dlg.SelectedPath;
        }

        private void SourceFolder_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_task == null)
                return;

            m_task.SourcePath = SourceFolder.Text;
        }

        private void EditFilterButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, "This feature is not yet implemented", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ServiceTypeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!m_isUpdating && m_task != null && m_task.RelationManager.ExistsInDb(m_task))
            {
                if (MessageBox.Show(this, "Selecting a different backend will remove the settings from the current backend. Are you sure you want to continue?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    try
                    {
                        m_isUpdating = true;
                        ServiceTypeCombo.SelectedIndex = ServiceTypeCombo.FindString(m_task.Service);
                        return;
                    }
                    finally
                    {
                        m_isUpdating = false;
                    }
                }
            }

            Control c = null;

            if (m_task != null)
                switch ((ServiceTypes)ServiceTypeCombo.SelectedIndex)
                {
                    case ServiceTypes.File:
                        c = fileSettings;
                        fileSettings.Setup(new Duplicati.Datamodel.Backends.File(m_task));
                        if (!m_isUpdating)
                            m_task.Service = "file";
                        break;

                    case ServiceTypes.FTP:
                        break;

                    case ServiceTypes.SSH:
                        c = sshSettings;
                        sshSettings.Setup(new Duplicati.Datamodel.Backends.SSH(m_task));
                        if (!m_isUpdating)
                            m_task.Service = "ssh";
                        break;

                    case ServiceTypes.S3:
                        c = s3Settings;
                        s3Settings.Setup(new Duplicati.Datamodel.Backends.S3(m_task));
                        if (!m_isUpdating)
                            m_task.Service = "s3";
                        break;

                    case ServiceTypes.WebDAV:
                        break;

                    case ServiceTypes.Custom:
                        break;
                }

            if (c != null)
                c.Dock = DockStyle.Fill;

            foreach (Control s in ServicePanel.Controls)
                s.Visible = c == s;
        }
    }
}
