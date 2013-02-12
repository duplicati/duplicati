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

namespace System.Windows.Forms.Wizard
{
    public partial class StartPage : UserControl, IWizardControl
    {
        public StartPage()
        {
            InitializeComponent();
        }

        public StartPage(string title, string introduction)
            : this()
        {
            WizardTitle.Text = title;
            WizardIntroduction.Text = introduction;
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return WizardTitle.Text; }
        }

        string IWizardControl.HelpText
        {
            get { return WizardIntroduction.Text; }
        }

        Image IWizardControl.Image
        {
            get { return null; }
        }

        bool IWizardControl.FullSize
        {
            get { return true; }
        }

        void IWizardControl.Enter(IWizardForm owner, PageChangedArgs args)
        {
        }

        void IWizardControl.Leave(IWizardForm owner, PageChangedArgs args)
        {
        }

        void IWizardControl.Display(IWizardForm owner, PageChangedArgs args)
        {
        }

        #endregion
    }
}
