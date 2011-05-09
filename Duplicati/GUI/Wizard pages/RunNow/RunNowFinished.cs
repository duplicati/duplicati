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


namespace Duplicati.GUI.Wizard_pages.RunNow
{
    public partial class RunNowFinished : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public RunNowFinished()
            : base(Strings.RunNowFinished.PageTitle, Strings.RunNowFinished.PageDescription)
        {
            InitializeComponent();

            MonoSupport.FixTextBoxes(this);
            
            base.PageEnter += new PageChangeHandler(RunNowFinished_PageEnter);
            base.PageLeave += new PageChangeHandler(RunNowFinished_PageLeave);
        }

        void RunNowFinished_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            m_wrapper.ForceFull = ForceFull.Checked;
        }

        void RunNowFinished_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            Summary.Text = string.Format(Strings.RunNowFinished.SummaryText, m_wrapper.ScheduleName);
            args.TreatAsLast = true;
        }

        public bool ForceFullBackup { get { return ForceFull.Checked; } }
    }
}
