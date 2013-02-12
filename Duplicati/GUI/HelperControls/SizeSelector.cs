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
    public partial class SizeSelector : UserControl
    {
        private bool m_useSpeedNotation = false;
        public SizeSelector()
        {
            InitializeComponent();
            ResetSuffixCombo();
            Suffix.SelectedIndex = 1;
        }

        public event EventHandler CurrentSizeInBytesChanged;
        public event EventHandler CurrentSizeChanged;

        [DefaultValue(false)]
        public bool UseSpeedNotation
        {
            get
            {
                return m_useSpeedNotation;
            }
            set
            {
                if (m_useSpeedNotation != value)
                {
                    m_useSpeedNotation = value;
                    ResetSuffixCombo();
                }
            }
        }

        private void ResetSuffixCombo()
        {
            int ix = Suffix.SelectedIndex;
            Suffix.Items.Clear();
            if (m_useSpeedNotation)
                Suffix.Items.AddRange(new string[] {
                    Strings.SizeSelector.BytesPerSecond,
                    Strings.SizeSelector.KBPerSecond,
                    Strings.SizeSelector.MBPerSecond,
                    Strings.SizeSelector.GBPerSecond
                });
            else
                Suffix.Items.AddRange(new string[] {
                    Strings.SizeSelector.BytesName,
                    Strings.SizeSelector.KBName,
                    Strings.SizeSelector.MBName,
                    Strings.SizeSelector.GBName
                });

            Suffix.SelectedIndex = ix;
        }

        public long CurrentSizeInBytes
        {
            get
            {
                return (long)((double)Number.Value * Math.Pow(2, 10 * Suffix.SelectedIndex));
            }
            set
            {
                Suffix.SelectedIndex = -1;
                for (int i = 0; i < Suffix.Items.Count; i++)
                    if (value < Math.Pow(2, 10 * (i + 1)))
                    {
                        Suffix.SelectedIndex = i;
                        Number.Value = (int)(value / (long)Math.Pow(2, 10 * i));
                        break;
                    }

                if (Suffix.SelectedIndex == -1)
                {
                    Suffix.SelectedIndex = 0;
                    Number.Value = 0;
                }
            }
        }

        public string CurrentSize
        {
            get
            {
                string[] suffixes = new string[] { "b", "kb", "mb", "gb" };
                long value = CurrentSizeInBytes;

                for (int i = 0; i < suffixes.Length; i++)
                    if (value < Math.Pow(2, 10 * (i + 1)))
                        return (value / (long)Math.Pow(2, 10 * i)) + suffixes[i];

                return value.ToString() + "b";
            }
            set
            {
                CurrentSizeInBytes = Duplicati.Library.Utility.Sizeparser.ParseSize(value);

                if (CurrentSizeChanged != null)
                    CurrentSizeChanged(this, null);
                if (CurrentSizeInBytesChanged != null)
                    CurrentSizeInBytesChanged(this, null);
            }
        }

        private void Suffix_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CurrentSizeChanged != null)
                CurrentSizeChanged(this, null);
            if (CurrentSizeInBytesChanged != null)
                CurrentSizeInBytesChanged(this, null);
        }

        private void Number_ValueChanged(object sender, EventArgs e)
        {
            if (CurrentSizeChanged != null)
                CurrentSizeChanged(this, null);
            if (CurrentSizeInBytesChanged != null)
                CurrentSizeInBytesChanged(this, null);
        }
    }
}
