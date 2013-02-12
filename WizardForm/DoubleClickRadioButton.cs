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
using System.Text;
using System.ComponentModel;

namespace System.Windows.Forms.Wizard
{
    /// <summary>
    /// Class to support double-click enabled radio buttons, which are common in wizards,
    /// but unavailable in standard .Net.
    /// </summary>
    public class DoubleClickRadioButton : RadioButton
    {
        public DoubleClickRadioButton()
        {
            this.SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            base.DoubleClick += new EventHandler(DoubleClickRadioButton_DoubleClick);
        }

        void DoubleClickRadioButton_DoubleClick(object sender, EventArgs e)
        {
            if (this.DoubleClick != null)
                this.DoubleClick(sender, e);
        }

        [BrowsableAttribute(true), DescriptionAttribute("Occurs when the user doubleclicks the RadioButton")]
        public new event EventHandler DoubleClick;
    }
}
