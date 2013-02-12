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
    public partial class GridContainer : WizardControl
    {
        public const string DESTINATION_EXTENSION_KEY = "Destination";

        protected IDictionary<string, string> m_options;
        protected IList<Library.Interface.ICommandLineArgument> m_commands;
        protected IWizardControl m_nextpage;

        protected string m_tablekey;
        protected string m_cachekey;
        protected string m_displayname;
        protected string m_description;

        /// <summary>
        /// Initializes a new GridContainer wizard page
        /// </summary>
        /// <param name="nextpage">The page to activate for the next button</param>
        /// <param name="commands">The list of supported commands</param>
        /// <param name="options">The list of saved options, if any</param>
        /// <param name="key">The item key, used to differentiate between different instances of the setup</param>
        /// <param name="displayname">The displayname for the page</param>
        /// <param name="description">The description for the page</param>
        public GridContainer(IWizardControl nextpage, IList<Library.Interface.ICommandLineArgument> commands, IDictionary<string, string> options, string key, string displayname, string description)
            : this()
        {
            m_autoFillValues = false;

            m_commands = commands;
            m_options = options;
            m_tablekey = "GridContainer:Table:" + key;
            m_cachekey = "GridContainer:Cache:" + key;
            m_displayname = displayname;
            m_description = description;
            m_nextpage = nextpage;

            base.PageEnter += new PageChangeHandler(GridContainer_PageEnter);
            base.PageLeave += new PageChangeHandler(GridContainer_PageLeave);
        }

        protected virtual void GridContainer_PageLeave(object sender, PageChangedArgs args)
        {
            m_options.Clear();
            foreach (KeyValuePair<string, string> p in OptionGrid.GetConfiguration())
                m_options.Add("--" + p.Key, p.Value);

            if (args.Direction == PageChangedDirection.Back)
                return;

            args.NextPage = m_nextpage;
        }

        protected virtual void GridContainer_PageEnter(object sender, PageChangedArgs args)
        {
            if (!OptionGrid.Unsupported)
            {
                if (!m_settings.ContainsKey(m_tablekey) || !m_settings.ContainsKey(m_cachekey))
                {
                    Dictionary<string, string> switches = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> p in m_options)
                        if (p.Key.StartsWith("--"))
                            switches.Add(p.Key.Substring(2), p.Value);

                    OptionGrid.Setup(m_commands, null, switches);
                    m_settings[m_tablekey] = OptionGrid.DataSet;
                    m_settings[m_cachekey] = OptionGrid.DataElementCache;
                }
                else
                {
                    OptionGrid.DataSet = (DataSet)m_settings[m_tablekey];
                    OptionGrid.DataElementCache = (Dictionary<string, Library.Interface.ICommandLineArgument>)m_settings[m_cachekey];
                }
            }

        }

        private GridContainer()
            : base("", "")
        {
            InitializeComponent();

        }

        public override string Title
        {
            get
            {
                return m_displayname;
            }
        }

        public override string HelpText
        {
            get
            {
                return m_description;
            }
        }
    }
}
