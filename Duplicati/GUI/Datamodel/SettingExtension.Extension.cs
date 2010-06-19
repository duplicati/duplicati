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
