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
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages
{
    public partial class SelectBackend : UserControl, IWizardControl, Interfaces.ITaskBased
    {
        public enum Provider
        {
            Unknown,
            File,
            FTP,
            SSH,
            WebDAV,
            S3
        }

        private Task m_task;

        public SelectBackend()
        {
            InitializeComponent();
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return "Select a place to store the backups"; }
        }

        string IWizardControl.HelpText
        {
            get { return "On this page you can select the type of device or service that store the backups. You may need information from the service provider when you continue."; }
        }

        Image IWizardControl.Image
        {
            get { return null; }
        }

        bool IWizardControl.FullSize
        {
            get { return false; }
        }

        void IWizardControl.Enter(IWizardForm owner)
        {
            string backend = m_task.Backend == null ? "" : m_task.Backend.SystemName;
            switch (backend)
            {
                case "file":
                    File.Checked = true;
                    break;
                case "ftp":
                    FTP.Checked = true;
                    break;
                case "ssh":
                    SSH.Checked = true;
                    break;
                case "webdav":
                    WebDAV.Checked = true;
                    break;
                case "s3":
                    S3.Checked = true;
                    break;
                default:
                    File.Checked = FTP.Checked = SSH.Checked = WebDAV.Checked = S3.Checked = false;
                    break;
            }
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            if (!(File.Checked || FTP.Checked || SSH.Checked || WebDAV.Checked || S3.Checked))
            {
                MessageBox.Show(this, "You must enter the storage method before you can continue.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cancel = true;
                return;
            }

            if (WebDAV.Checked)
            {
                MessageBox.Show(this, "WebDAV is not implemented yet.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cancel = true;
                return;
            }
        }

        #endregion


        public Provider SelectedProvider
        {
            get
            {
                if (File.Checked)
                    return Provider.File;
                else if (FTP.Checked)
                    return Provider.FTP;
                else if (SSH.Checked)
                    return Provider.SSH;
                else if (WebDAV.Checked)
                    return Provider.WebDAV;
                else if (S3.Checked)
                    return Provider.S3;
                else
                    return Provider.Unknown;
            }
        }

        #region ITaskBased Members

        public void Setup(Duplicati.Datamodel.Task task)
        {
            m_task = task;
        }

        #endregion
    }
}
