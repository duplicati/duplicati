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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.HelperControls
{
    public partial class DurationEditor : UserControl
    {
        private string[] m_values = { "1D", "1W", "2W", "1M", "" };

        public DurationEditor()
        {
            InitializeComponent();

            this.HandleCreated += new EventHandler(EasyDuration_SelectedIndexChanged);
        }

        public event EventHandler ValueChanged;

        /// <summary>
        /// Sets the available intervals
        /// </summary>
        /// <param name="values">A dictionary where the key is the string to display and the value is the duplicity time string</param>
        public void SetIntervals(List<KeyValuePair<string, string>> values)
        {
            EasyDuration.Items.Clear();

            if (values[values.Count - 1].Value != "")
                values.Add(new KeyValuePair<string, string>(Strings.DurationEditor.CustomDuration, ""));

            m_values = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                m_values[i] = values[i].Value;
                EasyDuration.Items.Add(values[i].Key);
            }
        }

        private void EasyDuration_SelectedIndexChanged(object sender, EventArgs e)
        {
            //To prevent some flickering when creating the control, we must wait until the handle is created
            if (this.IsHandleCreated)
            {
                CustomDuration.Visible = (EasyDuration.SelectedIndex == EasyDuration.Items.Count - 1);

                if (EasyDuration.SelectedIndex == EasyDuration.Items.Count - 1)
                    CustomDuration.Text = m_values[EasyDuration.Items.Count - 1];

                if (ValueChanged != null)
                    ValueChanged(sender, e);
            }
        }

        private void CustomDuration_TextChanged(object sender, EventArgs e)
        {
            try
            {
                Duplicati.Library.Utility.Timeparser.ParseTimeSpan(CustomDuration.Text);
            }
            catch (Exception ex)
            {
                errorProvider.SetError(CustomDuration, ex.Message);
                return;
            }

            errorProvider.SetError(CustomDuration, "");
            m_values[EasyDuration.Items.Count - 1] = CustomDuration.Text;

            if (ValueChanged != null)
                ValueChanged(sender, e);
        }

        public string Value
        {
            get
            {
                if (EasyDuration.SelectedIndex < 0)
                    return "";
                else
                    return m_values[EasyDuration.SelectedIndex];
            }
            set
            {
                for (int i = 0; i < m_values.Length - 1; i++)
                    if (m_values[i] == value)
                    {
                        EasyDuration.SelectedIndex = i;
                        return;
                    }

                m_values[EasyDuration.Items.Count - 1] = value;
                EasyDuration.SelectedIndex = EasyDuration.Items.Count - 1;
                CustomDuration.Text = m_values[EasyDuration.Items.Count - 1];

                if (ValueChanged != null)
                    ValueChanged(this, null);
            }
        }
    }
}
