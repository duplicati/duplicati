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
using Duplicati.Datamodel.Backends;

namespace Duplicati.Service_controls
{
    public partial class FileSettings : UserControl
    {
        private File m_file;
        private bool m_isUpdating = false;

        public FileSettings()
        {
            InitializeComponent();
        }

        public void Setup(File file)
        {
            try
            {
                m_isUpdating = true;
                m_file = file;

                DestinationFolder.Text = m_file.DestinationFolder;
                TimeSeperator.Text = m_file.TimeSeparator;
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void DestinationFolder_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_file == null)
                return;

            m_file.DestinationFolder = DestinationFolder.Text;
        }

        private void TimeSeperator_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_file == null)
                return;

            m_file.TimeSeparator = TimeSeperator.Text;
        }

        private void BrowseTargetFolder_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "Please select the folder where the backup will be stored";
            if (dlg.ShowDialog() == DialogResult.OK)
                DestinationFolder.Text = dlg.SelectedPath;

        }

    }
}
