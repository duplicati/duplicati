#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

namespace Duplicati.GUI.Wizard_pages.Backends
{
    public partial class GUIContainer : WizardControl
    {
        private Dictionary<string, string> m_backendOptions;
        private Control m_control;
        private Library.Backend.IBackendGUI m_backend;
        private WizardSettingsWrapper m_wrapper;

        public GUIContainer(Library.Backend.IBackendGUI backend)
            : this()
        {
            m_autoFillValues = false;
            m_backend = backend;

            base.PageEnter += new PageChangeHandler(GUIContainer_PageEnter);
            base.PageLeave += new PageChangeHandler(GUIContainer_PageLeave);
        }

        void GUIContainer_PageLeave(object sender, PageChangedArgs args)
        {
            m_backend.Leave(m_control);
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!m_backend.Validate(m_control))
            {
                args.Cancel = true;
                return;
            }

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                args.NextPage = new Add_backup.GeneratedFilenameOptions();
            else
                args.NextPage = new Add_backup.AdvancedOptions();
            
        }

        void GUIContainer_PageEnter(object sender, PageChangedArgs args)
        {
            Datamodel.ApplicationSettings appset = new Duplicati.Datamodel.ApplicationSettings(Program.DataConnection);
            m_wrapper = new WizardSettingsWrapper(m_settings);
            m_backendOptions = m_wrapper.BackendSettings;
            m_control = m_backend.GetControl(appset.CreateDetachedCopy(), m_backendOptions);
            m_control.SetBounds(0, 0, this.Width, this.Height);
            m_control.Visible = true;
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
                return m_backend.PageTitle;
            }
        }

        public override string HelpText
        {
            get
            {
                return m_backend.PageDescription;
            }
        }
    }
}
