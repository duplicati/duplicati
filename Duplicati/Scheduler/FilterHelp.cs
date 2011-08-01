using System;
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// Help for filters
    /// </summary>
    public partial class FilterHelp : Form
    {
        /// <summary>
        /// Shows a modified version of Duplicati's filter help page
        /// </summary>
        public FilterHelp()
        {
            InitializeComponent();
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                this.richTextBox1.Rtf = System.IO.File.ReadAllText(Application.StartupPath + "\\Filters.rtf");
            }
            catch (Exception Ex)
            {
                this.richTextBox1.Text = "Help not available: " + Ex.Message;
            }
        }
    }
}
