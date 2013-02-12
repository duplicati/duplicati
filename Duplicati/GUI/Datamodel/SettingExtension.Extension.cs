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

namespace Duplicati.Datamodel
{
    public partial class SettingExtension
    {
        public static IDictionary<string, string> GetExtensions(System.Data.LightDatamodel.IDataFetcher fetcher, string key)
        {
            return new ExtDict(fetcher, key);
        }

        private class ExtDict : SettingsHelper<SettingExtension, string, string>
        {
            private string m_key;
            public ExtDict(System.Data.LightDatamodel.IDataFetcher fetcher, string key)
                : base(fetcher, new List<SettingExtension>(fetcher.GetObjects<SettingExtension>("SettingKey LIKE ?", key)), "Name", "Value")
            {
                m_key = key;
            }

            public override void Add(string key, string value)
            {
                base.Add(key, value);
                base.InternalSettings[key].SettingKey = m_key;
            }
        }
    }
}
