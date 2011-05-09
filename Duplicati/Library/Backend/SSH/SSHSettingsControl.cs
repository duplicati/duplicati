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
