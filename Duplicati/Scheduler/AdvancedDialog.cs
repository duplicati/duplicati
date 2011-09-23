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
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// A dialog for lesser / weird settings
    /// </summary>
    public partial class AdvancedDialog : Form
    {
        private bool itsAutoDelete = true;
        /// <summary>
        /// delete unwanted files after each backup
        /// </summary>
        public bool AutoDelete { get { return itsAutoDelete; } set { itsAutoDelete = this.OrphanCheckBox.Checked = value; } }
        private bool itsMap = true;
        /// <summary>
        /// Map networked drives before backup
        /// </summary>
        public bool Map { get { return itsMap; } set { itsMap = this.MapCheckBox.Checked = value; } }
        /// <summary>
        /// Creator
        /// </summary>
        public AdvancedDialog()
        {
            InitializeComponent();
        }

        private void OKButton_Click(object sender, EventArgs e)
        {
            itsAutoDelete = this.OrphanCheckBox.Checked;
            itsMap = this.MapCheckBox.Checked;
            this.DialogResult = DialogResult.OK;
            Close();
        }

        private void CanButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
