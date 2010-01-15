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
    public partial class RawContainer : WizardControl
    {
        private Dictionary<string, string> m_backendOptions;
        private Library.Backend.IBackend m_backend;
        private WizardSettingsWrapper m_wrapper;

        public RawContainer(Library.Backend.IBackend backend)
            : this()
        {
            m_autoFillValues = false;
            m_backend = backend;
            ProtocolKey.Text = m_backend.ProtocolKey + "://";

            base.PageEnter += new PageChangeHandler(RawContainer_PageEnter);
            base.PageLeave += new PageChangeHandler(RawContainer_PageLeave);
        }

        void RawContainer_PageLeave(object sender, PageChangedArgs args)
        {
            m_backendOptions.Clear();
            m_backendOptions.Add(Datamodel.Task.DESTINATION_EXTENSION_KEY, Destination.Text);
            foreach (KeyValuePair<string, string> p in OptionGrid.GetConfiguration())
                m_backendOptions.Add("--" + p.Key, p.Value);

            m_wrapper.BackendSettings = m_backendOptions;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (string.IsNullOrEmpty(Destination.Text) || Destination.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, Library.Backend.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore)
                args.NextPage = new Add_backup.GeneratedFilenameOptions();
            else
                args.NextPage = new Add_backup.AdvancedOptions();
            
        }

        void RawContainer_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            m_backendOptions = m_wrapper.BackendSettings;

            if (m_backendOptions.ContainsKey(Datamodel.Task.DESTINATION_EXTENSION_KEY))
                Destination.Text = m_backendOptions[Datamodel.Task.DESTINATION_EXTENSION_KEY];

            if (!OptionGrid.Unsupported)
            {
                m_wrapper = new WizardSettingsWrapper(m_settings);

                if (m_settings.ContainsKey("Overrides:Table") && m_settings.ContainsKey("Overrides:Type"))
                    if ((string)m_settings["Overrides:Type"] != m_backend.ProtocolKey)
                        m_settings.Remove("Overrides:Table");

                if (!m_settings.ContainsKey("Overrides:Table"))
                {
                    Dictionary<string, string> switches = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> p in m_backendOptions)
                        if (p.Key.StartsWith("--"))
                            switches.Add(p.Key.Substring(2), p.Value);

                    OptionGrid.Setup(m_backend.SupportedCommands, switches);
                    m_settings["Overrides:Table"] = OptionGrid.DataSet;
                    m_settings["Overrides:Type"] = m_backend.ProtocolKey;
                }
                else
                {
                    OptionGrid.DataSet = (DataSet)m_settings["Overrides:Table"];
                }
            }

        }

        private RawContainer()
            : base("", "")
        {
            InitializeComponent();

        }

        public override string Title
        {
            get
            {
                return m_backend.DisplayName;
            }
        }

        public override string HelpText
        {
            get
            {
                return m_backend.Description;
            }
        }
    }
}
