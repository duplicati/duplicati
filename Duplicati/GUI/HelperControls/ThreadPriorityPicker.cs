#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
