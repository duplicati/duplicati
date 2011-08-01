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
