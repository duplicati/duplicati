using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
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
