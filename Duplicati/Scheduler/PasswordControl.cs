using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// A Duplicati style password control
    /// </summary>
    public partial class PasswordControl : UserControl
    {
        /// <summary>
        /// Selected encryption module
        /// </summary>
        public string CheckMod
        {
            get { return this.EncryptionModuleDropDown.SelectedText; }
            set
            {
                if (!string.IsNullOrEmpty(value) && this.EncryptionModuleDropDown.Items.Contains(value))
                    this.EncryptionModuleDropDown.SelectedText = value;
            }
        }
        /// <summary>
        /// Entered password as a protected string
        /// </summary>
        public byte[] Checksum
        {
            get { return this.Enabled ? this.secureTextBox1.ProtectedValue : new byte[0]; }
            set { itsValue = Utility.Tools.SecureUnprotect( value ); }
        }
        private System.Security.SecureString itsValue;
        /// <summary>
        /// Entered password as a SecureString
        /// </summary>
        public System.Security.SecureString Value
        {
            get { return this.secureTextBox1.Value; }
        }
        /// <summary>
        /// A Duplicati style password control
        /// </summary>
        public PasswordControl()
        {
            InitializeComponent();
            // Get the modules into the combo
            this.EncryptionModuleDropDown.Items.AddRange(
                (from Library.Interface.IEncryption qE in Library.DynamicLoader.EncryptionLoader.Modules
                 select qE.DisplayName).ToArray());
            if (this.EncryptionModuleDropDown.Items.Count > 0)
                this.EncryptionModuleDropDown.SelectedIndex = 0;
        }
        /// <summary>
        /// Change Button clicked, force user to enter old password prior to entering new one
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChangeButtonClick(object sender, EventArgs e)
        {
            if (itsValue.Length > 0)
            {
                for (bool OK = false; !OK; )
                {
                    System.Security.SecureString Check = EnterPassDialog.FetchPassword();
                    if (!(OK = Utility.Tools.GotIt(Check, itsValue)) &&
                        MessageBox.Show("Invalid, try again?", "INVALID", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                        return;
                }
            }
            this.secureTextBox1.ReadOnly = false;
        }
    }
}
