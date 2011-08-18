using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Scheduler.Utility
{
    public partial class GetCheck : Form
    {
        public string Prompt { get { return this.label1.Text; } set { this.label1.Text = value; } }
        public System.Security.SecureString Result { get; private set; }
        public GetCheck(string aPrompt)
            : this()
        {
            Prompt = aPrompt;
        }
        public GetCheck()
        {
            InitializeComponent();
        }
        protected override void OnShown(EventArgs e)
        {
            if (Prompt == null) this.label1.Text = Prompt;
            base.OnShown(e);
        }
        private void OKButton_Click(object sender, EventArgs e)
        {
            Result = this.secureTextBox1.Value;
            this.DialogResult = DialogResult.OK;
            Close();
        }

        private void CanButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }
        public static System.Security.SecureString Fetch(string aPrompt)
        {
            System.Security.SecureString Result = new System.Security.SecureString();
            using (GetCheck gc = new GetCheck(aPrompt))
                if (gc.ShowDialog() == DialogResult.OK) Result = gc.Result;
            return Result;
        }
    }
}
