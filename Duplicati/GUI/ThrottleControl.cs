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

namespace Duplicati.GUI
{
    public partial class ThrottleControl : Form
    {
        public ThrottleControl()
        {
            InitializeComponent();

#if DEBUG
            this.Text += " (DEBUG)";
#endif
        }

        private void ThrottleControl_Load(object sender, EventArgs e)
        {
            ThreadPriorityPicker.SelectedPriority = Program.LiveControl.ThreadPriority;
            BandwidthLimit.DownloadLimitInBytes = Program.LiveControl.DownloadLimit == null ? 0 : Program.LiveControl.DownloadLimit.Value;
            BandwidthLimit.UploadLimitInBytes = Program.LiveControl.UploadLimit == null ? 0 : Program.LiveControl.UploadLimit.Value;
        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            Program.LiveControl.ThreadPriority = ThreadPriorityPicker.SelectedPriority;
            Program.LiveControl.DownloadLimit = BandwidthLimit.DownloadLimitEnabled ? (long?)BandwidthLimit.DownloadLimitInBytes : null;
            Program.LiveControl.UploadLimit = BandwidthLimit.UploadLimitEnabled ? (long?)BandwidthLimit.UploadLimitInBytes : null;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
