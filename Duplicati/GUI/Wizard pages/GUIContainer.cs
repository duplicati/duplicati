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

namespace Duplicati.GUI.Wizard_pages
{
    /// <summary>
    /// Class that wraps an IGUIControl in a wizard page
    /// </summary>
    public partial class GUIContainer : WizardControl
    {
        private const string ACTION_MARKER = "*duplicati-action*";

        private IDictionary<string, string> m_backendOptions;
        private Control m_control;
        private Library.Interface.IGUIControl m_interface;
        private WizardSettingsWrapper m_wrapper;
        private IWizardControl m_nextpage;

        public GUIContainer(IWizardControl nextpage, Library.Interface.IGUIControl @interface)
            : this()
        {
            m_autoFillValues = false;
            m_interface = @interface;
            m_nextpage = nextpage;

            base.PageEnter += new PageChangeHandler(GUIContainer_PageEnter);
            base.PageLeave += new PageChangeHandler(GUIContainer_PageLeave);
        }

        void GUIContainer_PageLeave(object sender, PageChangedArgs args)
        {
            m_interface.Leave(m_control);
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!m_interface.Validate(m_control))
            {
                args.Cancel = true;
                return;
            }

            //Make sure we don't save it in the DB
            if (m_backendOptions.ContainsKey(ACTION_MARKER))
                m_backendOptions.Remove(ACTION_MARKER);

            args.NextPage = m_nextpage;
        }

        void GUIContainer_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            m_backendOptions = m_wrapper.BackendSettings;

            //We inject a marker option here so the backend can make 
            // intelligent testing based on the current action
            string marker;
            switch (m_wrapper.PrimayAction)
            {
                case WizardSettingsWrapper.MainAction.Add:
                    marker = "add";
                    break;
                case WizardSettingsWrapper.MainAction.Edit:
                    marker = "edit";
                    break;
                case WizardSettingsWrapper.MainAction.Restore:
                case WizardSettingsWrapper.MainAction.RestoreSetup:
                    marker = "restore";
                    break;
                default:
                    marker = "unknown";
                    break;
            }
            m_backendOptions[ACTION_MARKER] = marker;

            m_control = m_interface.GetControl(m_wrapper.ApplicationSettings, m_backendOptions);
            m_control.SetBounds(0, 0, this.Width, this.Height);
            m_control.Visible = true;
            this.Controls.Clear();
            this.Controls.Add(m_control);
        }

        private GUIContainer()
            : base("", "")
        {
            InitializeComponent();

        }

        public override string Title
        {
            get
            {
                return m_interface.PageTitle;
            }
        }

        public override string HelpText
        {
            get
            {
                return m_interface.PageDescription;
            }
        }
    }
}
