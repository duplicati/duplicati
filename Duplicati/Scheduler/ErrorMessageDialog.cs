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
    public partial class ErrorMessageDialog : Form
    {
        public ErrorMessageDialog()
        {
            InitializeComponent();
        }
        public DialogResult ShowDialog(string aMessage, Image aIcon)
        {
            this.labelIcon.Image = aIcon;
            this.richTextBox1.Text = aMessage;
            return ShowDialog();
        }
    }
}
