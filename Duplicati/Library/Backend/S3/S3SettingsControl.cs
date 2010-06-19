using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Backend
{
    public class S3SettingsControl : Interface.ISettingsControl
    {
        #region ISettingsControl Members

        public string Key
        {
            get { return "amz-s3"; }
        }

        public void BeginEdit(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions)
        {
            GetConfiguration(applicationSettings, guiOptions, null);
        }

        public void EndEdit(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions)
        {
            if (S3CommonOptions.ExtractAllowCredentialStorage(guiOptions))
                S3CommonOptions.EncodeAccounts(S3CommonOptions.ExtractAccounts(applicationSettings), guiOptions);
        }

        #endregion

        #region IGUIControl Members

        public string PageTitle
        {
            get { return Strings.S3SettingsControl.PageTitle; }
        }

        public string PageDescription
        {
            get { return Strings.S3SettingsControl.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new S3CommonOptions(applicationSettings, options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((S3CommonOptions)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((S3CommonOptions)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return S3CommonOptions.GetConfiguration(applicationSettings, guiOptions, commandlineOptions);
        }

        #endregion
    }
}
