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
            m_wrapper = new WizardSettingsWrapper(m_settings);
            m_backendOptions = m_wrapper.BackendSettings;
            m_control = m_backend.GetControl(m_backendOptions);
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
