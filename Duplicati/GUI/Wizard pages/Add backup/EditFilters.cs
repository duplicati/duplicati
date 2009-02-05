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
            : base("Edit filters", "On this page you can modify filters that control what files are included in the backup.")
        {
            InitializeComponent();

            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(FilterEditor_PageEnter);
            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(FilterEditor_PageLeave);
        }

        void FilterEditor_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            m_wrapper.EncodedFilters = filterEditor1.Filter;
            
            args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }

        void FilterEditor_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            filterEditor1.BasePath = m_wrapper.SourcePath;
            filterEditor1.Filter = m_wrapper.EncodedFilters;
        }
    }
}

