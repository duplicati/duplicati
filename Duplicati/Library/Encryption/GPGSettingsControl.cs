using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Encryption
{
    public class GPGSettingsControl : Library.Interface.ISettingsControl, Library.Interface.IGUIMiniControl
    {
        #region ISettingsControl Members

        public string Key
        {
            get { return "gpg-settings"; }
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
            get { return Strings.GPGSettingsControl.PageTitle; }
        }

        public string PageDescription
        {
            get { return Strings.GPGSettingsControl.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new GPGCommonOptions(applicationSettings, options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((GPGCommonOptions)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((GPGCommonOptions)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return GPGCommonOptions.GetConfiguration(applicationSettings, guiOptions, commandlineOptions);
        }

        #endregion
    }
}
