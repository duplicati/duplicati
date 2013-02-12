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
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Delete_backup
{
    public partial class DeleteFinished : WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public DeleteFinished()
            : base(Strings.DeleteFinished.PageTitle, Strings.DeleteFinished.PageDescription)
        {
            InitializeComponent();

            MonoSupport.FixTextBoxes(this);

            base.PageEnter += new PageChangeHandler(DeleteFinished_PageEnter);
        }

        void DeleteFinished_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            Summary.Text = string.Format(Strings.DeleteFinished.SummaryText, m_wrapper.ScheduleName);
            args.TreatAsLast = true;
        }

    }
}
