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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class EditFilters : System.Windows.Forms.Wizard.WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public EditFilters()
            : base(Strings.EditFilters.PageTitle, Strings.EditFilters.PageDescription)
        {
            InitializeComponent();

            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(FilterEditor_PageEnter);
            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(FilterEditor_PageLeave);
        }

        void FilterEditor_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            m_wrapper.EncodedFilterXml = filterEditor1.FilterXml;

            //Don't set args.NextPage, it runs on a list
        }

        void FilterEditor_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            filterEditor1.FilterXml = m_wrapper.EncodedFilterXml;

            List<KeyValuePair<bool, string>> lst = new List<KeyValuePair<bool, string>>();
            filterEditor1.BasePath = DynamicSetupHelper.GetSourceFolders(m_wrapper, new ApplicationSettings(m_wrapper.DataConnection ?? Program.DataConnection), lst);
            filterEditor1.DynamicFilter = Library.Utility.FilenameFilter.EncodeAsFilter(lst);
        }
    }
}

