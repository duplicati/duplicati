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
    public partial class SelectBackend : WizardControl
    {
        private Task m_task;

        public SelectBackend()
            : base("Select a place to store the backups", "On this page you can select the type of device or service that store the backups. You may need information from the service provider when you continue.")
        {
            InitializeComponent();
            base.PageEnter += new PageChangeHandler(SelectBackend_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectBackend_PageLeave);
        }

        void SelectBackend_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!(File.Checked || FTP.Checked || SSH.Checked || WebDAV.Checked || S3.Checked))
            {
                MessageBox.Show(this, "You must enter the storage method before you can continue.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (WebDAV.Checked)
            {
                MessageBox.Show(this, "WebDAV is not implemented yet.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            SaveSettings();

            switch ((string)m_settings["Backend:Backend"])
            {
                case "file":
                    args.NextPage = new Backends.File.FileOptions();
                    break;
                case "ftp":
                    args.NextPage = new Backends.FTP.FTPOptions();
                    break;
                case "ssh":
                    args.NextPage = new Backends.SSH.SSHOptions();
                    break;
                case "webdav":
                    args.NextPage = new Backends.WebDAV.WebDAVOptions();
                    break;
                case "s3":
                    args.NextPage = new Backends.S3.S3Options();
                    break;
                default:
                    args.NextPage = null;
                    args.Cancel = true;
                    return;
            }
        }

        private void SaveSettings()
        {
            if (File.Checked)
                m_settings["Backend:Backend"] = "file";
            else if (FTP.Checked)
                m_settings["Backend:Backend"] = "ftp";
            else if (SSH.Checked)
                m_settings["Backend:Backend"] = "ssh";
            else if (WebDAV.Checked)
                m_settings["Backend:Backend"] = "webdav";
            else if (S3.Checked)
                m_settings["Backend:Backend"] = "s3";

        }

        void SelectBackend_PageEnter(object sender, PageChangedArgs args)
        {
            m_task = ((Schedule)m_settings["Schedule"]).Tasks[0];
            Item_CheckChanged(null, null);

        }

        private void Item_CheckChanged(object sender, EventArgs e)
        {
            m_owner.NextButton.Enabled = File.Checked || FTP.Checked || SSH.Checked || WebDAV.Checked || S3.Checked;
        }
    }
}
