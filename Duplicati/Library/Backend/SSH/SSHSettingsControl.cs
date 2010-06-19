using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Backend
{
    public class SSHSettingsControl : Library.Interface.ISettingsControl, Library.Interface.IGUIMiniControl
    {
        #region ISettingsControl Members

        public string Key
        {
            get { return "ssh-settings"; }
        }

        public void BeginEdit(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions)
        {
            GetConfiguration(applicationSettings, guiOptions, null);
        }

        public void EndEdit(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions)
        {
            //No need to save
        }

        #endregion

        #region IGUIControl Members

        public string PageTitle
        {
            get { return Strings.SSHSettingsControl.PageTitle; }
        }

        public string PageDescription
        {
            get { return Strings.SSHSettingsControl.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new SSHCommonOptions(applicationSettings, options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((SSHCommonOptions)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((SSHCommonOptions)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return SSHCommonOptions.GetConfiguration(applicationSettings, guiOptions, commandlineOptions);
        }

        #endregion
    }
}
