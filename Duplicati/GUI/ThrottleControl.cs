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
