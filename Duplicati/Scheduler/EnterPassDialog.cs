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
    /// A dialog for entering passwords that is somewhat secure
    /// </summary>
    public partial class EnterPassDialog : Form
    {
        /// <summary>
        /// A dialog for entering passwords that is somewhat secure
        /// </summary>
        public EnterPassDialog()
        {
            InitializeComponent();
            Value = new System.Security.SecureString();
        }
        /// <summary>
        /// The entered password
        /// </summary>
        public System.Security.SecureString Value { get; set; }
        /// <summary>
        /// The entered password as a protected string
        /// </summary>
        public byte[] ProtectedValue { get; set; }
        /// <summary>
        /// The OK button (is hidden, triggered by [ENTER])
        /// </summary>
        private void button1_Click(object sender, EventArgs e)
        {
            Value = this.secureTextBox1.Value;
            ProtectedValue = this.secureTextBox1.ProtectedValue;
            this.DialogResult = DialogResult.OK;
            Close();
        }
        /// <summary>
        /// A static "ShowDialog" that gets the password as a secure string
        /// </summary>
        /// <returns>Entered password as a secure string</returns>
        public static System.Security.SecureString FetchPassword()
        {
            EnterPassDialog ed = new EnterPassDialog();
            ed.ShowDialog();
            return ed.Value;
        }
        /// <summary>
        /// A static "ShowDialog" that gets the password as a protected string
        /// </summary>
        /// <returns>Entered password as a protected string</returns>
        public static byte[] FetchProtectedPassword()
        {
            EnterPassDialog ed = new EnterPassDialog();
            ed.ShowDialog();
            return ed.ProtectedValue;
        }
    }
}
