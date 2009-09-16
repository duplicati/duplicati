#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class SettingOverrides : System.Windows.Forms.Wizard.WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public SettingOverrides()
            : base(Strings.SettingOverrides.PageTitle, Strings.SettingOverrides.PageDescription)
        {
            InitializeComponent();

            m_autoFillValues = false;
            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(SettingOverrides_PageEnter);
            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(SettingOverrides_PageLeave);
        }

        void SettingOverrides_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            if (!OptionGrid.Unsupported)
                m_wrapper.Overrides = OptionGrid.GetConfiguration();
            args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }

        void SettingOverrides_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (!OptionGrid.Unsupported)
            {
                m_wrapper = new WizardSettingsWrapper(m_settings);

                if (!m_settings.ContainsKey("Overrides:Table"))
                {
                    Library.Main.Options opt = new Library.Main.Options(new Dictionary<string, string>());
                    OptionGrid.Setup(opt.SupportedCommands, m_wrapper.Overrides);

                    m_settings["Overrides:Table"] = OptionGrid.DataSet;
                }
                else
                {
                    OptionGrid.DataSet = (DataSet)m_settings["Overrides:Table"]; 
                }
            }

        }
    }
}

