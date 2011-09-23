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

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Helper class to keep track of items in a combobox
    /// </summary>
    /// <typeparam name="T">The type of item to keep</typeparam>
    public class ComboBoxItemPair<T>
    {
        private T m_value;
        private string m_display;

        /// <summary>
        /// The item to be stored in a combobox
        /// </summary>
        /// <param name="display">The string to display for the item</param>
        /// <param name="value">The value to store for the item</param>
        public ComboBoxItemPair(string display, T value)
        {
            m_value = value;
            m_display = display;
        }

        /// <summary>
        /// Gets the display value for the item
        /// </summary>
        /// <returns>The display value for the item</returns>
        public override string ToString()
        {
            return m_display ?? "";
        }

        /// <summary>
        /// Gets the value stored in the instance
        /// </summary>
        public T Value { get { return m_value; } }
    }
}
