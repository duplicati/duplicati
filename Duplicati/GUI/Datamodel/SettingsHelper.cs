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
using System.Data.LightDatamodel;
using System.Reflection;

namespace Duplicati.Datamodel
{
    public class SettingsHelper<TBase, TKey, TValue> 
        : IDictionary<TKey, TValue>
        where TBase : IDataClass
    {
        private Dictionary<TKey, TBase> m_settings;
        private IList<TBase> m_col;
        private IDataFetcher m_parent;

        private PropertyInfo m_keyfield;
        private PropertyInfo m_valuefield;

        public SettingsHelper(IDataFetcher parent, IList<TBase> col, string keyfield, string valuefield)
            : this(parent, col, typeof(TBase).GetProperty(keyfield), typeof(TBase).GetProperty(valuefield))

        {
        }

        public SettingsHelper(IDataFetcher parent, IList<TBase> col, PropertyInfo keyfield, PropertyInfo valuefield)
        {
            m_parent = parent;
            m_col = col;
            m_keyfield = keyfield;
            m_valuefield = valuefield;

            if (m_parent == null)
                throw new ArgumentNullException("parent");
            if (m_col == null)
                throw new ArgumentNullException("col");
            if (m_keyfield == null)
                throw new ArgumentNullException("keyfield");
            if (m_valuefield == null)
                throw new ArgumentNullException("valuefield");

            parent.AfterDataConnection += new DataConnectionEventHandler(parent_AfterDataConnection);
        }

        void parent_AfterDataConnection(object sender, DataActions action)
        {
            if (action != DataActions.Fetch)
                m_settings = null;

        }

        public PropertyInfo KeyField { get { return m_keyfield; } }
        public PropertyInfo ValueField { get { return m_valuefield; } }

        protected Dictionary<TKey, TBase> InternalSettings
        {
            get
            {
                if (m_settings == null)
                {
                    m_settings = new Dictionary<TKey, TBase>();

                    foreach (TBase item in m_col)
                        if (!m_settings.ContainsKey((TKey)m_keyfield.GetValue(item, null)))
                            m_settings.Add((TKey)m_keyfield.GetValue(item, null), item);
                }

                return m_settings;
            }
        }

        #region IDictionary<TKey, TValue> Members

        public virtual void Add(TKey key, TValue value)
        {
            TBase item = Activator.CreateInstance<TBase>();
            m_keyfield.SetValue(item, key, null);
            m_valuefield.SetValue(item, value, null);

            InternalSettings.Add(key, item);
            m_parent.Add(item);
            m_col.Add(item);
        }

        public bool ContainsKey(TKey key)
        {
            return InternalSettings.ContainsKey(key);
        }

        public ICollection<TKey> Keys
        {
            get { return InternalSettings.Keys; }
        }

        public bool Remove(TKey key)
        {
            if (InternalSettings.ContainsKey(key))
            {
                m_col.Remove(InternalSettings[key]);
                m_parent.DeleteObject(InternalSettings[key]);
            }

            return InternalSettings.Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            TBase item;
            if (InternalSettings.TryGetValue(key, out item))
            {
                value = (TValue)m_valuefield.GetValue(item, null);
                return true;
            }
            else
            {
                value = default(TValue);
                return false;
            }

        }

        public ICollection<TValue> Values
        {
            get
            {
                List<TValue> lst = new List<TValue>();
                foreach (TBase item in InternalSettings.Values)
                    lst.Add((TValue)m_valuefield.GetValue(item, null));
                return lst;
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                TBase tmp;
                if (InternalSettings.TryGetValue(key, out tmp))
                    return (TValue)m_valuefield.GetValue(tmp, null);
                else
                    return default(TValue);
            }
            set
            {
                bool exists = InternalSettings.ContainsKey(key);

                if (value == null && exists)
                {
                    //Remove
                    this.Remove(key);
                }
                else if (value != null && !exists)
                {
                    //Add
                    this.Add(key, value);
                }
                else if (value != null && exists)
                {
                    //Update
                    m_valuefield.SetValue(InternalSettings[key], value, null);
                }
                //else: It should be removed, but does not exist

            }
        }

        #endregion

        #region ICollection<KeyValuePair<TKey,TValue>> Members

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            this.Add(item.Key, item.Value);
        }

        public void Clear()
        {
            while (m_col.Count > 0)
            {
                TBase item = m_col[0];
                m_col.Remove(item);
                m_parent.DeleteObject<TBase>(item);
            }

            InternalSettings.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            if (!InternalSettings.ContainsKey(item.Key))
                return false;

            TValue v = this[item.Key];
            if (v == null && item.Value == null)
                return true;
            else if (v == null)
                return false;
            else
                return v.Equals(item.Value);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)(InternalSettings)).CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return InternalSettings.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!this.Contains(item))
                return false;
            
            return this.Remove(item.Key);
        }

        #endregion

        #region IEnumerable<KeyValuePair<string,string>> Members

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return new TranslatoryEnumerator(this, InternalSettings.GetEnumerator());
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return ((System.Collections.IEnumerable)InternalSettings).GetEnumerator();
        }

        #endregion


        private class TranslatoryEnumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private SettingsHelper<TBase, TKey, TValue> m_parent;
            private IEnumerator<KeyValuePair<TKey, TBase>> m_base;

            public TranslatoryEnumerator(SettingsHelper<TBase, TKey, TValue> parent, IEnumerator<KeyValuePair<TKey, TBase>> en)
            {
                m_parent = parent;
                m_base = en;
            }

            #region IEnumerator<KeyValuePair<TKey,TValue>> Members

            public KeyValuePair<TKey, TValue> Current
            {
                get 
                {
                    return new KeyValuePair<TKey, TValue>(m_base.Current.Key, (TValue)m_parent.ValueField.GetValue(m_base.Current.Value, null));
                }
            }

            #endregion

            #region IDisposable Members

            public void Dispose()
            {
                m_parent = null;
                m_base = null;
            }

            #endregion

            #region IEnumerator Members

            object System.Collections.IEnumerator.Current
            {
                get { return new System.Collections.DictionaryEntry(m_base.Current.Key, (TValue)m_parent.KeyField.GetValue(m_base.Current.Value, null)); }
            }

            public bool MoveNext()
            {
                return m_base.MoveNext();
            }

            public void Reset()
            {
                m_base.Reset();
            }

            #endregion
        }

        public IDataFetcher DataParent { get { return m_parent; } }
        public IList<TBase> Collection { get { return m_col; } }
    }
}
