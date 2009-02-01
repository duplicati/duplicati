using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class AdvancedOptions : WizardControl
    {
        public AdvancedOptions()
            : base("Advanced settings", "On this page you can select more advanced settings for your backup. If you prefer, you can ignore those settings, and use the duplicati defaults.")
        {
            InitializeComponent();

            base.PageLeave += new PageChangeHandler(AdvancedOptions_PageLeave);
        }

        void AdvancedOptions_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["Advanced:When"] = SelectWhen.Checked;
            m_settings["Advanced:Incremental"] = SelectIncremental.Checked;
            m_settings["Advanced:Throttle"] = ThrottleOptions.Checked;
            m_settings["Advanced:Filters"] = EditFilters.Checked;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (SelectWhen.Checked)
                args.NextPage = new Wizard_pages.Add_backup.SelectWhen();
            else if (SelectIncremental.Checked)
                args.NextPage = new Wizard_pages.Add_backup.IncrementalSettings();
            else if (ThrottleOptions.Checked)
                args.NextPage = new Wizard_pages.Add_backup.ThrottleOptions();
            else if (EditFilters.Checked)
                args.NextPage = new Wizard_pages.Add_backup.FilterEditor();
            else
                args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }
    }
}
