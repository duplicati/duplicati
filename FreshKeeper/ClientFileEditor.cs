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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace FreshKeeper
{
    public partial class ClientFileEditor : Form
    {
        private string m_filename;
        private string m_pubkey;

        public ClientFileEditor()
        {
            InitializeComponent();
        }

        private void ClientFileEditor_Load(object sender, EventArgs e)
        {

        }

        public void Setup(string filename, List<string> apps, List<string> archs, List<string> vers, Version version, string pubkey)
        {
            m_filename = filename;
            m_pubkey = pubkey;

            UpdateApplication.Items.AddRange(apps.ToArray());
            UpdateArchitecture.Items.AddRange(archs.ToArray());
            UpdateVersion.Items.AddRange(vers.ToArray());
            UpdateVersion.SelectedIndex = UpdateVersion.FindString(version.ToString());
        }
    }
}