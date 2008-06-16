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
using System.Threading;
using Duplicati.Datamodel;

namespace Duplicati.HelperControls
{
    public partial class BackupItems : UserControl
    {
        private Thread m_workThread;
        private Schedule m_schedule;
        private string[] m_backups;
        private Exception m_exception;

        public event EventHandler ListLoaded;
        public event EventHandler LoadError;

        public BackupItems()
        {
            InitializeComponent();
        }

        public void Setup(Schedule schedule)
        {
            if (m_workThread != null)
                throw new Exception("Cannot re-use the loader dialog");

            m_exception = null;
            m_backups = null;
            m_schedule = schedule;
            m_workThread = new Thread(new ThreadStart(Runner));
            m_workThread.Start();
        }

        private void Loaded(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
                this.Invoke(new EventHandler(Loaded), sender, e);
            else
            {
                WaitPanel.Visible = false;

                if (m_exception != null)
                {
                    progressBar.Visible = false;
                    statusLabel.Text = "Error: " + m_exception.Message;
                    if (LoadError != null)
                        LoadError(this, null);
                    return;
                }

                try
                {
                    listView.Visible = true;
                    listView.BeginUpdate();
                    listView.Items.Clear();

                    foreach (string s in m_backups)
                        listView.Items.Add(s);
                }
                finally
                {
                    listView.EndUpdate();
                }

                if (ListLoaded != null)
                    ListLoaded(this, null);
            }
        }

        private void Runner()
        {
            try
            {
                DuplicityRunner r = new DuplicityRunner(Application.StartupPath, null);
                m_backups = r.ListBackups(m_schedule);
            }
            catch (Exception e)
            {
                m_exception = e;
            }

            Loaded(null, null);
        }

    }
}
