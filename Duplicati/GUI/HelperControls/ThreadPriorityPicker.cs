using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.HelperControls
{
    public partial class ThreadPriorityPicker : UserControl
    {

        /// <summary>
        /// Textual representation of priorities
        /// </summary>
        private static readonly string[] THREAD_PRIORITIES = new string[] {
            GUI.Strings.Common.ThreadPriorityHighest,
            GUI.Strings.Common.ThreadPriorityAboveNormal,
            GUI.Strings.Common.ThreadPriorityNormal,
            GUI.Strings.Common.ThreadPriorityBelowNormal,
            GUI.Strings.Common.ThreadPriortyLowest
        };

        /// <summary>
        /// Mapping the priority to the string index
        /// </summary>
        private static readonly System.Threading.ThreadPriority[] PRIORITY_LOOKUP = new System.Threading.ThreadPriority[] {
            System.Threading.ThreadPriority.Highest,
            System.Threading.ThreadPriority.AboveNormal,
            System.Threading.ThreadPriority.Normal,
            System.Threading.ThreadPriority.BelowNormal,
            System.Threading.ThreadPriority.Lowest,
        };

        public event EventHandler SelectedPriorityChanged;

        public ThreadPriorityPicker()
        {
            InitializeComponent();

            ThreadPriority.Items.Clear();
            ThreadPriority.Items.AddRange(THREAD_PRIORITIES);
            ThreadPriority.SelectedIndex = 2;
        }

        [DefaultValue(null)]
        public System.Threading.ThreadPriority? SelectedPriority
        {
            get 
            {
                if (ThreadPriorityEnabled.Checked)
                    return PRIORITY_LOOKUP[ThreadPriority.SelectedIndex];
                else
                    return null;
            }
            set
            {
                if (value == null)
                    ThreadPriorityEnabled.Checked = false;
                else
                {
                    ThreadPriorityEnabled.Checked = true;
                    ThreadPriority.SelectedIndex = Array.IndexOf<System.Threading.ThreadPriority>(PRIORITY_LOOKUP, value.Value);
                }

                if (SelectedPriorityChanged != null)
                    SelectedPriorityChanged(this, null);
            }
        }

        private void ThreadPriorityEnabled_CheckedChanged(object sender, EventArgs e)
        {
            ThreadPriority.Enabled = ThreadPriorityEnabled.Checked;

            if (SelectedPriorityChanged != null)
                SelectedPriorityChanged(this, null);
        }

        private void ThreadPriority_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SelectedPriorityChanged != null)
                SelectedPriorityChanged(this, null);
        }
    }
}
