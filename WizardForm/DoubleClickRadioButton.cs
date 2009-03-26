using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;

namespace System.Windows.Forms.Wizard
{
    /// <summary>
    /// Class to support double-click enabled radio buttons, which are common in wizards,
    /// but unavalible in standard .Net.
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
