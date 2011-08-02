using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.RunBackup
{
    /// <summary>
    /// Put that pesky little icon in the system tray
    /// </summary>
    public partial class Notifier : IDisposable
    {
        private System.Windows.Forms.NotifyIcon itsNotifyIcon;
        public Notifier()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Notifier));
            itsNotifyIcon = new System.Windows.Forms.NotifyIcon()
            {
                Icon = ((System.Drawing.Icon)(resources.GetObject("NotifyIcon.Icon"))),
                Text = "Backup",
                Visible = true,
            };
            itsNotifyIcon.Click += new EventHandler(itsNotifyIcon_Click);
        }

        void itsNotifyIcon_Click(object sender, EventArgs e)
        {
        }

        public void Dispose()
        {
            itsNotifyIcon.Visible = false; // Dern thing will not die.
            itsNotifyIcon.Dispose();
        }
    }
}
