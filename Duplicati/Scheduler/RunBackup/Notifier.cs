#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
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

namespace Duplicati.Scheduler.RunBackup
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
