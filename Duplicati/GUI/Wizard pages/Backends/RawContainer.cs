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

namespace Duplicati.GUI.Wizard_pages.Backends
{
    public partial class RawContainer : GridContainer
    {
        private Library.Interface.IBackend m_backend;

        public RawContainer(IWizardControl nextpage, Library.Interface.IBackend backend, IDictionary<string, string> settings)
            : base(nextpage, backend.SupportedCommands, settings, "backend:" + backend.ProtocolKey, backend.DisplayName, backend.Description)
        {
            InitializeComponent();

            m_backend = backend;
            ProtocolKey.Text = m_backend.ProtocolKey + "://";
        }

        protected override void GridContainer_PageLeave(object sender, PageChangedArgs args)
        {
            base.GridContainer_PageLeave(sender, args);
            m_options.Add(GridContainer.DESTINATION_EXTENSION_KEY, Destination.Text);

            if (args.Direction == PageChangedDirection.Back || args.Cancel == true)
                return;

            if (string.IsNullOrEmpty(Destination.Text) || Destination.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, Library.Interface.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }
        }

        protected override void GridContainer_PageEnter(object sender, PageChangedArgs args)
        {
            base.GridContainer_PageEnter(sender, args);
            if (m_options.ContainsKey(GridContainer.DESTINATION_EXTENSION_KEY))
                Destination.Text = m_options[GridContainer.DESTINATION_EXTENSION_KEY];
        }

    }
}
